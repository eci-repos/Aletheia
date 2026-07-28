# Current Sprint

Sprint: Chat Planning Foundation

Status: Planned

## Objective

Introduce conversational planning capabilities that classify chat prompts, estimate execution cost, and determine whether user approval is required before execution.

This sprint establishes planning abstractions but does not introduce background execution.

---

# Background

Current chat execution begins immediately.

The system lacks the ability to:

- Detect expensive requests
- Estimate execution effort
- Explain intended execution steps
- Distinguish simple requests from broad corpus-level requests

This sprint introduces planning as a first-class capability.

---

# Goals

✅ Create planning abstractions

✅ Classify prompt complexity

✅ Generate execution plans

✅ Estimate execution cost

✅ Determine approval requirements

✅ Preserve existing chat execution behavior

---

# Architecture

Create or extend:

```text
RAGS.Abstractions
RAGS.Application
```

---

# Core Abstractions

## IChatPlanningService

Capabilities:

```csharp
AnalyzePromptAsync()
CreatePlanAsync()
EstimatePlanAsync()
RequiresApproval()
```

---

## ChatExecutionPlan

Represent proposed execution work.

Include:

```text
PlanId
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

---

# Planning Rules

Detect broad requests including:

```text
last N years
all documents
all RFPs
timeline
matrix
compare
identify every
summarize corpus
```

Simple prompts should remain fast-path candidates.

---

# Validation

Add tests for:

```text
Prompt classification
Approval threshold decisions
Plan creation
Estimate generation
Fast-path detection
```

---

# Exit Criteria

✓ Plans can be generated

✓ Approval determination works

✓ Broad requests are detected

✓ Fast-path requests remain unchanged

✓ Build succeeds

✓ Unit tests pass