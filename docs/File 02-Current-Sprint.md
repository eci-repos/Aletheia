# Sprint 61 - Chat Approval Prompt and Admin Settings

**Status:** Active

Full authority: `docs/sprints/Sprint-61 - Chat Approval Prompt and Admin Settings.md` (created 2026-08-10). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 60 (GraphRAG and LazyGraphRAG Quick Wins) is **complete, committed, and pushed**: commit `c6c3e48` is on `origin/master`; its optional Docker smoke test remains as a parallel verification task.

## Objective

Fix the Copilot chat approval flow so the plan-approval prompt is never hidden, then give users and admins first-class control over when approval is required:

1. **Modal approval prompt (visibility fix)** — render the plan preview in a centered modal overlay above the Activity/Chats panels so a submitted prompt always surfaces its approval request; auto-expand a collapsed Execution column on submit.
2. **Server-side settings foundation** — `app_settings` / `user_settings` tables + migration + `init.sql`; singleton `SettingsService`; `GET/PUT /api/settings` (admin) and `GET/PUT /api/settings/me` (authenticated).
3. **Chat approval preference** — `copilot.requireApproval`, per-user, **default true**; "Don't ask again" checkbox on the modal writes the preference; when off, plans auto-approve and execute immediately.
4. **Admin override for approval** — admin-managed global/role setting that forces approval even for opted-out users.
5. **Admin Settings page** — `/settings` gated to Administrator, listing global settings with edit controls; users see their own editable preferences.

## Authorized Work (summary - see sprint file for details)

1. **Modal approval prompt**: render `PlanPreview` in a centered modal overlay (z-index above the Activity/Chats panels); auto-expand a collapsed Execution column on submit; keep the in-context plan preview in the column.
2. **Settings foundation**: `app_settings`/`user_settings` tables + idempotent migration + `init.sql`; singleton `SettingsService` with typed accessors + caching; `GET/PUT /api/settings` (admin) and `GET/PUT /api/settings/me` (authenticated).
3. **Approval preference**: `copilot.requireApproval` per-user, default true; "Don't ask again" checkbox on the modal; auto-approve + execute when off.
4. **Admin override**: admin-managed global/role setting forcing approval for opted-out users.
5. **Admin Settings page**: `/settings` Administrator-gated (Governance pattern) + admin NavMenu entry; users see their own preferences.
6. **Tests**: Web (modal markup/binding, settings service, preference), API (settings endpoints); existing suites green.
7. **Docs**: Architecture, OperationsGuide, Development-Guidelines, AGENTS, File 02/03, handoff; backlog statuses updated.

## Acceptance Criteria

- With the Activity or Chats panel open, submitting a chat prompt shows the approval prompt in a modal above the panels; the user can Run/Revise/Cancel.
- A collapsed Execution column auto-expands on submit; progress remains visible after approval.
- Settings foundation: `app_settings`/`user_settings` tables exist (migration + `init.sql` in sync); `SettingsService` caches; admin and per-user endpoints work.
- `copilot.requireApproval` defaults true; the modal's "Don't ask again" persists the preference; opting out auto-approves and executes.
- Admin override forces approval for opted-out users.
- `/settings` is Administrator-gated; users see their own preferences.
- RAGS / Repository / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Persisting the LazyGraphRAG corpus index to PostgreSQL (GraphRAG backlog item 2); batch GraphRAG ingest (GraphRAG backlog item 3); theme-aware graph retrieval (Canonical backlog item 5).

---

## Progress

### Sprint 61 item 1 — modal approval prompt (2026-08-10)

**Implemented.** The plan-approval prompt is no longer hidden behind the Activity/Chats panels or a collapsed Execution column:

- `Index.razor` renders `PlanPreview` inside a centered modal overlay (`.copilot-approval-backdrop` / `.copilot-approval-modal`, `z-index: 1050` — above the panels' `20`/`21`) whenever a plan is awaiting approval/run (`IsPlanPreviewVisible && _pendingPlan?.Status == ChatPlanStatus.Proposed`). The modal reuses the existing `PlanPreview` component (Run/Revise/Cancel), so there is no duplicated markup; the in-context plan preview stays in the Execution column.
- `SendChat()` now auto-expands a collapsed Execution column on submit, so the approval prompt and later progress are always visible.
- CSS added to `Index.razor.css` (fixed backdrop, centered card, `max-height` + scroll).

**Verification:** `dotnet build src/Aletheia.Web/Aletheia.Web.csproj` 0 warnings/0 errors; Aletheia.Web.UnitTests 39/39 green (binding tests still pass). Full solution build + unit suites below.

---

## Sprint 59/60 progress log (2026-08-07)

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
