File: openhands.md
# OpenHands / External Agent Directives

You are developing Aletheia.

## Documentation Order

Read these files before implementation:

1. `docs/File 00-Aletheia-Charter.md`
2. `docs/File 01-Aletheia-WorkPlan.md`
3. `docs/File 02-Current-Sprint.md`
4. This file
5. Any sprint file in `docs/sprints/` referenced by the current sprint

## Sprint Authority

`docs/File 02-Current-Sprint.md` is the active implementation authority.

Any work explicitly described in the current sprint file is authorized, regardless of phase number, historical release boundary, or module. If the current sprint references another sprint file, that referenced sprint scope is also authorized.

Do not force Phase 21 as a scope limit. Phase 21 documents are useful background for RAGS v2 and background operations, but they do not override the current sprint.

If this file, a handoff note, or `AGENTS.md` conflicts with `docs/File 02-Current-Sprint.md`, follow the current sprint.

The Charter remains authoritative for project principles. For implementation scope conflicts, Current Sprint overrides Work Plan and historical handoffs.

---

# Execution Rules

Always:

- Build incrementally
- Commit small working units when committing is requested
- Keep the solution compiling
- Write or update tests appropriate to the sprint
- Update documentation and handoff notes
- Match every ingested document to a canonical template in `docs/doc-templates` when one exists; since Sprint 59 the gate is softened, so a document with no matching template is ingested anyway with `template_status = Uncategorized` (no document brief) — add its template first for the full experience, then promote existing rows via `POST /api/knowledge/reevaluate`

Never:

- Skip the active sprint acceptance criteria
- Build future sprint work early unless the current sprint explicitly references it
- Implement speculative features
- Bypass abstractions
- Introduce infrastructure dependencies into Domain projects

---

# Required Architecture

Use:

- Clean Architecture
- Hexagonal Architecture
- DDD
- SOLID
- Dependency Injection

All dependencies must resolve through interfaces.

---

# Provider Rules

Implement only the currently approved provider or provider work explicitly named by the current sprint.

Future providers should be represented by abstractions and TODO backlog items.

Do not create production implementations for future providers unless explicitly instructed by the current sprint.

---

# Documentation Rules

For every completed feature update, update the relevant:

- README
- Architecture documentation
- API documentation when applicable
- Current sprint file
- Handoff documentation

---

# Testing Rules

Every feature requires:

- Unit tests where practical
- Integration tests for APIs and cross-service behavior

Do not close work items with failing tests unless the remaining failure is documented as an explicit blocker.

---

# Build Rules

The solution must always:

- Build successfully
- Pass relevant tests
- Run locally for UI/API work

---

# Completion Rules

A work item is complete only when:

- Code complete
- Tests passing or documented
- Documentation updated
- Acceptance criteria satisfied

Working software always takes priority over speculative extensibility.

If uncertain, choose the simplest architecture that satisfies current requirements while preserving abstraction boundaries.

This package should help OpenHands and other external agents continue without a monolithic architecture prompt.

---

# Historical Phase 21 Takeover Notes

Phase 21 - RAGS v2 Intelligence and Background Operations is historical context only unless reopened by the active sprint.

Before making changes to RAGS, WRAGS, background ingestion, or Copilot orchestration, also read:

1. `docs/Phase21-Background-Operations-Handoff.md`
2. The relevant sprint file in `docs/sprints/`

The first background-ingestion slice is implemented and validated. The lazy-enrichment slice is also implemented: uploads seed graph chunks without full document-wide LLM summarization, GraphRAG retrieval lazily enriches relevant chunks, and Copilot responses expose completion stats. WRAGS durability and maturity are implemented too: generated/edited wiki pages persist in PostgreSQL, `/wiki` can search/edit/show history/queue regeneration, pages have `Generated`/`Reviewed`/`Approved`/`NeedsReview`/`Stale` lifecycle controls, stale warnings, source-change stale detection, related topics, related-page lookup, and WRAGS participates in Search Center/Copilot retrieval context. Continue from the known maturity work in the handoff file rather than rebuilding these paths from scratch.

Important constraints:

- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints compatible unless the sprint explicitly changes them.
- Preserve the `/api/jobs` snapshot contract used by the Web Activity panel.
- Do not introduce a new queue provider or database unless it is part of the current sprint.
- Keep job progress concise: stage transitions plus coarse heartbeats are preferred over noisy per-token logs.
- Preserve the searchable-first upload path unless the sprint explicitly reopens full index-time enrichment.
- Treat Copilot `AlignmentConfidence` as a retrieval heuristic, not a calibrated correctness score.
- Preserve the current WRAGS API surface unless the sprint explicitly changes it: `/api/wiki/search`, `/api/wiki/recent`, `/api/wiki/retrieve`, `/api/wiki/pages/{id}`, `/api/wiki/pages/{id}/history`, `/api/wiki/pages/{id}/status`, `/api/wiki/pages/{id}/related`, `/api/wiki/regenerate`, and `/api/wiki/regenerate/job`.

