# Historical Phase 21 / RAGS Background Operations Handoff

Date: 2026-07-26

## Scope Authority

This handoff is background context. It does not limit current implementation work to Phase 21.

`docs/File 02-Current-Sprint.md` is the active implementation authority. Any work explicitly described in the current sprint, or in a sprint file referenced by the current sprint, is authorized even when it crosses phases, modules, UI, infrastructure, documentation, or tests.

## Current State

Use the current sprint file to determine active scope. Phase 21 remains historical context for RAGS v2 intelligence and background operations.

Implemented behavior:

- `POST /api/files/upload` stores the Repository artifact and returns quickly with `IngestionStatus = Queued` and `IngestionJobId`.
- Long-running extraction and RAGS ingestion run in the API background worker.
- Upload jobs now use lightweight graph seed indexing by default: taxonomy hints, source nodes, chunk nodes, and `has_chunk` edges are persisted without document-wide LLM entity extraction or summary generation.
- GraphRAG retrieval now performs bounded lazy enrichment for the top relevant chunks when stored summaries are absent. It creates typed entity nodes, `found_in`/`mentioned_in` edges, bounded relationships, entity summaries, marks touched chunks with `lazyEnriched`, and writes discovered entities/relationships back to PostgreSQL Taxonomy/Ontology.
- `GET /api/jobs` lists recent ingestion jobs.
- `GET /api/jobs/{jobId}` returns one job snapshot.
- `POST /api/jobs/rags/ingest`, `POST /api/jobs/graphrag/ingest`, and `POST /api/jobs/lazygraphrag/ingest` queue direct content-ingestion jobs.
- The Web Activity panel polls `/api/jobs`, shows active/completed jobs, and renders stage, heartbeat age, approximate percent complete, detail, and failures.
- Search Center now provides four visible retrieval modes: Semantic, WRAGS, GraphRAG, and LazyGraphRAG. It queues direct ingestion work instead of blocking on a single HTTP request, displays retrieval strategy labels and citations, exposes expansion controls for graph-backed modes, and surfaces technical API failure details in the page.
- WRAGS is the new name for the LLM Wiki initiative. WRAGS now has durable PostgreSQL-backed wiki pages exposed through `/api/wiki` and a Web UI page at `/wiki`. It searches saved pages first, generates pages from RAGS/GraphRAG/LazyGraphRAG on first miss, can queue explicit regeneration jobs, and renders citations, source/chunk details, scores, ranks, versions, timestamps, lifecycle status, stale warnings, related topics, related-page backlinks, history, and retrieval strategy labels. WRAGS mode is GraphRAG-first with LazyGraphRAG and Semantic fallback.
- WRAGS maturity now includes lifecycle status updates (`Generated`, `Reviewed`, `Approved`, `NeedsReview`, `Stale`), `reviewed_by`/`reviewed_at`, stale flags/reasons, source-change stale detection from linked file metadata, related-topic extraction during page generation, related-page lookup from shared source IDs/topics, editable page bodies, version history, and retrieval-context participation in Search Center and Copilot.
- LazyGraphRAG traversal budget handling was corrected so optional query-time enrichment stops at configured limits instead of incrementing counters past the limit and causing the whole retrieval to fail.
- Copilot assistant messages now include chat completion telemetry: elapsed seconds, estimated prompt/completion tokens, estimated tokens per second, retrieved context count, citation count, retrieval scores, and heuristic alignment confidence.
- Conversational planning system (Sprints 22.1–22.7) now provides plan preview, approval, background execution, durable progress polling, recovery after refresh, and plan-versus-actual telemetry reporting.
- Sprint 26 hardening: mandatory Copilot repository tool calls now fall back from GraphRAG/global graph to semantic RAGS when graph communities or summaries are not available. The fallback is surfaced in progress messages, the effective tool is reported as `AletheiaKnowledgePlugin.SearchRags`, and the prompt "Summarize registered RFP opportunities in the past 10 years." no longer fails only because Neo4j has no detected communities.
- Follow-up Sprint 26 hardening: if broad semantic fallback still returns no context, the engine now tries RFP-specific query variants and hydrates matching registered RFP metadata candidates through `IKnowledgeSourceIngestionService` before failing. This addresses registered documents that exist in Repository metadata but are not yet searchable in vector context.
- Sprint 27 lazy scoped WRAGS coverage: for scoped category prompts such as "all RFPs" or "registered RFP opportunities," the engine retrieves bounded context per matching registered source and passes the verified tool context into synthesis through `ChatRequestOptions.RetrievalResults`. This prevents final Copilot synthesis from re-running a single best/latest-source retrieval and dropping other in-scope documents.
- Sprint 27 UI/backend hardening: execution retrieval paths clamp zero retrieval estimates before constructing `RetrievalRequest`, prompts such as "list all found features required for AI" are treated as lazy scoped corpus requests, and the Copilot page resets plan/progress/telemetry state for every new request while requiring an explicit **Run** click for each plan.
- Sprint 28 context-scoped graph exploration: the Web client now remembers the last 10 successful uploads and last 10 Search Center queries in browser storage, records those events from Upload/Search Center, and lets Graph Explorer focus the rendered graph on selected recent context plus a 1-hop neighborhood. The full graph remains available through an explicit toggle.
- Sprint 29 Copilot session fidelity: Copilot now keeps the current chat session, draft input, output format, pending plan, progress, telemetry, active job ID, plan message, and execution-panel layout in Web client state plus browser `localStorage`. Returning to `/copilot` restores the visible conversation and resumes polling active work. Plan/progress labels were humanized, and the execution panel is resizable and collapsible.
- Sprint 30 Copilot evidence-fidelity regression fix: the Copilot acceptance panel no longer leaks `_planStatusMessage`, and CMP/document/engagement feature or requirement prompts now route to `AletheiaKnowledgePlugin.SearchRags` document-level evidence instead of graph/community-summary retrieval. Synthesis instructions now explicitly forbid surfacing graph communities, community IDs, chunk counts, retrieval strategies, or index internals as user-facing answers.
- Sprint 31 Copilot tool-heartbeat fix: mandatory repository tool calls now emit `Tool call` heartbeats while running SearchRags, SearchGraphRag, SearchLazyGraphRag, GlobalGraph, fallback query variants, and scoped per-source retrieval. A hung mandatory retrieval now fails the tool step with the configured step timeout instead of waiting for the heartbeat watchdog. Copilot also has a **New chat** action that clears conversation, draft, plan, progress, telemetry, active job, panel status, and browser-persisted Copilot state.
- Sprint 32 mandatory tool timeout tuning: mandatory repository tool calls now use `ChatExecutionEngineOptions.MandatoryToolTimeoutSeconds` instead of the generic 30-second `DefaultStepTimeoutSeconds`. This addresses opportunity/RFP corpus prompts such as "What opportunities are found for AI based engagements?" that can need more than 30 seconds for retrieval while still emitting heartbeats. Sprint 33 later raised the production value to 1800 seconds.
- Sprint 33 mandatory tool heartbeat cadence fix: `Tool call` now uses the normal `HeartbeatIntervalSeconds` cadence instead of `LongWaitHeartbeatIntervalSeconds`. The observed failure was a config mismatch: production long-wait heartbeats were 120 seconds, while the watchdog failed active jobs after 90 seconds without a heartbeat. Follow-up hardening makes `RunWithHeartbeatAsync` emit an immediate heartbeat, records the in-memory watchdog timestamp before appending to progress storage, and raises the production watchdog window to 10 minutes (`HeartbeatWatchdogMissedThreshold = 20`). Production mandatory-tool timeout is now a long safety net (`1800` seconds) and overall job timeout is `3600` seconds.
- Sprint 34 WRAGS orchestration: chat execution is now bounded-concurrent through `ChatExecutionEngineOptions.MaxConcurrentChatJobs` (production default/config value `3`). The execution worker no longer awaits one job inline before reading the next queued item, so a long WRAGS/RAGS/LazyGraphRAG mandatory tool call cannot leave later Copilot requests stuck in `Queued`.
- Sprint 34 scoped-source throughput: mandatory `SearchRags` detects scoped collection prompts such as RFP lists, registered opportunities, and CMP feature/requirement questions. When Repository metadata identifies matching registered sources, Copilot hydrates/searches those sources directly through bounded per-source WRAGS retrieval and skips the expensive broad repository retrieval pass.
- Sprint 35 RAG-first product surface was superseded by Sprint 44. Search Center and WRAGS Wiki now expose `Semantic`, `WRAGS`, `GraphRAG`, and `LazyGraphRAG`; scoped document/RFP prompts still route through `AletheiaKnowledgePlugin.SearchRags`.
- Sprint 35 Copilot plan hygiene: after approval starts execution, the visible `Execution Plan` card is hidden while the plan record remains internally available for progress polling. The progress panel remains the operator-facing execution feedback surface.
- Sprint 38 mandatory repository tool invocation fix: the engine now invokes the registered Semantic Kernel plugin function (`AletheiaKnowledgePlugin.SearchRags` / `RepositoryTool.SearchRepositoryDocuments`) directly instead of relying on a hard-coded internal dispatch that could silently hang. Tool-name parsing supports both plugin styles, the call is bounded by the step timeout, and failure messages include the plugin/function name. Existing scoped-source hydration fallback is preserved for when the plugin returns no results.
- Sprint 39 Chat Agent configuration: the agent's role, repository description, mandate, no-context response, configured tool names (`SearchRepository`, `SearchRepositoryFallback`), and behavior flags (`RequireRepositoryLookupBeforeAnswer`, `CiteSources`, `RefuseWhenNoContext`, `IncludeRepositorySummaryInPrompt`) now live in the `ChatAgent` config section. `SemanticKernelChatService` prepends a system prompt built from these options; `RetrievalAugmentedPromptBuilder` and `SemanticKernelCopilotService` consume the same options; and `ChatExecutionEngine` enforces repository lookup before answer synthesis when the flag is set.
- Sprint 41 chat-agent orchestration recovery was superseded by Sprint 44 for graph visibility. Copilot progress records prompt acceptance, repository tool dispatch, graph fallback when needed, repository tool verification, and synthesis handoff. The Web Activity panel polls recent Copilot chat jobs and mirrors chat progress messages so operators can see what prompt was sent, which tool ran, whether internal context was returned, and whether synthesis started.
- Sprint 44 full RAGS reactivation: Search Center and WRAGS Wiki expose all RAGS modes. Copilot no longer rewrites GraphRAG/LazyGraphRAG/global graph tools as hidden. Broad/global Copilot retrieval tries GraphRAG first, LazyGraphRAG second, and Semantic RAGS last; document-scoped RFP/CMP/feature prompts continue using source-scoped Semantic RAGS evidence.
- Sprint 48 source identity preservation: retrieved Copilot context is now grouped by `SourceId` before prompt construction. Each document receives its own source block with source name, source ID, citations, and chunks; instructions forbid using facts from one source to describe another. Multi-document answers must render separate source sections, and named-document answers must stay inside the named source block.
- Sprint 41 follow-up planning error fix: `RepositoryApiClient.PlanChatAsync` now throws detailed API failures for `POST /api/copilot/plan` instead of returning null. The Copilot page no longer displays the opaque `Unable to create execution plan.` message for API failures; it surfaces the HTTP status, endpoint, and server error body through the planning error and Activity failure entry. The root live-container blocker was JWT config precedence: `appsettings.json` short `dev-secret` overrode the longer Docker `ALETHEIA_JWT_SECRET`, causing login to fail with `IDX10653`. `AddAletheiaSecurity` now prefers the environment variable and the appsettings development fallback is long enough for HS256.
- Sprint 41 follow-up RAGS fallback fix: mandatory `SearchRags` fallback now performs source-scoped retrieval against already-indexed chunks before source hydration. This prevents two-document prompts such as "summarize RFP opportunities available in the past 5 years" from spending the whole tool budget re-downloading/re-extracting documents that should already have RAG chunks. Source-scoped retrieval and hydration are each wall-clock bounded; a slow source should be skipped or searched with existing chunks instead of timing out the full job.
- Sprint 41 follow-up RAGS metadata fallback: when matching registered sources exist but RAGS chunks are missing and bounded hydration cannot finish, the mandatory tool now returns repository metadata-backed `SearchResult` context for those sources. This keeps answers grounded in registered repository evidence instead of failing with "RAGS fallback returned no internal context from matching registered sources." Treat this as a degraded evidence path: it can list/identify registered source documents, but it is less rich than chunk-level RAG evidence.
- Sprint 41 follow-up Activity UX: each Activity Job card now has `Copy trace`, which copies that job summary and job-linked trace entries as plain text. The Activity header has `Copy all` for whole-panel export.
- Sprint 42 external orchestration and repair: Copilot RAGS/WRAGS flow guidance now lives in `src/Repository.API/Prompts/copilot-rags-orchestration.md`, loaded by `ChatAgent:OrchestrationScriptPath` and injected into Semantic Kernel prompts. The playbook tells the agent to treat Taxonomy/metadata matches such as `RFP` as valid repository scope and to treat missing source chunks as index drift, not empty corpus evidence. Operators can queue background repair with `POST /api/jobs/rags/repair?query=RFP` or repair all registered sources with `POST /api/jobs/rags/repair`; the job is visible in Activity as `RagsRepair`.
- Sprint 43 acronym/source-link fix: Taxonomy and Ontology labels now canonicalize `RFP`, including legacy `Rfp`/`Rpf` spellings. Lightweight ingestion includes the source filename during topic extraction and persists topic ontology entities with `found_in` relationships to the source document entity. This is the clean-container validation path for the two CMP RFP documents.

