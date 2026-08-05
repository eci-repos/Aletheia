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
- Match every ingested document to a canonical template in `docs/doc-templates` (the ingestion gate enforces this); when introducing a new document kind, add its template first

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
