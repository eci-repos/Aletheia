# Sprint 51 - Deterministic Source-Scoped Retrieval Enforcement

**Status:** Active (implementation complete; pending end-to-end Copilot verification)

## Objective

Eliminate cross-document mixing in Copilot answers by enforcing source scoping **deterministically** in `ChatExecutionEngine`, independent of planner/model behavior. A question that names a specific document (e.g., "CMP 2026 RFP") must retrieve only from that document, and a collection/summary question (e.g., "summary of CMP projects") must retrieve each matching document independently.

## Background

Reported defect: with two uploaded CMP RFP documents ("CMP 2022 - 3. RFP Analysis.docx", "CMP 2026 - 3. RFP Analysis.docx"), Copilot:

1. "provide project details about CMP 2026 RFP" → returned 2022 details.
2. "provide summary of CMP projects" → returned the 2026 title with 2022 data.

Diagnosis (`check-cmp-source.ps1`): storage is correct - each document has a distinct `source_id` in `embeddings` (53 chunks each) and correct per-document GraphRAG community summaries. The mixing happens at retrieval/synthesis:

- `IsMetadataCandidate` is a coarse boolean that matches **any** RFP-ish file name, so "CMP 2026" resolves **both** CMP files.
- The default `Retrieval` path (`RunRagsRetrieveAsync`) performs an **unscoped global top-k search** across all documents; the two near-identical RFPs blend (2026 title/metadata + 2022 chunks).
- Collection prompts that are not RFP-flavored (e.g., "summary of CMP projects") never enter the existing scoped-collection path (`IsScopedCollectionPrompt` requires RFP/requirements wording).

## Authority

The repository is the source of truth. Changes must:
- Honor the **Singleton Registration Rule** (no new scoped services depending on singletons; all logic stays inside the already-singleton `ChatExecutionEngine`).
- Use the **Result<T> pattern** for any new service returns (none required - this sprint is engine-internal).
- Keep behavior backward-compatible for existing tests (RFP collection prompts still resolve all RFP files; generic "what is an RFP" stays unscoped).

## Deliverables

1. **Token-weighted source matching** (`ResolvePromptSourceScopeAsync`):
   - Extract significant prompt terms (length >= 3, including years like 2026; drop generic words: provide, about, summary, project, rfp, ...).
   - Score each registered file name by token overlap (file name token = 2, tag token = 1).
   - **Single-source scope**: top score >= 2 and strictly greater than runner-up.
   - **Multi-source scope**: collection intent (summary/list/all/each/overview/past/last/registered/projects/opportunities) with >= 2 matched sources.
   - **Generic RFP collection fallback**: no significant terms but RFP intent + collection intent → all RFP-ish registered files (preserves current behavior for "Provide a summary list of all RFPs.").
   - No match → `null` (unscoped, current behavior).

2. **Enforce scoping in the RAGS retrieval paths** (`RunRagsRetrieveAsync`, `RunFastPathAsync`, `RunSmallCorpusRetrieveAsync`) via shared `TrySourceScopedRetrievalAsync`:
   - Single source → `RetrievalRequest(query, topK, sourceId)` (source-scoped; no cross-document mixing).
   - Multi-source → per-source bounded retrieval merged (reuse `RetrieveScopedCollectionResultsAsync`).
   - None → existing unscoped retrieval.

3. **Source-first routing for broad modes** (`CorpusAnalysis` / `TimelineAnalysis`): if the prompt resolves to registered source(s), use scoped RAGS retrieval instead of corpus-global GraphRAG search; otherwise keep global search.

4. **Improve `ResolveFallbackSourcesAsync`** to use the same weighted matcher (replaces the boolean `IsMetadataCandidate` gate in tool/fallback paths; `IsMetadataCandidate` remains only for the generic RFP-collection fallback).

5. **Tests** (RAGS.UnitTests):
   - `Engine_source_scoped_single_document_retrieval_for_year_qualified_prompt` - "CMP 2026 RFP" issues a source-scoped request for the 2026 file only (0 broad calls, 1 scoped call; captured request.SourceId == 2026 id).
   - `Engine_per_source_retrieval_for_project_summary_prompt` - "provide summary of CMP projects" retrieves both CMP files independently (2 distinct source ids in synthesis context).
   - `Engine_generic_rfp_prompt_remains_unscoped` - "what is an RFP" stays on the broad path.
   - `Engine_corpus_analysis_routes_to_scoped_rags_when_source_is_named` - broad mode with a named year routes to scoped RAGS (no global search).