## Takeover Observation

The prior Kimi K2.7 Code cloud agent completed a substantial share of Sprint 24-26 work: mandatory knowledge-tool calls, strict Aletheia-only grounding, RFP/domain intent planning, Semantic Kernel plugin exposure, and telemetry wiring. The remaining gap was validation depth rather than direction. It did not catch the production path where a small or fresh corpus has RAGS chunks but no GraphRAG communities, causing `SearchGraphRag` to fail with `No communities detected in the graph.` before synthesis. It also left Sprint 26 status/docs partially reconciled and allowed a mandatory tool failure to appear as a completed tool step.

Future agents should use the exact user-facing RFP prompt as a regression scenario, validate with a corpus that has only a few registered documents, confirm GraphRAG-to-RAGS fallback before closing, and keep `docs/File 02-Current-Sprint.md`, this handoff, and the relevant `docs/sprints/` file synchronized with code and test changes.

## Wiki Templates
- LLM Wiki functionality should map ingested documents based on templates into wiki exposed areas, find document templates in the docs/doc-templates.  

## Main Code Paths

- API job orchestration: `src/Repository.API/Services/IngestionJobService.cs`
- API job progress contract: `src/Repository.API/Services/IngestionProgress.cs`
- API job endpoints: `src/Repository.API/Controllers/JobsController.cs`
- API RAGS repair endpoint: `POST /api/jobs/rags/repair?query=RFP`
- Upload queue integration: `src/Repository.API/Controllers/FilesController.cs`
- Knowledge enrichment progress hooks: `src/Repository.API/Services/UploadedContentKnowledgeIndexer.cs`
- GraphRAG query-time lazy enrichment: `src/RAGS.Application/GraphRAG/GraphRagService.cs`
- Lazy Taxonomy/Ontology write-back abstraction: `src/RAGS.Abstractions/Interfaces/ILazyEnrichmentKnowledgeSink.cs`
- Lazy Taxonomy/Ontology write-back implementation: `src/RAGS.Infrastructure.PostgreSQL/Knowledge/LazyEnrichmentKnowledgeSink.cs`
- Copilot chat telemetry: `src/RAGS.Application/SemanticKernel/SemanticKernelCopilotService.cs`
- Chat stats model: `src/RAGS.Abstractions/Models/ChatMessage.cs`
- Copilot stats UI: `src/Aletheia.Web/Pages/Copilot/Index.razor`
- Copilot Web session persistence: `src/Aletheia.Web/Services/CopilotStateService.cs`
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
- API chat execution timeout configuration: `src/Repository.API/appsettings.json` -> `ChatExecutionEngine`
- API Chat Agent instruction configuration: `src/Repository.API/appsettings.json` -> `ChatAgent`
- External Copilot RAGS orchestration playbook: `src/Repository.API/Prompts/copilot-rags-orchestration.md`
- Taxonomy/Ontology acronym normalizer: `src/RAGS.Abstractions/Models/KnowledgeTermNormalizer.cs`
- Web API client job methods: `src/Aletheia.Web/Services/RepositoryApiClient.cs`
- Web RAGS repair client method: `RepositoryApiClient.RepairRagsIndexAsync(...)`
- Web activity state: `src/Aletheia.Web/Services/ActivityLogService.cs`
- Web Activity panel rendering/polling: `src/Aletheia.Web/Layout/ActivityPanel.razor`
- Web Activity panel styling: `src/Aletheia.Web/Layout/ActivityPanel.razor.css`
- Upload page queued status: `src/Aletheia.Web/Pages/Upload.razor`
- Search Center queued ingestion: `src/Aletheia.Web/Pages/SearchCenter.razor`
- Recent graph context memory: `src/Aletheia.Web/Services/RecentGraphContextService.cs`
- Context-scoped graph explorer UI/filtering: `src/Aletheia.Web/Pages/GraphExplorer.razor`
- Context-scoped graph explorer styling: `src/Aletheia.Web/Pages/GraphExplorer.razor.css`
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
- Chat agent config regression: `tests/RAGS.UnitTests/SemanticKernelCopilotServiceTests.cs` -> `RetrievalAugmentedPromptBuilder_uses_chat_agent_options_when_provided`
- Chat agent orchestration regression: `tests/RAGS.UnitTests/SemanticKernelCopilotServiceTests.cs` -> `RetrievalAugmentedPromptBuilder_includes_external_orchestration_instructions`
- RAGS repair endpoint regression: `tests/RAGS.UnitTests/BackgroundJobs/JobsControllerTests.cs` -> `RepairRags_returns_accepted_job_snapshot`
- RFP concept source-link regression: `tests/RAGS.UnitTests/UploadedContentKnowledgeIndexerTests.cs` -> `IndexLightweightAsync_links_rfp_taxonomy_and_ontology_to_each_source_when_postgres_is_available`
- Repository lookup enforcement regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_enforces_repository_lookup_when_behavior_flag_set_and_no_tool_required`
- GraphRAG mandatory-tool fallback regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_falls_back_to_rags_when_mandatory_graphrag_has_no_communities`
- Registered-source hydration fallback regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_hydrates_registered_rfp_sources_when_graphrag_and_broad_rags_return_no_context`
- Lazy scoped synthesis regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_passes_lazy_scoped_rfp_context_to_synthesis`
- Provided-context Copilot regression: `tests/RAGS.UnitTests/SemanticKernelCopilotServiceTests.cs` -> `ChatAsync_uses_provided_scoped_context_without_retrieving_again`
- Zero-topK regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_clamps_fast_path_zero_retrieval_count_before_retrieval`
- Scoped feature-list planning regression: `tests/RAGS.UnitTests/ChatPlanningServiceTests.cs` -> `CreatePlanAsync_treats_list_all_found_features_as_exhaustive_scoped_request`
- CMP engagement feature routing regression: `tests/RAGS.UnitTests/ChatPlanningServiceTests.cs` -> `CreatePlanAsync_routes_cmp_engagement_feature_request_to_document_rags`
- CMP feature execution regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_uses_rags_document_context_for_cmp_feature_request`
- Mandatory tool heartbeat regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_emits_heartbeats_during_mandatory_rags_tool_call`
- Mandatory tool watchdog cadence regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_keeps_mandatory_tool_alive_when_long_wait_interval_exceeds_watchdog_threshold`
- Mandatory tool timeout regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_honors_step_timeouts`
- Bounded concurrent queue regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_runs_second_chat_job_when_first_mandatory_tool_is_still_running`
- Scoped-source broad retrieval skip regression: `tests/RAGS.UnitTests/ChatExecutionEngineTests.cs` -> `Engine_skips_broad_rags_for_scoped_registered_sources`
- RAG-first planning regression: `tests/RAGS.UnitTests/ChatPlanningServiceTests.cs` -> `CreatePlanAsync_emits_tool_call_for_rfp_timeline_query`
- GraphRAG/LazyGraphRAG UI visibility regressions: `tests/Aletheia.Web.UnitTests/CopilotIndexBindingTests.cs`
- Copilot acceptance panel binding regression: `tests/Aletheia.Web.UnitTests/CopilotIndexBindingTests.cs`
- Copilot new-chat reset regression: `tests/Aletheia.Web.UnitTests/CopilotStateServiceTests.cs` -> `ClearAsync_resets_memory_state_and_removes_browser_state`
- Anti-community-leak prompt regression: `tests/RAGS.UnitTests/SemanticKernelCopilotServiceTests.cs` -> `RetrievalAugmentedPromptBuilder_instructs_model_not_to_surface_graph_internals`
- Chat telemetry service tests: `tests/RAGS.UnitTests/ChatTelemetryServiceTests.cs`
- Web progress panel tests: `tests/Aletheia.Web.UnitTests/ProgressPanelTests.cs`
- Web plan preview tests: `tests/Aletheia.Web.UnitTests/PlanPreviewTests.cs`
- Copilot state persistence tests: `tests/Aletheia.Web.UnitTests/CopilotStateServiceTests.cs`
- Recent graph context tests: `tests/Aletheia.Web.UnitTests/RecentGraphContextServiceTests.cs`
- Graph Explorer context filter tests: `tests/Aletheia.Web.UnitTests/GraphExplorerTests.cs`

