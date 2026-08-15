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

# Sprint 60 Notes (GraphRAG / LazyGraphRAG Quick Wins)

- Active sprint: `docs/sprints/Sprint-60 - GraphRAG and LazyGraphRAG Quick Wins.md` (created 2026-08-07). Sprint 59 complete/committed/pushed (HEAD `c151ea2`).
- **Per-request budget**: `IGraphTraversalBudget.CreatePerRequest()` + read-only counters (`LlmCalls`, `TokensConsumed`, `NodesVisited`, `RelationshipsTraversed`). LazyGraphRAG keeps the injected budget as a template and calls `CreatePerRequest()` per `RetrieveAsync`; GraphRAG constructs `new GraphTraversalBudget()` inline per request. The `AddSingleton<IGraphTraversalBudget>` in `Repository.API/Program.cs` is **removed** — do not re-add it. `LazyGraphRagService._indexedSources` is guarded by `lock (_indexedSourcesLock)`.
- **Token accounting**: `TokenUsageHelper.GetTotalTokens(ChatMessageContent?)` reads `ChatMessageContent.Metadata` (provider-agnostic input/output/total keys + nested `"Usage"` + reflection over provider usage objects). Wired into `EntityExtractionService.DiscoverAsync` and `LazyRelationshipDiscoveryService.DiscoverAtQueryTimeAsync`. **`RecordTokens` records actual consumption even when it breaches the budget** and returns `updated <= MaxTokenBudget`, so `IsExceeded()` fires and halts traversal — the token budget is no longer dead code.
- **Hard deadline**: both `RetrieveAsync` paths use `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` + `CancelAfter(MaxExecutionTime)`; all LLM/traversal calls flow the deadline token.
- **Noise entities**: `NoiseEntityFilter.IsNoise` (type `keyword` / `statistical-candidate`, case-insensitive) applied in `LazyEntityDiscoveryService.PersistAsync`, `LazyGraphRagService.PersistDiscoveryAsync` (also drops relationships with noise endpoints), `GraphRagService.IngestAsync`, and `GraphRagService.EnsureQueryTimeEnrichmentAsync`. Noise entities stay retrieval-only (in-memory for the request).
- **Retrieval trace**: `RetrievalTrace` model (strategy, LLM calls, tokens, nodes visited, relationships traversed, pruning ratio, elapsed ms, ordered steps) + settable `SearchResult.Trace` (additive/non-breaking). LazyGraphRAG reports real budget counters + pruning ratio; GraphRAG reports approximate `llmCalls` + budget tokens (per-call token accounting for GraphSummary/HierarchicalSummary/GraphReasoning/RelationshipExtraction services is a **documented follow-up**). Web Search Center renders the trace block on each result card.
- **Tests**: RAGS.UnitTests now **265** (was 251) — GraphTraversalBudgetTests (6), LazyGraphRagServiceTests (+3 per-request/concurrent/trace), LazyEntityDiscoveryServiceTests (+3 noise), GraphRagServiceTests (+2 noise/trace); all mocks updated for the new `IGraphTraversalBudget? budget` parameter. Repository 121 / Integration 8 / Foundation 55 green; `dotnet build Aletheia.slnx` succeeds. Web.UnitTests still has the same 6 **pre-existing** failures (verified on clean HEAD).
- Follow-up candidate (not in scope): per-call token accounting for the summary/reasoning/relationship services so GraphRAG's `TokensConsumed` reflects the full call chain.

# Sprint 61 Notes (Chat Approval Prompt and Admin Settings)

- Active sprint: `docs/sprints/Sprint-61 - Chat Approval Prompt and Admin Settings.md` (created 2026-08-10). Sprint 60 complete/committed/pushed (HEAD `c6c3e48`); its Docker smoke test was **run 2026-08-10** and results committed as `3c5b509` (see the Sprint 60 sprint file "Smoke Test Results").
- **Sprint 61 complete 2026-08-11**: all 5 items implemented and pushed — `4d10561` (modal approval prompt above the Activity/Chats panels + auto-expand Execution column), `793fc52` (server-side settings foundation `app_settings`/`user_settings` + `GET/PUT /api/settings[+/me]` + `copilot.requireApproval` per-user preference + `copilot.requireApproval.force` admin override), `f8f5292` (admin `/settings` page + admin-only NavMenu entry). Unit suites green: RAGS 270 / Repository 129 / Foundation 55 / Aletheia.Web.UnitTests 46; `dotnet build Aletheia.slnx` succeeds. Backlog items 1-5 marked implemented.
- Residual manual (user-side): hard-refresh `/copilot` and `/settings` for a live visual check (modal + settings page verified via unit/binding tests only).

# Sprint 62 Notes (GraphRAG Soft Deadline and Reembed Parity)

- Active sprint: `docs/sprints/Sprint-62 - GraphRAG Soft Deadline and Reembed Parity.md` (created 2026-08-11). Sprint 61 complete/committed/pushed (`4d10561`/`793fc52`/`f8f5292`).
- **Implemented 2026-08-11**: item 7 (reembed indexer parity — `KnowledgeIndexMode` Full/Lightweight on `EnsureIngestedAsync`, reembed passes Lightweight → `IndexLightweightAsync`; repair/chat keep Full) and item 8 (GraphRAG soft deadline — deadline-fires catch degrades to best-effort semantic retrieval under a ~10s secondary deadline with trace strategy `semantic-timeout-fallback` + steps `deadline-exceeded`/`semantic-fallback` instead of HTTP 400; caller-cancel still fails). Repository 130 (+1) / RAGS 272 (+2) / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds. Backlog items 7 + 8 marked promoted. Committed/pushed `26995d9`. Optional Docker smoke test (reembed speed, `semantic-timeout-fallback` trace under LLM saturation) is user-side.

# Sprint 63 Notes (Persisted LazyGraphRAG Corpus Index and Batch GraphRAG Ingest)

