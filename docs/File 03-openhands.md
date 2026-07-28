File: openhands.md
# OpenHands Instructions

You are developing Aletheia.

Follow all documents in the following order:

1. 00-Aletheia-Charter.md
2. 01-Aletheia-WorkPlan.md
3. 02-Current-Sprint.md

The Charter is authoritative.

If conflicts exist:

Current Sprint overrides Work Plan.

Work Plan overrides assumptions.

Charter overrides everything.

---

# Execution Rules

Always:

- Build incrementally
- Commit small working units
- Keep the solution compiling
- Write tests
- Update documentation

Never:

- Skip phases
- Build future phases early
- Implement speculative features
- Bypass abstractions
- Introduce infrastructure dependencies into Domain projects

---

# Required Architecture

Use:

- Clean Architecture
- Hexagonal Architecture
- DDD
- SOLID
- Dependency Injection

All dependencies must resolve through interfaces.

---

# Provider Rules

Implement only the currently approved provider.

Future providers should be represented by abstractions and TODO backlog items.

Do not create production implementations for future providers unless explicitly instructed.

---

# Documentation Rules

For every completed feature update:

- README
- Architecture diagrams
- API documentation (when applicable)

---

# Testing Rules

Every feature requires:

- Unit tests

Every API requires:

- Integration tests

Do not close work items with failing tests.

---

# Build Rules

The solution must always:

- Build successfully
- Pass tests
- Run locally

---

# Completion Rules

A work item is complete only when:

- Code complete
- Tests passing
- Documentation updated
- Acceptance criteria satisfied

Working software always takes priority over speculative extensibility.

If uncertain, choose the simplest architecture that satisfies current requirements while preserving abstraction boundaries.
This package should be the initial handoff to OpenHands and will provide significantly better results than a single monolithic architecture prompt.

---

# Phase 21 Takeover Notes

Current active phase:

- Phase 21 - RAGS v2 Intelligence and Background Operations

Before making changes, read:

1. `docs/File 00-Aletheia-Charter.md`
2. `docs/File 01-Aletheia-WorkPlan.md`
3. `docs/File 02-Current-Sprint.md`
4. `docs/Phase21-Background-Operations-Handoff.md`

The first background-ingestion slice is implemented and validated. The lazy-enrichment slice is also implemented: uploads seed graph chunks without full document-wide LLM summarization, GraphRAG retrieval lazily enriches relevant chunks, and Copilot responses expose completion stats. WRAGS durability and maturity are implemented too: generated/edited wiki pages persist in PostgreSQL, `/wiki` can search/edit/show history/queue regeneration, pages have `Generated`/`Reviewed`/`Approved`/`NeedsReview`/`Stale` lifecycle controls, stale warnings, source-change stale detection, related topics, related-page lookup, and WRAGS participates in Search Center/Copilot retrieval context. Continue from the known maturity work in the handoff file rather than rebuilding these paths from scratch.

Important constraints:

- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints compatible unless the sprint explicitly changes them.
- Preserve the `/api/jobs` snapshot contract used by the Web Activity panel.
- Do not introduce a new queue provider or database unless it is part of the current Phase 21 maturity work.
- Keep job progress concise: stage transitions plus coarse heartbeats are preferred over noisy per-token logs.
- Preserve the searchable-first upload path unless the sprint explicitly reopens full index-time enrichment.
- Treat Copilot `AlignmentConfidence` as a retrieval heuristic, not a calibrated correctness score.
- Preserve the current WRAGS API surface: `/api/wiki/search`, `/api/wiki/recent`, `/api/wiki/retrieve`, `/api/wiki/pages/{id}`, `/api/wiki/pages/{id}/history`, `/api/wiki/pages/{id}/status`, `/api/wiki/pages/{id}/related`, `/api/wiki/regenerate`, and `/api/wiki/regenerate/job`.

Recommended takeover target:

- Durable PostgreSQL-backed job state, followed by cancellation/retry controls, integration tests, provider-backed token usage telemetry, graph-derived WRAGS backlinks, editorial diff visualization, and quality scoring for wiki-as-context retrieval.
- Proposed next sprint: `docs/sprints/Sprint-22 - Conversational Chat Planning and Progress Feedback.md`, which scopes plan-before-run Copilot chat, durable background chat execution, user approval, progress checklists, heartbeats, cancellation, and final telemetry for long-running questions.
