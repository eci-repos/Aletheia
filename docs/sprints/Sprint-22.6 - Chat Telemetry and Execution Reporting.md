# Current Sprint

Sprint: Chat Telemetry and Execution Reporting

Status: In Progress

## Objective

Provide execution telemetry and plan-versus-actual reporting after job completion.

---

# Goals

✅ Surface execution metrics

✅ Compare estimates with actual execution

✅ Expose confidence indicators

✅ Support provider telemetry

---

# Telemetry

Capture:

```text
Elapsed Seconds
Token Counts
Tokens Per Second
Retrieval Count
Citation Count
LLM Call Count
Estimate Versus Actual
Alignment Confidence
Confidence Basis
```

---

# Reporting Rules

Prefer:

```text
Provider-reported metrics
```

Fallback:

```text
Heuristic calculations
```

---

# Validation

Add tests for:

```text
Telemetry creation
Telemetry persistence
Telemetry reporting
Estimate comparison
```

---

# Implementation Notes

Added telemetry domain models:

- `ChatExecutionTelemetry` in `RAGS.Abstractions.Models` captures elapsed seconds, token counts, tokens per second, retrieval count, citation count, LLM call count, estimate-vs-actual alignment confidence, confidence basis, and a comparison summary.
- `ChatEstimateComparison` exposes the same values in a dedicated report shape.

Updated persistence:

- `ChatProgressRecord` now has an optional `Telemetry` property.
- `IChatProgressStore` gained `SetTelemetryAsync`.
- `InMemoryChatProgressStore` implemented `SetTelemetryAsync`.

Added `ChatTelemetryService` in `RAGS.Application.Planning`:

- `BuildTelemetry` prefers provider-reported metrics from `ChatCompletionStats` and falls back to zero/heuristic values when unavailable.
- `CompareEstimate` returns a `ChatEstimateComparison` with a human-readable summary comparing duration, tokens, retrieval, and model calls against plan estimates.

Wired telemetry into execution:

- `ChatExecutionEngine` now depends on `IChatTelemetryService`, tracks total job elapsed time with a `Stopwatch`, and records telemetry before finalizing a successful job.
- Final results now include a formatted telemetry section with the comparison summary and token/retrieval/citation/call/confidence details.
- DI registration added for `IChatTelemetryService`.

API exposure:

- `CopilotController` now exposes `GET /api/copilot/jobs/chat/{jobId}/telemetry` returning `ChatExecutionTelemetry`.

Blazor UX:

- `RepositoryApiClient` has `GetChatJobTelemetryAsync`.
- `ProgressPanel.razor` renders a telemetry card when telemetry is provided, showing duration, tokens, retrieval/citations, model calls, alignment confidence, confidence basis, and estimate comparison summary.
- `Index.razor` polls telemetry from either the progress record or the telemetry endpoint on completion, and populates `ChatMessage.Stats` so the assistant message’s stats bar shows actual values.

Tests:

- `ChatTelemetryServiceTests` cover telemetry creation with provider metrics, heuristic fallback, and estimate comparison including zero-estimate edge cases.
- `ChatExecutionEngineTests` extended to verify telemetry is persisted and final results include the telemetry summary.
- `ProgressPanelTests` extended to verify the telemetry card renders when provided.

# Exit Criteria

✓ Final responses include telemetry

✓ Provider metrics are utilized

✓ Estimate comparison works

✓ Build succeeds

✓ Unit tests pass