- Active sprint: `docs/sprints/Sprint-63 - Persisted LazyGraphRAG Corpus Index and Batch GraphRAG Ingest.md` (created 2026-08-11). Sprint 62 complete/committed/pushed (`26995d9`).
- **Corpus index persistence (item 2)**: `ICorpusIndexRepository` (`UpsertDocumentAsync`/`LoadAsync`, models `CorpusIndexSnapshot`/`CorpusDocumentIndex`) → `PostgreSqlCorpusIndexRepository` (Dapper + `PostgreSqlConnectionFactory`, transaction upsert of `lazygraphrag_corpus_documents` + delete/reinsert `lazygraphrag_corpus_terms`; LEFT JOIN load). Tables in idempotent migration `2026-08-11-lazygraphrag-corpus-index.sql` + `scripts/init.sql`. `CorpusDiscoveryIndex` ctor takes `(ICorpusIndexRepository? repository = null, ILogger? logger = null)` — loads the persisted corpus at startup and persists write-through; both are **best-effort** (a load/persist failure logs a warning and never fails ingestion; the in-memory index stays authoritative). Registered `AddSingleton<ICorpusIndexRepository, PostgreSqlCorpusIndexRepository>()` in `Program.cs`.
- **Batch GraphRAG ingest (item 3)**: `IGraphProvider` gained `CreateNodesAsync`/`CreateRelationshipsAsync`/`UpdateNodesAsync` with **default interface implementations** (fall back to per-item calls — existing fakes like `MockGraphProvider`/`MemoryGraphProvider` keep compiling). `Neo4jGraphProvider` implements them with `UNWIND $rows AS row` Cypher, grouping nodes/updates by `BuildNodeLabels(type)` and relationships by `NormalizeToken(RelationshipType, "related_to")` (dynamic labels/types can't be set per-row). Both full-ingest paths (`UploadedContentKnowledgeIndexer.PersistGraphIntelligenceAsync` + `GraphRagService.IngestAsync`) are now 4 phases with `MaxLlmConcurrency = 4` (`SemaphoreSlim` + `Task.WhenAll`): (1) bounded per-chunk entity+relationship extraction, (2) one `CreateNodesAsync` + one `CreateRelationshipsAsync` per label/type group, (3) bounded entity summaries (deduped), (4) **gated community detection** — `SourceNodeExistsAsync` checked before the source node is created; community detection + bounded community summaries + `UpdateNodesAsync` run only when `!sourceExists` (first ingest). Re-ingests skip the O(graph) re-cluster; retrieval-time discovery still re-clusters on cache miss.
- **Tests**: RAGS 281 (+9) — `CorpusDiscoveryIndexTests` (4: write-through, restart-survival, persistence-failure tolerance, load-failure tolerance), `PostgreSqlCorpusIndexRepositoryTests` (1 live-DB round-trip, try-connect/`catch { return; }` skip), `GraphRagServiceTests` (+4: batched writes via `BatchRecordingGraphProvider`, bounded concurrency via `ConcurrencyTrackingEntityExtractionService`, community gate via `CountingCommunityDetectionService`). Repository 130 / Foundation 55 / Web 46 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog items 2 + 3 marked promoted. Committed/pushed `df7627d`. Optional Docker smoke test (restart corpus survival, batched-write ingest) is user-side.

# Sprint 64 Notes (Theme-Aware Graph Retrieval)

- Active sprint: `docs/sprints/Sprint-64 - Theme-Aware Graph Retrieval.md` (created 2026-08-11). Sprint 63 complete/committed/pushed (`df7627d`). Promotes Canonical backlog item 5 (the last item in `docs/backlog/archive/Canonical-Form-Themes-Filtering-Enhancements.md`).
- **Theme scope on graph retrieval**: optional `IReadOnlyList<Guid>? sourceIds = null` (after `cancellationToken`) on `IGraphRagService.RetrieveAsync`/`GlobalSearchAsync`, `ILazyGraphRagService.RetrieveAsync`/`GlobalSearchAsync`, and `IGlobalGraphSearchService.SearchAsync`. New `GraphThemeScope` static helper (`RAGS.Application/GraphRAG`): `TryGetSourceId` (reads `Properties["sourceId"]` as Guid/string, falls back to `Type == "Source"` → `Id`), `IsInScope`, `FilterNodes`, `ToAllowSet`, `CommunityHasMemberInScope` (match-any). `GraphRagService.RetrieveAsync` filters resolved entities + multi-hop expansion nodes to the allowlist and scopes semantic fallback / entity-expansion `RetrievalRequest`s; `LazyGraphRagService.RetrieveAsync` filters corpus seed sources (`SearchCorpus` results) and scopes fallback / expansion requests; `GlobalGraphSearchService.SearchAsync` builds a node→source map via `IGraphProvider.GetNodesAsync()` and filters communities with match-any semantics (returns `Failure("No communities in the selected themes.")` when scoped and empty). `GlobalGraphSearchService` ctor gained optional `IGraphProvider? graphProvider = null` (after `kernel`) — do not reorder.
- **API + Web wiring**: `GraphRagController`/`LazyGraphRagController` accept `?themes=` (comma-separated) on `Retrieve` and `GlobalSearch`, resolve via optional `IKnowledgeThemeService? themeService = null` ctor param (RagsController pattern), pass `sourceIds` through. `RepositoryApiClient.GraphRagRetrieveAsync`/`LazyGraphRagRetrieveAsync` accept themes and append `&themes=`. `SearchCenter.razor` passes `_selectedThemes` to graph-mode retrieve calls; the WRAGS note now reads "Theme scope does not apply to WRAGS search."
- **Tests**: RAGS 289 (+8) — `GraphRagServiceTests.RetrieveAsync_theme_scope_filters_resolved_entities` + `SearchAsync_theme_scope_filters_communities_by_member_source` (helper fakes `ScopedEntitiesReasoningService`/`SourceScopedCommunityDetectionService`), `LazyGraphRagServiceTests.RetrieveAsync_theme_scope_flows_source_ids_to_semantic_retrieval` + `RetrieveAsync_theme_scope_filters_corpus_seed_sources` (helper fakes `RecordingRagsService`/`RecordingCorpusIndex`), controller themes pass-through tests in `GraphRagControllerTests`/`LazyGraphRagControllerTests` (mock `IKnowledgeThemeService`). All fakes updated for the new signatures. Repository 130 / Foundation 55 / Web 46 unchanged; `dotnet build Aletheia.slnx` succeeds (only pre-existing AngleSharp NU1902 warning).

# Sprint 65 Notes (Wiki Markdown and HTML View Tabs)

- Active sprint: `docs/sprints/Sprint-65 - Wiki Markdown and HTML View Tabs.md` (created 2026-08-13). Sprint 64 complete/committed/pushed. Promotes `docs/backlog/Wiki-Markdown-HTML-Tabs.md`.
- **Complete 2026-08-13**: all 3 work items implemented, tested, and pushed. Wiki pages (markdown in `WikiPage.Summary`) now render through a **View / Source** tab bar in `Wiki.razor` (default View; View = `@((MarkupString)MarkdownRenderer.ToHtml(_selectedPage.Summary))`, Source = raw md in `<pre class="wiki-source-view">`). Tab choice is ephemeral page state (`_viewMode`), no API/wire changes; editing keeps the raw-markdown textarea.
- **`MarkdownRenderer` (`src/Aletheia.Web/Services/MarkdownRenderer.cs`)** is the single Web markdown renderer — a static `ToHtml(string)` returning an HTML string. It is the extraction of Copilot's former private `RenderMarkdown` helpers (headings `#`–`####`, pipe tables, `-`/`*` lists, paragraphs, inline `**bold**`/`` `code` ``), with all text `HtmlEncoder`-escaped **before** inline formatting so raw HTML in source content is never emitted as markup. Emitted table wrappers use neutral `md-table-wrap`/`md-table` classes (styled in both `Copilot/Index.razor.css` and `Wiki.razor.css`). Copilot's JSON special case (`{`/`[` blob → `<pre class="copilot-json">`) stays page-local; everything else delegates to `MarkdownRenderer.ToHtml`.
- **Tests**: Web.UnitTests 61 (+15) — `MarkdownRendererTests` (11: empty/null/whitespace, heading levels, paragraphs, lists, tables, inline bold/code, HTML escaping, CRLF) + `WikiViewTabsBindingTests` (4: View/Source tabs present, default View, Copilot delegates to the shared renderer, Copilot JSON branch preserved). Foundation 55 / Repository 130 / RAGS 290 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): hard-refresh `/wiki` for a live visual check of the tabs.