Recommended takeover targets remain subject to the active sprint:

- Durable PostgreSQL-backed job state
- Cancellation and retry controls
- Integration tests
- Provider-backed token usage telemetry
- Graph-derived WRAGS backlinks
- Editorial diff visualization
- Quality scoring for wiki-as-context retrieval


# Sprint 55 Notes (Document Briefs / End-User Wiki)

- The user-facing surface is **Wiki** (never "WRAGS"); "WRAGS" stays internal (code/logs/docs).
- Document briefs are generated per registered document by DocumentBriefService + SemanticKernelDocumentBriefGenerator (RAGS.Application) through an IngestionJobService background job (kind DocumentBriefs). Briefs open with the document's nature/purpose (opening chunks) then follow the canonical template's ordered sections, grounded and cited; stored as wiki_pages rows with generated_from = 'document-brief', primary_source_id = document, source_ids = [document id].
- Briefs trigger after EnsureIngestedAsync succeeds and after upload ingestion jobs; regenerate via POST /api/wiki/briefs/regenerate (omit body for all documents, or send { sourceId, sourceName } for one).
- Wiki search/recent exclude generated_from = 'graphrag' community summaries and order document briefs first; community summaries stay internal for graph answers/diagnostics.
- Internal search surfaces (raw Wiki/WRAGS modes, GraphRAG, LazyGraphRAG, global-graph) are gated by FeatureFlags:ShowInternalSearch (default false) via IInternalSearchGate/InternalSearchGate. Gated endpoints return HTTP 404; the Search Center and Wiki UI hide the controls.
- Tests: DocumentBriefServiceTests, InternalSearchGateTests, WikiControllerInternalSearchGateTests, and GraphRAG/LazyGraphRAG controller gating tests (RAGS.UnitTests).

# Sprint 56 Notes (Duplicate Upload Detection / Document Update Flow)

- Active sprint: `docs/sprints/Sprint-56 - Duplicate Upload Detection and Document Update Flow.md`. Sprint 55 is complete/committed (HEAD 8e4bcb4).
- Uploads are fingerprinted server-side with SHA-256 before any storage write; `file_metadata.content_hash` (init.sql + idempotent migration) stores it; `FileMetadata.ContentHash` surfaces it.
- Exact-duplicate posts (same content hash) are trapped: HTTP 409 `{ duplicate = true, message, existingFileId, existingFileName, existingUploadedAt, existingVersion }`, no blob/metadata/ingestion/brief. Web shows a "Duplicate - already exists" badge and an Activity warning.
- Document updates use `POST /api/files/upload` with optional `existingFileId`: same-hash = no-change trap; changed file = new version under the same fileId, ingestion enqueued with the same sourceId, prior knowledge-index/graph rows replaced (reuse UploadedContentKnowledgeIndexer.DeleteSourceAsync + IVectorStore.DeleteBySourceAsync), and the Wiki brief regenerates (existing EnsureIngestedAsync trigger).
- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints and the /api/jobs snapshot contract untouched.

Implementation status (2026-08-05):
- Content fingerprinting: SHA-256 computed in FilesController.Upload over the temp file; FileMetadata.ContentHash + UploadRequest.ContentHash; file_metadata.content_hash (init.sql + idempotent SQL migration under src/Repository.Infrastructure.PostgreSQL/Migrations/).
- Duplicate trap: IDuplicateDetectionService (Repository.Application) + IMetadataRepository.FindByContentHashAsync (default no-op on interface; PostgreSQL implementation overrides); HTTP 409 payload contract; Web Upload page duplicate/no-change badges; RepositoryApiClient maps 409 -> UploadClientResult (IsDuplicate/NoChange/DuplicateMessage/ExistingFileId/ExistingFileName).
- Document update: POST /api/files/upload optional existingFileId -> no-change 409 / 400 when missing / version snapshot via IVersioningUseCase.CreateVersionAsync + blob+metadata replace + ingestion job with same sourceId; Replace semantics in RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync (knowledge-index + graph DeleteSource before RAGS ingest; brief regenerates).
- IGraphProvider.DeleteSourceAsync added with a default "not supported" implementation; Neo4jGraphProvider implements DETACH DELETE by n.sourceId.
- Admin duplicate report: GET /api/files/duplicates [Authorize(Roles = Administrator)].
- Web UI: Browse gains an update (↻) action linking to /upload?update=<fileId>&fileName=<name>; Upload page supports update mode.
- Tests: Repository.UnitTests 102 (DuplicateDetectionServiceTests + FilesControllerTests); RAGS 225; Foundation 55. Web.UnitTests (RepositoryApiClientUploadTests) added but not runnable in this sandbox (WASM ComputeWasmBuildAssets task-host failure blocks building Aletheia.Web, pre-existing environment issue; CI does not run Web.UnitTests).


