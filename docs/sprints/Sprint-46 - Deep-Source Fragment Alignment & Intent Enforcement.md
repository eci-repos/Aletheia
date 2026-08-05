Sprint 46 - Deep-Source Fragment Alignment & Intent Enforcement
Status: Planned
Objective
Ensure that when a specific document (e.g., CMP 2026) is named in a prompt, the ChatExecutionEngine performs a comprehensive "Deep-Scan" of that specific entity, ensuring that specific technical intents (like "AI features") are not buried by general document metadata.
Background
Previous sprints established Mandatory Tool Calling [Sprint 25, 217] and Independent Source Synthesis [Sprint 35, History]. However, current behavior shows that the retrieval layer is still "collapsing" the context by providing chunks that are semantically broad rather than intent-specific. Rule 6 of the Aletheia Copilot RAGS Orchestration Playbook (Independent Holistic Synthesis) must be enforced through a Secondary Targeted Search when a primary search fails to find specific requirements in a named document [History].
Goals
Mandatory Entity Filter: When a document is named (CMP 2026), the SearchRags tool must apply a strict source_id filter to exclude noise from other documents like CMP 2022
.
Secondary Intent Scan: If a primary search for "AI requirements" in a scoped document returns low-confidence chunks, trigger a Keyword-Augmented Deep Scan of that specific source's index
.
Enforce Playbook Rule 9 (Index Integrity): If metadata exists for a named document but specific content is missing, the agent must report potential "index drift" or trigger a RAGS index repair/re-hydration for that document [History].
Requirements
1. Intent-Driven Scoped Retrieval
Task: Update IChatPlanningService and ChatExecutionEngine.
Requirement: When a prompt names a specific document (CMP 2026), the Execution Plan must include a CallTool: SearchRags step with a mandatory source_id filter
.
Requirement: If the first retrieval pass for a technical intent (AI) returns no results, the engine must perform a Secondary Search using synonym expansion (e.g., "artificial intelligence," "machine learning," "automated reasoning") specifically within that source_id
.
2. Synthesis Fidelity Hardening
Task: Update RetrievalAugmentedPromptBuilder.
Requirement: Instruct the agent that if it was given context for a specific named document but the chunks seem unrelated to the user's specific technical query, it must state that it is re-scanning the document metadata rather than concluding the information does not exist [History].
Requirement: Force the synthesis to use Rule 7 (Unique Entity Linking) to ensure that if any information is found, it is uniquely tied to the CMP 2026 record [History].
3. Verification via Telemetry
Task: Enhance the Telemetry Panel to show "Search Expansion" attempts.
Requirement: If a secondary "Deep-Scan" was triggered, it must be visible in the Retrieval Strategy (e.g., semantic-deep-scan)
.
Requirement: The Citation Count must reflect chunks specifically from the CMP 2026 source_id
.
Validation
Scenario Test: Prompt: "List all AI requirements as detailed in CMP 2026."
Success Metric: The Execution Plan shows a mandatory tool call filtered to CMP 2026. The response extracts the AI features from that document specifically, with zero blending from the CMP 2022 Data Warehouse project
.
UI Metric: All steps in the Progress Panel (Planning, Retrieving Context, Deep Scanning, Synthesizing) must show green Completed indicators
.
Exit Criteria
✓ "Not found" errors for named documents are eliminated when content is present.
✓ Mandatory source-filtering is applied to named-document queries.
✓ Secondary deep-scans are triggered for low-confidence technical queries.
✓ CMP 2022 details no longer "bleed" into CMP 2026 scoped questions.
✓ Build and all RAGS/Web unit tests pass.