# Sprint 66 Notes (Remove Redundant Metadata Nav Item)

- Active sprint: `docs/sprints/Sprint-66 - Remove Redundant Metadata Nav Item.md` (created 2026-08-13). Sprint 65 complete/committed/pushed. Promotes `docs/backlog/Remove-Redundant-Metadata-Nav-Item.md`.
- **Complete 2026-08-13**: the **Metadata** side-menu item was removed from `NavMenu.razor` (it duplicated Browse — both list files and both lead to the metadata editor). The `/metadata` page (`MetadataEditor.razor`), its route, and Browse's ✎ Edit deep-link (`metadata?fileId=...&fileName=...&version=...`) are untouched; the editor stays reachable through Browse. Do not re-add a Metadata nav entry.
- **The "Searching…" hang on the Metadata/Browse search is an API-availability diagnostic, not a code bug**: `GET /api/search` → `SearchUseCase` → plain PostgreSQL metadata query (no LLM), so a long "Searching…" means the API is not responding.
- **Tests**: Web.UnitTests 64 (+3) — `NavMenuBindingTests` (nav entry gone, Browse still deep-links to `metadata?fileId=`, page route intact). Foundation 55 / Repository 130 / RAGS 290 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): hard-refresh to see the nav without Metadata.
- **Post-sprint nav grouping (2026-08-13)**: added a divider + muted **Management** label above Governance in `NavMenu.razor` (Governance + Settings were already at the bottom; the label makes the grouping visible). Hidden when the sidebar is collapsed. Web 65 (+1).
- **Post-sprint Dashboard card tints (2026-08-13)**: the Dashboard action cards (Upload/Browse/Search Center/Wiki/Copilot) each got a `dashboard-action-<name>` modifier class styled in the new `Dashboard.razor.css` — a very light pastel background wash, a 3px colored top border, and a darker shade on the card title (soft green/blue/amber/violet/teal respectively). Text stays dark for contrast; buttons unchanged. Web 67 (+2, `DashboardBindingTests`).
- **Post-sprint Dashboard loading indicator (2026-08-13)**: while `_recentFiles` is still `null` (the body only renders after the first API call returns), the Dashboard shows a Bootstrap spinner + "Loading repository data…" in a `.dashboard-loading` block — a slow refresh no longer looks like a blank page. Web 68 (+1, `DashboardBindingTests.Dashboard_shows_loading_indicator_while_data_loads`).

# Sprint 67 Notes (Source Verification: View the Exact Passage in the Document)

- Active sprint: `docs/sprints/Sprint-67 - Source Verification View in Document.md` (created 2026-08-13). Sprint 66 complete/committed/pushed. Promotes `docs/backlog/Source-Verification-View-in-Document.md`.
- **Complete 2026-08-13**: all 6 items implemented, tested, and pushed. End-users can now verify an answer in the source without downloading it.
- **Chunk source locator (item 1)**: `Chunk` gains nullable `PageNumber`/`OffsetInPage` (optional ctor params, non-breaking). `ChunkingPipeline.Chunk` gains a page-boundary overload (`IReadOnlyList<TextPage>? pages`; `TextPage(PageNumber, StartOffset, Length)` in `RAGS.Abstractions/Models/TextPage.cs`); a chunk straddling a page boundary is stamped with the page it starts on. `UploadedFileTextExtractor` gains a **page-aware PDF path** (UglyToad.PdfPig, restricted feed `0.1.9-alpha001-patch1`) that builds the normalized text page-by-page so page offsets stay valid; `IsPdf` is public static. Embeddings schema gains `page_number` (idempotent migration `2026-08-13-embeddings-page-number.sql` + `init.sql` + `PgVectorSchema`); `PgVectorStore` persists/returns it. Pre-existing rows get locators via the lightweight reembed flow.
- **Preview endpoint (item 2)**: `GET /api/files/{id}/preview` (optional `?version=`) in `FilesController` streams the original blob inline — PDF → raw PDF bytes (`enableRangeProcessing: true`), text/docx → `FileTextPreviewResponse` (fileName, contentType, text, pages), unsupported → 415. Resolves metadata by file id alone via `IMetadataRepository.GetByFileIdAsync` (default-impl no-op on the interface; PostgreSQL override).
- **In-app document viewer (item 3)**: `Pages/Document/View.razor` at `/document/{id}` with `Page`/`Chunk`/`Version` query params. PDF renders via `window.renderPdf` (PDF.js v3.11.174 from unpkg, text layer enabled); non-PDF renders per-page `<section class="text-page">` sections (or a plain `<pre>` when no page markers). `RepositoryApiClient.PreviewAsync` returns `FilePreviewClientResult` (PDF stream or text+pages).
- **Passage highlight + auto-scroll (item 4)**: the chunk's leading phrase is highlighted in the PDF.js text layer or the text preview (`<mark class="passage-highlight">`); scroll-to-highlight with a page-jump fallback (`#page-N`) when the text doesn't align between extraction and render — never a hard error.
- **Wire-through (item 5)**: Search Center result cards render a "View in document (p. N)" button linking to `document/{sourceId}?page=&chunk=<leading phrase>`. Copilot answers carry `ChatMessage.Citations` (`ChatCitation(Number, SourceId, PageNumber, LeadingPhrase)`), populated by `RetrievalAugmentedPromptBuilder.BuildCitations` (same ranked → grouped-by-source → sequential numbering as the prompt's context blocks); `Index.razor` `LinkCitations` turns `[N]` markers into `<a class="copilot-citation">` viewer links.
- **Tests (item 6)**: RAGS 293 (+3, `RetrievalAugmentedPromptBuilderTests` — BuildCitations numbering/phrase/empty) / Repository 134 (+4, `FilesControllerTests` — preview PDF/text/415/404) / Web 76 (+8, `DocumentViewerBindingTests` — viewer route/params, PDF/text renderers, highlight/scroll, CSS, index.html PDF.js, Search Center link, Copilot citation links, `RepositoryApiClient.PreviewAsync`) / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): hard-refresh `/search` + `/copilot` for a live visual check; optional Docker smoke pass (upload a PDF → search → open the passage in `/document/{id}`).

