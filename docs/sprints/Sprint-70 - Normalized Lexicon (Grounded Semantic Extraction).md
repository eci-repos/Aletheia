# Sprint 70 - Normalized Lexicon (Grounded Semantic Extraction)

**Status:** Active (2026-08-14)

Full authority: this file. Sprint 69 (Ingestion Status in the Repository Browser) is **complete, committed, and pushed** on `origin/master` (`debdfeb`).

Promotes `docs/backlog/Normalized-Lexicon-for-Term-Resolution.md` — a project-owner-driven need. Copilot missed the RFP due dates even though they were on the first page of the source ("Proposal Due Date: February 24, 2022, at 2:00 p.m. EST"); a second source phrased the same concept differently ("Bid due: August 26, 2026, 2:00 PM Pacific Time") and was missed too. Diagnosis: **not a bug** — the systematic limit of retrieval. Vector similarity + the whole-string ILIKE keyword fallback both fail on terse, varied-phrase facts. The fix generalizes the Sprint 69 "ground truth" instinct into a **canonical lexicon** that resolves terminology diversity across source documents — and, per project-owner direction, it is **semantic** (understands paraphrase and novel terminology) **without losing fidelity to the source** (nothing stored that is not verifiable in the text).

## Objective

Give the platform a normalized lexicon applied on **both sides** of retrieval:

- **Ingestion side** — grounded fact extraction: the LLM proposes facts with the exact source span each was read from; a **fidelity gate** confirms the span exists in the extracted text and the value parses against the concept's value pattern; verified facts normalize to canonical concepts and persist as structured, page-anchored rows.
- **Query side** — concept expansion: a query that mentions a concept (via any alias) is widened to the full alias family before embedding, so "submission due date" retrieves documents that say "Bid due" or "Proposal Due Date".
- **Governance loop** — concept hints that match no known concept are recorded as unmapped terms for admin review (the growth mechanism; the admin surface itself is a follow-up).

## Decisions (from the backlog item, settled 2026-08-14)

1. **Grounded semantic extraction is the core — propose → verify → normalize.** The LLM is the *recognition* layer (semantic, wide coverage); the source text is the *fidelity* gate; the lexicon is the *normalization* layer. No single layer carries the whole burden.
2. **The fidelity gate is mandatory.** A proposal becomes a stored fact only when (a) the quoted source span actually exists in the extracted text (whitespace-tolerant match) and (b) the value parses against the concept's value pattern (`date`/`currency`/`number`/`text`). Anything else is dropped — nothing enters the knowledge base that is not in the source. Unverified LLM extraction is explicitly out of scope.
3. **The lexicon is a concept registry, not a word list.** Each concept has a canonical key, label, alias set, value pattern, and optional template scope. Seeded defaults live in `LexiconSeedData` (RAGS.Abstractions) mirrored by the SQL seed in `init.sql` + the migration (a binding test keeps them in sync).
4. **Facts are durable and page-anchored.** `document_facts` rows carry source_id, concept_key, normalized value, source span, page/offset — the same durable-ground-truth principle as Sprint 69's embeddings-based status, reusing the Sprint 67 page machinery.
5. **Query expansion keeps the original query.** Concept expansion appends the alias family to the embedding query; the keyword fallback keeps the original (whole-string ILIKE). Same contract as Sprint 68's acronym expansion.
6. **Best-effort at ingestion.** Fact extraction never blocks ingestion; failures are logged and skipped.

## Deliverables

### 1. Lexicon data model + repository
- `LexiconConcept` / `DocumentFact` / `ProposedFact` / `UnmappedTerm` models + `LexiconSeedData` defaults (RAGS.Abstractions).
- `ILexiconRepository` (RAGS.Abstractions) → `PostgreSqlLexiconRepository` (RAGS.Infrastructure.PostgreSQL): `GetAllConceptsAsync`, `UpsertConceptAsync`, `SaveFactsAsync` (replace-on-reingest), `GetFactsAsync`, `RecordUnmappedTermAsync`, `GetUnmappedTermsAsync`.
- Tables `lexicon_concepts` / `lexicon_aliases` / `document_facts` / `lexicon_unmapped_terms` in `scripts/init.sql` + migration `2026-08-14-lexicon-and-facts.sql` (idempotent, seeded); `PostgreSqlLexiconSchema` + hosted initializer as a startup safety net.

### 2. Grounded fact extraction (propose + verify)
- `IFactProposer` → `SemanticKernelFactProposer`: LLM pass over the extracted text proposing `{concept, value, span}` with the span quoted verbatim; returns nothing on failure (never fabricates into the pipeline).
- `FactValueParser` (date/currency/number/text normalization) + `FactVerifier` (span-existence + value-parse fidelity gate, page/offset anchoring via `WhitespaceCollapser`).
- `IFactExtractionService` → `GroundedFactExtractionService`: propose → verify → normalize → persist, plus unmapped-term recording.
- Wired into `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` (best-effort, after text extraction).

