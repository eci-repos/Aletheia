# Sprint 59 - Canonical Gate Softening, Multi-Theme, and Shared Theme Scope

**Status:** Active

Full authority: `docs/sprints/Sprint-59 - Canonical Gate Softening, Multi-Theme, and Shared Theme Scope.md` (created 2026-08-07). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 58 (Session Knowledge Theme Filtering) is **complete, committed, and pushed**: commit `4fdfaf0` is on `origin/master` (HEAD `4fdfaf0`). Remaining Sprint 58 verification: Docker smoke test (upload -> ingest -> themes endpoint -> theme-scoped vs all-themes Copilot session) - can run in parallel with Sprint 59 work.

## Objective

Three coordinated improvements to the canonical-template / theme / filtering model, delivered in one pass because they share the `file_metadata` schema:

1. **Soften the canonical gate** — a document with no matching template is ingested anyway (RAGS + knowledge index + graph seed) instead of being refused; template-dependent features (document briefs, per-section retrieval, theme) stay gated on `Canonical` status.
2. **Multi-theme per document** — templates declare a set of themes (`Theme: Analysis, As-Built`); `file_metadata.theme` becomes a set; theme filtering matches any selected theme; the picker shows each theme with its document count.
3. **Persist derived themes (backfill)** — a re-evaluation operation derives and persists `template_name` + `theme` + `template_status` for null rows; the read-time fallback is demoted to a safety net.
4. **Shared theme scope across surfaces (Phase 1)** — a shared scope state (localStorage) that Search Center honors as an optional theme filter on semantic search, with a visible "scoped to themes" indicator. Copilot keeps its session-scoped filter; Wiki stays curated.

## Authorized Work (summary - see sprint file for details)

1. **Soften the canonical gate**: `file_metadata.template_status` (`Canonical`/`Uncategorized`); ingestion ingests uncategorized documents instead of hard-stopping; briefs gated on `Canonical`; `GET /api/knowledge/uncategorized` + `POST /api/knowledge/reevaluate` (admin list + promotion trigger).
2. **Multi-theme**: templates declare a theme set; `file_metadata.theme` becomes `text[]`; `TryGetThemes`; match-any filtering; per-theme counts.
3. **Backfill**: re-evaluate persists `template_name`/`theme`/`template_status` for null rows; read-time fallback demoted to safety net.
4. **Shared scope**: `SearchScopeStateService` (localStorage); Search Center theme filter on semantic search with visible indicator; `GET /api/rags/retrieve?themes=` resolves and restricts.
5. **Tests**: RAGS (multi-theme, match-any, uncategorized ingestion), Repository (template_status, uncategorized list, re-evaluate, retrieve?themes=), Web CoreCompile; existing suites green.
6. **Docs**: Architecture, AdministratorGuide, OperationsGuide, Development-Guidelines, user guide (Search Center / Knowledge Themes), AGENTS, File 02/03, handoff.

## Acceptance Criteria

- A document with no matching template is ingested with `template_status = Uncategorized`; no brief; admin list shows it; re-evaluate promotes it and generates the brief once a template matches.
- Templates may declare multiple themes; a document in multiple themes is matched by any and counted in each; picker shows per-theme counts.
- Pre-Sprint-58 rows get `template_name`/`theme`/`template_status` persisted by re-evaluate; read-time fallback remains a safety net.
- Search Center semantic search honors the shared theme scope with a visible indicator; Copilot keeps its session-scoped filter; graph modes and Wiki unaffected.
- Repository / RAGS / Foundation suites green; Web C#/Razor compiles.

## Out of Scope

- Theme-aware GraphRAG/LazyGraphRAG retrieval and community summaries (backlog item 5, parked); theme scope over Wiki / Browse / graph surfaces beyond Search Center semantic search; rerankers; multi-tenant/ACL scoping; new session stores.

---

## Progress (2026-08-07)

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
