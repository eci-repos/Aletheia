# Copilot Progress API Documentation

## Base URL

All routes are relative to the API base, for example `http://localhost:8080/api`.

All endpoints require JWT bearer authentication unless the deployment explicitly disables it.

## Planning

### Create a plan

```http
POST /copilot/plan
```

Request body:

```json
{
  "prompt": "What requirements are defined in the Cleveland Metroparks RFP related to activities?"
}
```

Response: `200 OK` with `ChatPlanRecord`.

The returned plan may have `requiresApproval: false` for fast-path work. The Blazor UI auto-approves and executes fast-path plans. Plans with `requiresApproval: true` are shown in `PlanPreview.razor` for explicit approval.

### Approve a plan

```http
POST /copilot/plans/{planId:guid}/approve
```

Response: `200 OK` with the approved `ChatPlanRecord`, or `400 Bad Request` if the plan is not in `Proposed` status or has expired.

### Cancel a plan

```http
POST /copilot/plans/{planId:guid}/cancel
```

Optional request body:

```json
{
  "reason": "User changed their mind."
}
```

Response: `200 OK` with the cancelled `ChatPlanRecord`, or `400 Bad Request` if the plan is not in `Proposed` status.

### Get a plan

```http
GET /copilot/plans/{planId:guid}
```

Response: `200 OK` with `ChatPlanRecord`, `404 Not Found`, or `400 Bad Request`.

## Execution

### Execute an approved plan

```http
POST /copilot/plans/{planId:guid}/execute
```

Response: `202 Accepted` with a `ChatJobSnapshot`. The job is queued and runs in the background.

The plan must be in `Approved` status. If it is not, the response is `400 Bad Request`.

### Get a job snapshot

```http
GET /copilot/jobs/chat/{jobId:guid}
```

Response: `200 OK` with `ChatJobSnapshot`, or `404 Not Found`.

### Cancel a running job

```http
POST /copilot/jobs/chat/{jobId:guid}/cancel
```

Response: `204 No Content` on success, or `400 Bad Request` if the job is not found.

Cancellation is cooperative. The engine checks `ChatJobState.IsCancelled` before expensive operations. If the job has already completed, cancellation has no effect.

### List recent chat jobs

```http
GET /copilot/jobs/chat?take=50
```

Response: `200 OK` with a list of `ChatJobSnapshot`.

## Progress

### Get plan progress

```http
GET /copilot/plans/{planId:guid}/progress
```

Response: `200 OK` with `ChatProgressRecord`, or `404 Not Found` if no execution job exists for the plan.

This is the primary endpoint for polling. It returns the latest progress for the most recent job associated with the plan, including:

- Current status
- Percent complete
- Step checklist
- Heartbeats
- Messages
- Partial result
- Final result or error
- Telemetry (if execution has completed)

### Get job telemetry

```http
GET /copilot/jobs/chat/{jobId:guid}/telemetry
```

Response: `200 OK` with `ChatExecutionTelemetry`, `404 Not Found` if progress or telemetry is not available, or `400 Bad Request`.

Use this endpoint to fetch telemetry explicitly if the progress record has not yet been updated with telemetry.

## Models

### ChatPlanRecord

```json
{
  "planId": "guid",
  "prompt": "string",
  "mode": "CorpusAnalysis",
  "status": "Proposed",
  "steps": [ "Planning", "Retrieving context", "Synthesizing answer" ],
  "estimatedSecondsMin": 1,
  "estimatedSecondsMax": 5,
  "estimatedLlmCalls": 1,
  "estimatedInputTokens": 100,
  "estimatedOutputTokens": 50,
  "estimatedRetrievalCount": 10,
  "requiresApproval": true,
  "expiresAt": "2026-07-26T20:00:00Z"
}
```

### ChatJobSnapshot

