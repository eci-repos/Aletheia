### Sprint 26 - Agentic Grounding and Domain Intent Enforcement
**Status:** Completed

#### Objective
Programmatically force the **Copilot** to treat the **Aletheia Knowledge Estate (Repository/RAGS/WRAGS)** as the primary and exclusive source of truth. This sprint will implement **mandatory agentic tool-calling**, hardened system grounding, and intent-driven alias resolution to ensure queries like "RFP" never default to generic LLM training data.

---

#### Background
Despite previous efforts, the Copilot still occasionally fails to recognize the internal repository as the authoritative source for domain-specific terms like "RFP," leading to requests for clarification or general hallucinations. While **Sprint 25** initiated the transition to a tool-calling model, this sprint focuses on enforcing the **mandatory** execution of those tools and strictly limiting the model's reasoning boundaries to the retrieved internal context [Sprint 25, 150].

---

#### Authority
The **Repository** is the authoritative system of record. This work utilizes existing **Semantic Kernel** orchestration, **Clean Architecture** patterns, and the **Chat Planning** framework established in Sprint 22.

---

#### Goals
*   **Mandatory Plugin Integration:** Formally expose `IRagsService` and `IGraphRagService` as Semantic Kernel Plugins to force the agent to programmatically "fetch" knowledge [16, Sprint 25].
*   **Intent-Triggered Planning:** Update the **Chat Planner** to treat specific keywords as "High-Grounding Required," mandating a tool call in every **Execution Plan** [161, Sprint 25].
*   **Strict Identity Grounding:** Harder system instructions to define the agent's identity as being solely limited to the **Aletheia Knowledge Estate** [Sprint 24, Sprint 25].
*   **Prioritized Alias Resolution:** Configure intent resolution to treat "RFP" as a prioritized internal document alias [211, Sprint 25].

---

#### Requirements

##### 1. Implement Agentic Tool Calling (Semantic Kernel Plugins)
*   **Task:** Create or finalize the `AletheiaKnowledgePlugin` [Sprint 25].
*   **Requirement:** Wrap existing `IRagsService` (Standard RAG) and `IGraphRagService` (GraphRAG) into callable functions like `SearchRepositoryDocuments(query)` [16, Sprint 25].
*   **Requirement:** Update `IChatPlanningService`. When a query contains keywords like **"RFP,"** the generated `ChatExecutionPlan` **must** include a discrete `CallTool` step [Sprint 25].
*   **Requirement:** The model must be programmatically blocked from synthesizing an answer until the repository search tool has been successfully executed [Sprint 25].

##### 2. Hardened System Grounding
*   **Task:** Update the core system prompt in `SemanticKernelCopilotService`.
*   **Requirement (Identity Directive):** Explicitly state: "You are an agent of the Aletheia platform. Your knowledge is limited to the provided WRAGS and RAGS context. If information is not in the retrieved context, you must state it is not found rather than using external knowledge" [Sprint 24, Sprint 25].
*   **Requirement (Zero-Hallucination Policy):** Explicitly prohibit the agent from providing statistics, dimensions, or summaries (e.g., market data or generic status breakdowns) not present in the tool output [Sprint 25].

##### 3. Alias and Intent Resolution
*   **Task:** Update `IKnowledgeSourceResolver` and `IChatPlanningService`.
*   **Requirement:** Configure "RFP" as a **prioritized alias** for internal document categories [Sprint 25].
*   **Requirement:** Implement "High-Grounding Required" intent recognition. When triggered, the agent must assume the query refers to the internal repository by default [Sprint 25].
*   **Requirement:** Ensure `ChatExecutionEngine` uses tool output as the **sole authoritative context** for the final synthesis [180, Sprint 25].

##### 4. Verification via Telemetry
*   **Task:** Utilize the **Telemetry Panel** in the Web UI to validate implementation.
*   **Requirement:** Verify the **Retrieval Strategy** reflects the used tool (e.g., `summary-entity` or `semantic`).
*   **Requirement:** Confirm the **Citation Count** matches the internal documents (e.g., the 2 registered RFPs).
*   **Requirement:** Validate that the **Heuristic Alignment Confidence** score accurately reflects reliance on internal citations.

---

#### Validation
*   **Scenario Test:** Prompt: "Summarize registered RFP opportunities in the past 10 years."
*   **Success Metric:** The **ChatExecutionPlan** must show a `CallTool` step. The response must only summarize the 2 RFPs in the repository with 100% citation coverage.
*   **UI Metric:** The **Progress Panel** must show all steps as `Completed` (correcting the previous "Failed" reporting error) [Sprint 24].
*   **Telemetry Metric:** The assistant response must show a non-zero citation count and the correct retrieval strategy used.

#### Completion Notes
*   `ChatPlanningService` treats RFP and repository-wide prompts as high-grounding intents requiring mandatory tool calls.
*   `ChatExecutionEngine` blocks synthesis until mandatory tool output returns internal context, and fails the job when the tool fails or returns no context.
*   `RetrievalAugmentedPromptBuilder` contains the Aletheia-only identity directive and zero-hallucination policy.
*   `MetadataKnowledgeSourceResolver` prioritizes RFP aliases when ranking candidate source filenames.
*   `ProgressPanel.razor` now surfaces retrieval strategy, tool name, and invocation count in execution telemetry.
*   `ChatExecutionEngineTests.Engine_executes_rfp_ten_year_scenario_with_mandatory_tool_and_grounding_telemetry` covers the scenario requested by this sprint.
*   `ChatExecutionEngine` now falls back from mandatory GraphRAG/global graph tool calls to `AletheiaKnowledgePlugin.SearchRags` when graph summaries or communities are unavailable, preserving strict internal grounding for small or freshly ingested corpora.
*   `ChatExecutionEngineTests.Engine_falls_back_to_rags_when_mandatory_graphrag_has_no_communities` covers the observed failure `No communities detected in the graph.` for "Summarize registered RFP opportunities in the past 10 years."
*   The fallback now tries RFP-specific query variants and hydrates all matching registered RFP metadata candidates when broad semantic RAGS returns no context.
*   `ChatExecutionEngineTests.Engine_hydrates_registered_rfp_sources_when_graphrag_and_broad_rags_return_no_context` covers registered RFP sources that require hydration before retrieval.
*   `SemanticKernelPluginRegistrationTests.AddAletheiaAI_registers_agentic_knowledge_plugins_on_kernel` verifies plugin registration on the live Kernel.
*   RAGS unit tests pass with 149 tests; Web unit tests pass with 10 tests; `dotnet build Aletheia.slnx` passes.
*   Local Docker validation rebuilt `api` and `web`; `/health/live`, `/health/ready`, and `/copilot` returned 200.

---

#### Exit Criteria
*   **✓** "RFP" queries trigger mandatory repository tool calls.
*   **✓** Copilot identity is strictly grounded to the **Aletheia Knowledge Estate**.
*   **✓** Hallucinations based on generic training data are eliminated for domain terms.
*   **✓** Telemetry confirms that internal repository context was the source of the answer.
*   **✓** Missing GraphRAG communities no longer fail mandatory RFP grounding when semantic RAGS has internal context.
*   **✓** Missing broad semantic context can trigger registered RFP source hydration before failing.
*   **✓** Build succeeds and all RAGS/Web unit tests pass.
