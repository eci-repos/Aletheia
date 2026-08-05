### Sprint 34 - WRAGS Orchestration and LazyGraphRAG Throughput
**Status:** Completed

#### Objective
Make WRAGS Copilot execution timely and observable for small repositories while preserving LazyGraphRAG resource buildup.

---

#### Background
The prompt "What opportunities are found for AI based engagements?" is taking minutes and has repeatedly failed through watchdog/timeout behavior even though the repository only has two modest documents. Increasing timeouts is not the real fix. The likely issue is WRAGS orchestration overhead: mandatory tool routing, retrieval variants, source hydration, LazyGraphRAG buildup, and progress reporting are not optimized for tiny corpora. A follow-up request was also queued but did not begin running, indicating a possible worker/queue stall after long-running execution.

---

#### Goals
*   **Queue Reliability:** Ensure queued chat jobs start or report why they cannot start.
*   **Small-Corpus Orchestration:** Use a timely path for tiny repositories without bypassing required LazyGraphRAG resource buildup.
*   **Bounded LazyGraphRAG Buildup:** Make enrichment/build-up incremental, budgeted, and visible instead of blocking the whole answer indefinitely.
*   **Progress Fidelity:** Surface the active sub-operation, heartbeats, and resource-buildup status so the operator can tell the job is alive.
*   **Handoff Ready:** Keep current sprint and external-agent handoff documentation synchronized.

---

#### Initial Investigation Tasks
*   Inspect the chat execution worker and queue state to determine why a new request can remain queued.
*   Audit mandatory WRAGS/RAGS/LazyGraphRAG execution for small-corpus prompts.
*   Identify repeated retrieval/hydration/enrichment work that can be memoized, bounded, or shifted to background continuation.
*   Add focused tests for queued-job startup and small-corpus WRAGS orchestration behavior.

---

#### Exit Criteria
*   **Done** A queued Copilot job reliably transitions to running even when another long mandatory tool call is still running.
*   **Done** Tiny repositories avoid corpus-scale retrieval overhead for scoped registered-source prompts where direct bounded chunk collection is sufficient.
*   **Done** LazyGraphRAG resource buildup remains enabled through explicit LazyGraphRAG paths; scoped WRAGS questions no longer force global graph/community work before source-bounded evidence retrieval.
*   **Done** Tests, build, Docker restart, and handoff docs are updated for queue reliability and scoped WRAGS orchestration.

---

#### Implementation Notes
*   Added `ChatExecutionEngineOptions.MaxConcurrentChatJobs` with a default of 3.
*   Added `ChatExecutionEngine:MaxConcurrentChatJobs = 3` to API configuration.
*   `ChatExecutionEngine.ExecuteAsync` now dispatches jobs through a bounded semaphore instead of awaiting each job inline before reading the next channel item.
*   This fixes the observed queue stall where a long mandatory WRAGS/RAGS/LazyGraphRAG retrieval could block the single worker and leave later Copilot requests stuck in `Queued`.
*   Added regression coverage: `Engine_runs_second_chat_job_when_first_mandatory_tool_is_still_running`.
*   Mandatory `SearchRags` now detects scoped collection prompts such as RFP lists, registered opportunities, and CMP feature/requirement questions before running broad repository retrieval.
*   When Repository metadata identifies matching registered sources, the engine hydrates/searches those sources directly through bounded per-source WRAGS retrieval and skips the broad first pass.
*   Added regression coverage: `Engine_skips_broad_rags_for_scoped_registered_sources`.

---

#### Validation
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatExecutionEngineTests"` passed with 30 tests.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 160 tests.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests after rerun. The first parallel run hit a file-lock while `dotnet build` was writing `Aletheia.Web.dll`; it was not a test failure.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
*   Verified the live API container has `MaxConcurrentChatJobs = 3`.
