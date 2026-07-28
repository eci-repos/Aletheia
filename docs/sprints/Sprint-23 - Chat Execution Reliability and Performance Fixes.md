# Sprint 23 - Chat Execution Reliability and Performance Fixes
**Status:** Active

## Objective
Investigate and resolve critical performance regressions in the **Copilot Background Execution Engine** where specific steps ("Planning execution steps" and "Retrieving relevant context") hang indefinitely (45+ minutes) despite low document volume (e.g., 2 documents). Ensure the background execution lifecycle is resilient, time-bounded, and accurately reflects state transitions.

---

## Background
Sprint 22 introduced the **Conversational Chat Planning** and **Background Execution Engine** to handle long-running requests. Current telemetry and user reports indicate that the `ChatExecutionEngine` stalls during the transition between plan approval and context retrieval. Specifically, even with a minimal corpus of 2 RFP documents, the system fails to move past the initial execution stages, suggesting a deadlock, an unhandled async exception, or a logic loop in the planning-to-retrieval handoff.

---

## Authority
The repository is the source of truth. This sprint is a high-priority hardening effort focused on the reliability of features introduced in Phase 22. No new architectural patterns are authorized.

---

## Goals
*   **Diagnose and Resolve Execution Hangs:** Identify the root cause of the stall in the "Planning" and "Retrieving context" stages within the `ChatExecutionEngine`.
*   **Implement Step Timeouts:** Introduce mandatory timeouts for individual execution steps to prevent indefinite "Running" states.
*   **Refine Retrieval Logic for Small Corpora:** Ensure the engine does not overhead-loop when document counts are low.
*   **Enhance Progress State Transitions:** Validate that the `IChatProgressStore` correctly transitions from `Running` to `Completed` or `Failed` without orphan "Running" steps.
*   **Add Engine Heartbeat Watchdog:** Implement a mechanism to detect and auto-fail stalled background jobs that have missed multiple heartbeats.

---

## Requirements

### 1. Execution Engine Hardening
*   **Task:** Review the `ChatExecutionEngine` background service loop.
*   **Requirement:** Ensure that the planning execution step accurately consumes the pre-approved `ChatExecutionPlan` rather than attempting to re-plan or entering an infinite wait state.
*   **Requirement:** Add `CancellationToken` propagation to all retrieval service calls (RAGS, GraphRAG, LazyGraphRAG) to ensure they honor the job-level cancellation and system timeouts.

### 2. Retrieval Resilience
*   **Task:** Audit `IKnowledgeSourceResolver` and context retrieval logic used by the engine.
*   **Requirement:** Implement a "Fast-Fail" or "Quick-Return" path for small document sets where exhaustive graph traversal or community analysis is not required.
*   **Requirement:** Ensure that "Retrieving context" does not hang if the vector store or graph database returns zero results or a small result set.

### 3. Progress Tracking Reliability
*   **Task:** Update `IChatProgressStore` and `ChatExecutionEngine` integration.
*   **Requirement:** If an exception occurs during a background step, the engine must catch it, record it in the `ChatProgressRecord` as a `Failed` step, and finalize the job status.
*   **Requirement:** Resolve the issue where a step remains in the `Running` state indefinitely in the UI despite the underlying thread potentially being dead or faulted.

---

## Validation
*   **Scenario Test:** Execute the prompt "Provide a summary of RFP's as registered in the last 10 years" against a 2-document corpus.
*   **Success Metric:** Planning and Context Retrieval must complete in under 30 seconds for a minimal corpus.
*   **Load/Stress Test:** Simulate a retrieval failure or timeout to verify the engine correctly reports the failure to the UI instead of hanging.
*   **Unit Tests:**
    *   `Engine_honors_step_timeouts`: Verify a step fails if it exceeds a configured duration.
    *   `Engine_transitions_to_failed_on_exception`: Verify no steps remain "Running" on a fatal error.
    *   `Engine_completes_instantly_on_small_corpus`: Verify execution speed for < 5 documents.

---

## Exit Criteria
*   **✓** "Planning" and "Retrieving context" steps no longer hang for small corpora.
*   **✓** Background jobs correctly finalize state (Succeeded/Failed/Cancelled) in all scenarios.
*   **✓** Step-level timeouts are enforced.
*   **✓** Heartbeats reliably indicate engine health to the UI.
*   **✓** Build succeeds and all RAGS/Web unit tests pass.

---

## Out Of Scope
*   Adding new retrieval strategies or AI providers.
*   Modifying the Repository (system of record) or file storage logic.
*   Redesigning the Blazor UI components (ProgressPanel/PlanPreview) beyond state-display fixes.

