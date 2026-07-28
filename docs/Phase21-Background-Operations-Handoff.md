# Phase 21 Background Operations Handoff

Date: 2026-07-26

## Current State

Phase 21 now includes RAGS v2 intelligence and the first operational background-ingestion slice.

Implemented behavior:

- `POST /api/files/upload` stores the Repository artifact and returns quickly with `IngestionStatus = Queued` and `IngestionJobId`.
- Long-running extraction and RAGS ingestion run in the API background worker.
- Upload jobs now use lightweight graph seed indexing by default: taxonomy hints, source nodes, chunk nodes, and `has_chunk` edges are persisted without document-wide LLM entity extraction or summary generation.
- GraphRAG retrieval now performs bounded lazy enrichment for the top relevant chunks when stored summaries are absent. It creates typed entity nodes, `found_in`/`mentioned_in` edges, bounded relationships, entity summaries, marks touched chunks with `lazyEnriched`, and writes discovered entities/relationships back to PostgreSQL Taxonomy/Ontology.
- `GET /api/jobs` lists recent ingestion jobs.
- `GET /api/jobs/{jobId}` returns one job snapshot.
- `POST /api/jobs/rags/ingest`, `POST /api/jobs/graphrag/ingest`, and `POST /api/jobs/lazygraphrag/ingest` queue direct content-ingestion jobs.
- The Web Activity panel polls `/api/jobs`, shows active/completed jobs, and renders stage, heartbeat age, approximate percent complete, detail, and failures.
- Search Center now provides four retrieval modes: Semantic, WRAGS, GraphRAG, and LazyGraphRAG. It queues direct RAGS, GraphRAG, and LazyGraphRAG ingestion jobs instead of blocking on a single HTTP request, displays retrieval strategy labels and citations, exposes expansion controls, and surfaces technical API failure details in the page.
- WRAGS is the new name for the LLM Wiki initiative. WRAGS now has durable PostgreSQL-backed wiki pages exposed through `/api/wiki` and a Web UI page at `/wiki`. It searches saved pages first, generates pages from RAGS/GraphRAG/LazyGraphRAG on first miss, can queue explicit regeneration jobs, and renders citations, source/chunk details, scores, ranks, versions, timestamps, lifecycle status, stale warnings, related topics, related-page backlinks, history, and retrieval strategy labels. WRAGS mode is GraphRAG-first with LazyGraphRAG and Semantic fallback.
- WRAGS maturity now includes lifecycle status updates (`Generated`, `Reviewed`, `Approved`, `NeedsReview`, `Stale`), `reviewed_by`/`reviewed_at`, stale flags/reasons, source-change stale detection from linked file metadata, related-topic extraction during page generation, related-page lookup from shared source IDs/topics, editable page bodies, version history, and retrieval-context participation in Search Center and Copilot.
- LazyGraphRAG traversal budget handling was corrected so optional query-time enrichment stops at configured limits instead of incrementing counters past the limit and causing the whole retrieval to fail.
- Copilot assistant messages now include chat completion telemetry: elapsed seconds, estimated prompt/completion tokens, estimated tokens per second, retrieved context count, citation count, retrieval scores, and heuristic alignment confidence.
- Conversational planning system (Sprints 22.1–22.7) now provides plan preview, approval, background execution, durable progress polling, recovery after refresh, and plan-versus-actual telemetry reporting.

## Main Code Paths

