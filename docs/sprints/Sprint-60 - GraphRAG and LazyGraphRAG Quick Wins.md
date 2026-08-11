# Sprint 60 - GraphRAG and LazyGraphRAG Quick Wins

**Status:** Complete (2026-08-10)

Full authority: this file. Sprint 59 (Canonical Gate Softening, Multi-Theme, and Shared Theme Scope) is **committed and pushed** (`c151ea2` on `origin/master`).

Promotes backlog items 1, 4, 5, 6 from `docs/backlog/GraphRAG-LazyGraphRAG-Enhancements.md` (items 2 and 3 stay parked for a later focused sprint).

## Objective

Four small, high-value fixes to the GraphRAG / LazyGraphRAG retrieval paths, delivered in one pass because they all touch the same services:

1. **Per-request `GraphTraversalBudget`** — replace the shared-singleton budget with one constructed per `RetrieveAsync` call; make `LazyGraphRagService._indexedSources` thread-safe.
2. **Real token accounting + hard deadline** — wire SemanticKernel usage into `RecordTokens` (currently dead code) and enforce `CancellationTokenSource.CancelAfter(MaxExecutionTime)`.
3. **Stop noise-entity persistence** — do not persist `keyword` / `statistical-candidate` terms as graph nodes; keep them retrieval-only.
4. **Per-query retrieval trace** — expose LLM calls, tokens, nodes/edges traversed, pruning ratio, and which fallback strategy produced the answer.

## Background

- `GraphTraversalBudget` is registered as a singleton in `src/Repository.API/Program.cs` and mutated via `Reset()` inside `RetrieveAsync`; concurrent requests corrupt each other's budget.
- `RecordTokens` exists but is never fed real SemanticKernel usage; the 30s `MaxExecutionTime` budget is never enforced because no `CancellationTokenSource.CancelAfter` is wired.
- Entity-extraction LLM fallback and LazyGraphRAG statistical candidates persist `keyword` / `statistical-candidate` nodes into Neo4j, polluting the graph.
- `GraphRagService.RetrieveAsync` is a long fallback cascade; the fired path is opaque to operators and the Web UI.

## Deliverables

### 1. Per-request `GraphTraversalBudget`
- Remove the singleton registration; construct a fresh `GraphTraversalBudget` per `RetrieveAsync` call (and per LazyGraphRAG retrieval where it uses one).
- Make `LazyGraphRagService._indexedSources` thread-safe (lock or concurrent collection) so concurrent retrievals do not corrupt the corpus index.

### 2. Real token accounting + hard deadline
- Wire SemanticKernel `ChatResult`/`TextResult` usage (input/output tokens) into `RecordTokens` so the token budget is actually enforced.
- Add `CancellationTokenSource.CancelAfter(MaxExecutionTime)` around the LLM call chain so a single slow call cannot blow the budget.

### 3. Stop noise-entity persistence
- Do not persist `keyword` / `statistical-candidate` terms as graph nodes; keep them retrieval-only (in-memory for the request).

### 4. Per-query retrieval trace
- Expose a trace on the retrieval result: LLM calls made, tokens consumed, nodes/edges traversed, pruning ratio, and which fallback strategy produced the answer.
- Surface it through the existing RAGS/GraphRAG/LazyGraphRAG endpoints and the Web diagnostics without breaking the existing response contracts.

### 5. Tests
- RAGS.UnitTests: per-request budget isolation (concurrent retrievals do not share budget), token accounting wired, noise entities not persisted, retrieval trace populated.
- Existing suites (RAGS 251 / Repository 121 / Foundation 55) remain green; Web C#/Razor compiles.

### 6. Docs
- `docs/Architecture.md`, `docs/OperationsGuide.md`, `docs/Development-Guidelines.md`, AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, sprint handoff updated. Backlog item statuses updated.

## Acceptance Criteria

- Concurrent GraphRAG retrievals no longer corrupt each other's traversal budget; `_indexedSources` is safe under concurrency.
- Token budget is enforced from real SemanticKernel usage; a slow LLM call is cancelled at `MaxExecutionTime`.
- No new `keyword` / `statistical-candidate` nodes are persisted to the graph; existing behavior for retrieval is unchanged.
- Retrieval results carry a trace (LLM calls, tokens, traversed nodes/edges, pruning ratio, fired strategy) surfaced without breaking existing contracts.
- RAGS / Repository / Foundation suites green; Web C#/Razor compiles.

## Out of Scope

- Persisting the LazyGraphRAG corpus index to PostgreSQL (backlog item 2).
- Batch GraphRAG ingest / `UNWIND` writes / gated community re-clustering (backlog item 3).
- Theme-aware graph retrieval (Canonical backlog item 5).
- New queue providers, session stores, or database changes.

