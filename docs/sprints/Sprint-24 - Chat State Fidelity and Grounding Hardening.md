### Sprint 24 - Chat State Fidelity and Grounding Hardening

**Sprint:** Chat State Fidelity and Grounding Hardening

**Status:** Completed

#### Objective
Resolve UI reporting discrepancies where successful execution steps are erroneously marked as "Failed" and harden the **Copilot's** system instructions to ensure strict grounding within the **Aletheia Knowledge Estate (WRAGS/Repository)**, eliminating external model hallucinations.

---

#### Background
While Sprint 23 resolved execution hangs, current telemetry shows a state-reporting mismatch: the `ChatExecutionEngine` reports a "Succeeded" overall status, yet the `IChatProgressStore` records individual steps like "Planning" and "Completed" as "Failed". Additionally, users report that queries regarding repository content (e.g., RFPs) are eliciting general internet-based knowledge rather than retrieving from the **WRAGS Wiki** or **RAGS** chunks. This indicates a need for more assertive system prompting and tighter integration between the planning and retrieval layers.

---

#### Authority
The repository is the source of truth. This sprint is a remediation effort for the **Conversational Chat Planning** system and does not authorize new architectural modules. All work must align with existing **Clean Architecture** and **Semantic Kernel** orchestration.

---

#### Goals
*   **Correct Progress Step Transitions:** Fix the logic in the `ChatExecutionEngine` and `ProgressPanel.razor` to ensure steps transition to `Completed` instead of `Failed` upon successful task finishing.
*   **Enforce Strict Grounding:** Update the **Copilot** system prompts to explicitly define the **Aletheia Knowledge Estate** as the sole authoritative source, forbidding the use of general world knowledge for repository-specific queries.
*   **Validate RFP Retrieval:** Ensure that inquiries about RFPs specifically trigger retrieval from the **WRAGS repository** and return cited evidence.
*   **Synchronize Status Reporting:** Align the overall `ChatJobStatus` with individual `ChatProgressStep` records to provide a consistent user experience.

---

#### Requirements

##### 1. Progress State Remediation
*   **Task:** Audit the `ChatExecutionEngine` background service loop.
*   **Requirement:** Ensure that when a `ChatExecutionPlan` step completes successfully, it calls `IChatProgressStore.UpdateStepAsync` with `ChatProgressStatus.Completed`.
*   **Requirement:** Investigate the "Completed" step logic to ensure it does not default to a `Failed` state during the finalization of the assistant response.
*   **Requirement:** Fix the Blazor `ProgressPanel.razor` component to correctly interpret and display the success state.

##### 2. Knowledge Grounding & Prompt Engineering
*   **Task:** Update the core prompt template in `SemanticKernelCopilotService`.
*   **Requirement:** Insert a strict directive: "You are an agent of the Aletheia platform. Your knowledge is limited to the provided WRAGS and RAGS context. If information about RFPs is not in the retrieved context, state that it is not found in the repository rather than using external knowledge".
*   **Requirement:** Refine the `IChatPlanningService` to ensure that broad queries (e.g., "RFP summaries") are always routed through the appropriate retrieval strategy (**GraphRAG** or **WRAGS**) to ensure internal context is found.

##### 3. Telemetry and Verification
*   **Task:** Enhance the `ChatTelemetryService`.
*   **Requirement:** Verify that the "Heuristic Alignment Confidence" correctly reflects the reliance on internal citations.
*   **Requirement:** Ensure that the final response telemetry explicitly lists **WRAGS** or **GraphRAG** as the retrieval strategy used.

---

#### Validation
*   **Scenario Test:** Execute the prompt "Provide a summary of RFP's as registered in the last 10 years".
*   **Success Metric:** The response must contain **zero** general internet facts and at least one citation from the internal Repository or WRAGS Wiki.
*   **UI Metric:** All steps in the Progress Panel (Planning, Retrieving Context, Synthesizing, Completed) must show green "Completed" indicators upon a successful answer.
*   **State Metric:** Confirm `GET /api/copilot/jobs/chat/{id}` returns `Succeeded` and the `ChatProgressRecord` steps are all `Completed`.

#### Completion Notes
*   `ChatExecutionEngineTests.Engine_marks_all_successful_steps_completed` verifies successful jobs do not leave `Planning` or `Completed` steps in `Failed` state.
*   `ProgressPanelTests.Completed_step_renders_success_badge` verifies completed steps render as success badges.
*   `ChatExecutionEngineTests.Engine_executes_rfp_ten_year_scenario_with_mandatory_tool_and_grounding_telemetry` verifies the RFP scenario completes with mandatory internal tool context and citation telemetry.
*   Follow-up hardening: mandatory GraphRAG tool failures caused by missing communities now fall back to semantic RAGS, and actual tool errors mark the `Call repository tool` step failed instead of showing a false completed tool step.
*   RAGS and Web unit tests pass after remediation.

---

#### Exit Criteria
*   **✓** "Planning" and "Completed" steps no longer erroneously report as "Failed" in the UI.
*   **✓** Copilot responses for RFP queries are strictly grounded in repository content.
*   **✓** System prompts are updated to prioritize the **Aletheia Knowledge Estate** over LLM training data.
*   **✓** Build succeeds and all RAGS/Web unit tests pass.

---

#### Out Of Scope
*   Adding new vector or graph database providers.
*   Modifying the **MinIO** or **PostgreSQL** storage schemas.
*   Redesigning the **WRAGS Wiki** lifecycle management.