- API job orchestration: `src/Repository.API/Services/IngestionJobService.cs`
- API job progress contract: `src/Repository.API/Services/IngestionProgress.cs`
- API job endpoints: `src/Repository.API/Controllers/JobsController.cs`
- Upload queue integration: `src/Repository.API/Controllers/FilesController.cs`
- Knowledge enrichment progress hooks: `src/Repository.API/Services/UploadedContentKnowledgeIndexer.cs`
- GraphRAG query-time lazy enrichment: `src/RAGS.Application/GraphRAG/GraphRagService.cs`
- Lazy Taxonomy/Ontology write-back abstraction: `src/RAGS.Abstractions/Interfaces/ILazyEnrichmentKnowledgeSink.cs`
- Lazy Taxonomy/Ontology write-back implementation: `src/RAGS.Infrastructure.PostgreSQL/Knowledge/LazyEnrichmentKnowledgeSink.cs`
- Copilot chat telemetry: `src/RAGS.Application/SemanticKernel/SemanticKernelCopilotService.cs`
- Chat stats model: `src/RAGS.Abstractions/Models/ChatMessage.cs`
- Copilot stats UI: `src/Aletheia.Web/Pages/Copilot/Index.razor`
- Plan preview UI: `src/Aletheia.Web/Pages/Copilot/PlanPreview.razor`
- Progress panel UI: `src/Aletheia.Web/Pages/Copilot/ProgressPanel.razor`
- Chat planning service: `src/RAGS.Application/Planning/ChatPlanningService.cs`
- Plan approval service: `src/RAGS.Application/Planning/ChatPlanApprovalService.cs`
- Execution engine: `src/RAGS.Application/Planning/ChatExecutionEngine.cs`
- Telemetry service: `src/RAGS.Application/Planning/ChatTelemetryService.cs`
- Progress store abstraction: `src/RAGS.Abstractions/Interfaces/IChatProgressStore.cs`
- In-memory progress store: `src/RAGS.Application/Planning/InMemoryChatProgressStore.cs`
- Chat telemetry model: `src/RAGS.Abstractions/Models/ChatExecutionTelemetry.cs`
- Chat estimate comparison model: `src/RAGS.Abstractions/Models/ChatEstimateComparison.cs`
- API DI registration: `src/Repository.API/Program.cs`
- Web API client job methods: `src/Aletheia.Web/Services/RepositoryApiClient.cs`
- Web activity state: `src/Aletheia.Web/Services/ActivityLogService.cs`
- Web Activity panel rendering/polling: `src/Aletheia.Web/Layout/ActivityPanel.razor`
- Web Activity panel styling: `src/Aletheia.Web/Layout/ActivityPanel.razor.css`
- Upload page queued status: `src/Aletheia.Web/Pages/Upload.razor`
- Search Center queued ingestion: `src/Aletheia.Web/Pages/SearchCenter.razor`
- WRAGS Wiki page: `src/Aletheia.Web/Pages/Wiki.razor`
- WRAGS Wiki styling: `src/Aletheia.Web/Pages/Wiki.razor.css`
- WRAGS navigation: `src/Aletheia.Web/Layout/NavMenu.razor`
- WRAGS API endpoints: `src/Repository.API/Controllers/WikiController.cs`
- WRAGS application service: `src/RAGS.Application/Wiki/WragsWikiService.cs`
- WRAGS abstractions: `src/RAGS.Abstractions/Interfaces/IWragsWikiService.cs`, `src/RAGS.Abstractions/Interfaces/IWikiPageRepository.cs`, `src/RAGS.Abstractions/Models/WikiPage.cs`, `src/RAGS.Abstractions/Models/WikiPageEditRequest.cs`, `src/RAGS.Abstractions/Models/WikiPageHistoryEntry.cs`, `src/RAGS.Abstractions/Models/WikiPageLink.cs`, `src/RAGS.Abstractions/Models/WikiPageStatusUpdate.cs`, `src/RAGS.Abstractions/Models/WikiSearchRequest.cs`
- WRAGS PostgreSQL persistence: `src/RAGS.Infrastructure.PostgreSQL/Wiki/PostgreSqlWikiPageRepository.cs`, `src/RAGS.Infrastructure.PostgreSQL/Wiki/PostgreSqlWikiSchema.cs`, `src/RAGS.Infrastructure.PostgreSQL/Wiki/PostgreSqlWikiSchemaInitializer.cs`
- WRAGS Web API client methods: `src/Aletheia.Web/Services/RepositoryApiClient.cs`
- Search Center API client methods and technical error propagation: `src/Aletheia.Web/Services/RepositoryApiClient.cs`
- LazyGraphRAG traversal budget guardrails: `src/RAGS.Application/LazyGraphRAG/GraphTraversalBudget.cs`
- Focused tests: `tests/RAGS.UnitTests/BackgroundJobs/JobsControllerTests.cs`
- LazyGraphRAG budget regression test: `tests/RAGS.UnitTests/LazyGraphRAG/LazyGraphRagServiceTests.cs`
- Chat execution engine tests: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs`
- Chat telemetry service tests: `tests/RAGS.UnitTests/ChatTelemetryServiceTests.cs`
- Web progress panel tests: `tests/Aletheia.Web.UnitTests/ProgressPanelTests.cs`
- Web plan preview tests: `tests/Aletheia.Web.UnitTests/PlanPreviewTests.cs`

## Validation Already Run

```powershell
dotnet build Aletheia.slnx
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
```

Earlier Phase 21 validation also ran foundation tests and Docker/UI smoke testing.

Docker/UI smoke validation:

- Fresh Docker stack built and started.
- `GET http://localhost:8080/health/live` returned `200`.
- `GET http://localhost:8080/health/ready` returned `200`.
- Web UI login with seeded `admin` succeeded.
- Search Center GraphRAG ingestion queued a background job.
- Activity panel showed one running job with stage `GraphRAG enrichment`, heartbeat age, and approximate progress before the lazy-enrichment change.
- Search Center API smoke passed for Semantic, GraphRAG, and LazyGraphRAG retrieval after direct ingestion.
- Authenticated Search Center UI smoke passed for Semantic, GraphRAG, and LazyGraphRAG retrieval on `http://localhost:8081/search`; GraphRAG returned `GRAPHRAG Results (5)` through `/api/graphrag/retrieve`, LazyGraphRAG returned `LAZYGRAPHRAG Results (3)` through `/api/lazygraphrag/retrieve`, and browser console errors were not observed during the isolated GraphRAG UI check.
- WRAGS maturity smoke passed on Docker: API health returned `200`, `/wiki` returned `200`, temporary wiki pages were searchable, status PATCH updated `Reviewed`, status PATCH updated `Stale` with `IsStale = true`, and related-page lookup returned the expected related page. Temporary smoke rows were deleted after validation.