```json
{
  "jobId": "guid",
  "planId": "guid",
  "prompt": "string",
  "status": "Running",
  "stage": "Synthesizing answer",
  "percentComplete": 75,
  "detail": "Generating the final response.",
  "createdAt": "2026-07-26T19:00:00Z",
  "startedAt": "2026-07-26T19:00:01Z",
  "lastHeartbeatAt": "2026-07-26T19:00:30Z",
  "completedAt": null,
  "result": null,
  "error": null
}
```

### ChatProgressRecord

```json
{
  "jobId": "guid",
  "planId": "guid",
  "prompt": "string",
  "status": "Running",
  "steps": [
    { "name": "Planning", "status": "Completed", "order": 0 },
    { "name": "Retrieving context", "status": "Running", "order": 3, "detail": "Retrieving chunks" }
  ],
  "heartbeats": [
    { "timestamp": "2026-07-26T19:00:30Z", "stage": "Synthesis", "detail": "Still generating.", "percentComplete": 75 }
  ],
  "messages": [],
  "partialResult": "Retrieved 12 chunks",
  "finalResult": null,
  "error": null,
  "percentComplete": 75,
  "telemetry": null
}
```

### ChatExecutionTelemetry

```json
{
  "jobId": "guid",
  "planId": "guid",
  "elapsedSeconds": 3.5,
  "promptTokens": 120,
  "completionTokens": 80,
  "tokensPerSecond": 22.86,
  "retrievalCount": 12,
  "citationCount": 4,
  "llmCallCount": 1,
  "estimatedSecondsMin": 1,
  "estimatedSecondsMax": 5,
  "estimatedInputTokens": 100,
  "estimatedOutputTokens": 50,
  "estimatedLlmCalls": 1,
  "estimatedRetrievalCount": 10,
  "alignmentConfidence": 0.92,
  "confidenceBasis": "Provider-reported metrics.",
  "estimateComparisonSummary": "Duration: actual 3.5s within estimate 1-5s; Tokens: actual 200 vs estimated 150 (133%); Retrieval: actual 12 vs estimated 10 (120%); Model calls: actual 1 vs estimated 1 (100%).",
  "usedProviderMetrics": true
}
```

## Status Values

### ChatPlanStatus

- `Proposed` — awaiting approval
- `Approved` — ready to execute
- `Cancelled` — explicitly cancelled
- `Expired` — past `expiresAt`

### ChatJobStatus

- `Queued` — waiting for the background worker
- `Running` — currently executing
- `Succeeded` — completed successfully
- `Failed` — failed with an error
- `Cancelled` — cancelled by user

### ChatProgressStepStatus

- `Pending` — not started
- `Running` — in progress
- `Completed` — finished successfully
- `Failed` — step failed
- `Skipped` — not applicable

## Polling Guidance

The Blazor UI polls `GET /copilot/plans/{planId}/progress` every 2 seconds while a job is active. Polling stops when the status is terminal (`Succeeded`, `Failed`, or `Cancelled`).

For production use with many clients, consider:

- Capping the polling rate (e.g., 2–5 seconds).
- Adding a server-side cache for progress reads.
- Replacing polling with Server-Sent Events or WebSockets for lower latency and less load.

## Error Handling

All endpoints return structured errors:

```json
{ "error": "Plan must be approved before execution. Current status: Proposed." }
```

HTTP status codes:

| Status | Meaning |
| --- | --- |
| `200 OK` | Success |
| `202 Accepted` | Execution started, job queued |
| `204 No Content` | Cancellation succeeded |
| `400 Bad Request` | Validation or business rule failure |
| `404 Not Found` | Plan, job, progress, or telemetry not found |
| `500 Internal Server Error` | Unexpected server error |

## Operational Notes

- Job state and progress are currently stored in memory. An API restart loses active jobs and history.
- The background worker is process-local and single-service; it is not distributed.
- Heartbeats are intentionally coarse: every 30 seconds during active synthesis, every 2 minutes during long waits.
- Telemetry is best-effort. Provider-reported metrics are preferred; text-length heuristics are used as fallback.
- `AlignmentConfidence` is a retrieval heuristic, not a calibrated correctness score.

## Validation

Run:

```bash
dotnet build Aletheia.slnx
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
```