# Sprint 57 Notes (Search Center Retrieval Quality and Troubleshooting)

- Active sprint: `docs/sprints/Sprint-57 - Search Center Retrieval Quality and Troubleshooting.md`. Sprint 56 committed/pushed (HEAD e34bba7; working tree clean).
- Zero Search Center results == zero embeddings (PgVectorStore has no similarity threshold). Diagnose via Activity panel + `/api/jobs`; template gate, extraction failure, and fresh DB are the usual causes.
- Sprint 57 adds: `GET /api/rags/status` diagnostics + Search Center empty-state messaging; configurable real embeddings (Ollama) with SimpleEmbeddingProvider fallback; `RAGS:MinimumScore` + keyword fallback with `RetrievalStrategy`; Reembed background job (kind `Reembed`) that replaces embeddings per source.
- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints and the /api/jobs snapshot contract untouched.

# Sprint 58 Notes (Session Knowledge Theme Filtering)

- Active sprint: `docs/sprints/Sprint-58 - Session Knowledge Theme Filtering.md`. Sprint 57 committed/pushed (HEAD 7220987); its Docker smoke test remains as parallel verification.
- Goal: end-user scopes a Copilot session to knowledge themes derived from canonical templates (Analysis, As-Built, As-Proposed, or combinations). UX option #1: theme picker at session creation + theme chips in the session header (editable mid-session).
- Theme model: `docs/doc-templates` template files gain a first-line `Theme: <theme>`; `DocumentTemplateRegistry` parses it and exposes `TryGetTheme` / `ListThemes` (missing => Uncategorized).
- Persistence: `file_metadata.template_name` + `file_metadata.theme` (idempotent migration 2026-08-06 + init.sql); `RepositoryKnowledgeSourceIngestionService` persists at ingestion; read-time fallback derives theme from file name for pre-migration rows; Reembed job backfills.
- Filter path: `ChatSession.ThemeFilter` -> `ChatPayload` -> `ChatRequestOptions` -> engine; `RetrievalRequest.SourceIds` (set predicate in PgVectorStore SearchAsync + SearchKeywordAsync); engine intersects with Sprint 51 single-document scope, union for collections; `GET /api/knowledge/themes` returns themes + registered-document counts.
- Constraints: keep /api/jobs snapshot contract and existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints compatible; GraphRAG/LazyGraphRAG internals and community summaries out of scope; sessions remain client-side state (CopilotStateService localStorage, key bump to v2).
### Sprint 58 Implementation Status (2026-08-06)

- D1-D4 implemented: theme metadata on templates (`Theme: Analysis` + registry `TryGetTheme`/`ListThemes`), `file_metadata.template_name`/`theme` persistence (migration 2026-08-06), `RetrievalRequest.SourceIds` + PgVectorStore set predicates, `KnowledgeThemeService` singleton + `GET /api/knowledge/themes`, theme filter through `ChatSession` -> `ChatPayload`/`PlanPayload` -> `ChatRequestOptions`/`ChatPlanRecord` (preserved through approval), engine enforcement (RAGS paths + Sprint 51 intersection + tool-result post-filter), Web picker + header chips (session storage v2).
- D5: RAGS 249 / Repository 113 / Foundation 55 green; Web CoreCompile 0 errors. Remaining: Docker smoke test + commit.

# Sprint 59 Notes (Canonical Gate Softening, Multi-Theme, Shared Theme Scope)

