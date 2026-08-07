# Sprint 59 - Canonical Gate Softening, Multi-Theme, and Shared Theme Scope

**Status:** Active

Full authority: this file. Sprint 58 (Session Knowledge Theme Filtering) is **committed and pushed** (`4fdfaf0` on `origin/master`); its remaining verification is the Docker smoke test, which can run in parallel with Sprint 59 work.

Promotes backlog items 1-4 from `docs/backlog/Canonical-Form-Themes-Filtering-Enhancements.md` (item 5, theme-aware graph retrieval, stays parked).

## Objective

Three coordinated improvements to the canonical-template / theme / filtering model, delivered in one pass because they share the `file_metadata` schema:

1. **Soften the canonical gate** — a document with no matching template is ingested anyway (RAGS + knowledge index + graph seed) instead of being refused, so a new document kind arriving before its template is written is never lost. Template-dependent features (document briefs, per-section retrieval, theme) stay gated on `Canonical` status.
2. **Multi-theme per document** — templates declare a set of themes (`Theme: Analysis, As-Built`); `file_metadata.theme` becomes a set; theme filtering matches any selected theme; the picker shows each theme with its document count (a doc in multiple themes counts in each).
3. **Persist derived themes (backfill)** — a re-evaluation operation derives and persists `template_name` + `theme` + `template_status` from the template registry for rows where they are null; the read-time file-name fallback is demoted to a safety net.
4. **Shared theme scope across surfaces (Phase 1)** — a shared scope state (localStorage) that Search Center honors as an optional theme filter on semantic search, with a visible "scoped to themes" indicator. Copilot keeps its session-scoped filter; Wiki stays curated.

## Background

- Sprint 58 introduced the canonical template gate (`RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync`), the theme model (`file_metadata.template_name`/`theme`, `KnowledgeThemeService`, `GET /api/knowledge/themes`), and Copilot-session theme filtering (`ChatSession.ThemeFilter` -> `RetrievalRequest.SourceIds` -> pgvector `source_id = ANY(...)`).
- Watch points that motivated this sprint (from the backlog discussion):
  1. **Hard gate brittleness**: a document with no matching template is refused entirely, so estate completeness hinges on template coverage.
  2. **Single-theme coarseness**: one theme per document forces a choice; real documents are multi-faceted.
  3. **Copilot-only filtering**: the same document is theme-scoped in Copilot but not in Search Center — inconsistent views of the estate.

## Deliverables

### 1. Soften the canonical gate (`template_status`)
- `file_metadata` gains `template_status TEXT` (`Canonical` / `Uncategorized`; null = pre-Sprint-59 row awaiting re-evaluation). `PendingTemplate` from the backlog is folded into `Uncategorized` — a document is "pending" until a template matches, and the re-evaluate trigger promotes it.
- `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync`: when no canonical template matches, persist `template_status = Uncategorized` (template_name/theme null) and **continue** ingestion (download, extract, RAGS, knowledge index, graph seed). Content-quality gates (supported type, extractable text) are unchanged. Document briefs are enqueued only for `Canonical` documents.
- `DocumentBriefService` already skips documents with no canonical template; that behavior is now the explicit `Canonical` gate (no change needed beyond the ingestion-side skip).
- Admin surface: `GET /api/knowledge/uncategorized` lists rows that are not `Canonical` (null or `Uncategorized`); `POST /api/knowledge/reevaluate` re-resolves the template for one or all such rows, persists `template_name`/`theme`/`template_status`, and enqueues a document brief for rows that become `Canonical`.

### 2. Multi-theme per document
- Template format: `Theme: Analysis, As-Built` (comma-separated, backward compatible with a single value). `DocumentTemplateRegistry` parses the set; `IDocumentTemplateRegistry.TryGetTheme` is replaced by `TryGetThemes(string fileName)` returning `IReadOnlyList<string>?`; `ListThemes()` flattens all declared themes (distinct, ordered).
- `file_metadata.theme` becomes `text[]` (idempotent migration casts existing TEXT values; btree index replaced by GIN). `FileMetadata.Theme` and `FileThemeRow.Theme` become `IReadOnlyList<string>?`.
- `KnowledgeThemeService`: `ResolveSourceIdsAsync` matches a row when **any** of its themes is in the requested set; `GetThemesWithCountsAsync` counts a document in **each** of its themes. Read-time fallback derives the theme set from the file name via the registry (safety net only).
- `SetTemplateAsync` persists the theme set + `template_status`.