### 3. Query-time concept expansion
- `LexiconExpander` (RAGS.Application): appends a matched concept's label + full alias family to the embedding query.
- `ILexiconProvider` → `LexiconProvider` (cached, invalidatable) injected into `RagsService.RetrieveAsync` after `QueryExpander` (acronyms). Optional ctor param — existing fakes keep compiling.

### 4. Tests + docs
- **RAGS 338** (+36): `LexiconExpanderTests` (6), `FactValueParserTests` (8), `FactVerifierTests` (7), `GroundedFactExtractionServiceTests` (5), `RagsServiceTests` (+2 lexicon wiring).
- **Web 84** (+3): `LexiconBindingTests` — tables in migration + init, seed mirrors `LexiconSeedData`.
- Repository 138 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors).
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived.

## Acceptance Criteria

- A document whose text says "Bid due: August 26, 2026" yields a verified `due_date` fact (span exists, value parses) with page/offset; a proposal whose span is not in the source is dropped.
- A query mentioning any due-date alias embeds the full alias family ("submission due date" → + "bid due", "proposal due date", "deadline", …); the keyword fallback keeps the original query.
- Unmapped concept hints are recorded for the governance loop.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Admin settings panel for the lexicon (browse/add aliases, review unmapped terms) — the governance *surface*; the loop's data collection is in this sprint.
- Surfacing facts in Browse/Copilot/document viewer (the `document_facts` rows are queryable; UI is a follow-up).
- Per-template concept scoping enforcement (the `template_scope` column exists; matching is global for now).
- Unverified LLM extraction — the fidelity gate is mandatory.

---

## Implementation Status

**Implemented (2026-08-14).** All 4 items complete; tests green.

### Item 1 — Lexicon data model + repository
- `src/RAGS.Abstractions/Models/`: `LexiconConcept.cs`, `DocumentFact.cs`, `ProposedFact.cs`, `UnmappedTerm.cs`, `LexiconSeedData.cs` (5 seeded concepts: due_date, budget, page_limit, vendor, submission).
- `src/RAGS.Abstractions/Interfaces/ILexiconRepository.cs` → `src/RAGS.Infrastructure.PostgreSQL/Lexicon/PostgreSqlLexiconRepository.cs` (Dapper; `SaveFactsAsync` replaces on re-ingest).
- Tables in `scripts/init.sql` + `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-14-lexicon-and-facts.sql` (idempotent, seeded); `PostgreSqlLexiconSchema` + `PostgreSqlLexiconSchemaInitializer` (hosted safety net) registered in `Program.cs`.

### Item 2 — Grounded fact extraction (propose + verify)
- `src/RAGS.Application/Lexicon/`: `SemanticKernelFactProposer.cs` (LLM propose, span-quoting prompt, empty-on-failure), `FactValueParser.cs` (date/currency/number/text), `FactVerifier.cs` (span-existence + value-parse fidelity gate, page anchoring), `WhitespaceCollapser.cs`, `GroundedFactExtractionService.cs` (orchestration + unmapped-term recording).
- `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` calls `IFactExtractionService.ExtractAsync` after text extraction — best-effort, never blocks ingestion.

### Item 3 — Query-time concept expansion
- `src/RAGS.Application/Lexicon/LexiconExpander.cs` (alias-family expansion, original query kept) + `LexiconProvider.cs` (cached, invalidatable).
- `RagsService.RetrieveAsync` applies `LexiconExpander` after `QueryExpander` when an `ILexiconProvider` is present (optional ctor param — existing fakes compile).

### Item 4 — Tests + docs
- **RAGS 338** (+36): `Lexicon/LexiconExpanderTests` (6), `Lexicon/FactValueParserTests` (8), `Lexicon/FactVerifierTests` (7), `Lexicon/GroundedFactExtractionServiceTests` (5), `RagsServiceTests` (+2: embeds lexicon-expanded query, skips when provider absent).
- **Web 84** (+3): `LexiconBindingTests` — lexicon tables in migration + init, seed mirrors `LexiconSeedData`.
- Repository 138 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors).

**Residual manual (user-side):** `docker compose up -d --build` (fresh DB gets the tables + seed from init.sql; an existing deployment needs the migration `2026-08-14-lexicon-and-facts.sql` applied once, or the API's schema initializer self-heals at startup). Then re-upload the CMP 2026 RFP (or run a repair job) so grounded facts are extracted, and re-ask "What is the submission due date for the CMP 2026 RFP?" — the query now embeds the due-date alias family, so "Bid due" / "Proposal Due Date" documents should surface.
