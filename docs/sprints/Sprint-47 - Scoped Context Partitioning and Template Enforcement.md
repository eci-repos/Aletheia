Sprint 47 - Scoped Context Partitioning and Template Enforcement
Status: Planned
Objective
Eliminate information bleeding by programmatically partitioning the context provided to the LLM into distinct Source Blocks and enforcing a Loop-Based Synthesis Template that prevents the agent from skipping or merging documents.
Goals
Context Partitioning: Modify the RetrievalAugmentedPromptBuilder to wrap retrieval results in clear XML-style tags or JSON blocks that explicitly separate SourceID: CMP-2022 from SourceID: CMP-2026.
Template-Based Synthesis: Force the agent to follow a strict output schema: [For Each Source_ID -> Render Section].
Fidelity Check: Implement a "Post-Synthesis Audit" in the engine that verifies the number of sections in the response matches the number of unique Source IDs in the retrieval set.
Requirements
1. Mandatory Context Demarcation
Task: Update the RetrievalAugmentedPromptBuilder.
Requirement: Instead of a flat list of text chunks, the prompt must now present the context as: --- START SOURCE: [Source_Name] (ID: [Source_ID]) --- [Text Chunks] --- END SOURCE: [Source_Name] ---
Requirement: Instruct the agent: "You are prohibited from using information found between the CMP 2022 tags to answer questions regarding the CMP 2026 entity."
2. Synthesis Structural Enforcement
Task: Update the core system prompt for the Synthesizing stage.
Requirement: The agent must be told: "Your response must be a collection of independent summaries. If the retrieval set contains 2 distinct Source IDs, your output MUST contain 2 distinct headers. Failure to list a separate summary for every source ID provided is a grounding violation."
3. Deep-Scan Intent Hardening (RFP/AI focus)
Task: Refine Sprint 36's Deep-Scan trigger.
Requirement: When "AI features" are requested for CMP 2026, the Secondary Intent Scan must use an expanded keyword vector (e.g., "Machine Learning," "LLM," "Automation") to ensure technical fragments are prioritized over "Schedule" or "Insurance" fragments.
Validation
Scenario Test: Prompt: "Provide a summary of the CMP projects as described in the RFPs."
Success Metric: The UI renders two distinct sections with accurate, non-blended details for both 2022 (Data Warehouse) and 2026 (AI).
Telemetry Metric: The Telemetry Panel must show citations from both document IDs and reflect the scoped-collection strategy.