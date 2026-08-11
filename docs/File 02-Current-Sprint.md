# Sprint 64 - Theme-Aware Graph Retrieval

**Status:** Active (2026-08-11)

Full authority: `docs/sprints/Sprint-64 - Theme-Aware Graph Retrieval.md` (created 2026-08-11). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 63 (Persisted LazyGraphRAG Corpus Index and Batch GraphRAG Ingest) is **complete, committed, and pushed** (`df7627d` on `origin/master`). Sprint 62 (GraphRAG Soft Deadline and Reembed Parity) is **complete** (`26995d9`); its optional Docker smoke test (reembed speed + `semantic-timeout-fallback` trace under LLM saturation) is user-side and can run in parallel.

## Objective

Extend theme enforcement to the GraphRAG / LazyGraphRAG retrieval paths and to global (community-summary) search. Today theme filtering works only on the semantic RAG path: `RetrievalRequest.SourceIds` → PgVectorStore set predicate. Graph modes are explicitly out of scope for theme filtering (Sprint 58 boundary), so a user scoping Search Center to a theme gets theme-filtered semantic results but unfiltered GraphRAG / LazyGraphRAG / global-graph results — an inconsistent view of the estate.

## Authorized Work (summary - see sprint file for details)

1. **Theme scope on graph retrieval:** optional `IReadOnlyList<Guid>? sourceIds = null` (after `cancellationToken`) on `IGraphRagService.RetrieveAsync` / `GlobalSearchAsync`, `ILazyGraphRagService.RetrieveAsync` / `GlobalSearchAsync`, and `IGlobalGraphSearchService.SearchAsync`. New `GraphThemeScope` static helper (resolve a node's source id; filter nodes/communities to an allowlist). `GraphRagService` filters resolved entities + expansion nodes and scopes semantic fallback / expansion `RetrievalRequest`s; `LazyGraphRagService` filters corpus seed sources and scopes fallback / expansion requests; `GlobalGraphSearchService` filters communities via a node→source map (match-any semantics).
2. **API + Web wiring:** `GraphRagController` / `LazyGraphRagController` accept `?themes=` (comma-separated) on `Retrieve` and `GlobalSearch`, resolve via optional `IKnowledgeThemeService? themeService = null` ctor param (RagsController pattern), pass `sourceIds` through. `RepositoryApiClient.GraphRagRetrieveAsync` / `LazyGraphRagRetrieveAsync` accept themes and append `&themes=`. `SearchCenter.razor` passes `_selectedThemes` to graph-mode retrieve calls.
3. **Tests**: RAGS (theme-scoped GraphRAG retrieval filters entities/communities; theme-scoped LazyGraphRAG retrieval filters corpus seeds; `GlobalGraphSearchService` theme-scoped search filters communities; controllers pass themes through). Existing suites green.
4. **Docs**: Architecture, OperationsGuide, Development-Guidelines, AGENTS, File 02/03, sprint file; backlog item 5 status updated.

## Acceptance Criteria

- A Search Center theme scope filters GraphRAG / LazyGraphRAG retrieval results and global-graph (community summary) results to documents in the selected themes.
- No theme scope → behavior identical to pre-Sprint-64 (graph modes unfiltered).
- Semantic fallback and entity-expansion `RetrievalRequest`s inside graph retrieval carry the same `sourceIds` scope, so a graph query that degrades to semantic retrieval stays theme-scoped.
- Repository / RAGS / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- New theme model changes (multi-theme, backfill — already done in Sprint 59).
- Copilot session theme filter changes (already theme-scoped via `RetrievalRequest.SourceIds`).
- Wiki / WRAGS theme scoping (Wiki stays curated).
- Changing the per-request traversal budget, token accounting, or soft-deadline behavior (Sprint 60/62).

---

## Progress

### Sprint 64 — theme-aware graph retrieval (2026-08-11)

**Implemented, committed, and pushed.** See the sprint file "Implementation Status" for full detail:

- **Item 1 (theme scope on graph retrieval):** `sourceIds` params on all three graph services; `GraphThemeScope` helper (`TryGetSourceId`, `IsInScope`, `FilterNodes`, `ToAllowSet`, `CommunityHasMemberInScope`); `GraphRagService` filters resolved entities + multi-hop expansion nodes and scopes semantic fallback / entity-expansion `RetrievalRequest`s; `LazyGraphRagService` filters corpus seed sources and scopes fallback / expansion requests; `GlobalGraphSearchService` builds a node→source map via `IGraphProvider.GetNodesAsync()` and filters communities with match-any semantics (returns `Failure("No communities in the selected themes.")` when scoped and empty).
- **Item 2 (API + Web wiring):** `?themes=` on both graph controllers' `Retrieve` + `GlobalSearch`; `RepositoryApiClient` appends `&themes=`; `SearchCenter.razor` passes `_selectedThemes` to graph-mode retrieve calls (WRAGS note now reads "Theme scope does not apply to WRAGS search.").
- **Item 3 (tests):** RAGS 289 (+8) — theme-scoped entity/community filtering, corpus-seed filtering, source-id flow to semantic fallback, controller themes pass-through. All fakes updated for the new signatures.

**Verification:** RAGS 289 / Repository 130 / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds (only pre-existing AngleSharp NU1902 warning).

---

## Sprint 63 progress log (2026-08-11) — completed

### Sprint 63 items 1 + 2 — corpus index persistence + batch ingest (2026-08-11)

**Implemented, committed, and pushed (`df7627d`).** See the Sprint 63 sprint file "Implementation Status" for full detail:

- **Item 1 (corpus index persistence):** `ICorpusIndexRepository` → `PostgreSqlCorpusIndexRepository` (Dapper, `lazygraphrag_corpus_documents` + `lazygraphrag_corpus_terms`, migration `2026-08-11-lazygraphrag-corpus-index.sql` + `init.sql` in sync); `CorpusDiscoveryIndex` loads the persisted corpus at startup and persists write-through (best-effort — a persistence failure never fails ingestion); `AddSingleton<ICorpusIndexRepository, PostgreSqlCorpusIndexRepository>()` in `Program.cs`.
- **Item 2 (batch ingest):** `IGraphProvider` batch methods (`CreateNodesAsync`/`CreateRelationshipsAsync`/`UpdateNodesAsync`, default interface impls fall back to per-item calls so existing fakes keep compiling); `Neo4jGraphProvider` UNWIND implementations grouped by label/type; both full-ingest paths (`UploadedContentKnowledgeIndexer.PersistGraphIntelligenceAsync` + `GraphRagService.IngestAsync`) refactored into 4 phases with `SemaphoreSlim(MaxLlmConcurrency = 4)`; community re-clustering gated on `!sourceExists`.

**Verification:** RAGS 281 (+9) / Repository 130 / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds. Optional Docker smoke test (restart corpus survival + batched-write ingest) is user-side.

---

## Sprint 62 progress log (2026-08-11) — completed

### Sprint 62 items 1 + 2 — reembed parity + soft deadline (2026-08-11)

**Implemented, committed, and pushed (`26995d9`).** See the Sprint 62 sprint file "Implementation Status" for full detail:

- **Item 1 (reembed parity):** `KnowledgeIndexMode` enum (`Full`/`Lightweight`) in `RAGS.Abstractions.Models`; `EnsureIngestedAsync` takes `mode = Full` and branches to `IndexLightweightAsync` when Lightweight; `RunReembedJobAsync` passes `Lightweight` (repair/chat keep `Full`).
- **Item 2 (soft deadline):** `GraphRagService.RetrieveAsync` catch distinguishes deadline-fires from caller-cancel — deadline degrades to best-effort semantic retrieval under a ~10s secondary deadline, returning Success with trace strategy `semantic-timeout-fallback` + steps `deadline-exceeded`/`semantic-fallback`; caller-cancel and other exceptions still fail. Optional `budgetFactory` ctor param for tests.

**Verification:** Repository 130 (+1) / RAGS 272 (+2) / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds. Optional Docker smoke test (reembed speed + `semantic-timeout-fallback` trace under LLM saturation) is user-side.

---

## Sprint 61 progress log (2026-08-10/11) — completed

### Sprint 61 item 1 — modal approval prompt (2026-08-10)

**Implemented.** The plan-approval prompt is no longer hidden behind the Activity/Chats panels or a collapsed Execution column:

- `Index.razor` renders `PlanPreview` inside a centered modal overlay (`.copilot-approval-backdrop` / `.copilot-approval-modal`, `z-index: 1050` — above the panels' `20`/`21`) whenever a plan is awaiting approval/run (`IsPlanPreviewVisible && _pendingPlan?.Status == ChatPlanStatus.Proposed`). The modal reuses the existing `PlanPreview` component (Run/Revise/Cancel), so there is no duplicated markup; the in-context plan preview stays in the Execution column.
- `SendChat()` now auto-expands a collapsed Execution column on submit, so the approval prompt and later progress are always visible.
- CSS added to `Index.razor.css` (fixed backdrop, centered card, `max-height` + scroll).

**Verification:** `dotnet build src/Aletheia.Web/Aletheia.Web.csproj` 0 warnings/0 errors; Aletheia.Web.UnitTests 39/39 green (binding tests still pass). Committed `4d10561`.

### Sprint 61 items 2+3+4 — settings foundation + approval preference + admin override (2026-08-10)

**Implemented and pushed (`793fc52`).** See the sprint file for full detail:

- **Item 2 (settings foundation):** `app_settings` + `user_settings` tables in `init.sql` + idempotent migration `2026-08-10-app-user-settings.sql`; `ISettingsRepository` → `PostgreSqlSettingsRepository` (Dapper `ON CONFLICT` upsert) → `ISettingsService` → `SettingsService` (singleton, in-memory caching, typed `GetBool/SetBool`); `GET/PUT /api/settings` (Administrator) + `/api/settings/me` (authenticated), caller id from JWT `NameIdentifier`.
- **Item 3 (approval preference):** `copilot.requireApproval` per-user, default true; modal "Don't ask again" checkbox writes the preference; when off the client auto-approves + executes (`SendChat` → `ApprovePlan`).
- **Item 4 (admin override):** `copilot.requireApproval.force` global (default false) forces approval for opted-out users, never for non-expensive plans. Keys in `Aletheia.RAGS.Abstractions.Configuration.ChatApprovalSettings`. `ChatPlanApprovalService.CreatePlanAsync` takes the caller's userId and applies `base && (userPrefersApproval || adminOverride)`.

**Verification:** RAGS 270 / Repository 129 / Web 44 / Foundation 55 green; build succeeds.

### Sprint 61 item 5 — admin Settings page (2026-08-10)

**Implemented and pushed (`f8f5292`).** `Pages/Settings/Index.razor` at `/settings` — **My Preferences** (own `copilot.requireApproval` toggle, any authenticated user) + **Global Settings (Administrator)** card (`copilot.requireApproval.force` toggle) rendered only via `AuthorizeView Roles="Administrator"`; loads/saves via the item 2 settings endpoints. Admin-only **Settings** entry added to the NavMenu (`.icon-settings`). Gating matches the Governance pattern (API enforces admin; UI hides the admin card/nav entry for non-admins while every user edits their own preference).

**Verification:** Aletheia.Web.UnitTests **46** (was 44, +2) green; RAGS 270 / Repository 129 / Foundation 55 unchanged; build succeeds.

### Sprint 61 complete (2026-08-11)

All 5 items implemented, committed, and pushed to `origin/master`. Unit suites green: RAGS 270 / Repository 129 / Foundation 55 / Aletheia.Web.UnitTests 46; `dotnet build Aletheia.slnx` succeeds. The parallel Sprint 60 Docker smoke test was completed 2026-08-10 (committed `3c5b509`).

---

## Sprint 59/60 progress log (2026-08-07)

### Sprint 59 (completed) — Canonical Gate Softening, Multi-Theme, and Shared Theme Scope

Committed and pushed as `c151ea2`. Full details in the Sprint 59 sprint file and the pre-Sprint-62 history.

### Sprint 60 implementation (2026-08-07) — all four deliverables implemented

See `docs/sprints/Sprint-60 - GraphRAG and LazyGraphRAG Quick Wins.md` "Smoke Test Results (2026-08-10)" for the verified traces, concurrency checks, hard-deadline behavior, and reembed timing that motivated Sprint 62 items 7 + 8.

**Verification:** RAGS.UnitTests **265 passed**; `dotnet build Aletheia.slnx` succeeds; Aletheia.Web.UnitTests 6 pre-existing failures fixed 2026-08-10 (all stale tests — see below).

### Post-implementation web-test fix (2026-08-10)

The 6 pre-existing `Aletheia.Web.UnitTests` failures fixed — all **stale tests, no code regressions**:

- `RepositoryApiClientUploadTests` ×4 — fake `HttpClient` missing the `BaseAddress` production always sets (`Program.cs:27`); fake now sets `http://localhost`.
- `CopilotStateServiceTests.ClearAsync` — asserted storage key `v1`; intentionally bumped to `v2` in `dfc9d1b` (Sprint 58). Test now asserts `v2`.
- `CopilotIndexBindingTests.Wiki_shows_all_rags_mode_buttons` — asserted `>WRAGS</button>`; renamed to `>Wiki</button>` in Sprint 55.

**Verification:** Aletheia.Web.UnitTests **39 passed**; full solution build 0 errors; RAGS 265 / Repository 121 / Foundation 55 green. Committed with the Sprint-16 sprint-file filename normalization (space → dash).

### Post-implementation chat fix (2026-08-07)

Smoke-test report "Chat does not work at all" traced to the Copilot restore path: after a page reload the Web page restored a pending plan and polled `GET /api/copilot/plans/{id}/progress`, which returned **404** for a plan with no execution job yet — the client then polled every 2s **forever**. Fixed:

- API `GetPlanProgress`: a plan without an execution job now returns **200** with `JobId = Guid.Empty` (not-started state) instead of 404; "plan not found" still 404s.
- Web `Index.razor`: the polling loop treats `JobId == Guid.Empty` as "not started" — clears stale restored execution state, keeps the plan preview so **Run** works — and stops after 3 consecutive no-progress polls instead of looping indefinitely.

Verified end-to-end via curl; RAGS 251 / Repository 121 / Foundation 55 green; Web.UnitTests still the same 6 pre-existing failures at the time. Containers rebuilt. **Browser action required: hard refresh (Ctrl+F5)** to load the new WASM bundle.

### Post-implementation graph UX fix (2026-08-07)

Smoke-test feedback: the Graph Explorer "jumps around" while the layout runs and gives no feedback. Fixed:

- **Visible "preparing graph" state**: `GraphExplorer.razor` spinner + staged status line over the canvas; Refresh/Import/Fit/Re-layout/Spread/Find Path disabled during load.
- **Render once, don't re-layout**: `window.initGraph` accepts `dotNetRef` + `preservePositions`; on scope changes the graph re-renders keeping node positions (`randomize: false`); JS hooks `layoutstop` → `OnGraphLayoutSettled` clears the overlay.

Contract: `initGraph(containerId, nodes, edges, dotNetRef, preservePositions)`; page owns a `DotNetObjectReference<GraphExplorer>` disposed in `Dispose`. Web project builds clean.
