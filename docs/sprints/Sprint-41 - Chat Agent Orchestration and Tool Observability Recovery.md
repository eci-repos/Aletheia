### Sprint 41 - Chat Agent Orchestration and Tool Observability Recovery
**Status:** Completed.

#### Objective
Recover Copilot chat reliability by fixing the orchestration path between plan approval, repository tool invocation, and answer synthesis. Operators must be able to see the prompt entering the chat agent, the repository tool selected, whether the tool actually ran, and when the final synthesis request is sent.

---

#### Background
Recent timeout fixes focused too heavily on longer budgets and database credentials. The observed failures show a deeper orchestration problem: prompts could plan against unavailable GraphRAG/LazyGraphRAG tools, report a normalized tool name in telemetry, but still invoke the hidden graph tool path. When the corpus was small or graph communities were absent, the chat failed before it used the available RAGS/WRAGS repository evidence.

The first recovery step is not to re-enable every graph feature. The first step is to make the supported chat path boring and observable: prompt accepted, active repository tool invoked, context verified, synthesis sent, telemetry shown.

---

#### Goals
* **Prompt Delivery Visibility:** Show the user prompt in Activity when planning starts and when synthesis is sent to the chat agent.
* **Tool Invocation Verification:** Add progress/activity messages that state which repository tool is being dispatched and whether it returned internal context.
* **Hidden Graph Tool Normalization:** Ensure hidden GraphRAG/LazyGraphRAG/global graph plans are normalized to the configured `ChatAgent:ToolNames:SearchRepository` tool before invocation, not only before telemetry reporting.
* **Activity Panel Coverage:** Poll recent Copilot chat jobs in the global Activity panel alongside ingestion jobs.
* **No Complexity Denial Path:** Preserve the configured mandatory repository lookup path so complex prompts background-execute instead of being denied only because they require repository reasoning.
* **GraphRAG Reactivation Gate:** Keep GraphRAG and LazyGraphRAG hidden from normal UI until the observable Semantic/Vector RAG and WRAGS chat path is validated end to end.

---

#### Implementation Notes
* `ChatExecutionEngine` now appends progress messages when a chat request is accepted, when a hidden graph tool is normalized, when the repository tool returns context, and when synthesis is sent to the chat agent.
* `ChatExecutionEngine.InvokeToolCoreAsync` now invokes `effectiveToolName` after normalization. A legacy plan for `SearchGraphRag`, `SearchLazyGraphRag`, or `SearchGlobalGraph` is routed to `AletheiaKnowledgePlugin.SearchRags` unless config changes the active repository tool.
* The Copilot page mirrors progress messages into `ActivityLogService`, including the original prompt, approval, job queue state, tool-call messages, synthesis handoff, completion, failure, and cancellation.
* The global Activity panel now polls `/api/copilot/jobs/chat` and renders Copilot chat jobs in the same activity feed as ingestion/background jobs.
* Follow-up correction: Copilot plan creation no longer collapses API failures into the generic `Unable to create execution plan.` message. `RepositoryApiClient.PlanChatAsync` now throws a detailed `HttpRequestException` built from the API response, and the Copilot page surfaces that detail in the planning error and Activity feed.
* Follow-up correction: JWT secret resolution now prefers `ALETHEIA_JWT_SECRET` over `Authentication:Jwt:Secret`, and the development appsettings fallback is long enough for HS256. This fixes the live login 500 that caused plan creation to fail with a downstream 401.
* Follow-up correction: mandatory RAGS fallback now searches each matching registered source for already-indexed chunks before attempting source hydration. Hydration and source-scoped vector searches are wall-clock bounded so one source cannot consume the full mandatory-tool timeout.
* Follow-up correction: if matching registered sources have no RAGS chunks and bounded hydration cannot finish, the mandatory tool returns repository metadata context for those sources instead of failing with no internal context.
* Follow-up UX: Activity now includes a `Copy trace` action on each Job card, plus `Copy all` for the whole panel. Job-level trace copy is the preferred debugging handoff path when many traces are present.
* The sprint explicitly does not re-expose GraphRAG/LazyGraphRAG in the normal UI. Reactivation is a later sprint after the operator can verify prompt handoff and tool usage from Activity.

---

#### Exit Criteria
* A prompt submitted in Copilot creates Activity entries for planning and background execution.
* Progress includes the prompt being sent to synthesis and the number of retrieved context chunks.
* Hidden graph tool plans invoke the configured Semantic/Vector RAG repository tool rather than the graph service directly.
* Activity panel polls and displays recent Copilot chat jobs.
* Planning failures show the actual API status/body details instead of the old generic message.
* Local Docker login succeeds and authenticated `POST /api/copilot/plan` returns a plan.
* Two-document RFP fallback uses already-indexed source chunks and skips hydration when possible.
* Two-document RFP fallback returns repository metadata context when chunks are missing.
* Activity traces can be copied per job from the UI.
* Regression tests cover hidden graph tool normalization, synthesis handoff progress, and Activity panel chat job polling.
* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatExecutionEngineTests"` passes.
* `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passes.
* `dotnet build Aletheia.slnx` passes.

---

#### Validation
* Updated test: `Engine_uses_rags_document_context_for_cmp_feature_request` / hidden graph plan coverage verifies the effective tool is `AletheiaKnowledgePlugin.SearchRags` and GraphRAG/LazyGraphRAG services are not called.
* New Web source-level regression: `Copilot_mirrors_chat_progress_to_activity_log`.
* New Web source-level regression: `Activity_panel_polls_chat_jobs`.
* New Web source-level regression: `Copilot_planning_errors_surface_api_details`.
* Updated engine regression: source-scoped fallback uses indexed chunks before hydration.
* New engine regression: registered source metadata is used when RAGS chunks are missing and hydration is slow.
* New Web source-level regression: `Activity_panel_can_copy_trace_to_clipboard`.
* Run focused RAGS engine tests, Web unit tests, full RAGS tests, and solution build.

---

#### Risks
* Activity entries intentionally include prompt snippets. This is useful for local troubleshooting but should be reviewed before production deployments with sensitive prompts.
* Chat progress and jobs remain process-local. API restart still loses active chat progress until durable PostgreSQL-backed job state is implemented.
* GraphRAG/LazyGraphRAG backend code remains available for future reactivation, but normal UI should continue to steer users to Semantic/Vector RAG and WRAGS until performance and fallback behavior are proven.