## Validation Already Run

```powershell
dotnet build Aletheia.slnx
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj
```

Latest Sprint 38 mandatory tool invocation fix validation on 2026-07-31:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 178 tests (3 new regression tests: plugin invocation, `RepositoryTool` alias acceptance, hanging plugin timeout with plugin name).
- `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
- `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 27 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- The engine now invokes the registered Semantic Kernel plugin function for `AletheiaKnowledgePlugin.SearchRags` and `RepositoryTool.SearchRepositoryDocuments`, falls back to scoped source retrieval when the plugin returns no results, and reports the plugin name in timeout/failure messages.

Latest Sprint 41 chat-agent orchestration validation on 2026-08-01:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatExecutionEngineTests"` passed with 37 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 29 tests.
- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 180 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- Follow-up validation after the planning-error fix: `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 30 tests; `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatPlanningServiceTests|FullyQualifiedName~ChatPlanApprovalServiceTests" --no-restore` passed with 54 tests; `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- Follow-up validation after the JWT precedence fix: Web unit tests passed with 31 tests; RAGS planning/approval tests passed with 54 tests after rerun; `dotnet build Aletheia.slnx` passed. `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers. `GET /health/live`, `GET /health/ready`, and `GET http://localhost:8081/copilot` returned `200`. Authenticated `POST /api/copilot/plan` returned a plan for both `hello` and the literal prompt `Unable to create execution plan.`.
- Follow-up validation after the source-scoped fallback and Activity copy fix: focused ChatExecutionEngine tests passed with 37 tests; Web unit tests passed with 32 tests. The updated regression confirms already-indexed chunks are used before hydration and that the Activity panel exposes copy-to-clipboard trace export.
- Follow-up validation after metadata fallback and per-job trace copy: focused ChatExecutionEngine tests passed with 38 tests; Web unit tests passed with 32 tests; full RAGS unit tests passed with 181 tests; `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- Final validation after rebuild: full RAGS unit tests passed with 181 tests; `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning; `docker compose up -d --build api web` rebuilt/restarted the API and Web containers; `GET /health/live`, `GET /health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Sprint 44 code validation should confirm GraphRAG/LazyGraphRAG/global graph tool plans stay active, Copilot progress records graph fallback only when needed, and the Activity panel polls chat jobs.
- Sprint 42 validation on 2026-08-01: focused RAGS tests passed with 17 tests, Web unit tests passed with 32 tests, full RAGS unit tests passed with 183 tests, and `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning. `docker compose up -d --build api web` rebuilt/restarted the running stack; `/health/live`, `/health/ready`, and `http://localhost:8081/copilot` returned `200`; `/app/Prompts/copilot-rags-orchestration.md` was verified inside the API container.
- Sprint 43 validation on 2026-08-01: focused normalizer/indexer tests passed with 9 tests, full RAGS unit tests passed with 192 tests, and `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning. The indexer regression validates two RFP source documents link to canonical `RFP` in both Taxonomy and Ontology when PostgreSQL is available. `docker compose up -d --build api web` rebuilt/restarted the running stack; `/health/live`, `/health/ready`, and `http://localhost:8081/copilot` returned `200`.
- Sprint 44 validation on 2026-08-01: focused RAGS planning/execution tests passed with 80 tests, Web unit tests passed with 32 tests, full RAGS unit tests passed with 193 tests, and `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning. `docker compose up -d --build api web` rebuilt/restarted the running stack; `/health/live`, `/health/ready`, `http://localhost:8081/search`, and `http://localhost:8081/wiki` returned `200`.
- Sprint 48 validation on 2026-08-01: focused source-partition/progress tests passed, focused repair regression passed with 4 tests, full RAGS unit tests passed with 194 tests, Repository integration tests passed with 8 tests, Web unit tests passed with 32 tests, and `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning. `docker compose up -d --build api web` rebuilt/restarted the running stack after fixing the API Dockerfile publish path; `/health/live`, `/health/ready`, and `http://localhost:8081/copilot` returned `200`.