- Active sprint: `docs/sprints/Sprint-59 - Canonical Gate Softening, Multi-Theme, and Shared Theme Scope.md` (created 2026-08-07). Sprint 58 complete/committed/pushed (HEAD `4fdfaf0`).
- **Softer gate**: `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` no longer stops when no canonical template matches. It persists `template_status = Uncategorized` (template_name/theme null) and continues ingestion (download, extract, RAGS, knowledge index, graph seed). Document briefs enqueued only for `Canonical` documents. Content-quality gates unchanged.
- **template_status** column (`Canonical`/`Uncategorized`; null = pre-Sprint-59 row). Backlog's `PendingTemplate` folded into `Uncategorized`.
- **Multi-theme**: `Theme: Analysis, As-Built` (comma-separated); `IDocumentTemplateRegistry.TryGetTheme` -> `TryGetThemes(string fileName) -> IReadOnlyList<string>?`; `file_metadata.theme` is `text[]` with a GIN index (migration 2026-08-07 casts existing TEXT). `KnowledgeThemeService.ResolveSourceIdsAsync` matches ANY theme; `GetThemesWithCountsAsync` counts a doc in each theme. Read-time fallback demoted to safety net.
- **Backfill/promotion**: `TemplateReevaluationService` (singleton) — `GET /api/knowledge/uncategorized` lists non-Canonical rows; `POST /api/knowledge/reevaluate` re-resolves template for one or all, persists template_name/theme/template_status, enqueues brief on promotion; returns summary (evaluated/promoted/uncategorized).
- **Shared scope (Phase 1)**: `SearchScopeStateService` (scoped, localStorage `aletheia.search.scope.v1`); Search Center theme filter chips applied to semantic search only (`GET /api/rags/retrieve?themes=`) with "Scoped to N themes" indicator; Copilot keeps session-scoped filter; Wiki curated. Backlog item 5 (theme-aware graph retrieval) parked.
- Diagnostics repurposed: `UncategorizedIngestCount`/`UncategorizedIngests` replace template-gate-skip counters in `GET /api/rags/status`.

### Sprint 59 Implementation Status (2026-08-07)

- Deliverables 1-4 implemented (see above): migration + init.sql, models (`FileMetadata.Theme` -> `IReadOnlyList<string>?`, `TemplateStatus`), `PostgreSqlMetadataRepository` text[] mapping + `ListUncategorizedAsync`, `DocumentTemplateRegistry.TryGetThemes`, `KnowledgeThemeService` match-any/per-theme counts, softened ingestion gate, `IngestionDiagnostics` rename, `TemplateReevaluationService`, `KnowledgeController` uncategorized + reevaluate, `RagsController` `?themes=`, `SearchScopeStateService`, SearchCenter theme scope + admin panel, `RepositoryApiClient` themes/uncategorized/reevaluate.
- Tests: RAGS 251 / Repository 121 / Foundation 55 green; `dotnet build Aletheia.slnx` succeeds. Aletheia.Web.UnitTests 33/39 — 6 failures are **pre-existing** (verified identical on clean `4fdfaf0` via git stash; unrelated files: `RepositoryApiClientUploadTests` x4, Copilot page/state tests), tracked for a separate fix.
- Remaining: optional Docker smoke test.

### Sprint 59 post-implementation chat fix (2026-08-07)

- **Bug**: "Chat does not work at all" during smoke test. The Web page, after a reload, restored a pending plan from browser state and polled `GET /api/copilot/plans/{planId}/progress`; the API returned **404** for a plan with no execution job yet, so `StartProgressPollingAsync` polled every 2s forever (nine 404s observed in ~30s). Container restarts made it worse: in-memory plans (`InMemoryChatPlanRepository`) and chat jobs are lost, so restored browser state referenced dead plans.
- **Fix**: API `GetPlanProgress` now returns **200 with `JobId = Guid.Empty`** (not-started, status Queued) when the plan exists but has no execution job; true "plan not found" remains 404. Web `Index.razor` `RefreshProgressAsync` treats empty `JobId` as "not started" — clears stale restored `_activeJobId`/`_progress`/`_telemetry`, keeps the plan preview so the user can click Run — and `StartProgressPollingAsync` stops after **3 consecutive** no-progress polls instead of looping.
- Verified: build ok; RAGS 251 / Repository 121 / Foundation 55 green; Web.UnitTests unchanged (6 pre-existing). End-to-end curl: plan -> progress-before-execute 200/empty jobId -> approve -> execute -> job completes. Containers rebuilt. **User must hard-refresh (Ctrl+F5)** to load the new WASM bundle.

### Sprint 59 post-implementation graph UX fix (2026-08-07)

- **Bug**: Graph Explorer "jumps around" while the `cose` layout runs and gives no feedback; users press buttons and think it is running wild.
- **Fix**: (1) `GraphExplorer.razor` shows a spinner + staged status line over the canvas ("Loading graph…" → "Loading edges…" → "Rendering layout…") and disables Refresh/Import/Fit/Re-layout/Spread/Find Path while loading; (2) `window.initGraph` gained `dotNetRef` + `preservePositions` params — scope changes keep existing node positions (`randomize: false`) instead of re-randomizing, and `layoutstop` invokes the page's `[JSInvokable] OnGraphLayoutSettled()` to clear the overlay. The page owns a `DotNetObjectReference<GraphExplorer>` (disposed in `Dispose`).
- Contract: `initGraph(containerId, nodes, edges, dotNetRef, preservePositions)`; `OnGraphLayoutSettled` clears the loading state. Web project builds clean.