6. **Docs**: update `docs/File 03-openhands.md`/orchestration playbook note that source scoping is now enforced at the engine (no longer model-discretion), and record the fix in `docs/sprints/Sprint-51 - ...md`.

## Requirements (Detailed)

### `ResolvePromptSourceScopeAsync`

```csharp
private sealed record PromptSourceScope(IReadOnlyList<KnowledgeSource> Sources, bool IsSingle);

private async Task<PromptSourceScope?> ResolvePromptSourceScopeAsync(string query, CancellationToken cancellationToken)
```

- Returns `null` when `_metadataRepository` is null or no significant terms.
- Queries `SearchRequest(null, 1, 200)` (same as existing fallback resolution).
- Scoring and single/multi decision per Deliverables item 1.

### `TrySourceScopedRetrievalAsync`

```csharp
private async Task<IReadOnlyList<SearchResult>?> TrySourceScopedRetrievalAsync(
    ChatJobWorkItem item, ChatJobState state, int topK, CancellationToken cancellationToken,
    PromptSourceScope? preResolvedScope = null)
```

- Returns `null` when no scope applies (caller falls through to unscoped retrieval).
- Single: `RetrieveWithQueryVariantsAsync(item.JobId, state, BuildMandatoryFallbackQueries(item.Prompt), topK, source.SourceId, ct)`.
- Multi: `RetrieveScopedCollectionResultsAsync(item.JobId, state, item.Prompt, topK, scope.Sources, Array.Empty<SearchResult>(), ct)`.
- Emits progress messages stating the resolved source name(s).

### Integration points

- `RunRagsRetrieveAsync`, `RunFastPathAsync`, `RunSmallCorpusRetrieveAsync`: attempt scoped retrieval first, then unscoped.
- Mode switch (`CorpusAnalysis`, `TimelineAnalysis`): `RunSourceAwareBroadAnalysisAsync` (scoped RAGS when source resolves, else `RunGlobalSearchAsync`).
- `ResolveFallbackSourcesAsync`: replace boolean gate with `ResolvePromptSourceScopeAsync` (+ generic RFP collection fallback).

## Acceptance Criteria

- "CMP 2026 RFP" retrieves only the 2026 document (no 2022 chunks in context).
- "summary of CMP projects" retrieves both documents independently with separate source sections.
- "what is an RFP" (no distinguishing tokens) remains unscoped.
- Existing RAGS.UnitTests pass (no regressions in RFP collection scenarios).
- `dotnet build Aletheia.slnx` and `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` succeed.


## Execution Status (2026-08-03)

Implemented and verified:

- `ChatExecutionEngine` now enforces source scoping in the tool path (`InvokeToolCoreAsync`), the default RAGS path (`RunRagsRetrieveAsync`), `RunFastPathAsync`, `RunSmallCorpusRetrieveAsync`, and broad modes (`RunSourceAwareBroadAnalysisAsync`), using the new token-weighted `ResolvePromptSourceScopeAsync` matcher.
- `ResolveFallbackSourcesAsync` uses the weighted matcher; generic RFP-collection resolution is preserved.

Tests (all green):

- RAGS.UnitTests: 200/200 passed (includes 3 new Sprint 51 tests: year-qualified single-document scoping, per-source retrieval for project summaries, generic RFP prompt stays unscoped).
- Aletheia.Foundation.UnitTests: 55/55.
- Repository.UnitTests: 91/91.

Pre-existing fixes applied during execution:

- `ConfigurableTermNormalizer.LoadPhrasesFromTemplates` used a fixed relative path that never resolved `docs/doc-templates`; replaced with an upward directory search (`LocateDocTemplatesFolder`). Fixed `Normalize_Preserves_Phrase_Exemption`.
- `UploadedContentKnowledgeIndexerTests` constructor call updated for the Sprint 50 `ITermNormalizer` parameter; SQL expectations updated to normalized lowercase tags (`rfp`).
- API Dockerfile now copies `docs/doc-templates` into the image so phrase exemptions work at container runtime.

Environment notes:

- Host SDK 10.0.302 was repaired via `fix-dotnet-sdk.ps1` (recreated the two missing workload locator SDK folders with placeholder import files inside each `Sdk` subfolder).
- In sandboxed shells, builds need `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, and `-m:1`; the Blazor WASM `ComputeWasmBuildAssets` task cannot spawn its out-of-proc task host in the sandbox (Aletheia.Web C# compiles cleanly via `-t:CoreCompile`). Full web builds should succeed on a normal machine or in Docker.