### 3. Persist derived themes (backfill)
- The re-evaluate operation (deliverable 1) doubles as the backfill: it derives and persists `template_name` + `theme` + `template_status` for rows where they are null (pre-Sprint-58 rows), demoting the read-time fallback to a safety net. Synchronous admin operation (estate is small); returns a summary (evaluated / promoted / uncategorized counts).

### 4. Shared theme scope across surfaces (Phase 1)
- New `SearchScopeStateService` (scoped, localStorage key `aletheia.search.scope.v1`) holds the shared theme selection (empty = all themes).
- Search Center (`Pages/SearchCenter.razor`): a theme filter control (chips/dropdown from `GET /api/knowledge/themes`, with document counts) above the search box; selection persists in the shared scope and is applied to **semantic** search. A visible "Scoped to N themes" indicator shows when active.
- `GET /api/rags/retrieve` gains an optional `themes` query parameter (comma-separated); the controller resolves themes -> source ids via `IKnowledgeThemeService.ResolveSourceIdsAsync` and passes them to `RetrievalRequest.SourceIds`. `RepositoryApiClient.RagsRetrieveAsync` maps it.
- Graph modes (WRAGS/GraphRAG/LazyGraphRAG) and Wiki stay out of scope for the theme filter (Sprint 58 boundary); the indicator notes the scope applies to semantic search.

### 5. Tests
- RAGS.UnitTests: multi-theme registry parsing (`TryGetThemes`, `ListThemes` flattening); `KnowledgeThemeService` match-any resolution and per-theme counting; ingestion proceeds (not stops) when no template matches and persists `Uncategorized`; briefs not enqueued for uncategorized.
- Repository.UnitTests: ingestion persists `template_status`; `GET /api/knowledge/uncategorized`; `POST /api/knowledge/reevaluate` summary; `GET /api/rags/retrieve?themes=` resolves and restricts; PostgreSQL `SetTemplateAsync`/`ListThemeRowsAsync`/`ListUncategorizedAsync` with `text[]`.
- Web: C#/Razor CoreCompile 0 errors; `SearchScopeStateService` state logic.
- Existing suites (Repository 113 / RAGS 249 / Foundation 55) remain green.

### 6. Docs
- `docs/Architecture.md`: canonical gate softened (ingest-uncategorized), multi-theme model, shared scope stage in Search Center.
- `docs/AdministratorGuide.md`: `template_status`, uncategorized list + re-evaluate endpoints, multi-theme `Theme:` convention.
- `docs/OperationsGuide.md`: troubleshooting uncategorized documents and promotion.
- `docs/Development-Guidelines.md`: templates may declare multiple themes.
- User guide: `05-Search-Center.md` (theme scope), `07-Copilot.md` (unchanged behavior note), `04-Knowledge-Themes.md` (multi-theme).
- AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, sprint handoff updated.

## Acceptance Criteria

- A document with no matching template is ingested (RAGS + knowledge index + graph seed) with `template_status = Uncategorized`; no document brief is generated; the admin list shows it; re-evaluate promotes it to `Canonical` and generates the brief once a template matches.
- Templates may declare multiple themes; a document in multiple themes is matched by any of them and counted in each; the picker shows per-theme counts.
- Pre-Sprint-58 rows get `template_name`/`theme`/`template_status` persisted by re-evaluate; the read-time fallback remains as a safety net.
- Search Center semantic search honors the shared theme scope with a visible indicator; Copilot keeps its session-scoped filter; graph modes and Wiki are unaffected.
- Repository / RAGS / Foundation suites green; Web C#/Razor compiles.

## Out of Scope

- Theme-aware GraphRAG/LazyGraphRAG retrieval and community summaries (backlog item 5, parked).
- Theme scope over Wiki / Browse / graph surfaces beyond Search Center semantic search.
- Rerankers, multi-tenant/security scoping, per-document ACLs.
- New queue providers or session stores (sessions and scope remain client-side state).

---

## Implementation Status (2026-08-07)

