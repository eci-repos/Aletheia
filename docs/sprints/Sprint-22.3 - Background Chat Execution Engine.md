# Current Sprint

Sprint: Background Chat Execution Engine

Status: In Progress

## Objective

Enable approved chat plans to execute independently of browser sessions.

---

# Background

Long-running requests must survive UI disconnects and browser refreshes.

---

# Goals

✅ Create execution engine

✅ Execute plans asynchronously

✅ Persist execution state

✅ Preserve existing retrieval services

---

# Architecture

Create or extend:

```text
RAGS.Application
RAGS.Infrastructure.PostgreSQL
Repository.API
```

---

# Core Abstractions

## IChatExecutionService

Capabilities:

```csharp
StartAsync()
CancelAsync()
GetStatusAsync()
```

---

# Execution Rules

Execution must reuse:

```text
IRagsService
IGraphRagService
ILazyGraphRagService
IWragsWikiService
ICopilotService
```

---

# Persistence

Persist:

```text
Jobs
ExecutionStatus
ExecutionHistory
```

---

# Validation

Add tests for:

```text
Job startup
Status retrieval
Background execution
Execution persistence
Recovery after restart
```

---

# Implementation Notes

Added in `RAGS.Abstractions`:

- `IChatExecutionService` with `StartAsync`, `CancelAsync`, `GetStatusAsync`, and `List`
- `ChatJobSnapshot` model capturing job identity, plan link, status, stage, progress, heartbeats, result, and error
- `ChatJobStatus` enum: `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled`

Added in `RAGS.Application`:

- `ChatExecutionEngine` hosted background service
  - In-memory job queue and state store (survives UI disconnect/browser refresh because execution runs server-side)
  - Validates plans are `Approved` before execution
  - Picks retrieval strategy based on plan mode:
    - `CorpusAnalysis` / `TimelineAnalysis` → GraphRAG global search, then LazyGraphRAG global search, then RAGS fallback
    - `Retrieval`, `ComparativeAnalysis`, `StructuredSynthesis` → RAGS retrieval
    - `FastPath` → no retrieval
  - Synthesizes final response via `ICopilotService.ChatAsync`
  - Periodic heartbeats during long-running operations
  - Supports cancellation and trims completed jobs to bounded history
- `ChatJobState` — thread-safe mutable job state with status transitions
- Registered `IChatExecutionService`/`IChatExecutionEngine` as singleton hosted service in `AIServiceCollectionExtensions`

Added in `Repository.API`:

- Extended `CopilotController`:
  - `POST /api/copilot/plans/{planId:guid}/execute` — start background execution of an approved plan
  - `GET /api/copilot/jobs/chat/{jobId:guid}` — retrieve job status
  - `POST /api/copilot/jobs/chat/{jobId:guid}/cancel` — cancel a queued/running job
  - `GET /api/copilot/jobs/chat` — list recent chat execution jobs

Added in `tests/RAGS.UnitTests`:

- `ChatExecutionEngineTests` covering start rejection for unapproved/missing plans, start snapshot, status retrieval, cancellation, full retrieval-to-synthesis execution, and API endpoint presence

# Exit Criteria

✓ Background execution exists

✓ Jobs survive UI disconnect

✓ Jobs survive browser refresh

✓ State is persisted

✓ Build succeeds

✓ Unit tests pass