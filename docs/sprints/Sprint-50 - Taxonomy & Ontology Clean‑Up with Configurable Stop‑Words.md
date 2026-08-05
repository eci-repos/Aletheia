# Sprint 50 - Taxonomy & Ontology Clean‑Up with Configurable Stop‑Words

**Status:** Active

## Objective

Create a robust, configuration‑driven taxonomy and ontology cleaning pipeline that:

1. **Loads stop‑words from a config file** (no hard‑coded list in code). 
2. **Normalizes terms** by removing stop‑words, punctuation, and trivial tokens while preserving meaningful multi‑word phrases.
3. **Respects recognized entity phrases** defined in `docs/doc-templates/*.md` – those phrases should be exempt from stop‑word removal because they represent domain‑specific concepts (e.g., “RFP opportunity”, “AI‑enabled system”).
4. **Keeps the architecture clean** – follows `docs/Architecture.md`, the **Singleton Registration Rule**, and the **Result<T> pattern** used throughout the code base.
5. **Back‑fills existing noisy data** in the PostgreSQL `Taxonomy` and `Ontology` tables and provides a migration strategy.
6. **Adds comprehensive tests** and documentation updates.

## Background

Since Phase 21, the ingestion pipeline writes raw tokens directly into the taxonomy and ontology stores. This has resulted in many irrelevant entries such as “and”, “The”, punctuation, and single‑character tokens. These noisy terms degrade the usefulness of the UI (graph explorers, WRAGS wiki, search filters) and also cause unnecessary space consumption in Neo4j/PostgreSQL.

The current `KnowledgeTermNormalizer` only lower‑cases strings; it does not filter stop‑words and does not support a configurable list. Moreover, some domain‑specific multi‑word phrases are essential and should **not** be broken up – they are declared in the markdown templates under `docs/doc-templates` (e.g., a template for RFPs may contain a placeholder `{{RfpOpportunity}}`).

## Authority

*The repository is the source of truth.* All changes must:
- Honor the **Singleton Registration Rule** (`IGraphProvider` and any dependent services must be singletons).
- Use the **Result<T> pattern** for all service returns.
- Respect the **Architecture.md** guidelines (layered clean‑architecture, dependency injection, separate contracts, etc.).
- Keep the UI UI‑friendly – taxonomy & ontology must stay meaningful for end‑users.

## Deliverables

1. **Configuration Section** (`appsettings.json`)
   ```json
   "Taxonomy": {
     "StopWords": ["a","an","the","and","or","but","if","while","for","to","of","in","on","by","as","at","from","into","that","this","it","is","was","were","be","been","being","has","have","had","not","no","yes"]
   }
   ```
   The list can be extended per‑environment.

2. **ITermNormalizer Interface** (unchanged) and **ConfigurableTermNormalizer** implementation that:
   - Loads the stop‑word list via `IOptions<TaxonomyOptions>`.
   - Reads all markdown files in `docs/doc-templates` at startup, extracts candidate phrases (any word or phrase surrounded by double braces `{{...}}` or headings that mark domain terms) and stores them in a hash set for **phrase exemption**.
   - Normalizes a term: lower‑case, strip punctuation, split, filter out stop‑words **unless** the original raw term matches a known phrase (exact match after case‑fold). If a phrase is recognized, the term is returned unchanged (aside from trimming).
   - Returns `null` for pure stop‑words.

3. **DI Registration** (singleton) in `Program.cs`:
   ```csharp
   services.Configure<TaxonomyOptions>(Configuration.GetSection("Taxonomy"));
   services.AddSingleton<ITermNormalizer, ConfigurableTermNormalizer>();
   ```

4. **Ingestion Pipeline Update**
   - Inject `ITermNormalizer` into `UploadedContentKnowledgeIndexer` and any other component that writes taxonomy/ontology (`TopicExtractionService`, `LazyEnrichmentKnowledgeSink`).
   - Apply normalization before calling `WriteTaxonomyAsync` / `WriteOntologyAsync`.
   - Preserve the **Result<T>** handling – propagate failures.

5. **Migration / Back‑fill**
   - EF Core migration `CleanTaxonomyOntology` that:
     * Loads stop‑words from the same config section.
     * Reads existing terms, normalizes them using the new normalizer (re‑using the same logic), and deletes rows that become `null`.
   - Optional one‑off background job `TaxonomyRebuilderJob` that re‑processes all source IDs to repopulate clean terms.

6. **Tests**
   - **Unit**: `ConfigurableTermNormalizerTests` – verify stop‑word removal, phrase exemption, punctuation stripping, and config loading.
   - **Integration**: Ingestion of a document containing both stop‑words and a known template phrase (e.g., “{{RfpOpportunity}}”) results in a wiki page whose taxonomy only contains the phrase and other meaningful terms.
   - **Migration Test**: Run migration on a test database seeded with noisy data and assert that the resulting tables contain no stop‑words.
   - Update coverage to remain ≥ 99 % (per *Coverage Notes*).

7. **Documentation**
   - Add a “Taxonomy & Ontology Normalization” section to `docs/Architecture.md` describing the new normalizer and its config.
   - Update `docs/Phase21-Background-Operations-Handoff.md` with a note on the cleaned taxonomy.
   - Add a brief usage guide in `docs/README.md` explaining how to extend the stop‑word list via configuration.

## Requirements (Detailed)

