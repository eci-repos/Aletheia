# Sprint 62 - GraphRAG Soft Deadline and Reembed Parity

**Status:** Active (2026-08-11)

Full authority: this file. Sprint 61 (Chat Approval Prompt and Admin Settings) is **complete, committed, and pushed** (`4d10561` / `793fc52` / `f8f5292` on `origin/master`). Its residual manual verification (hard-refresh `/copilot` + `/settings`) is user-side and optional.

Promotes backlog items 7 and 8 from `docs/backlog/archive/GraphRAG-LazyGraphRAG-Enhancements.md`, both surfaced by the Sprint 60 Docker smoke test (2026-08-10).

## Objective

Two small GraphRAG / ingestion follow-ups, delivered in one pass because they both come out of the Sprint 60 smoke test:

1. **Reembed indexer parity** — `POST /api/jobs/rags/reembed` currently runs the **full** `UploadedContentKnowledgeIndexer.IndexAsync` (~100+ serial cloud LLM calls per doc: entity discovery + node summaries + relationship extraction + community detection + community summaries), while file uploads use `IndexLightweightAsync` (no LLM: deterministic topic extraction, taxonomy tags, ontology source+topic entities, graph source/chunk seed nodes). A 3-doc corpus reembed took 40+ minutes against a cloud model. Make reembed honor the lightweight path so re-embedding after a provider/dimension change is fast.
2. **GraphRAG soft deadline / best-partial result** — under LLM saturation (Ollama serializes cloud-model calls), concurrent GraphRAG retrievals hit the 30s `CancelAfter(MaxExecutionTime)` before the final semantic fallback and return HTTP 400 `Vector search failed. The operation was canceled.` Surface the deadline as a **soft** signal: degrade to plain semantic retrieval and return the best partial result with a visible timeout notice instead of hard-failing the whole request.

## Background

- **Reembed** (`IngestionJobService.RunReembedJobAsync` → `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` → `_knowledgeIndexer.IndexAsync`) runs the full graph-intelligence pipeline even though its purpose is to regenerate **embeddings** after a provider/dimension change — not to re-derive the graph intelligence, which is only produced lazily during retrieval anyway. Uploads (`IndexLightweightAsync`) seed the graph and defer intelligence to query time (`lazyEnrichmentStatus = "Pending"`). Repair keeps the full path (it is a deeper recovery operation).
- **Hard deadline** (Sprint 60, backlog item 4): `GraphRagService.RetrieveAsync` builds `new GraphTraversalBudget()` (default `MaxExecutionTime` 30s), wires `timeoutCts.CancelAfter(budget.MaxExecutionTime)`, and flows `ct` through every LLM/traversal call. The generic `catch (Exception ex)` maps *any* failure — including a deadline cancellation — to `Result.Failure` → controller HTTP 400. The deadline is documented behavior but it fails the request outright instead of degrading to best-available.

## Deliverables

### 1. Reembed indexer parity (`KnowledgeIndexMode`)
- New `KnowledgeIndexMode` enum (`Full` / `Lightweight`) in `Aletheia.RAGS.Abstractions.Models` (next to the other mode enums).
- `IKnowledgeSourceIngestionService.EnsureIngestedAsync` gains an optional `KnowledgeIndexMode mode = KnowledgeIndexMode.Full` parameter (default keeps every existing caller on the full path).
- `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` branches on the mode: `Lightweight` → `_knowledgeIndexer.IndexLightweightAsync(source.SourceId, source.SourceName, text, null, ct)`; `Full` (default) → `IndexAsync` as today. All other steps (template gate, download, extract, cleanup, RAGS ingestion, brief enqueue) are unchanged.
- `RunReembedJobAsync` passes `KnowledgeIndexMode.Lightweight`; `RunRepairJobAsync` and the chat-hydration callers keep `Full` (default).

