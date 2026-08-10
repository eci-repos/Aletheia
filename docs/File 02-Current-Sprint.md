# Sprint 60 - GraphRAG and LazyGraphRAG Quick Wins

**Status:** Active

Full authority: `docs/sprints/Sprint-60 - GraphRAG and LazyGraphRAG Quick Wins.md` (created 2026-08-07). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 59 (Canonical Gate Softening, Multi-Theme, and Shared Theme Scope) is **complete, committed, and pushed**: commit `c151ea2` is on `origin/master` (HEAD `c151ea2`).

## Objective

Four small, high-value fixes to the GraphRAG / LazyGraphRAG retrieval paths, delivered in one pass because they all touch the same services:

1. **Per-request `GraphTraversalBudget`** — replace the shared-singleton budget with one constructed per `RetrieveAsync` call; make `LazyGraphRagService._indexedSources` thread-safe.
2. **Real token accounting + hard deadline** — wire SemanticKernel usage into `RecordTokens` (currently dead code) and enforce `CancellationTokenSource.CancelAfter(MaxExecutionTime)`.
3. **Stop noise-entity persistence** — do not persist `keyword` / `statistical-candidate` terms as graph nodes; keep them retrieval-only.
4. **Per-query retrieval trace** — expose LLM calls, tokens, nodes/edges traversed, pruning ratio, and which fallback strategy produced the answer.

## Authorized Work (summary - see sprint file for details)

1. **Per-request budget**: remove the `GraphTraversalBudget` singleton; construct per `RetrieveAsync`; make `_indexedSources` thread-safe.
2. **Token accounting + deadline**: wire SemanticKernel usage into `RecordTokens`; `CancelAfter(MaxExecutionTime)` on the LLM call chain.
3. **Noise entities**: stop persisting `keyword` / `statistical-candidate` graph nodes; keep them retrieval-only.
4. **Retrieval trace**: expose LLM calls, tokens, traversed nodes/edges, pruning ratio, fired strategy on the retrieval result without breaking existing contracts.
5. **Tests**: RAGS (budget isolation, token accounting, noise not persisted, trace populated); existing suites green; Web C#/Razor compiles.
6. **Docs**: Architecture, OperationsGuide, Development-Guidelines, AGENTS, File 02/03, handoff; backlog statuses updated.

## Acceptance Criteria

- Concurrent GraphRAG retrievals no longer corrupt each other's traversal budget; `_indexedSources` is safe under concurrency.
- Token budget is enforced from real SemanticKernel usage; a slow LLM call is cancelled at `MaxExecutionTime`.
- No new `keyword` / `statistical-candidate` nodes are persisted to the graph; retrieval behavior unchanged.
- Retrieval results carry a trace (LLM calls, tokens, traversed nodes/edges, pruning ratio, fired strategy) surfaced without breaking existing contracts.
- RAGS / Repository / Foundation suites green; Web C#/Razor compiles.

## Out of Scope

- Persisting the LazyGraphRAG corpus index to PostgreSQL (backlog item 2); batch GraphRAG ingest (backlog item 3); theme-aware graph retrieval (Canonical backlog item 5); new queue providers, session stores, or database changes.

---

## Progress (2026-08-07)

### Sprint 59 (completed) — Canonical Gate Softening, Multi-Theme, and Shared Theme Scope

Committed and pushed as `c151ea2`. Full details below for reference.

### Implementation complete — all four deliverables implemented, tested, documented

**Backend (1-3):**
- Migration `2026-08-07-file-metadata-template-status-themes.sql` + `init.sql`: `template_status TEXT`, `theme` -> `text[]` (GIN index), `idx_file_metadata_template_status`.
- Softened gate: no matching template => persist `Uncategorized` and continue ingestion; briefs only for `Canonical`.
- Multi-theme: `TryGetThemes`, `ResolveSourceIdsAsync` match-any, per-theme counts, `text[]` persistence.
- Backfill/promotion: `TemplateReevaluationService` + `GET /api/knowledge/uncategorized` + `POST /api/knowledge/reevaluate`.
- `GET /api/rags/retrieve?themes=` resolves theme set -> `SourceIds`.
- Diagnostics: template-gate-skip counters repurposed to `UncategorizedIngestCount`/`UncategorizedIngests`.