---

## Implementation Status (2026-08-07)

**Implementation complete.** All four deliverables implemented, tested, and documented. Pending: commit (requested by the user) and optional Docker smoke test.

### 1. Per-request `GraphTraversalBudget` — DONE
- `IGraphTraversalBudget` gained `CreatePerRequest()` (fresh budget with the same limits) and read-only counter properties (`LlmCalls`, `TokensConsumed`, `NodesVisited`, `RelationshipsTraversed`); `GraphTraversalBudget` implements them with `Volatile.Read`/`Interlocked`.
- `LazyGraphRagService`: the injected budget is now a **template** (`_budgetTemplate`, optional constructor param moved to the end); `RetrieveAsync` calls `CreatePerRequest()` per request. `_indexedSources` guarded by `lock (_indexedSourcesLock)` for thread-safe check-and-add.
- `GraphRagService.RetrieveAsync` constructs `new GraphTraversalBudget()` inline (defaults) per request.
- Removed the `AddSingleton<IGraphTraversalBudget>` registration from `src/Repository.API/Program.cs`.

### 2. Real token accounting + hard deadline — DONE
- New `TokenUsageHelper.GetTotalTokens(ChatMessageContent?)`: reads `ChatMessageContent.Metadata` with provider-agnostic input/output/total key sets (camelCase/PascalCase/snake_case) plus nested `"Usage"`, and reflects over provider-specific usage objects by property name (OpenAI `ChatTokenUsage` etc.) — no provider SDK references.
- Wired into `EntityExtractionService.DiscoverAsync` and `LazyRelationshipDiscoveryService.DiscoverAtQueryTimeAsync`: `budget?.RecordTokens(TokenUsageHelper.GetTotalTokens(response))`.
- **`RecordTokens` semantics fixed during testing**: it previously capped tokens at the budget, so `TokensConsumed` could never exceed `MaxTokenBudget` and `IsExceeded()` never fired from tokens — the token budget was dead code. It now **records actual consumption even when it breaches the budget** and returns `updated <= MaxTokenBudget`, so `IsExceeded()` halts traversal on the next check. (GraphTraversalBudgetTests updated to assert `TokensConsumed == 120` after a rejected 60+60 against a 100 budget.)
- Hard deadline: both `RetrieveAsync` paths use `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` + `CancelAfter(MaxExecutionTime)`; all LLM/traversal calls flow the deadline token `ct`.

### 3. Stop noise-entity persistence — DONE
- New `NoiseEntityFilter.IsNoise(ExtractedEntity)` / `IsNoise(string?)`: `keyword` + `statistical-candidate` (ordinal-ignore-case).
- Applied in: `LazyEntityDiscoveryService.PersistAsync`, `LazyGraphRagService.PersistDiscoveryAsync` (also drops relationships whose endpoints are noise entities), `GraphRagService.IngestAsync`, and `GraphRagService.EnsureQueryTimeEnrichmentAsync`. Noise entities stay in-memory for retrieval only.

### 4. Per-query retrieval trace — DONE
- New `RetrievalTrace` model (strategy, llm calls, tokens consumed, nodes visited, relationships traversed, pruning ratio, elapsed ms, ordered steps) + settable `SearchResult.Trace` (additive, non-breaking).
- `LazyGraphRagService.RetrieveAsync` reports real budget counters, `PruningRatio = pruned/traversal nodes`, and step labels (corpus-search → entity-discovery → relationship-discovery → graph-build → traversal → pruning → community-resolution → context-build → ranking).
- `GraphRagService.RetrieveAsync` reports an approximate `llmCalls` count, budget tokens (from lazy-enrichment extraction), and step labels (entity-resolution → community-resolution → summary-retrieval → context-build → summary-candidates → lazy-enrichment → graph-aware → semantic-retrieval → ranking). Per-call token accounting for GraphSummary/HierarchicalSummary/GraphReasoning/RelationshipExtraction is a documented follow-up.
- Web Search Center (`SearchCenter.razor`) renders the trace block on each result card: strategy, LLM calls, tokens, nodes, relationships, pruning retained %, elapsed ms, and the step chain.

