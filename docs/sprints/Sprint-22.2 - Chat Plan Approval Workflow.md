# Current Sprint

Sprint: Chat Plan Approval Workflow

Status: In Progress

## Objective

Allow users to review, approve, revise, or cancel planned chat execution prior to expensive processing.

---

# Background

Planning exists but execution begins immediately.

Users must be able to review planned work before resources are consumed.

---

# Goals

✅ Persist plans

✅ Provide approval workflow

✅ Allow cancellation before execution

✅ Provide plan inspection APIs

---

# Architecture

Create or extend:

```text
Repository.API
RAGS.Infrastructure.PostgreSQL
RAGS.Application
```

---

# Persistence

Persist:

```text
Plans
PlanSteps
PlanMetadata
```

---

# API Requirements

Implement:

```text
POST /api/copilot/plan
POST /api/copilot/plans/{planId}/approve
POST /api/copilot/plans/{planId}/cancel
GET  /api/copilot/plans/{planId}
```

---

# UI Requirements

Display:

```text
Plan overview
Estimated duration
Estimated retrieval scope
Estimated model calls
Approval controls
Cancellation controls
```

---

# Validation

Add tests for:

```text
Plan persistence
Plan approval
Plan cancellation
Plan expiration
API behavior
```

---

# Implementation Notes

Added in `RAGS.Abstractions`:

- `IChatPlanRepository` with `SaveAsync`, `GetAsync`, `UpdateStatusAsync`, `GetPendingAsync`
- `IChatPlanApprovalService` with `CreatePlanAsync`, `ApproveAsync`, `CancelAsync`, `GetAsync`
- `ChatPlanRecord` persistence model including plan details, status, reviewer, timestamps, and cancellation reason
- `ChatPlanStatus` enum: `Proposed`, `Approved`, `Cancelled`, `Expired`, `Executed`

Added in `RAGS.Application`:

- `InMemoryChatPlanRepository` — thread-safe in-memory plan store with expiration-aware pending queries
- `ChatPlanApprovalService` — creates plans via the planning service, supports approve/cancel/get, enforces state transition rules, and auto-expires plans on read
- Registered `IChatPlanRepository` and `IChatPlanApprovalService` in DI via `AIServiceCollectionExtensions`

Added in `Repository.API`:

- Extended `CopilotController` with:
  - `POST /api/copilot/plan` — create a plan from a prompt
  - `POST /api/copilot/plans/{planId}/approve` — approve a proposed plan
  - `POST /api/copilot/plans/{planId}/cancel` — cancel a proposed plan
  - `GET /api/copilot/plans/{planId}` — retrieve a plan, auto-expiring it if past `ExpiresAt`

Added in `tests/RAGS.UnitTests`:

- `ChatPlanApprovalServiceTests` covering plan persistence, approval, cancellation, expiration, state guards, repository behavior, and API endpoint presence

# Exit Criteria

✓ Plans can be approved

✓ Plans can be canceled

✓ Approval state is persisted

✓ API endpoints operate correctly

✓ Build succeeds

✓ Unit tests pass