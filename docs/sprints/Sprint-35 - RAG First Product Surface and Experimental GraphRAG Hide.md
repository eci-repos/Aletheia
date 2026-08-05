### Sprint 35 - RAG First Product Surface and Experimental GraphRAG Hide
**Status:** Completed

#### Objective
Make the supported WRAGS product surface simpler and faster by keeping Semantic/Vector RAG and WRAGS Wiki visible, while hiding GraphRAG and LazyGraphRAG UI entry points until their performance is production-ready.

---

#### Background
GraphRAG and LazyGraphRAG remain valuable research directions, but they are currently too slow for normal operator workflows, even with a tiny two-document corpus. The immediate product should focus on reliable Semantic/Vector RAG, WRAGS Wiki, chat grounding, taxonomy, ontology, and vocabulary support.

---

#### Goals
*   **Copilot Plan Hygiene:** Hide the visible Execution Plan card once approval is accepted and execution starts.
*   **RAG-First Chat:** Route grounded Copilot tool plans through `AletheiaKnowledgePlugin.SearchRags`.
*   **UI Surface Cleanup:** Hide GraphRAG and LazyGraphRAG modes from Search Center and WRAGS Wiki.
*   **Preserve Future Backend:** Keep GraphRAG/LazyGraphRAG backend services, APIs, and tests intact for future reactivation.
*   **Knowledge Data Continuity:** Preserve Semantic/Vector RAG, WRAGS Wiki, taxonomy, ontology, vocabularies, and related data entities.

---

#### Implementation Notes
*   Copilot now renders `PlanPreview` only while a plan is awaiting execution. After approval starts a job, the plan stays internally available for polling, but the visible card is replaced by progress or a short queued message.
*   `ChatPlanningService` now selects `AletheiaKnowledgePlugin.SearchRags` for mandatory repository tool calls.
*   Search Center visible modes are now `Semantic` and `WRAGS`.
*   WRAGS Wiki visible modes are now `WRAGS` and `Semantic`.
*   GraphRAG and LazyGraphRAG client/API methods remain in code but are no longer exposed through the normal UI.

---

#### Exit Criteria
*   **Done** Approval/run hides the visible Execution Plan card until a new plan is needed.
*   **Done** Chat grounding uses Semantic/Vector RAG as the default mandatory tool path.
*   **Done** Search Center no longer shows GraphRAG/LazyGraphRAG modes.
*   **Done** WRAGS Wiki no longer shows GraphRAG/LazyGraphRAG mode buttons.
*   **Done** Regression tests and handoff documentation are updated.

---

#### Validation
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatPlanningServiceTests|FullyQualifiedName~ChatExecutionEngineTests"` passed with 71 tests.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 160 tests.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 27 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/search` returned `200`.