Latest Sprint 26 hardening validation on 2026-07-28:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 10 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.

Latest Sprint 27 follow-up validation on 2026-07-29:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 10 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.

Latest Sprint 27 validation on 2026-07-29:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 151 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 10 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.

Latest Sprint 28 validation on 2026-07-29:

- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 15 tests.
- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests on rerun. The first run had one timing-shaped `Engine_honors_step_timeouts` failure where the job was still `Running`; rerun passed and no Sprint 28 path was involved.
- `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
- `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/graph` returned `200`.
- Browser smoke on `http://localhost:8081/graph` confirmed the rendered Recent Context panel and full-graph control are present.

Latest Sprint 29 validation on 2026-07-29:

- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 21 tests.
- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests.
- `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
- `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Browser smoke on `http://localhost:8081/copilot` confirmed persisted Copilot messages survive navigation away/back, execution panel collapse persists, and drag resize changes/stores panel width.

Latest Sprint 30 validation on 2026-07-29:

- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 22 tests.
- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 156 tests.
- `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
- `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Browser smoke on `http://localhost:8081/copilot` confirmed the acceptance panel renders `Review the plan and click Run to start.`, does not render `_planStatusMessage`, shows friendly `Corpus analysis`, and displays `AletheiaKnowledgePlugin.SearchRags` for the CMP feature prompt.

