# Current Sprint

Sprint: Copilot Progress User Experience

Status: In Progress

## Objective

Surface planning, approval, execution, and progress directly within the Copilot chat experience.

---

# Goals

✅ Plan preview

✅ Approval controls

✅ Cancellation controls

✅ Progress visualization

✅ Recovery after refresh

---

# Architecture

Update:

```text
Aletheia.Web/Pages/Copilot/Index.razor
```

---

# UI Requirements

Support:

```text
Plan preview
Run button
Revise button
Cancel button
Background status
Step checklist
Elapsed time
Heartbeat indicator
Percent complete
Final answer insertion
Failure summaries
```

---

# Recovery Requirements

Users must be able to:

```text
Refresh the browser
Close the tab
Return later
Continue monitoring
```

---

# Validation

Add tests for:

```text
Plan rendering
Approval actions
Cancellation actions
Progress rendering
Resume after refresh
```

---

# Implementation Notes

Added in `Aletheia.Web`:

- Extended `RepositoryApiClient` with planning/execution/progress client methods:
  - `PlanChatAsync`, `ApproveChatPlanAsync`, `CancelChatPlanAsync`, `ExecuteChatPlanAsync`
  - `GetChatJobAsync`, `CancelChatJobAsync`, `GetChatPlanProgressAsync`
- Added `PlanPreview.razor` component showing plan mode, estimated duration/model calls/retrieval, expiry, step list, and Run/Revise/Cancel controls
- Added `ProgressPanel.razor` component showing status badge, percent-complete progress bar, elapsed time, last heartbeat, partial result, step checklist, final result/failure messages, and Cancel execution button
- Updated `Pages/Copilot/Index.razor`:
  - Sends user prompt through `/api/copilot/plan` to preview work before execution
  - Shows `PlanPreview` for plans requiring approval
  - Auto-approves and executes fast-path plans
  - Polls `GET /api/copilot/plans/{planId}/progress` every 2 seconds during background execution
  - Inserts final answer into the conversation on success
  - Restores active execution on page load by querying recent jobs and progress so users can refresh, close the tab, and return later

Added tests in `tests/Aletheia.Web.UnitTests`:

- New Razor unit test project using bUnit
- `PlanPreviewTests` covering plan details/steps, null plan, fast-path badge/controls
- `ProgressPanelTests` covering progress rendering, success completion, failure display, null progress

# Exit Criteria

✓ Entire planning workflow visible

✓ Entire progress workflow visible

✓ Browser refresh recovery works

✓ Build succeeds

✓ Unit tests pass