### 2. GraphRAG soft deadline / best-partial result
- In `GraphRagService.RetrieveAsync`, the catch block distinguishes **deadline-fires** (`timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested`) from **caller-cancel**.
  - **Deadline-fires:** run a best-effort plain semantic retrieval (`_ragsService.RetrieveAsync`) under a short secondary deadline (`~10s`); return `Result.Success` with a trace strategy `semantic-timeout-fallback` and steps `deadline-exceeded` / `semantic-fallback` so operators and the Web UI see the degraded path. If even the semantic fallback fails, return the failure as today.
  - **Caller-cancel:** return `Result.Failure` with a "cancelled" message (no fallback — the caller is leaving).
  - **Other exceptions:** unchanged generic failure.
- New optional constructor param `Func<IGraphTraversalBudget>? budgetFactory = null` (defaults to `() => new GraphTraversalBudget()`) so tests can inject a short-deadline budget.
- The observability surface stays the retrieval trace (`RetrievalTrace` on `SearchResult.Trace`) — no new logging dependency in `RAGS.Application`.

### 3. Tests
- Repository.UnitTests: `EnsureIngestedAsync` with `KnowledgeIndexMode.Lightweight` calls `IndexLightweightAsync` once and does **not** call `IndexAsync`; the existing full-path tests still verify `IndexAsync` behavior.
- RAGS.UnitTests: deadline-fires → success with `semantic-timeout-fallback` trace strategy + steps (`deadline-exceeded`, `semantic-fallback`); caller-cancel → failure. Existing suites remain green.

### 4. Docs
- `docs/Architecture.md`, `docs/OperationsGuide.md` (reembed description), `docs/Development-Guidelines.md`, AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, sprint handoff updated. Backlog items 7 + 8 statuses updated.

## Acceptance Criteria

- Reembed runs the lightweight indexer (no LLM graph-intelligence calls); re-embedding a 3-doc corpus completes in minutes, not 40+.
- A GraphRAG retrieval that blows the 30s execution deadline returns HTTP 200 with a semantic result carrying a trace strategy `semantic-timeout-fallback` and steps including `deadline-exceeded` — not HTTP 400.
- A caller-cancelled retrieval still returns failure (no fallback), and non-deadline exceptions still fail as before.
- Repository / RAGS / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Persisting the LazyGraphRAG corpus index to PostgreSQL (backlog item 2); batch GraphRAG ingest / `UNWIND` writes (backlog item 3); theme-aware graph retrieval (Canonical backlog item 5).
- Parallelizing or batching the graph-intelligence LLM calls (backlog item 3 territory — the lightweight path sidesteps it for reembed).
- Changing repair or chat-hydration indexing behavior (they keep `Full`).

---

## Implementation Status (2026-08-11)

**Implementation complete.** Both items implemented, tested, and documented. Committed/pushed (`26995d9`); a Docker smoke test run 2026-08-11 surfaced and fixed a follow-up defect in the soft-deadline path (see "Docker Smoke Test" below, committed `88164e4`).

### 1. Reembed indexer parity (`KnowledgeIndexMode`) — DONE
- New `KnowledgeIndexMode` enum (`Full` / `Lightweight`) in `Aletheia.RAGS.Abstractions.Models` (`src/RAGS.Abstractions/Models/KnowledgeIndexMode.cs`).
- `IKnowledgeSourceIngestionService.EnsureIngestedAsync` and `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` gained `KnowledgeIndexMode mode = KnowledgeIndexMode.Full`. Lightweight branches to `_knowledgeIndexer.IndexLightweightAsync(source.SourceId, source.SourceName, text, null, ct)` (no LLM graph intelligence); all other steps (template gate, download, extract, cleanup, RAGS ingestion, brief enqueue) unchanged.
- `IngestionJobService.RunReembedJobAsync` passes `KnowledgeIndexMode.Lightweight`; repair (`RunRepairJobAsync`), plugin, and chat-hydration callers keep `Full` (default).

