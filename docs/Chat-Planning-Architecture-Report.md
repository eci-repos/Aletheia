# Chat Planning Architecture Report

## Purpose

This document describes the conversational planning system added to Aletheia in Sprints 22.1–22.7. It covers the architecture of chat plan creation, approval, background execution, progress tracking, telemetry reporting, and the Blazor user experience.

## Goals

- Surface what the Copilot will do before it does it.
- Require explicit approval for expensive or risky work.
- Execute long-running plans in the background without blocking the chat UI.
- Report durable progress so users can refresh, close the tab, and return later.
- Capture execution telemetry and compare estimates with actuals.
- Provide a production-ready handoff point for future sprints.

## High-Level Flow

```text
User prompt
    |
    v
POST /api/copilot/plan  -> IChatPlanningService creates a ChatPlanRecord
    |
    v
Plan preview shown in Blazor (PlanPreview.razor)
    |
    v
User approves -> POST /api/copilot/plans/{planId}/approve
    |
    v
POST /api/copilot/plans/{planId}/execute -> IChatExecutionService queues a ChatJob
    |
    v
ChatExecutionEngine background worker runs the job
    |
    v
Progress persisted via IChatProgressStore
    |
    v
GET /api/copilot/plans/{planId}/progress polled by Blazor
    |
    v
Final answer inserted into conversation with telemetry
```

## Domain Models

### ChatPlanRecord

A proposed or approved unit of work.

| Field | Meaning |
| --- | --- |
| `PlanId` | Stable identifier |
| `Prompt` | User prompt driving execution |
| `Mode` | Execution mode (`CorpusAnalysis`, `TimelineAnalysis`, `ComparativeAnalysis`, `StructuredSynthesis`, `Retrieval`, etc.) |
| `Status` | `Proposed`, `Approved`, `Cancelled`, `Expired` |
| `Steps` | Human-readable step names |
| `EstimatedSecondsMin/Max` | Expected duration range |
| `EstimatedLlmCalls` | Expected model calls |
| `EstimatedInput/OutputTokens` | Expected token usage |
| `EstimatedRetrievalCount` | Expected number of retrieved chunks |
| `RequiresApproval` | Whether the UI must ask before running |
| `ExpiresAt` | Plan validity deadline |

### ChatJobSnapshot

Runtime state of a queued or running execution.

| Field | Meaning |
| --- | --- |
| `JobId` | Runtime identifier |
| `PlanId` | Parent plan |
| `Status` | `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled` |
| `Stage` | Current high-level stage |
| `PercentComplete` | Approximate progress |
| `Detail` | Human-readable detail |
| `CreatedAt`, `StartedAt`, `LastHeartbeatAt`, `CompletedAt` | Lifecycle timestamps |
| `Result`, `Error` | Final outcome |

### ChatProgressRecord

Durable progress report persisted while the job runs.

| Field | Meaning |
| --- | --- |
| `JobId` / `PlanId` | Identifiers |
| `Status` | Current job status |
| `Steps` | Checklist of steps with status |
| `Heartbeats` | Liveness events |
| `Messages` | Operational messages |
| `PartialResult` | Intermediate output |
| `FinalResult` / `Error` | Terminal output |
| `PercentComplete` | Progress percentage |
| `Telemetry` | Optional execution telemetry |

### ChatExecutionTelemetry

Post-execution metrics and estimate comparison.

| Field | Meaning |
| --- | --- |
| `ElapsedSeconds` | Actual wall-clock time |
| `PromptTokens` / `CompletionTokens` | Token usage |
| `TokensPerSecond` | Throughput |
| `RetrievalCount` / `CitationCount` | Retrieved chunks and citations |
| `LlmCallCount` | Model invocations |
| `Estimated*` | Values from the original plan |
| `AlignmentConfidence` | Retrieval-based confidence |
| `ConfidenceBasis` | Explanation of confidence |
| `EstimateComparisonSummary` | Human-readable plan-vs-actual |
| `UsedProviderMetrics` | Whether provider-reported metrics were used |

## Services