**Web (4):**
- `SearchScopeStateService` (localStorage scope), Search Center theme filter chips + "Scoped to N themes" indicator (semantic only), admin uncategorized list + re-evaluate panel.
- `RepositoryApiClient`: `themes=` param, `GetUncategorizedAsync`, `ReevaluateTemplatesAsync`.

**Verification:**
- RAGS.UnitTests 251 passed / Repository.UnitTests 121 passed / Foundation.UnitTests 55 passed / `dotnet build Aletheia.slnx` succeeds.
- Aletheia.Web.UnitTests 33 passed / 6 failed — **pre-existing failures** (verified identical on clean `4fdfaf0` via `git stash`); unrelated to Sprint 59 (UploadAsync, Copilot page/state), tracked for a separate fix.

**Docs:** Architecture / AdministratorGuide / OperationsGuide / Development-Guidelines / user guide (04/05/07) / AGENTS / File 02/03 / handoff updated; backlog item statuses updated.

**Remaining:** Docker smoke test (optional, can run in parallel — see Sprint 59 sprint file).

### Post-implementation chat fix (2026-08-07)

Smoke-test report "Chat does not work at all" traced to the Copilot restore path: after a page reload the Web page restored a pending plan and polled `GET /api/copilot/plans/{id}/progress`, which returned **404** for a plan with no execution job yet — the client then polled every 2s **forever**. Fixed:

- API `GetPlanProgress`: a plan without an execution job now returns **200** with `JobId = Guid.Empty` (not-started state) instead of 404; "plan not found" still 404s.
- Web `Index.razor`: the polling loop treats `JobId == Guid.Empty` as "not started" — clears stale restored execution state, keeps the plan preview so **Run** works — and stops after 3 consecutive no-progress polls instead of looping indefinitely (covers API restarts where in-memory chat plans/jobs are lost).

Verified end-to-end via curl (plan → progress-before-execute 200/empty jobId → approve → execute → job completes with an answer); RAGS 251 / Repository 121 / Foundation 55 green; Web.UnitTests still the same 6 pre-existing failures. Containers rebuilt. **Browser action required: hard refresh (Ctrl+F5)** to load the new WASM bundle.

### Post-implementation graph UX fix (2026-08-07)

Smoke-test feedback: the Graph Explorer "jumps around" while the layout runs and gives no feedback, so users press buttons and think it is running wild. Fixed with two coordinated changes:

- **Visible "preparing graph" state**: `GraphExplorer.razor` now shows a spinner + staged status line over the canvas ("Loading graph…" → "Loading edges…" → "Rendering layout…") while the graph loads and lays out. The Refresh / Import / Fit / Re-layout / Spread / Find Path buttons are disabled during the load so the user cannot trigger more work mid-render. The overlay clears when the layout settles.
- **Render once, don't re-layout**: `window.initGraph` now accepts a `dotNetRef` + `preservePositions` flag. On scope changes (context selection, chunk toggle) the page re-renders the graph but keeps existing node positions (`randomize: false`) instead of re-running the randomized `cose` layout, so the view no longer jumps around. The JS hooks `layoutstop` to invoke `OnGraphLayoutSettled` on the page, which clears the loading overlay.

Contract: `initGraph(containerId, nodes, edges, dotNetRef, preservePositions)`; the page owns a `DotNetObjectReference<GraphExplorer>` (disposed in `Dispose`) and exposes `[JSInvokable] OnGraphLayoutSettled()`. Web project builds clean (0 warnings/errors).

### Sprint 60 implementation status (2026-08-07) — all four deliverables implemented

**1. Per-request `GraphTraversalBudget`**
- `IGraphTraversalBudget` gained `CreatePerRequest()` + read-only counters (`LlmCalls`, `TokensConsumed`, `NodesVisited`, `RelationshipsTraversed`); `GraphTraversalBudget` implements via `Volatile`/`Interlocked`.
- `LazyGraphRagService` keeps the injected budget as a **template** (`_budgetTemplate`, optional ctor param moved to the end) and calls `CreatePerRequest()` per `RetrieveAsync`; `_indexedSources` guarded by `lock (_indexedSourcesLock)`.
- `GraphRagService.RetrieveAsync` constructs `new GraphTraversalBudget()` inline per request; the `AddSingleton<IGraphTraversalBudget>` in `Repository.API/Program.cs` was removed.

