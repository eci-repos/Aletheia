# Current Sprint

Sprint: Conversational Chat Planning and Progress Feedback

Status: Active

## Objective

Make long-running Copilot chat requests visible, explainable, cancelable, and user-approved before expensive execution begins.

When a user asks a broad question such as:

```text
Identify RFP requests as registered in the last 10 years.
```

the system must not leave the user waiting for 20 minutes without context.

Instead, Copilot should prepare an execution plan, estimate effort, ask the user to approve or cancel, then show progress as each step completes.

---

# Background

Current Copilot chat can run expensive retrieval, WRAGS, GraphRAG, LazyGraphRAG, summarization, and synthesis work behind a single request.

For broad corpus-level questions, the user cannot tell whether the system is:

- retrieving documents
- expanding graph context
- reading wiki summaries
- waiting on the LLM
- stuck
- close to completion
- likely to exceed a practical budget

This sprint introduces a conversational execution layer for long-running chat work.

---

# Authority

The repository is the source of truth.

Phase 21 supersedes the previous v1.0 release-freeze sprint.

Completed v1.0 functionality must not be regressed.

---

# Goals

This sprint shall:

✅ Detect chat prompts likely to take a long time

✅ Generate a visible execution plan before starting heavy work

✅ Estimate elapsed time, token cost, retrieval breadth, and LLM call count where possible

✅ Let the user approve, cancel, or revise the plan

✅ Execute approved plans as background chat jobs independent of the UI tab

✅ Show step-by-step progress in the chat conversation

✅ Persist progress so the user can leave the UI and return later

✅ Return final answer telemetry after completion

✅ Preserve existing fast chat behavior for simple prompts

---

# User Experience

## Fast Path

Simple questions continue to execute immediately.

Example:

```text
What is the due date in the latest RFP?
```

Expected behavior:

```text
Copilot answers directly with citations and stats.
```

---

## Planned Path

Broad or expensive prompts trigger planning.

Example:

```text
Identify RFP requests as registered in the last 10 years.
```

Expected behavior:

```text
This may take several minutes.

Plan:
1. Locate RFP-related documents.
2. Filter records to the last 10 years.
3. Retrieve relevant chunks and WRAGS pages.
4. Extract request names, dates, owners, and citations.
5. Synthesize a table with evidence.

Estimated time: 5-15 minutes
Estimated model calls: 4-12
Estimated context: 2 documents, 40-80 chunks

Proceed?
```

The user can choose:

```text
Run plan
Revise plan
Cancel
```

---

## Progress Path

After approval, the chat displays progress as conversational updates.

Example:

```text
Running plan...

✓ Located 2 candidate RFP documents.
✓ Filtered documents by registration date.
→ Retrieving WRAGS and GraphRAG context.
Pending: extraction, synthesis, final citation validation.

Progress: 45%
Elapsed: 3m 20s
```

Updates should appear on stage transitions and periodic heartbeats.

Do not stream noisy internal traces into the chat.

---

# Architecture

Create or extend:

```text
RAGS.Abstractions
RAGS.Application
Repository.API
Aletheia.Web
RAGS.Infrastructure.PostgreSQL
```

Do not bypass existing abstractions.

Do not make chat background execution depend on the browser tab remaining open.

---

# Core Abstractions

## IChatPlanningService

Classify a chat prompt before execution.

Capabilities:

```csharp
AnalyzePromptAsync()
CreatePlanAsync()
EstimatePlanAsync()
RequiresApproval()
```

Plan signals should include:

```text
Prompt breadth
Expected retrieval mode
Expected document count
Expected chunk count
Expected LLM calls
Estimated elapsed time
Estimated token budget
Confidence in estimate
```

---

## ChatExecutionPlan

Represent the proposed work.

Fields:

```text
PlanId
SessionId
Prompt
Mode
Steps
EstimatedSecondsMin
EstimatedSecondsMax
EstimatedLlmCalls
EstimatedInputTokens
EstimatedOutputTokens
EstimatedRetrievalCount
RequiresApproval
CreatedAt
ExpiresAt
```

Each step should include:

```text
StepId
Name
Description
Status
PercentWeight
StartedAt
CompletedAt
Error
```

---

## IChatExecutionService

Execute approved plans.

Capabilities:

```csharp
StartAsync()
CancelAsync()
GetStatusAsync()
AppendProgressAsync()
CompleteAsync()
FailAsync()
```

