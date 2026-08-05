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

The returned plan may have `requiresApproval: false` for fast-path work. The Blazor UI still shows the plan and waits for the operator to click **Run** so every new request has a clear acceptance point.

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

The Blazor Copilot page persists the visible session, pending plan, progress, telemetry, active job ID, draft input, output format, and execution-panel layout in browser storage under `aletheia.copilot.session.v1`. This is UI convenience state only; API plan/job records remain process-local until durable server-side persistence is added.

The Copilot page also mirrors progress messages into the global Activity panel. Activity entries include a prompt snippet at planning time, approval/job queue events, tool dispatch, graph fallback messages when applicable, repository context verification, and the final synthesis handoff. These entries are intended to prove that the chat agent received the request and that the configured repository tool was actually called.

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

Mandatory repository tool calls also append `Tool call` heartbeats while SearchRags, GraphRAG, LazyGraphRAG, global graph, fallback query variants, or scoped per-source retrieval are still running. `Tool call` emits an immediate heartbeat and then uses the normal `HeartbeatIntervalSeconds` cadence, not the long-wait cadence. A hung tool call should fail the `Call repository tool` step by `ChatExecutionEngine:MandatoryToolTimeoutSeconds` or the longer watchdog backstop rather than by a short 90-second watchdog window.

Copilot chat now keeps GraphRAG/LazyGraphRAG/global graph tools active. Broad/global retrieval tries GraphRAG first, LazyGraphRAG second, and Semantic RAGS fallback when graph context is missing. Scoped RFP/CMP/document feature prompts remain on Semantic RAGS because source-scoped document evidence is the desired answer context.

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
  "retrievalStrategy": "semantic",
  "toolName": "AletheiaKnowledgePlugin.SearchRags",
  "toolInvocationCount": 2,
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

The Blazor UI polls `GET /copilot/plans/{planId}/progress` every 2 seconds while a job is active. Polling stops when the status is terminal (`Succeeded`, `Failed`, or `Cancelled`). After a plan is approved and execution starts, the visible Execution Plan card is hidden until another approval is needed; progress and telemetry remain visible in the execution panel.

For production use with many clients, consider:

- Capping the polling rate (e.g., 2–5 seconds).
- Adding a server-side cache for progress reads.
- Replacing polling with Server-Sent Events or WebSockets for lower latency and less load.

## Error Handling

All endpoints return structured errors:

Copilot mandatory repository plans route by intent: scoped RFP/document prompts use `AletheiaKnowledgePlugin.SearchRags`, broad non-RFP corpus prompts use `AletheiaKnowledgePlugin.SearchGraphRag`, and explicit lazy graph prompts can use `AletheiaKnowledgePlugin.SearchLazyGraphRag`. If a graph path returns no usable context, the engine falls back to Semantic RAGS, including query variants and registered-source hydration for scoped prompts.

If Repository metadata or Taxonomy can identify the scope, such as `RFP`, but RAGS retrieval still returns no chunks, treat the condition as RAGS index drift. Queue background repair with:

```http
POST /api/jobs/rags/repair?query=RFP
```

The repair endpoint returns an Activity-visible background job snapshot. Use `POST /api/jobs/rags/repair` with no query to rebuild all registered Repository sources.

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
- Activity now polls recent Copilot chat jobs in addition to ingestion jobs. Activity prompt snippets are operational traces; review access controls before using this in a sensitive production environment.
- RAGS index repair jobs appear as `RagsRepair` background jobs and report document-level progress. They do not depend on the UI staying open.
- Copilot orchestration guidance is loaded from `ChatAgent:OrchestrationScriptPath` and defaults to `Prompts/copilot-rags-orchestration.md` in the API container.
- Telemetry is best-effort. Provider-reported metrics are preferred; text-length heuristics are used as fallback.
- `AlignmentConfidence` is a retrieval heuristic, not a calibrated correctness score.

## Validation

Run:

```bash
dotnet build Aletheia.slnx
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
```
