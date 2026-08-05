### Sprint 45 - Holistic Independent Synthesis and Entity Linking
**Status:** Planned

#### Objective
Resolve the **"Source Collapse"** regression where the Copilot blends multiple project entities into a single narrative or ignores specific documents (e.g., CMP 2026). This sprint programmatically enforces **Holistic Independent Synthesis**, ensuring every project’s requirements, descriptions, and notes are extracted and linked uniquely to their respective document entities.

---

#### Background
Recent execution telemetry shows that even when the **Aletheia Knowledge Estate** identifies multiple relevant sources (like the 2022 and 2026 RFPs), the synthesis layer often collapses them into a single summary or ignores one entirely [History]. While the **RAGS Orchestration Playbook** provides guidance, this sprint moves that logic into the **ChatExecutionEngine** and **RetrievalAugmentedPromptBuilder** as a mandatory structural constraint to ensure 100% identity fidelity for every project in the retrieved set [Sprint 27, 241, History].

---

#### Authority
The **Repository** is the authoritative system of record. All work must utilize the existing **Semantic Kernel** orchestration and **Clean Architecture** patterns established in the core platform.

---

#### Goals
*   **Enforce Per-Source Synthesis:** Programmatically mandate that the agent renders a distinct section for every unique **Source ID** identified in the retrieval context [History].
*   **Holistic Detail Extraction:** Ensure descriptions, notes, and metadata are extracted alongside requirements for a complete project profile [History].
*   **Eliminate Information Blending:** Prevent the mixing of facts between distinct projects (e.g., CMP 2022 and 2026) [History].
*   **Unique Entity Linking:** Explicitly link every synthesized statement to its specific document entity via citations and UI headers [Sprint 30, 257].

---

#### Requirements

##### 1. Synthesis Constraint Hardening
*   **Task:** Update the `RetrievalAugmentedPromptBuilder`.
*   **Requirement:** Insert a mandatory structural directive: "You are provided with context for [X] distinct document entities. You **must** provide a holistic summary for each entity independently, including its description, notes, and requirements. Do not merge their details. Use the Source Name as a primary header for each section" [History].
*   **Requirement:** Explicitly forbid the agent from omitting any source provided in the `RetrievalResults` set [Sprint 27, 241].

##### 2. Execution Engine Fidelity
*   **Task:** Refine the `ChatExecutionEngine` synthesis handoff.
*   **Requirement:** Verify that the **scoped retrieval set** contains bounded context for *every* identified metadata match before triggering synthesis [Sprint 27, 241].
*   **Requirement:** Pass the verified **Source ID** list as a metadata hint to the synthesis prompt to prevent "latest-source bias" [History].

##### 3. Holistic Linking and UI Segmentation
*   **Task:** Update the `Copilot` synthesis logic and `Aletheia.Web` components.
*   **Requirement:** Ensure the generated Markdown utilizes independent headers for each project to visually separate "Description," "Notes," and "Requirements" per entity [History].
*   **Requirement:** Strengthen **Evidence Fidelity** to ensure citations for "Notes" or "Descriptions" point correctly to their specific source chunks [Sprint 30, 257].

---

#### Validation
*   **Scenario Test:** Execute the prompt: "Summarize the projects for the registered RFPs."
*   **Success Metric:** The response must contain **two distinct sections** (CMP 2022 and CMP 2026), each containing its specific descriptions, requirements, and notes with unique citations [History].
*   **UI Metric:** The **Telemetry Panel** must show citations from both document IDs, and the **Retrieval Strategy** should indicate `scoped-collection` [Sprint 27, 241].
*   **Regression Test:** Verify that broad "RFP" queries still trigger the mandatory tool path before synthesis [Sprint 26, 235].

---

#### Exit Criteria
*   **✓** Projects are synthesized independently with no blending of facts.
*   **✓** Holistic details (descriptions, notes) are linked to the correct document entities.
*   **✓** CMP 2026 details are no longer ignored in the summary.
*   **✓** Telemetry confirms multi-source context was used for the final answer.
*   **✓** Build and all RAGS/Web unit tests pass.

---

#### Out Of Scope
*   Modifying the underlying database schemas in PostgreSQL or Neo4j.
*   Redesigning the **Search Center** or **WRAGS Wiki** layouts.