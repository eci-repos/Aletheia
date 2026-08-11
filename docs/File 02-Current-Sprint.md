# Sprint 62 - GraphRAG Soft Deadline and Reembed Parity

**Status:** Active (2026-08-11)

Full authority: `docs/sprints/Sprint-62 - GraphRAG Soft Deadline and Reembed Parity.md` (created 2026-08-11). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 61 (Chat Approval Prompt and Admin Settings) is **complete, committed, and pushed**: commits `4d10561` (item 1 modal), `793fc52` (items 2+3+4 settings foundation + approval preference + admin override), `f8f5292` (item 5 admin Settings page) are on `origin/master`. All unit suites green (RAGS 270 / Repository 129 / Foundation 55 / Aletheia.Web.UnitTests 46). Its residual manual verification (hard-refresh `/copilot` + `/settings`) is user-side and optional.

## Objective

Two GraphRAG / ingestion follow-ups surfaced by the Sprint 60 Docker smoke test (2026-08-10):

1. **Reembed indexer parity** — make `POST /api/jobs/rags/reembed` honor the lightweight indexer (`IndexLightweightAsync`, no LLM) instead of the full graph-intelligence pipeline, so re-embedding after a provider/dimension change is fast (~minutes, not 40+ for a 3-doc corpus).
2. **GraphRAG soft deadline / best-partial result** — a GraphRAG retrieval that blows the 30s `CancelAfter(MaxExecutionTime)` under LLM saturation should degrade to plain semantic retrieval (HTTP 200, trace strategy `semantic-timeout-fallback`) instead of hard-failing with HTTP 400.

## Authorized Work (summary - see sprint file for details)

1. `KnowledgeIndexMode` enum (`Full` / `Lightweight`) in `RAGS.Abstractions.Models`; optional `mode = KnowledgeIndexMode.Full` param on `IKnowledgeSourceIngestionService.EnsureIngestedAsync` and its implementation (branch to `IndexLightweightAsync` when Lightweight); reembed passes `Lightweight`, repair + chat hydration keep `Full`.
2. Soft-deadline catch in `GraphRagService.RetrieveAsync`: deadline-fires → best-effort semantic fallback under a ~10s secondary deadline, Success with trace strategy `semantic-timeout-fallback` + steps `deadline-exceeded` / `semantic-fallback`; caller-cancel → Failure; other exceptions unchanged. Optional `Func<IGraphTraversalBudget>? budgetFactory` ctor param for testability.
3. **Tests**: Repository (lightweight mode calls `IndexLightweightAsync`, not `IndexAsync`), RAGS (deadline-fires → `semantic-timeout-fallback` success; caller-cancel → failure). Existing suites green.
4. **Docs**: Architecture, OperationsGuide (reembed), Development-Guidelines, AGENTS, File 02/03, handoff; backlog items 7 + 8 statuses updated.

## Acceptance Criteria

- Reembed runs the lightweight indexer (no LLM graph-intelligence calls); a 3-doc corpus re-embeds in minutes.
- A GraphRAG retrieval that blows the execution deadline returns HTTP 200 with a semantic result carrying trace strategy `semantic-timeout-fallback` and a `deadline-exceeded` step — not HTTP 400.
- A caller-cancelled retrieval still fails; non-deadline exceptions still fail as before.
- Repository / RAGS / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Persisting the LazyGraphRAG corpus index to PostgreSQL (GraphRAG backlog item 2); batch GraphRAG ingest (GraphRAG backlog item 3); theme-aware graph retrieval (Canonical backlog item 5).
- Parallelizing / batching graph-intelligence LLM calls (backlog item 3); changing repair or chat-hydration indexing behavior.

---

## Progress

### Sprint 62 items 1 + 2 — reembed parity + soft deadline (2026-08-11)

**Implemented.** See the sprint file "Implementation Status" for full detail:

- **Item 1 (reembed parity):** `KnowledgeIndexMode` enum (`Full`/`Lightweight`) in `RAGS.Abstractions.Models`; `EnsureIngestedAsync` takes `mode = Full` and branches to `IndexLightweightAsync` when Lightweight; `RunReembedJobAsync` passes `Lightweight` (repair/chat keep `Full`).
- **Item 2 (soft deadline):** `GraphRagService.RetrieveAsync` catch distinguishes deadline-fires from caller-cancel — deadline degrades to best-effort semantic retrieval under a ~10s secondary deadline, returning Success with trace strategy `semantic-timeout-fallback` + steps `deadline-exceeded`/`semantic-fallback`; caller-cancel and other exceptions still fail. Optional `budgetFactory` ctor param for tests.

**Verification:** Repository 130 (+1) / RAGS 272 (+2) / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds. Pending: commit + optional Docker smoke test (reembed speed + `semantic-timeout-fallback` trace under LLM saturation).

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