| Service | Interface | Responsibility |
| --- | --- | --- |
| `ChatPlanningService` | `IChatPlanningService` | Create plans from a user prompt |
| `ChatPlanApprovalService` | `IChatPlanApprovalService` | Persist, approve, cancel, and expire plans |
| `InMemoryChatPlanRepository` | `IChatPlanRepository` | In-memory plan persistence |
| `ChatExecutionEngine` | `IChatExecutionService`, `IChatExecutionEngine` | Background worker that executes approved plans |
| `InMemoryChatProgressStore` | `IChatProgressStore` | Durable-ish progress persistence (in-memory) |
| `ChatTelemetryService` | `IChatTelemetryService` | Build telemetry and estimate comparison |
| `SemanticKernelCopilotService` | `ICopilotService` | Final synthesis step |

## Execution Stages

The engine runs the following fixed stages:

1. Planning
2. Finding candidate sources
3. Filtering sources
4. Retrieving context
5. Expanding graph context
6. Extracting requested facts
7. Validating citations
8. Synthesizing answer
9. Finalizing telemetry
10. Completed

Each stage becomes a `ChatProgressStep` with `Pending`, `Running`, `Completed`, `Skipped`, or `Failed` status. Heartbeats are emitted during long stages.

## API Surface

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/copilot/plan` | Create a plan from a prompt |
| POST | `/api/copilot/plans/{planId:guid}/approve` | Approve a proposed plan |
| POST | `/api/copilot/plans/{planId:guid}/cancel` | Cancel a proposed plan |
| GET | `/api/copilot/plans/{planId:guid}` | Get plan details |
| POST | `/api/copilot/plans/{planId:guid}/execute` | Start execution of an approved plan |
| GET | `/api/copilot/jobs/chat/{jobId:guid}` | Get job snapshot |
| POST | `/api/copilot/jobs/chat/{jobId:guid}/cancel` | Cancel a running job |
| GET | `/api/copilot/jobs/chat` | List recent chat jobs |
| GET | `/api/copilot/plans/{planId:guid}/progress` | Get durable progress for a plan |
| GET | `/api/copilot/jobs/chat/{jobId:guid}/telemetry` | Get execution telemetry |

## Blazor Components

| Component | Responsibility |
| --- | --- |
| `Pages/Copilot/Index.razor` | Chat page, planning flow, approval, polling, recovery |
| `Pages/Copilot/PlanPreview.razor` | Show plan details, Run/Revise/Cancel controls |
| `Pages/Copilot/ProgressPanel.razor` | Progress bar, step checklist, heartbeat, elapsed time, telemetry |
| `Services/RepositoryApiClient.cs` | HTTP client for all Copilot planning/progress endpoints |

## Recovery and Refresh Safety

- `Index.razor` calls `RestoreActiveExecutionAsync` on init.
- It lists recent jobs and recovers an active chat job if one exists.
- It rebuilds a minimal `ChatPlanRecord` from the recovered progress and resumes polling.
- Progress is stored via `IChatProgressStore`, so a job that finishes while the user is away still reports telemetry and final result on the next poll.

## Reliability Considerations

- Job state and progress are currently in memory (`InMemoryChatProgressStore`). API restart loses active jobs and history.
- Cancellation is cooperative: `ChatJobState.IsCancelled` is checked before expensive operations.
- The background queue is process-local and single-service.
- Heartbeats are coarse (30 s for active synthesis, 2 min for long waits).
- Telemetry is best-effort; provider-reported metrics are preferred, with text-length heuristics as fallback.
- `AlignmentConfidence` is a retrieval heuristic, not a calibrated correctness score.

## Future Hardening Recommendations

1. Persist plans, jobs, and progress in PostgreSQL so they survive API restart.
2. Add retry policy with exponential backoff for transient LLM/retrieval failures.
3. Add admin endpoints to inspect and cancel any job.
4. Replace polling with SSE or WebSockets for lower-latency progress.
5. Add integration tests against a real HTTP host for the full Copilot planning flow.
6. Replace estimated token counts with provider usage metadata when available.
7. Calibrate `AlignmentConfidence` against a benchmark set.

## Validation

Run:

```bash
dotnet build Aletheia.slnx
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj
```