### 1. ConfigurableTermNormalizer Implementation
```csharp
public sealed class ConfigurableTermNormalizer : ITermNormalizer
{
    private readonly HashSet<string> _stopWords;
    private readonly HashSet<string> _exemptPhrases; // loaded from doc‑templates

    public ConfigurableTermNormalizer(IOptions<TaxonomyOptions> opts, ILogger<ConfigurableTermNormalizer> logger)
    {
        _stopWords = new HashSet<string>(opts.Value.StopWords ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _exemptPhrases = LoadPhrasesFromTemplates(logger);
    }

    private static HashSet<string> LoadPhrasesFromTemplates(ILogger logger)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "docs", "doc-templates");
        var phraseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("Doc‑template folder not found: {Folder}", folder);
            return phraseSet;
        }
        foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
        {
            var content = File.ReadAllText(file);
            // Capture {{Phrase}} placeholders.
            foreach (Match m in Regex.Matches(content, "{{(.*?)}}"))
            {
                var phrase = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(phrase))
                    phraseSet.Add(phrase);
            }
            // Capture headings that look like domain concepts (e.g., "## RFP Opportunity").
            foreach (Match m in Regex.Matches(content, "^##\s+(.+)$", RegexOptions.Multiline))
            {
                var heading = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(heading))
                    phraseSet.Add(heading);
            }
        }
        logger.LogInformation("Loaded {Count} phrase exemptions from doc‑templates.", phraseSet.Count);
        return phraseSet;
    }

    public string? Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        // If the raw term exactly matches a known phrase, keep it as‑is (lower‑cased).
        if (_exemptPhrases.Contains(trimmed))
            return trimmed.ToLowerInvariant();

        // Standard cleaning pipeline.
        var lowered = trimmed.ToLowerInvariant();
        var cleaned = Regex.Replace(lowered, "[^\p{L}\p{N}\-']+", " ");
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .Where(t => t.Length > 1 && !_stopWords.Contains(t))
                           .ToArray();
        if (tokens.Length == 0) return null;
        return string.Join(' ', tokens);
    }
}
```
*Note*: The regular expression extracts placeholders and headings from *all* markdown templates at startup, satisfying the requirement that **phrases defined in doc‑templates are preserved**.

### 2. Options Model
```csharp
public class TaxonomyOptions
{
    public IEnumerable<string>? StopWords { get; set; }
}
```
This model goes into `src/Repository.API/Options/TaxonomyOptions.cs` (or an appropriate folder).

### 3. Updating Existing Services
All services that currently call `KnowledgeTermNormalizer` will be switched to `ITermNormalizer`. Example change in `UploadedContentKnowledgeIndexer.cs`:
```csharp
private readonly ITermNormalizer _termNormalizer;
public UploadedContentKnowledgeIndexer(..., ITermNormalizer termNormalizer, ...)
{
    _termNormalizer = termNormalizer;
}

// In the indexing method
var normalized = rawTerms
    .Select(t => _termNormalizer.Normalize(t))
    .Where(t => t != null)!;
```
Preserve the **Result<T>** return flow.

### 4. Migration Logic (EF Core)
Create a migration that re‑uses the same normalizer logic (via a small service instance) to decide which rows to delete. The migration script is similar to the one from Sprint 49 but references the config‑driven stop‑words.

### 5. Background Rebuilder Job (Optional, but part of the sprint)
If the team chooses to run a re‑index, expose an endpoint `POST /api/jobs/taxonomy/rebuild` that enqueues a `TaxonomyRebuilderJob`. The job iterates all source IDs, extracts raw terms again (via existing extractor), runs the new normalizer, and writes clean taxonomy/ontology back.

## Validation

- **Scenario**: Upload a document `RFP_2026.md` which contains the phrase `{{RfpOpportunity}}` and many filler words. After ingestion:
  * The taxonomy for that source includes `rfp opportunity` (preserved phrase) and other domain terms.
  * No entries like `the`, `and`, `a`, punctuation‑only tokens exist.
- **Graph UI**: The taxonomy explorer shows only clean terms, searchable filters no longer return stop‑words.
- **Wiki**: The WRAGS wiki page generated from the same document shows proper sections where the phrase is used as a heading.
- **Telemetry**: An event `taxonomy.terms.filtered` reports the count of removed stop‑words per job.
- **Coverage**: Run `dotnet test --collect:"XPlat Code Coverage"` and verify line‑coverage stays ≥ 99 %.

## Exit Criteria

- ✅ All new code compiles (`dotnet build Aletheia.slnx`).
- ✅ Unit and integration tests pass, coverage ≥ 99 %.
- ✅ Migration runs cleanly on a test database and removes all stop‑words.
- ✅ Ingestion of a document with stop‑words and a template phrase creates a clean taxonomy and ontology without noise.
- ✅ UI explorers (graph, search filters, WRAGS wiki) no longer show irrelevant tokens.
- ✅ Documentation updated (Architecture.md, Phase21‑Background‑Operations‑Handoff.md, README). 
- ✅ No hard‑coded stop‑word lists remain in source code.

## Out of Scope

- Persistent job storage beyond the in‑memory queue (already covered by other sprints).
- Full‑blown NLP lemmatization or stemming – can be added in a future sprint.
- Redesign of the wiki UI layout – the current layout is sufficient once the taxonomy is clean.
- Changing the underlying PostgreSQL schema beyond adding the migration.
- Bulk import of legacy external taxonomies – only the internal noisy data is cleaned.

---

*All work respects the existing clean‑architecture patterns, singleton registration, and `Result<T>` error handling throughout the code base.*