Execution must reuse existing retrieval services:

```text
IRagsService
IGraphRagService
ILazyGraphRagService
IWragsWikiService
ICopilotService
```

---

## IChatProgressStore

Persist plan and execution state.

Initial provider:

```text
PostgreSQL
```

Persist:

```text
Plans
Jobs
Steps
Heartbeats
Partial progress messages
Final assistant response
Telemetry
Failure details
Cancellation state
```

---

# API Requirements

Add endpoints:

```text
POST /api/copilot/plan
POST /api/copilot/plans/{planId}/approve
POST /api/copilot/plans/{planId}/cancel
GET  /api/copilot/plans/{planId}
GET  /api/copilot/plans/{planId}/progress
GET  /api/copilot/jobs/{jobId}
```

Existing:

```text
POST /api/copilot/chat
```

must remain compatible for fast-path chat.

---

# UI Requirements

Update:

```text
Aletheia.Web/Pages/Copilot/Index.razor
```

The chat UI must support:

- Plan preview in the conversation
- Run / Revise / Cancel controls
- Background job status
- Step-by-step checklist
- Elapsed time
- Heartbeat age
- Percent complete
- Final answer insertion when complete
- Useful failure details

The user must be able to refresh the browser and continue watching the running chat job.

---

# Planning Rules

Planning should trigger for prompts that imply broad work, including:

```text
last N years
all RFPs
compare all documents
identify every request
summarize corpus
main themes
timeline
matrix
compliance review
across registered documents
```

Fast-path chat should continue for narrow prompts that can be answered from a small retrieval set.

---

# Progress Rules

Use concise progress updates.

Recommended stages:

```text
Planning
Finding candidate sources
Filtering sources
Retrieving context
Expanding graph context
Extracting requested facts
Validating citations
Synthesizing answer
Finalizing telemetry
Completed
```

Heartbeat frequency:

```text
30-60 seconds while active
2-5 minutes for long model waits
```

Progress must not claim exact precision when only estimates are available.

Use:

```text
about 40%
roughly halfway
waiting on model response
```

instead of false precision.

---

# Cancellation

Users must be able to cancel a planned or running chat job.

Cancellation should:

- Mark the job as canceled
- Stop pending steps
- Preserve completed progress
- Leave partial results visible
- Avoid corrupting chat history

---

# Telemetry

Final assistant messages should include:

```text
Elapsed seconds
Tokens per second
Estimated or provider-reported token counts
Retrieval count
Citation count
LLM call count
Plan estimate vs actual
Alignment confidence
Confidence basis
```

Prefer provider-reported usage when available.

Fallback to existing heuristic estimates.

---

# Validation

Execute:

```bash
dotnet build Aletheia.slnx
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
```

Add tests for:

- Prompt planning threshold
- Plan estimate creation
- Plan approval
- Background execution status
- Step progress updates
- Cancellation
- Persistent resume after UI reload
- Fast-path chat compatibility
- Failure details surfaced to UI

---

# Deliverables

Create or update:

```text
Chat Planning Architecture Report
Copilot Progress API documentation
AdministrationGuide
Architecture
Technical-Presentation-Guide
Phase21-Background-Operations-Handoff
```

Implementation deliverables:

```text
IChatPlanningService
IChatExecutionService
IChatProgressStore
PostgreSQL chat progress persistence
Copilot planning APIs
Copilot background chat execution
Copilot progress UI
Focused unit tests
```

---

# Exit Criteria

✓ Broad chat prompts show an execution plan before expensive work starts

✓ User can approve, revise, or cancel the plan

✓ Approved long-running chat work runs in the background

✓ Progress survives browser refresh or closed UI tab

✓ Chat shows stage-level task completion

✓ Heartbeats indicate the job is alive during long waits

✓ Final answer includes telemetry and citations

✓ Fast chat remains compatible

✓ Build succeeds

✓ RAGS unit tests pass

✓ Repository unit tests pass

✓ Documentation is updated for next-agent handoff

---

# Out Of Scope

Do NOT:

- Replace existing Copilot prompt logic wholesale
- Replace RAGS, GraphRAG, LazyGraphRAG, or WRAGS retrieval services
- Add a new queue provider unless explicitly authorized
- Add noisy token-by-token chat logging
- Claim exact ETA or percent complete when only estimates exist
- Remove authentication

Focus on user-visible planning, durable progress, and recoverable long-running chat execution.