Latest Sprint 31 validation on 2026-07-29:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 157 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests.
- `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj --no-restore` passed with 91 tests.
- `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj --no-restore` passed with 55 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Browser smoke on `http://localhost:8081/copilot` confirmed **New chat** renders, clears `aletheia.copilot.session.v1`, and returns the page to the empty-conversation state.
- Covered the exact failure class from the prompt `Based on CMP 2026 list required features for this engagement`: mandatory RAGS tool retrieval now emits `Tool call` heartbeats while waiting, and a truly hung retrieval fails by step timeout rather than heartbeat watchdog.

Latest Sprint 32 validation on 2026-07-29:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 157 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests after rerun. The first parallel attempt hit a file-lock while `dotnet build` was writing `Aletheia.Web.dll`; it was not a test failure.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Covered the exact timeout reported for `What opportunities are found for AI based engagements?`: mandatory tool calls moved off the generic 30-second step timeout and continue to emit `Tool call` heartbeats while retrieval runs.

Latest Sprint 33 validation on 2026-07-29:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 158 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Covered the exact watchdog failure reported for `What opportunities are found for AI based engagements?`: mandatory `Tool call` heartbeats now use the normal 30-second cadence, so active repository retrieval cannot be killed merely because the long-wait interval exceeds the watchdog threshold.
- Follow-up live-container verification confirmed `MandatoryToolTimeoutSeconds = 1800`, `OverallJobTimeoutSeconds = 3600`, `HeartbeatIntervalSeconds = 30`, `LongWaitHeartbeatIntervalSeconds = 120`, and `HeartbeatWatchdogMissedThreshold = 20`.

