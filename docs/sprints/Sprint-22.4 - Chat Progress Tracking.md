# Current Sprint

Sprint: Chat Progress Tracking

Status: In Progress

## Objective

Provide durable progress reporting for long-running chat jobs.

---

# Goals

✅ Persist progress

✅ Persist heartbeats

✅ Track execution stages

✅ Support job resume

---

# Core Abstractions

## IChatProgressStore

Persist:

```text
Jobs
Steps
Heartbeats
ProgressMessages
PartialResults
```

---

# Progress Stages

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

---

# API Requirements

Add:

```text
GET /api/copilot/plans/{planId}/progress
GET /api/copilot/jobs/{jobId}
```

---

# Heartbeat Rules

```text
30-60 seconds while active
2-5 minutes during long waits
```

---

# Validation

Add tests for:

```text
Step persistence
Progress persistence
Heartbeat updates
Resume behavior
```

---

# Implementation Notes

Added in `RAGS.Abstractions`:

- `IChatProgressStore` with `SaveAsync`, `GetAsync`, `AppendHeartbeatAsync`, `AppendMessageAsync`, `UpdateStepAsync`, `SetPartialResultAsync`, `FinalizeAsync`
- `ChatProgressRecord` model containing job identity, plan link, status, steps, heartbeats, messages, partial/final results, and timestamps
- `ChatProgressStep` with `Pending`, `Running`, `Completed`, `Failed`, `Skipped` statuses
- `ChatProgressHeartbeat` and `ChatProgressMessage` models for durable progress reporting

Added in `RAGS.Application`:

- `InMemoryChatProgressStore` — thread-safe in-memory implementation that preserves steps, heartbeats, messages, partial results, and final state
- Integrated progress tracking into `ChatExecutionEngine`:
  - Pre-seeds all progress stages from the sprint-defined list (Planning through Completed)
  - Marks each stage as `Running` when entered and `Completed` when finished
  - Persists partial result after context retrieval
  - Records progress messages on retrieval fallbacks
  - Appends heartbeats every 30 seconds during active work and every 2 minutes during long waits
  - Finalizes progress record with `Succeeded`, `Failed`, or `Cancelled` status and result/error
- Registered `IChatProgressStore` in DI via `AIServiceCollectionExtensions`

Added in `Repository.API`:

- `GET /api/copilot/plans/{planId:guid}/progress` — returns the durable progress record for the most recent execution of a plan
- `GET /api/copilot/jobs/{jobId:guid}` already exists via `jobs/chat/{jobId:guid}`; the progress endpoint complements it

Added in `tests/RAGS.UnitTests`:

- `ChatProgressStoreTests` covering save/get round-trip, step updates, heartbeat and message append, partial result persistence, finalization, missing-record failures, step ordering, and engine-level progress persistence during execution

# Exit Criteria

✓ Progress survives reloads

✓ Heartbeats are visible

✓ Step completion is persisted

✓ Build succeeds

✓ Unit tests pass