## Current Runtime Caveats

- Job state is in memory. API restart loses job history and active jobs.
- The background queue is process-local and single-service; it is not distributed.
- Upload jobs copy the request stream to a temp file before queueing; temp files are cleaned after the job finishes or fails.
- Progress is approximate and stage-based. Upload indexing should now be much shorter because it records graph seed nodes instead of doing full document-wide graph summarization.
- Heartbeat updates are intentionally coarse, about every two minutes during long operations plus stage transitions.
- The Activity panel polls every 10 seconds; there is no SSE/WebSocket streaming yet.
- Direct legacy endpoints such as `POST /api/graphrag/ingest` still exist for compatibility and may run synchronously.
- LazyGraphRAG budgets are guardrails, not success criteria. Hitting the LLM/node/relationship/token limit should stop optional expansion and return the best available results; a budget exception during normal retrieval should be treated as a regression.
- WRAGS persists generated wiki page snapshots and now has editable page bodies, version history, basic related-page backlinks, source-aware stale detection, approval/status lifecycle controls, and background regeneration jobs. Remaining maturity work is richer graph-derived backlinks, editorial diff visualization, and durable job persistence for regeneration jobs.
- Taxonomy/Ontology explorers update as query-time lazy enrichment runs. Fresh uploads still start with lightweight topic/source metadata only; entity/relationship richness appears after relevant GraphRAG queries touch the content.
- `ChatCompletionStats` token counts are estimates derived from text length. Replace them with provider-reported token usage when Semantic Kernel/Ollama exposes reliable usage metadata.
- `AlignmentConfidence` is a retrieval heuristic, not a calibrated truth score. It combines retrieval scores, context count, and citations.
- Chat plan and execution state are in memory. Plan, job, and progress records do not survive API restart.

## Recommended Next Work

1. Add durable job persistence in PostgreSQL so jobs survive API restart.
2. Add cancellation and retry endpoints with safe cleanup for temp files and partially indexed graph/vector state.
3. Add integration tests for `/api/jobs` authorization, upload queueing, and job lifecycle snapshots.
4. Add tests around `UploadedContentKnowledgeIndexer.IndexLightweightAsync` with real graph provider fakes.
5. Add integration coverage for `ILazyEnrichmentKnowledgeSink` against PostgreSQL schema.
6. Replace estimated Copilot token stats with provider usage metadata.
7. Calibrate or relabel `AlignmentConfidence` after a benchmark set exists.
8. Replace or supplement polling with SSE when the UI needs lower-latency progress updates.
9. Add admin controls for stale summary refresh and summary regeneration.
10. Add richer graph-derived WRAGS backlinks, editorial diff visualization, and durable PostgreSQL-backed job state for regeneration/ingestion jobs.
11. Persist chat plans, jobs, and progress in PostgreSQL and add integration tests for the full `/api/copilot` planning flow.
12. Add retry policy with exponential backoff for transient LLM/retrieval failures in `ChatExecutionEngine`.
13. Add admin endpoints to list/cancel chat jobs and inspect plan history.