### 5. Tests — DONE
- RAGS.UnitTests now **265 passed** (was 251). New: `GraphTraversalBudgetTests` (6: CreatePerRequest fresh limits, no shared counters, token breach, LLM-call limit, time exceeded, counter reflection), `LazyGraphRagServiceTests` (+3: per-request budget does not mutate template, 5 concurrent retrievals do not corrupt budgets, retrieval trace populated), `LazyEntityDiscoveryServiceTests` (+3: keyword skipped, statistical-candidate skipped, existing noise nodes not updated), `GraphRagServiceTests` (+2: keyword entities not persisted, retrieval trace populated). All existing mock doubles updated for the new `IGraphTraversalBudget? budget` parameters.
- Full suite: RAGS 265 / Repository 121 / Repository.IntegrationTests 8 / Foundation 55 **green**; `dotnet build Aletheia.slnx` succeeds. Web C#/Razor compiles clean (0 errors). Aletheia.Web.UnitTests had the same **6 pre-existing failures** (CopilotStateService session-key `v1` vs `v2`, RepositoryApiClientUploadTests x4, Wiki mode-buttons) — verified identical on a clean HEAD worktree, unrelated to Sprint 60; **all 6 fixed 2026-08-10 as stale tests** (fake `HttpClient` missing `BaseAddress`, intentional Sprint 58 storage-key `v1`→`v2` bump, WRAGS→Wiki button rename). Suite now 39 green.

### 6. Docs — DONE
- `docs/Architecture.md`, `docs/OperationsGuide.md`, `docs/Development-Guidelines.md`, AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, and this sprint file updated. Backlog items 1, 4, 5, 6 marked promoted/implemented.

## Remaining
- **Committed and pushed** as `c6c3e48` (2026-08-07).
- **Docker smoke test — DONE (2026-08-10).** See "Smoke Test Results" below. The only uncommitted change from the smoke test is the compose env mapping for the internal-search gate (`docker-compose.yml` + the matching note in `docs/OperationsGuide.md`) — commit decision pending.

## Smoke Test Results (2026-08-10)

Ran the Sprint 60 Docker smoke test against a rebuilt stack with `SHOW_INTERNAL_SEARCH=true` (new compose env mapping for `FeatureFlags:ShowInternalSearch`, default `false`; see `docker-compose.yml` + OperationsGuide). **All acceptance criteria verified end-to-end via the API:**

- **Retrieval trace present on every result.** LazyGraphRAG (`GET /api/lazygraphrag/retrieve`) returned 6 results, each with the full trace: strategy `lazy-semantic`, llmCalls 5, tokensConsumed 510, nodesVisited 3, relationshipsTraversed 2, pruningRatio 1.0 (100% retained), elapsedMs ~15210, and the 12-step chain (`corpus-search → entity-discovery → relationship-discovery → graph-build → traversal → pruning → community-resolution → context-build → ranking → ...`). GraphRAG (`GET /api/graphrag/retrieve`) returned 2 results with strategy `lazy-enrichment` (fallback — no community summaries yet), llmCalls 3, tokensConsumed 951, elapsedMs ~40690, step chain `entity-resolution → community-resolution → summary-retrieval → context-build → summary-candidates → lazy-enrichment`. Citations, scores, and ranks ride alongside the trace.
- **Per-request budgets under concurrency — no shared-singleton corruption.** 5 concurrent LazyGraphRAG retrievals all returned HTTP 200 with identical per-request traces (same query + corpus → same result; no cross-request budget bleed). `LazyGraphRagService._indexedSources` remained safe under the lock.
- **Hard deadline fires under LLM saturation — by design, but a UX rough edge.** 3 concurrent GraphRAG retrievals all returned HTTP 400 `Vector search failed. The operation was canceled.` Root cause: Ollama serializes calls to the cloud model, and GraphRAG's long cascade exceeds the 30s `CancelAfter(MaxExecutionTime)` before its final semantic fallback runs. This is the documented hard-timeout outcome ("a visible traversal-budget error indicates ... a hard timeout"); it fails the request outright rather than degrading to best-available. Tracked as backlog item 8 in `docs/backlog/GraphRAG-LazyGraphRAG-Enhancements.md` (soft deadline / best-partial result).
- **Reembed re-verified (slow path, succeeded).** `POST /api/jobs/rags/reembed` ran to **Succeeded 100%** (job `e9121da9`): all 3 registered documents re-embedded, 138 embedding chunks / 3 ingested sources / 3 registered docs, no error. It runs the **full** `UploadedContentKnowledgeIndexer.IndexAsync` (LLM-heavy: per-chunk entity discovery + node summaries + relationship extraction + community detection + community summaries) — ~100+ serial cloud LLM calls per document, so a 3-doc corpus took 40+ minutes. Uploads use `IndexLightweightAsync` by comparison. Tracked as backlog item 7 in `docs/backlog/GraphRAG-LazyGraphRAG-Enhancements.md` (reembed indexer parity).
- **Browser confirmation pending (user).** Hard-refresh `http://localhost:8081`, open Search Center with operator controls enabled (`SHOW_INTERNAL_SEARCH=true`), run GraphRAG / LazyGraphRAG searches, and eyeball the per-card trace block. API-side traces are verified; the Web render is the last visual check.