Latest Sprint 34 queue-reliability validation on 2026-07-29:

- `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 159 tests.
- `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests after rerun. The first parallel run hit a file-lock while `dotnet build` was writing `Aletheia.Web.dll`; it was not a test failure.
- `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
- `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
- `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
- Verified the live API container has `MaxConcurrentChatJobs = 3`.

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
- Recent graph context is browser-local. It survives refresh in the same browser but does not follow the user across browsers, devices, or cleared site storage.
- Copilot visible session state is browser-local. It survives navigation and refresh in the same browser, but it is not server-durable and will not follow the user across browsers/devices or cleared site storage. Background chat execution itself is still process-local unless future durable job persistence is added.
- The Copilot **New chat** action intentionally clears only the visible browser-local session and execution panel state. It does not cancel already-running server-side background jobs unless the operator uses the execution panel cancel action first.
- Copilot Activity entries now include prompt snippets and tool-call trace messages. This is intentional for local troubleshooting of chat-agent orchestration, but production deployments with sensitive prompts should review retention and visibility before enabling broad operator access to Activity.
- GraphRAG/global graph is still useful for broad corpus themes, but scoped document questions asking for features, requirements, capabilities, or engagement details should use document-level RAGS evidence. Treat any response that mentions communities or chunk counts as the answer to such a prompt as a regression.
- GraphRAG/LazyGraphRAG are re-enabled in normal UI as of Sprint 44. Keep Activity validation in place: prompt accepted, repository tool dispatched, graph fallback when applicable, internal context returned, synthesis sent, and answer/telemetry completed.
- The heartbeat watchdog remains valid and should not be disabled to hide stalls. If a mandatory tool call stalls, first confirm it is using the heartbeat-aware `RunToolRagsRetrieveAsync` or `RunWithHeartbeatAsync` path and that `Tool call` uses the normal heartbeat cadence.
- Mandatory tool calls are intentionally bounded by `MandatoryToolTimeoutSeconds` rather than the generic step timeout. Current production default is 1800 seconds. Tune `ChatExecutionEngine:MandatoryToolTimeoutSeconds` for slow embedding/vector backends before changing watchdog thresholds.
- Do not move `Tool call` back to the long-wait heartbeat cadence unless the watchdog threshold is changed accordingly. With the current production settings, `Tool call` must heartbeat every 30 seconds and the watchdog is a 10-minute backstop.
- Chat execution is bounded-concurrent, not single-file. Preserve `MaxConcurrentChatJobs` so one slow LazyGraphRAG/WRAGS job does not block all later Copilot requests. Next Sprint 34 work should focus on small-corpus orchestration and bounded LazyGraphRAG buildup, not more timeout-only changes.

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
14. Consider server-side recent-context persistence after durable user/session identity is finalized, so Graph Explorer scope follows users across browsers.
15. Move Copilot session/progress state to server-side durable persistence after PostgreSQL-backed chat/job state is introduced.
16. After Sprint 41 validation, design a gated GraphRAG/LazyGraphRAG reactivation sprint that starts from observed Activity/tool traces instead of timeout-only tuning.