# Sprint 68 Notes (Query Expansion for Acronyms)

- Active sprint: `docs/sprints/Sprint-68 - Query Expansion for Acronyms.md` (created 2026-08-13). Sprint 67 complete/committed/pushed (`9cbd886`). Promotes `docs/backlog/Query-Expansion-for-Acronyms.md`.
- **Complete 2026-08-13**: all 3 items implemented, tested, and pushed. Fixes a user-reported retrieval miss: a broad Copilot question ("provide a summary of RFP opportunities related to AI") missed a fully-ingested AI RFP whose content is phrased as "Generative AI"/"GenAI" disclosure clauses, while a specific follow-up found it immediately — the two-letter acronym "AI" does not reliably connect to "Artificial Intelligence" in the local embedding model.
- **`QueryExpander` (`src/RAGS.Application/QueryExpander.cs`)**: static class with a public `Expansions` dictionary (17 domain acronyms: AI, GenAI, RFP, RFI, ML, LLM, NLP, API, SOW, SLA, KPI, POC, MVP, OCR, PDF, SQL, RAG) and `Expand(string)` — a single-pass, longest-first, word-boundary regex (`\b(GenAI|AI|…)\b`, IgnoreCase, Compiled) that appends the expansion after each standalone acronym. The original token is always kept; an expansion's own text is never re-scanned. **Extend the dictionary, don't add expansion logic elsewhere.**
- **Embedding vs keyword split**: `RagsService.RetrieveAsync` embeds `QueryExpander.Expand(request.Query)` but the **keyword fallback keeps the original query** — `PgVectorStore.SearchKeywordAsync` is a whole-string `ILIKE '%query%'` match, so the expanded phrase would match nothing. Keep that split if you touch the retrieval path.
- **Tests**: RAGS 302 (+9) — `QueryExpanderTests` (7: expansion keeps the original token, case-insensitivity, no in-word expansion, multi-acronym, longest-first "GenAI" over "AI" with no cascade, null/whitespace, no-op for acronym-free queries) + `RagsServiceTests` (+2: `RetrieveAsync_embeds_expanded_query_for_acronyms` via a `RecordingEmbeddingProvider`, `RetrieveAsync_keyword_fallback_uses_original_query` via `FakeVectorStore.LastKeywordQuery`). Foundation 55 / Repository 134 / Web 76 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): hard-refresh `/search` + `/copilot`, then re-ask the broad question to confirm the AI RFP is now retrieved.

# Sprint 69 Notes (Ingestion Status in the Repository Browser)

- Active sprint: `docs/sprints/Sprint-69 - Ingestion Status in Repository Browser.md` (created 2026-08-13). Sprint 68 complete/committed/pushed (`3a77fe5`). Promotes `docs/backlog/Ingestion-Status-in-Repository-Browser.md`.
- **Implemented 2026-08-13**: all 4 items complete, tests green. Fixes a user-reported failure: a document (CMP 2026 – 3. RFP Analysis.docx) was uploaded and listed in the Repository Browser but its ingestion job failed, so it had no embeddings and was invisible to retrieval — the user discovered it only after a long Copilot debugging session. Upload and ingestion are separate steps; nothing in the UI surfaced the difference.
- **Ground truth is embeddings, not job status**: a source is "ingested" when it has ≥1 embedding in the durable embeddings table; ingestion jobs are in-memory and lost on container restart, so they are never the signal. `FileMetadata.Ingested` is computed as `ChunkCount is > 0`.
- **Chunk-count query (item 1)**: `IVectorStore.GetChunkCountsAsync(IReadOnlyList<Guid> sourceIds, CancellationToken)` returns `Result<IReadOnlyDictionary<Guid, int>>` with a **default no-op impl** (empty map) so existing fakes keep compiling; `PgVectorStore` overrides with a grouped `SELECT e.source_id, COUNT(*) FROM embeddings e WHERE e.source_id = ANY(@SourceIds) GROUP BY e.source_id`.
- **API stamping (item 2)**: `FileMetadata.ChunkCount` (int?) + computed `Ingested`; `SearchController` (Repository.API) injects `IVectorStore` — it already hosts both modules, so no cross-module dependency is introduced — and after a successful search stamps the current page's files (0 when the source is missing from the map).
- **UI badge (item 3)**: `Browse.razor` gains an **Ingestion** column — green `text-bg-success` **Ingested** badge (tooltip "N chunk(s) embedded — retrievable") or amber `text-bg-warning` **Not ingested** badge (tooltip "No embeddings found — the ingestion job may have failed or not completed").
- **Tests (item 4)**: Repository 137 (+3) — `SearchControllerTests` (stamps chunk counts from the vector store: 42 → Ingested, 0 → Not ingested; marks missing sources as not ingested; returns BadRequest on use-case failure). Web 79 (+3) — `BrowseBindingTests` (Ingestion column header, Ingested/Not ingested badges, tooltip text). RAGS 302 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds. Residual manual (user-side): hard-refresh `/browse`; the CMP 2026 – 3. RFP Analysis.docx row should now show an amber **Not ingested** badge, confirming the diagnosis. Re-upload it (or run a repair job) to turn it green.
- **Post-sprint refinement (2026-08-14)**: the badge is now **three-state** — blue **Processing** (`FileMetadata.IsProcessing`, stamped by `SearchController` via the new `IIngestionJobService.HasActiveIngestion(sourceId)`; true while an upload/rag/graph/lazy-graph content job for the source is active **or** a global re-embed/repair job is running), green **Ingested** (no active job, ≥1 embedding), amber **Not ingested** (no active job, 0 embeddings). Tooltips state the embeddings-only scope — graph/taxonomy/wiki-brief readiness is a documented follow-up. Repository 138 (+1) / Web 81 (+2) / RAGS 302 (fakes updated for the new interface member); build 0 errors.
- **Post-fix — invisible badges (2026-08-14)**: the vendored Bootstrap is **v5.1.0** (no `text-bg-*` utilities, which are 5.2+), so the Ingestion badges rendered white-on-white (blank column, header visible). Fixed to the app convention: `bg-info text-dark` / `bg-success` / `bg-warning text-dark` in `Browse.razor`, and `bg-light text-dark border` for the two Dashboard stat badges. Web 81 green; build 0 errors. **Gotcha: never use `text-bg-*` in this app.**

# Sprint 70 Notes (Normalized Lexicon / Grounded Semantic Extraction)

