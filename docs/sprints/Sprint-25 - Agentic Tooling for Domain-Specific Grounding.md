### Sprint 25 - Agentic Tooling for Domain-Specific Grounding

**Sprint:** Agentic Tooling for Domain-Specific Grounding

**Status:** Completed

#### Objective
Formalize the **agentic tool-calling framework** for the Copilot by auditing and exposing existing service resources as **Semantic Kernel Plugins**. This ensures that domain-specific queries (e.g., "RFP") trigger a mandatory "fetch" from the local **Aletheia Knowledge Estate** instead of relying on model training data.

---

#### Background
Previous phases established a robust foundation for AI orchestration, including the migration to **Semantic Kernel** in Phase 11 and the completion of the **Graph SDK** in Phases 12–13. However, current Copilot behavior indicates that while the retrieval logic exists, it is not being consistently invoked as a mandatory "agentic tool" when the model identifies domain-specific entities. This sprint focuses on "catching up" with existing codebases to expose retrieval services as callable tools for the AI agent.

---

#### Authority
The repository is the source of truth. This sprint must utilize the existing **Clean Architecture** and **Semantic Kernel** abstractions established in Sprint 11 and Sprint 22.

---

#### Goals
*   **Audit Existing AI Resources:** Review `RAGS.Abstractions` and `KnowledgeGraph.Abstractions` to identify services suitable for conversion into Semantic Kernel Plugins.
*   **Expose Service-Based Tools:** Formalize the `AletheiaKnowledgePlugin` by wrapping existing `IRagsService`, `IGraphRagService`, and `ILazyGraphRagService` methods as **[KernelFunction]** calls.
*   **Implement Domain-Term Intent Triggers:** Configure the `IChatPlanningService` to recognize "RFP" as a high-priority repository entity that requires a mandatory tool call.
*   **Hardened Local-Only Grounding:** Update system instructions to prioritize tool outputs over parametric model knowledge to eliminate hallucinations regarding repository statistics.

---

#### Requirements

##### 1. Infrastructure Audit & Tool Mapping
*   **Task:** Identify existing "Search" and "Retrieve" methods in the current codebase that can be converted into agentic tools.
*   **Requirement:** Review the `IKnowledgeSourceResolver` and `IKnowledgeSourceIngestionService` for inclusion in the plugin suite.
*   **Requirement:** Ensure `IGlobalGraphSearchService` (from Sprint 14) is exposed as a tool for broad corpus-level summaries.

##### 2. Semantic Kernel Plugin Formalization
*   **Task:** Develop or update the `RepositoryToolPlugin`.
*   **Requirement:** Use **Semantic Kernel attributes** to describe functions like `GetRfpSummaries` or `SearchLocalKnowledge` so the planner can discover them.
*   **Requirement:** Ensure these tools return structured context with **citations**, allowing the Copilot to maintain its 100% grounding requirement.

##### 3. Planner & Execution Hardening
*   **Task:** Refine the `IChatPlanningService` logic to prioritize tool usage.
*   **Requirement:** When a query contains "RFP," the generated `ChatExecutionPlan` must explicitly include a tool-call step before any synthesis occurs.
*   **Requirement:** The `ChatExecutionEngine` must handle the tool's output as the primary context for the "Synthesizing" stage.

---

#### Validation
*   **Scenario Test:** Ask: "Summarize registered RFP opportunities in the past 10 years."
*   **Success Metric:** The **Execution Plan** identifies a tool call to the local repository. The final response summarizes only the 2 RFPs in the WRAGS repository with accompanying citations.
*   **Telemetry Metric:** The **Telemetry Panel** indicates that the response was generated using the "Repository Plugin" rather than general knowledge.
*   **Build/Test:** Execute `dotnet test` on `RAGS.UnitTests` to ensure no regressions in the planning or execution engine.

#### Completion Notes
*   `AletheiaKnowledgePlugin` exposes RAGS, GraphRAG, LazyGraphRAG, global graph search, source resolution, and ingestion functions with `[KernelFunction]` metadata.
*   `RepositoryToolPlugin` exposes repository-facing tool names for local knowledge search, GraphRAG search, and source resolution.
*   `AIServiceCollectionExtensions.AddAletheiaAI` registers both plugins on the live Semantic Kernel instance.
*   `SemanticKernelPluginRegistrationTests.AddAletheiaAI_registers_agentic_knowledge_plugins_on_kernel` verifies the Kernel contains the expected callable functions.
*   `ChatPlanningServiceTests` verifies RFP prompts produce mandatory tool-call plans.
*   `ChatExecutionEngine` preserves mandatory tool grounding even when the selected GraphRAG/global graph tool has no communities or summaries yet by falling back to `AletheiaKnowledgePlugin.SearchRags` and recording the effective tool in telemetry.

---

#### Exit Criteria
*   **✓** Existing RAGS and Graph services are successfully exposed as SK Plugins.
*   **✓** "RFP" queries reliably trigger local tool calls.
*   **✓** Telemetry confirms the use of internal tools for domain-specific answers.
*   **✓** The model no longer provides external statistics for repository-specific terms.
*   **✓** Build and all unit tests pass.

---

#### Out Of Scope
*   Adding new vector/graph database providers.
*   Modifying the core **PostgreSQL** or **Neo4j** storage schemas.
*   Redesigning the **Blazor UI** beyond existing telemetry/progress displays.