**2. Real token accounting + hard deadline**
- `TokenUsageHelper.GetTotalTokens(ChatMessageContent?)` reads `Metadata` with provider-agnostic key sets (input/output/total, camel/Pascal/snake_case) + nested `"Usage"` + reflection over provider usage objects (no provider SDK refs).
- Wired into `EntityExtractionService.DiscoverAsync` and `LazyRelationshipDiscoveryService.DiscoverAtQueryTimeAsync` (`budget?.RecordTokens(...)`).
- **`RecordTokens` semantics corrected during testing**: it previously capped tokens, so the token budget never fired `IsExceeded()` (dead code). It now records actual consumption even past the budget and returns `updated <= MaxTokenBudget`, so `IsExceeded()` halts traversal (test asserts 120 recorded against a 100 budget).
- Both `RetrieveAsync` paths: `CreateLinkedTokenSource(cancellationToken)` + `CancelAfter(MaxExecutionTime)`; all LLM/traversal calls flow `ct`.

**3. Stop noise-entity persistence**
- `NoiseEntityFilter.IsNoise` (`keyword` + `statistical-candidate`); applied in `LazyEntityDiscoveryService.PersistAsync`, `LazyGraphRagService.PersistDiscoveryAsync` (also drops relationships with noise endpoints), `GraphRagService.IngestAsync`, `GraphRagService.EnsureQueryTimeEnrichmentAsync`. Noise entities stay retrieval-only.

**4. Per-query retrieval trace**
- New `RetrievalTrace` model + settable `SearchResult.Trace` (additive). LazyGraphRAG reports real budget counters + pruning ratio + steps; GraphRAG reports approximate `llmCalls` + budget tokens + steps (per-call token accounting for summary/reasoning services is a documented follow-up).
- Web Search Center renders the trace block per result card (strategy, LLM calls, tokens, nodes, relationships, pruning retained %, elapsed ms, step chain).

**Verification**
- RAGS.UnitTests **265 passed** (was 251): new GraphTraversalBudgetTests (6), LazyGraphRagServiceTests (+3: per-request budget isolation, 5 concurrent retrievals, trace), LazyEntityDiscoveryServiceTests (+3 noise), GraphRagServiceTests (+2: noise not persisted, trace). All mocks updated for the new `IGraphTraversalBudget? budget` params.
- `dotnet build Aletheia.slnx` succeeds (pre-existing AngleSharp NU1902 warning only). RAGS 265 / Repository 121 / Repository.IntegrationTests 8 / Foundation 55 green. Web compiles clean.
- Aletheia.Web.UnitTests had the same **6 pre-existing failures** (CopilotStateService session-key `v1` vs `v2`, RepositoryApiClientUploadTests x4, Wiki mode-buttons) — verified identical on a clean HEAD worktree; unrelated to Sprint 60. **Fixed 2026-08-10** (see below).

**Remaining:** commit when the user requests; optional Docker smoke test.

### Post-implementation web-test fix (2026-08-10)

The 6 pre-existing `Aletheia.Web.UnitTests` failures are fixed — all were **stale tests, no code regressions** (verified against the production wiring and git history):

- `RepositoryApiClientUploadTests` ×4 — the test harness built `new HttpClient(handler)` with no `BaseAddress`, but `UploadAsync` posts a relative `/api/files/upload`; production always sets `BaseAddress` via `ConfigureRepositoryApi` (`Program.cs:27`). The fake now sets `BaseAddress = new Uri("http://localhost")` in `CreateClient`.
- `CopilotStateServiceTests.ClearAsync` — asserted storage key `v1`; the key was **intentionally** bumped to `v2` in `dfc9d1b` (Sprint 58 session theme filtering, serialized session shape changed). Test now asserts `v2`.
- `CopilotIndexBindingTests.Wiki_shows_all_rags_mode_buttons` — asserted a `>WRAGS</button>` button; the wiki's internal `wrags` mode was renamed to the user-facing `>Wiki</button>` label in Sprint 55. Test now asserts `>Wiki</button>`.

**Verification:** Aletheia.Web.UnitTests **39 passed** (was 33/6); full solution build 0 errors; RAGS 265 / Repository 121 / Foundation 55 green. `Repository.IntegrationTests` (8) not run — PostgreSQL container not up (needs live PG + Neo4j). Committed with the Sprint-16 sprint-file filename normalization (space → dash).