### 2. GraphRAG soft deadline / best-partial result — DONE
- `GraphRagService.RetrieveAsync` distinguishes **deadline-fires** (`timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested`): runs a best-effort plain semantic retrieval under a fresh `CancelAfter(FallbackExecutionTime)` (~10s) secondary deadline and returns `Result.Success` with trace strategy `semantic-timeout-fallback` and steps `deadline-exceeded`/`semantic-fallback`; if even the fallback fails, returns the failure with a timeout notice. **Caller-cancel** returns `Result.Failure` with "The operation was cancelled." (no fallback); other exceptions keep the generic failure.
- **Follow-up fix (smoke test, `88164e4`)**: the degrade must cover the **returned-Failure** path too. `PgVectorStore.SearchAsync` converts a cancelled vector search into a returned `Failure("Vector search failed. The operation was canceled.")` (not a thrown `OperationCanceledException`), so a deadline firing during the semantic base retrieval bypassed the catch block and still hard-failed with HTTP 400. The fallback was factored into a shared `RunSemanticTimeoutFallbackAsync` helper invoked from **both** the thrown-exception catch path and the returned-Failure base-retrieval path (`baseResults.IsFailure` + deadline condition).
- New optional ctor param `Func<IGraphTraversalBudget>? budgetFactory = null` (default `() => new GraphTraversalBudget()`); `RetrieveAsync` builds the budget from it. No new logging dependency — the trace is the observability surface.

### 3. Tests — DONE
- Repository.UnitTests **130** (was 129): new `EnsureIngestedAsync_lightweight_mode_uses_lightweight_indexer_not_full` — verifies `IndexLightweightAsync` called once with the source and `IndexAsync` never.
- RAGS.UnitTests **272** (was 270): new `RetrieveAsync_deadline_fires_degrades_to_semantic_timeout_fallback` (50ms-deadline budget + `SlowSelectEntitiesReasoningService` that blocks on `Task.Delay(1000, ct)` → asserts Success, trace strategy `semantic-timeout-fallback`, steps `deadline-exceeded` + `semantic-fallback`) and `RetrieveAsync_caller_cancellation_returns_failure_not_fallback` (pre-cancelled token → Failure with "cancelled"). Three `IKnowledgeSourceIngestionService` test fakes updated for the new `mode` param.
- Foundation 55 / Aletheia.Web.UnitTests 46 unchanged; `dotnet build Aletheia.slnx` succeeds (pre-existing AngleSharp NU1902 warning only).

### 4. Docs — DONE
- `docs/Architecture.md` (per-request budget note + `KnowledgeIndexMode` indexer-mode paragraph), `docs/OperationsGuide.md` (reembed now lightweight + soft-deadline monitoring note), `docs/Development-Guidelines.md` (budget factory + degrade-don't-fail contract), AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, this sprint file updated. Backlog items 7 + 8 marked promoted.

## Remaining
- **Commit** when the user requests.
- Optional Docker smoke test: rebuild the api container, run `POST /api/jobs/rags/reembed` and confirm it completes quickly (minutes, not 40+), and run a concurrent GraphRAG retrieval to see trace strategy `semantic-timeout-fallback` instead of HTTP 400 under LLM saturation.

## Docker Smoke Test — RUN 2026-08-11 (complete)
- **Part 1 — reembed speed: VERIFIED.** `POST /api/jobs/rags/reembed` on the 3-doc corpus completed in **~70 seconds** (18:53:54 → 18:55:04 UTC, lightweight path) vs the 40+ minutes the full indexer took in the Sprint 60 smoke test. Job reported `Succeeded` — "Re-embedding completed for 3 registered document(s)." — with 138 embeddings re-created (53/46/39 per source; semantic search returns results immediately after). Embeddings survive an API container restart (verified 138 → restart → 138).
- **Part 2 — soft deadline under LLM saturation: VERIFIED (with a fix).** 16 concurrent GraphRAG retrievals against fresh queries saturated the Ollama LLM. **All 16 returned HTTP 200 with zero HTTP 400s; 6 of them hit the 30s execution deadline and degraded to trace strategy `semantic-timeout-fallback` with real corpus results (3 each).** Pre-fix, the same conditions produced HTTP 400 `Vector search failed. The operation was canceled.` on 3 of 8 requests — the deadline fired during the semantic base retrieval and the returned `Failure` bypassed the catch block (the degrade only covered thrown `OperationCanceledException`). Fix committed `88164e4` (see item 2 above); new unit test `RetrieveAsync_deadline_fires_with_returned_failure_degrades_to_semantic_timeout_fallback` (RAGS.UnitTests now **290**, all green).