- Active sprint: `docs/sprints/Sprint-70 - Normalized Lexicon (Grounded Semantic Extraction).md` (created 2026-08-14). Sprint 69 complete/committed/pushed (`debdfeb`). Promotes `docs/backlog/Normalized-Lexicon-for-Term-Resolution.md`.
- **Complete 2026-08-14**: all 4 items implemented, tested, and green. Fixes a project-owner-reported retrieval miss: Copilot missed the RFP due dates even though they were on the first page of the source ("Proposal Due Date: February 24, 2022, at 2:00 p.m. EST"); a second source phrased the same concept differently ("Bid due: August 26, 2026, 2:00 PM Pacific Time") and was missed too. Diagnosis: **not a bug** — the systematic limit of retrieval (vector similarity + whole-string ILIKE both fail on terse, varied-phrase facts). The fix is a **canonical lexicon** applied on both sides of retrieval, **semantic** (LLM understands paraphrase and novel terminology) **without losing fidelity to the source** (nothing stored that is not verifiable in the text).
- **Lexicon data model + repository (item 1)**: `LexiconConcept` (Key, Label, Aliases, ValuePattern `date`/`currency`/`number`/`text`, optional TemplateScope) / `DocumentFact` / `ProposedFact` / `UnmappedTerm` + `LexiconSeedData.Defaults` (5 seeded concepts: due_date, budget, page_limit, vendor, submission) in `RAGS.Abstractions/Models`. `ILexiconRepository` → `PostgreSqlLexiconRepository` (Dapper + `PostgreSqlConnectionFactory`; `SaveFactsAsync` = delete-then-insert replace-on-reingest). Tables `lexicon_concepts`/`lexicon_aliases`/`document_facts`/`lexicon_unmapped_terms` in `scripts/init.sql` + idempotent migration `2026-08-14-lexicon-and-facts.sql` (seeded, `ON CONFLICT DO NOTHING`); `PostgreSqlLexiconSchema` + `PostgreSqlLexiconSchemaInitializer` (hosted) self-heal at startup. **The SQL seed mirrors `LexiconSeedData` — `LexiconBindingTests` (Web.UnitTests) enforces it, so keep the three in sync when adding a concept.**
- **Grounded fact extraction (item 2)**: `IFactProposer` → `SemanticKernelFactProposer` (LLM pass quoting the **exact source span**; returns empty on any failure — never fabricates into the pipeline) → `FactVerifier` (the **fidelity gate**: span must exist in the extracted text via `WhitespaceCollapser` whitespace-tolerant match, and the value must parse via `FactValueParser` against the concept's value pattern; anything else is dropped) → `GroundedFactExtractionService` (normalizes to the canonical concept, anchors page/offset via the Sprint 67 `TextPage` machinery, persists `document_facts`, records concept hints matching no known key/alias as unmapped terms). Wired best-effort into `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` (optional `IFactExtractionService?` ctor param; try/catch — a failure never blocks ingestion).
- **Query-time concept expansion (item 3)**: `LexiconExpander` (RAGS.Application/Lexicon) appends a matched concept's label + full alias family to the embedding query (word-boundary, longest-first, original query always kept); `RagsService.RetrieveAsync` applies it **after** `QueryExpander` (acronyms) when an `ILexiconProvider` is present. `ILexiconProvider` → `LexiconProvider` (cached, invalidatable; failed loads not cached) is an **optional ctor param** on `RagsService` — existing fakes compile without it. The keyword fallback keeps the original query (Sprint 68 contract).
- **Tests (item 4)**: RAGS 338 (+36) — `LexiconExpanderTests` (6), `FactValueParserTests` (8), `FactVerifierTests` (7), `GroundedFactExtractionServiceTests` (5), `RagsServiceTests` (+2: embeds lexicon-expanded query, skips when provider absent). Web 84 (+3) — `LexiconBindingTests` (tables in migration + init, seed mirrors `LexiconSeedData`). Repository 138 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): `docker compose up -d --build` (or apply the migration once), re-upload the CMP 2026 RFP (or run a repair job) so grounded facts are extracted, then re-ask "What is the submission due date for the CMP 2026 RFP?" — the query now embeds the due-date alias family.
- **Out of scope (documented follow-ups)**: admin settings panel for the lexicon (browse/add aliases, review unmapped terms — the governance *surface*; the loop's data collection is in this sprint), surfacing facts in Browse/Copilot/document viewer, per-template concept scoping enforcement, unverified LLM extraction.

# Sprint 71 Notes (Lexicon Governance and Glossary Surface)

- Active sprint: `docs/sprints/Sprint-71 - Lexicon Governance and Glossary Surface.md` (created 2026-08-14). Sprint 70 complete/committed/pushed (`229229d`). Promotes `docs/backlog/Lexicon-Governance-and-Glossary-Surface.md` — the project-owner-directed follow-up to Sprint 70. Closes the governance loop: a glossary/lexicon for a given document domain that **end users can view and download** and **admins can extend and manage**.
- **Complete 2026-08-14**: all 5 items implemented, tested, and green. Two surfaces, one sprint: admin management (growth mechanism) + end-user read-only glossary (surfacing). The connective tissue is **`template_scope` enforcement** — a concept with a template scope applies only to documents of that template; unscoped concepts stay global.
- **Admin API + repository (item 1)**: `ILexiconRepository` + `PostgreSqlLexiconRepository` gain `DeleteConceptAsync`, `ResolveUnmappedTermAsync`, `GetAllFactsAsync`; `GetUnmappedTermsAsync` returns **pending only**. `lexicon_unmapped_terms` gains `status` (`pending`/`resolved`) + `resolved_at` — idempotent migration `2026-08-14-lexicon-unmapped-status.sql` + `init.sql` + `PostgreSqlLexiconSchema`. `LexiconController` (Repository.API): `GET /api/lexicon/concepts?template=` (authenticated read), `PUT`/`DELETE /api/lexicon/concepts` (admin), `GET /api/lexicon/unmapped` + `POST /api/lexicon/unmapped/resolve` (admin). **Admin writes call `ILexiconProvider.Invalidate()`** so edits take effect on the next retrieval read. Admin UI: `Pages/Lexicon/Index.razor` at `/lexicon` (admin-gated via `AuthorizeView Roles="Administrator"`, nav entry in the Management group) — browse concepts + aliases, add/edit a concept (key, label, value pattern, template scope, comma-separated aliases), delete a concept, dismiss pending unmapped terms.
- **`template_scope` enforcement (item 2)**: `FactVerifier.Verify` and `IFactExtractionService.ExtractAsync` / `GroundedFactExtractionService` take an optional `templateName`; `FactVerifier.IsApplicable(concept, templateName)` is the single source of truth (unscoped concepts always apply). `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` passes the matched canonical template name. **Out-of-scope hints behave like unknown hints**: the verifiable value is still stored as raw text under the raw hint (the fidelity gate passes) and the hint is recorded as unmapped for admin review. Query-time `LexiconExpander` stays global (a query has no template context).
- **End-user glossary (item 3)**: `GET /api/lexicon/glossary?template=` joins `document_facts` with source names via `IMetadataRepository.GetByFileIdAsync` (cross-module pattern from `SearchController`; `IMetadataRepository?` optional ctor param). `Pages/Glossary/Index.razor` at `/glossary` — read-only per-domain glossary (concept, label, aliases, value pattern, verified facts with value/source/page), template filter (server-side `?template=`), download buttons; nav entry after Copilot (`icon-glossary` in `NavMenu.razor.css`). Web mirrors the API DTOs as `GlossaryClientResponse`/`GlossaryFactClient` records in `RepositoryApiClient.cs`.
- **Download/export (item 4)**: `GET /api/lexicon/glossary/export?format=csv|json&template=` returns `File(...)` (`text/csv` / `application/json`); Web buttons via `RepositoryApiClient.ExportGlossaryAsync` → `DotNetStreamReference` + `downloadFileFromStream`.
- **Tests (item 5)**: RAGS 343 (+5) — `FactVerifierTests` template-scope cases (4: scoped applies on match, out-of-scope → unmapped text, no-template → unmapped text, unscoped applies anywhere) + `GroundedFactExtractionServiceTests` pass-through (1). Repository 151 (+13) — `LexiconControllerTests` (13: concepts read/filter, upsert/delete + provider invalidate, unmapped list/resolve, glossary source-name join + scope filter, CSV/JSON export). Web 90 (+6) — `LexiconBindingTests` (unmapped status columns in migration/init/schema, glossary page route + download buttons, glossary nav entry, admin `/lexicon` page + admin gate, admin nav entry, client methods). Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): `docker compose up -d --build` (fresh DB gets the `status`/`resolved_at` columns from init.sql; an existing deployment needs the migration `2026-08-14-lexicon-unmapped-status.sql` applied once, or the API's schema initializer self-heals at startup), then hard-refresh `/glossary` (and `/lexicon` for the admin surface) for a live visual check.
- **Out of scope (documented follow-ups)**: changing the fidelity gate or the propose → verify → normalize → persist pipeline (Sprint 70), per-user lexicons, machine translation / cross-language normalization, replacing the taxonomy/ontology entity machinery, editing `LexiconSeedData`/SQL-seed defaults from the UI (admin edits override at runtime; the seed stays the code-owned default).
- **Post-sprint (2026-08-14)**: user-reported gaps fixed. (1) **Admin edit flow** — `/lexicon` concept cards gain an **Edit** button that pre-fills the form (key, label, value pattern, template scope, aliases joined by comma) with an inline "replaces the current alias set" hint + a **New concept** reset; the upsert stays full-replace, so editing preserves existing aliases and lets an admin add a synonym to an existing concept. (2) **Plural-tolerant expansion** — `LexiconExpander.BuildAliasRegex` now also matches each alias's plural form (append `s` to the last word unless it already ends in `s`), so "end dates" / "due dates" / "deadlines" trigger expansion; the old "deadlines must not expand" test was inverted to the new intended behavior. (3) **Seed** — `due_date` gains the `end date` alias in `LexiconSeedData` + `init.sql` + the migration. RAGS 345 (+2) / Web 91 (+1).

# Sprint 74 Notes (UI List Scrolling and Collapsible Forms)

- Active sprint: `docs/sprints/Sprint-74 - UI List Scrolling and Collapsible Forms.md` (created 2026-08-15). Sprint 73 complete/committed/pushed (`1cdb2d8`). Promotes `docs/backlog/UI-List-Scroll-and-Collapsible-Forms.md` — the project-owner-directed UI pass: long lists should scroll inside their own panel (not force the whole panel/page to scroll), the `/lexicon` "Add concept" form should be **collapsed by default** (expand on demand), and Add concept + Unmapped terms belong in a **tab control**; the ideas apply to every page with a similar control layout. **Web-only — no API, backend, or schema changes.**
- **Scrollable-list convention (item 1)**: `wwwroot/css/app.css` gains `.list-scroll` and `.table-scroll` utilities — `max-height: 70vh; overflow-y: auto; overscroll-behavior: contain;`. A short list keeps natural height; a long list scrolls internally. `.table-scroll` composes with Bootstrap `table-responsive` on the same element (which already handles `overflow-x`). When adding a new Web list that can outgrow the viewport, wrap its container in `.list-scroll` (or `.table-scroll` for a table) instead of letting the panel grow.
- **`/lexicon` tab control + collapsible form (item 2)**: `Pages/Lexicon/Index.razor` right column is now a `nav-tabs` control with **Add concept** / **Unmapped terms** tabs. Default active tab = **Unmapped terms**, so the Add/Edit form is **collapsed on page load**. `EditConceptAsync` sets `_activeTab = LexiconTab.AddConcept` (auto-opens the form for editing) and the tab label flips to "Edit concept" while `_editingKey` is set. Tab switching is Blazor state (a `LexiconTab` enum + conditional render) — **not** Bootstrap collapse/tab JS. Concepts list + Unmapped list wrapped in `.list-scroll`.
- **Applied to similar pages (item 3)**: Glossary — Concepts cards in `.list-scroll`, Verified-facts table wrapper `table-responsive table-scroll`; Wiki — `.wiki-index` sidebar gains `max-height: calc(100vh - 16rem); overflow-y: auto;` (scoped CSS in `Wiki.razor.css`); Governance — Roles/PII/Policies lists in `.list-scroll`, Audit table `table-responsive table-scroll`; Taxonomy — Categories list in `.list-scroll`; Ontology — Entities list in `.list-scroll`, Relationships table `table-responsive table-scroll`.
- **Left in page flow on purpose**: paginated/bounded lists (Browse, Metadata picker, Dashboard, Upload), primary-content lists where whole-page scroll is the reading model (Search Center results, Document viewer), and surfaces already height-constrained (Copilot, Graph Explorer).
- **Tests + docs (item 4)**: Web **125** (+10) — `LexiconBindingTests` (+4: tab control markup, collapsed-by-default `_activeTab = LexiconTab.Unmapped`, Edit switches to the form tab, lists use `.list-scroll`) + new `ListScrollBindingTests` (+6: `app.css` defines `.list-scroll`/`.table-scroll` with `overflow-y: auto` + `max-height`; Glossary/Governance/Taxonomy/Ontology use them; Wiki.razor.css scrolls `.wiki-index`). Foundation 55 / Repository 157 / RAGS 361 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Backlog item archived. Residual manual (user-side): `docker compose up -d --build`, then hard-refresh `/lexicon` (tabs; Add concept collapsed by default; long lists scroll in place) and `/glossary` `/wiki` `/governance` `/taxonomy` `/ontology` for a live visual check.
- **Out of scope (documented follow-ups)**: Bootstrap collapse/tab JS widgets (Blazor state is used instead), re-architecting pages that already constrain their own scroll regions (Copilot, Graph Explorer), making paginated/bounded lists scroll, turning Search Center results or the Document viewer into internal scroll regions, any backend/controller/schema change.
- **Post-sprint (2026-08-15): one-click Promote in Unmapped terms.** Each pending unmapped term row in `/lexicon` gains a **Promote** button next to **Dismiss** (`PromoteTermAsync` in `Pages/Lexicon/Index.razor`): upserts a concept (`Key` = `Label` = the term, `ValuePattern = "text"`) then resolves the pending record — one action, the term graduates from unmapped to canonical; refine value pattern/scope/aliases afterwards via **Edit** on the concept card. The `_message` alert moved above the tab content so Promote/Dismiss feedback shows on both tabs. Web 127 (+2) — `Lexicon_unmapped_terms_have_a_promote_button` + `Lexicon_promote_upserts_a_concept_and_resolves_the_term` (`LexiconBindingTests`). The Concepts list on `/lexicon` **and** `/glossary` was already `.list-scroll`-wrapped from Items 2 + 3 — confirmed, no change.

# Sprint 73 Notes (Ingestion Guard-Rails and Summaries Readability)

- Active sprint: `docs/sprints/Sprint-73 - Ingestion Guard-Rails and Summaries Readability.md` (created 2026-08-15). Sprint 72 complete/committed/pushed (`0950dad`). Promotes `docs/backlog/Ingestion-Guard-Rails-Durable-Jobs-and-Self-Healing.md` — the project-owner-approved **core fix** for the 2026-08-14 operational incident (the Repository Browser flipped all three documents from **Ingested** to **Not ingested** after an API rebuild). The durable job queue (backlog item 1) is **explicitly deferred**; this sprint ships items 2 (write-new-then-swap) + 3 (startup reconciliation) plus the project-owner-directed Summaries readability work.
- **Write-new-then-swap ingestion (A1 + A2)**: `IVectorStore.ReplaceSourceAsync(Guid sourceId, IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken)` has a **default delete-then-store implementation** (so fakes keep compiling — same pattern as `GetChunkCountsAsync`); `PgVectorStore` overrides it with delete + insert in **one transaction** (reuse the `StoreBatchAsync` insert path; the old `DeleteBySourceAsync` ran on a separate connection with no transaction). `RagsService.IngestAsync` now chunks + embeds first (builds the full item set), then calls `ReplaceSourceAsync` — the old `DeleteBySourceAsync` → `StoreBatchAsync` sequence is gone. **An interrupted re-ingestion leaves the old embeddings intact — never zero.**
- **`last_ingested_at` marker (A3 + A4)**: `file_metadata.last_ingested_at TIMESTAMPTZ NULL` (`init.sql` + idempotent migration `2026-08-15-last-ingested-at.sql`); `FileMetadata.LastIngestedAt` (`DateTimeOffset?`); `IMetadataRepository.SetLastIngestedAtAsync` (mirror `SetTemplateAsync`). `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` stamps it on **completion** (success or no-text), **never on failure** — a failed ingest stays NULL so the sweep retries it. Distinguishes "never successfully ingested" (NULL → reconciliation candidate) from "checked and non-ingestable" (set → leave alone).
- **Startup reconciliation sweep (A5)**: `IMetadataRepository.GetSourcesMissingIngestionAsync` returns `file_id`s where `last_ingested_at IS NULL AND NOT EXISTS (SELECT 1 FROM embeddings e WHERE e.source_id = file_metadata.file_id)` (embeddings is in the same PostgreSQL DB). `IngestionJobService.EnqueueRagsRepairForSources(IReadOnlyList<Guid>)` + a new `IngestionJobWorkItem` kind (`RagsRepairSources`, `SourceIds` list) + a runner that iterates the fixed source list calling `EnsureIngestedAsync` (reuse the per-source loop from `RunRagsRepairJobAsync`, minus the query scan). `IngestionReconciliationService` — **new** `BackgroundService`: after a short startup delay (default 10s), calls `GetSourcesMissingIngestionAsync`; if any, `EnqueueRagsRepairForSources(...)` and logs what it enqueued. Runs once. Registered via `AddHostedService` in `Program.cs`. **This auto-repairs the currently-broken documents on the next API restart — no manual SQL.**
- **Summaries readability (B1 + B2)**: `SummaryResultFormatter` (`src/Aletheia.Web/Services/SummaryResultFormatter.cs`) — static helper: `IsSummary(SearchResult)` → `RetrievalStrategy` starts with `"summary-"` (GraphRAG synthesized summaries; LazyGraphRAG fallback results are real `lazy-*` passages and keep the standard semantic card treatment); `Body(string content)` → strips the `Entity Summary: {label}` / `Community Summary: {name}` prefix line and the trailing `Structured GraphRAG Context` dump, trims; `ShowViewInDocument(SearchResult)` → `false` for summaries (no single verbatim passage; the current link is dead). `SearchCenter.razor` — for summary results: a **"Summaries" badge** (`badge bg-info text-dark` — Bootstrap 5.1 convention, never `text-bg-*`), the body rendered through **`MarkdownRenderer.ToHtml`** (shared renderer) instead of raw `<p class="card-text">`, a **Sources** list from `result.Citations` (document names), **no "View in document" button**, and no `Chunk N from source <guid>` footer. Semantic / LazyGraphRAG-fallback cards are untouched. `.summary-body` scoped CSS in `SearchCenter.razor.css` (headings constrained to card size, list padding, `p:last-child` margin 0). **The backend content is left untouched** ("internally they can stay as they are").
- **Tests (A7 + B3)**: RAGS — `IngestAsync_replaces_source_embeddings_atomically` + `IngestAsync_replaces_existing_embeddings_on_reingest` (`RagsServiceTests`), `ReplaceSourceAsync_replaces_embeddings_atomically` (`PgVectorStoreTests`). Repository — `RagsRepairForSources_runs_ingestion_for_each_targeted_source` (`IngestionJobServiceRoutingTests`), `IngestionReconciliationServiceTests` (enqueues targeted repair when sources missing; no-op when nothing missing), `EnsureIngestedAsync_stamps_last_ingested_at_on_success` + `EnsureIngestedAsync_does_not_stamp_last_ingested_at_on_failure`. Web — `SummaryResultFormatterTests` (`IsSummary` true for `summary-*` / false for `lazy-*` and `semantic`; `Body` strips prefix + context dump; `ShowViewInDocument` false for summaries). All suites green; `dotnet build Aletheia.slnx` succeeds. Backlog item archived (item 1 durable queue explicitly deferred). Residual manual (user-side): `docker compose up -d --build` (fresh DB gets `last_ingested_at` from init.sql; an existing deployment needs the migration `2026-08-15-last-ingested-at.sql` applied once, or the API's schema initializer self-heals at startup), then **restart the API** — the reconciliation sweep logs the 3 sources and auto-repairs them; hard-refresh `/browse` → rows show **Ingested** (green), not "Not ingested"; hard-refresh `/search` → Summaries results show a "Summaries" badge, a readable markdown body, a Sources list, and no dead "View in document" button.
- **Out of scope (documented follow-ups)**: the **durable job queue** (backlog item 1 — explicitly deferred; the swap + sweep make the *data* survive an interruption even though the in-memory *job* does not), job stage tracking + resume (backlog item 4), changing the ingestion pipeline's fidelity guarantees (Sprint 70), changing how summaries are produced, making the job queue distributed or multi-host.

# Sprint 72 Notes (Search UX Clarity — Semantic vs Summaries)

- Active sprint: `docs/sprints/Sprint-72 - Search UX Clarity (Semantic vs Summaries).md` (created 2026-08-15). Sprint 71 complete/committed/pushed (`1d5b06c`). Promotes `docs/backlog/Search-UX-Clarity-Semantic-vs-Summaries.md` — the project-owner-directed follow-up from the 2026-08-15 product/UX review. Three clarity gaps: (1) the Browse "Search files..." box does not say what it searches; (2) the GraphRAG/LazyGraphRAG summary search is invisible to end users (mode buttons gated behind `FeatureFlags:ShowInternalSearch`, default false); (3) when summaries get created and how they are managed is opaque, which matters on large KBs where summaries are load-bearing.
- **Complete 2026-08-15**: all 6 items implemented, tested, and green. **Naming constraint (explicit)**: "Graph" / "LazyGraph" never appear in user-facing copy — they are the same product, only the production differs (batch-built at ingest vs. built on demand at query time). Use gentle words like "summaries"; an info icon (click/hover) gives detail for the curious.
- **Summaries retrieval service (item 1)**: `ISummariesRetrievalService` (`RAGS.Abstractions.Interfaces`) — `RetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken, IReadOnlyList<Guid>? sourceIds = null)`. `SummariesRetrievalService` (`RAGS.Application/GraphRAG`) — GraphRAG-first, LazyGraphRAG-fallback: calls `IGraphRagService.RetrieveAsync`, returns its results when present, otherwise falls back to `ILazyGraphRagService.RetrieveAsync` (empty or failure). `GraphRagService.RetrieveAsync` already has an internal fallback chain (summaries → lazy enrichment → graph-aware → semantic), so the service-level fallback triggers only on empty/failure. Both engines carry the Sprint 64 `sourceIds` theme scope.
- **Summaries status service (item 2)**: `ISummariesStatusService` — `GetAsync(CancellationToken)` returning `Result<SummariesStatusSnapshot>`. `SummariesStatusSnapshot`/`SourceSummaryStatus` (`RAGS.Abstractions.Models`) — graph-level counts (GraphExists, NodeCount, EntityCount, CommunityCount, SummarizedCommunityCount, SourceCount) + per-source breakdown (SourceId, SourceName, EntityCount, CommunityCount, SummarizedCommunityCount). `SummariesStatusService` reads all graph nodes/edges via `IGraphProvider`, classifies Source/Entity/Community/Chunk nodes, builds an entity→source map, aggregates per-source via `has_member` edges, counts summarized communities via a non-empty `summary` property (persisted during GraphRAG ingest). **Note**: `SourceSummaryStatus` count properties are `get; set;` (the service mutates them incrementally — `init` caused CS8852).
- **SummariesController (item 3)**: `[Route("api/summaries")]`, `[Authorize]` in `Repository.API/Controllers`. `GET retrieve` (query, topK, themes) — **NOT gated by `ShowInternalSearch`** (user-facing mode); resolves themes→sourceIds via `IKnowledgeThemeService` (RagsController pattern) and calls `ISummariesRetrievalService`. `GET status` — `[Authorize(Roles = RoleDefinitions.Administrator)]` (operator surface). Both services registered as singletons in `Program.cs` (they depend on the singleton `IGraphProvider`).
- **Search Center modes (item 4)**: `SearchCenter.razor` — always-visible **Semantic** / **Summaries** mode buttons (`SetMode` allows `"semantic" or "summaries"` when internal search is off); WRAGS/GraphRAG/LazyGraphRAG stay behind `@if (ShowInternalSearch)`. Per-mode info icon (`&#9432;`, `ModeInfoTitle`/`ModeInfoDetail`/`ToggleModeInfo`/`_showModeInfo`). Summaries mode calls `RepositoryApiClient.SummariesRetrieveAsync` (themes forwarded). Admin **Graph summaries** block (`<AuthorizeView Roles="Administrator">`) — coverage counts from `GetSummariesStatusAsync` + **Re-cluster communities** button (`DetectClustersAsync` → `POST /api/communities/detect`). Copy: "Summaries are generated from the connections between your documents — pre-built during GraphRAG ingestion, or on demand when you run a Summaries search."
- **Browse search caption (item 5)**: `Browse.razor` — caption under the search box "Searches file metadata (file name) — not document content." + info icon (`&#9432;`, `ToggleSearchInfo`/`_showSearchInfo`) explaining the metadata fields and pointing to Search Center ("matches by meaning across document chunks"; **Summaries** mode answers from the higher-level connections between documents). The search box is a plain PostgreSQL metadata query (`file_name ILIKE '%query%'`), not content search.
- **Tests (item 6)**: RAGS 359 (+14) — `SummariesRetrievalServiceTests` (4: uses graphrag when present, falls back when empty, falls back when fails, forwards sourceIds), `SummariesStatusServiceTests` (4: empty graph, counts communities/summarized, per-source aggregation via has_member, community counted once per source), `SummariesControllerTests` (6: retrieve OK/fail/themes, status OK, status admin-only attribute, retrieve not admin-only). Web 100 (+9) — `SearchCenterBindingTests` (6: Semantic/Summaries buttons, internal modes gated behind ShowInternalSearch, mode info icon, summaries-mode explanation, admin graph-summaries block, summaries-mode calls SummariesRetrieveAsync) + `BrowseBindingTests` (3: metadata-filter caption, info icon, points to Search Center). Repository 151 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds. Backlog item archived. Residual manual (user-side): `docker compose up -d --build`, then hard-refresh `/search` (Semantic/Summaries toggle + admin Graph summaries block) and `/browse` (search caption + info icon). No schema migration — this sprint is API + Web only.
- **Out of scope (documented follow-ups)**: a new dedicated surface for browsing summaries (the mode toggle is the surface), renaming the internal GraphRAG/LazyGraphRAG services/controllers/routes (internal code/docs may keep the terms; only user-facing copy changes), changing how summaries are produced, per-document summary status in the Repository Browser (the Sprint 69 Ingestion column pattern — this sprint ships the admin graph-level status block instead), making summary generation distributed or multi-host.