### Backend (deliverables 1-3) — implemented
- Migration `2026-08-07-file-metadata-template-status-themes.sql` (idempotent): adds `template_status TEXT`, casts `theme TEXT` -> `text[]`, replaces btree index with GIN, adds `idx_file_metadata_template_status`. `init.sql` updated for fresh deployments.
- `FileMetadata`/`FileThemeRow`: `TemplateStatus` added; `Theme` -> `IReadOnlyList<string>?`; `NormalizeThemes` (trim/dedup/case-insensitive, null when empty).
- `IMetadataRepository`: `SetTemplateAsync` gains `templateStatus`; new `ListUncategorizedAsync`; `PostgreSqlMetadataRepository` maps `text[]` and persists/reads `template_status`.
- `DocumentTemplateRegistry`: `TryGetTheme` -> `TryGetThemes(string fileName)` (comma-separated `Theme:` line); `ListThemes` flattens all theme sets.
- `KnowledgeThemeService`: `ResolveSourceIdsAsync` match-any; `GetThemesWithCountsAsync` per-theme counting; `ResolveThemes` (persisted set, registry fallback safety net, else `[Uncategorized]`); `Canonical` const.
- `RepositoryKnowledgeSourceIngestionService`: softened gate — no matching template => persist `template_status = Uncategorized` and continue ingestion; document briefs enqueued only for `Canonical` documents.
- `IngestionDiagnostics`: `RecordUncategorizedIngest` (renamed from template-gate skip) + `UncategorizedIngestCount`/`UncategorizedIngests`; `RagsStatusSnapshot` fields renamed accordingly.
- New `TemplateReevaluationService` (singleton): `ReevaluateAsync(Guid? sourceId)` lists non-Canonical rows, re-resolves template, persists `template_name`/`theme`/`template_status`, enqueues brief on promotion; returns summary (evaluated/promoted/uncategorized).
- `KnowledgeController`: `GET /api/knowledge/uncategorized`, `POST /api/knowledge/reevaluate`.
- `RagsController`: `GET /api/rags/retrieve?themes=` (comma-separated) resolves theme set -> `SourceIds` via `ResolveSourceIdsAsync`.

### Web (deliverable 4) — implemented
- New `SearchScopeStateService` (scoped, localStorage `aletheia.search.scope.v1`).
- `SearchCenter.razor`: theme filter chips (per-theme counts from `GET /api/knowledge/themes`), "Scoped to N themes" indicator, scope applied to semantic search only; admin panel for uncategorized list + re-evaluate.
- `RepositoryApiClient`: `RagsRetrieveAsync(..., themes)` appends `&themes=`; `GetUncategorizedAsync`; `ReevaluateTemplatesAsync`.

### Tests
- RAGS.UnitTests 251 passed, Repository.UnitTests 121 passed, Foundation.UnitTests 55 passed.
- Aletheia.Web.UnitTests 33 passed / 6 failed — the 6 failures (`RepositoryApiClientUploadTests` x4, `CopilotIndexBindingTests.Wiki_shows_all_rags_mode_buttons`, `CopilotStateServiceTests.ClearAsync_resets_memory_state_and_removes_browser_state`) are **pre-existing** (verified by `git stash` + run against `4fdfaf0`; identical 6 failures on the clean tree). They touch files unrelated to this sprint (`UploadAsync`, Copilot page/state) and are tracked for a separate fix.
- `dotnet build Aletheia.slnx` succeeds.

### Docs
- File 02 updated; Architecture / AdministratorGuide / OperationsGuide / Development-Guidelines / user guide updated (see deliverable 6). Backlog item statuses updated.

### Post-implementation fix: Copilot chat stuck after page restore (2026-08-07)

Reported during smoke testing ("Chat does not work at all"). Root cause and fix:

- **API** (`CopilotController.GetPlanProgress`): when a plan exists but has **no execution job yet** (normal "waiting for the user to approve" state), the endpoint returned **404** "No execution job found for this plan." Changed it to return **200** with a not-started `ChatProgressRecord` (`JobId = Guid.Empty`, plan prompt/createdAt, `Status = Queued`). `JobId == Guid.Empty` is now the client contract for "plan not started"; a true "plan not found" still 404s.
- **Web** (`Index.razor` `StartProgressPollingAsync` / `RefreshProgressAsync`): after a page reload the page restored `_pendingPlan` from browser state and began progress polling, which — combined with the API 404 — **polled every 2s forever** (observed nine 404s in ~30s in the proxy trace). The polling loop now:
  - treats a `JobId == Guid.Empty` response as "plan not started": clears stale restored `_activeJobId`/`_progress`/`_telemetry`, keeps the plan preview visible, and stops polling (the user can click **Run**);
  - stops after **3 consecutive** no-progress polls instead of looping indefinitely (covers the API-restart case where in-memory plans/jobs were lost).
- Verified: build succeeds; RAGS 251 / Repository 121 / Foundation 55 green; Aletheia.Web.UnitTests still exactly the 6 pre-existing failures. End-to-end via curl: plan → progress-before-execute returns 200 empty `jobId` → approve → execute → job completes with an answer. Containers rebuilt and restarted.
