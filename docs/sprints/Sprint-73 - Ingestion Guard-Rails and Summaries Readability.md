# Sprint 73 - Ingestion Guard-Rails and Summaries Readability

**Status:** Active (2026-08-15)

Full authority: this file. Sprint 72 (Search UX Clarity — Semantic vs Summaries) is **complete, committed, and pushed** on `origin/master` (`0950dad`).

Promotes `docs/backlog/Ingestion-Guard-Rails-Durable-Jobs-and-Self-Healing.md` — the project-owner-approved **core fix** for the 2026-08-14 operational incident (the Repository Browser flipped all three documents from **Ingested** to **Not ingested** after an API rebuild). The durable job queue (backlog item 1) is **explicitly deferred**; this sprint ships items 2 (write-new-then-swap) + 3 (startup reconciliation) plus the project-owner-directed Summaries readability work.

## Objective

Two problems, one sprint:

1. **Ingestion becomes resilient.** An interrupted re-ingestion must never leave a source with zero embeddings, and anything already zeroed self-heals at startup. The user's framing: "ingestion once done is done and the status should not change just because the app was refreshed." Root cause: `RagsService.IngestAsync` **deletes** existing embeddings *before* chunking/embedding/writing the new ones, and the job queue is in-memory — an API rebuild mid-job leaves the source with zero embeddings and nothing ever re-checks it.
2. **Summaries results read like a product, not a debug dump.** The new Summaries mode returns the right summaries, but (a) the cards are unreadable — raw content with internal scaffolding (`Community Summary: {name}` prefix, a `Structured GraphRAG Context` dump, a `Chunk 0 from source {guid}` footer) — and (b) **"View in document" does nothing**: community summaries carry a *synthetic* `SourceId` (`StableGuid("community-source", …)`) so the document page can't load it, and even for entity summaries the `chunk=` leading phrase is synthesized text that can't be found in the document, so the highlight silently fails.

## Decisions (from the backlog item, settled 2026-08-15)

1. **Write-new-then-swap ingestion.** `RagsService.IngestAsync` chunks + embeds first (builds the full item set), then atomically replaces the source's embeddings in a single transaction. An interrupted ingestion leaves the **old** embeddings intact — never zero. `IVectorStore.ReplaceSourceAsync` gets a default delete-then-store implementation so fakes keep compiling; `PgVectorStore` overrides it with delete + insert in one transaction.
2. **`last_ingested_at` marker on `file_metadata`.** Distinguishes "never successfully ingested" (NULL → reconciliation candidate) from "checked and non-ingestable" (set → leave alone). Stamped on **completion** (success or no-text), never on failure — a failed ingest stays NULL so the sweep retries it.
3. **Startup reconciliation sweep.** A `BackgroundService` finds documents with zero embeddings AND `last_ingested_at IS NULL` and enqueues a targeted RAGS-repair job for exactly those sources. Runs once after a short startup delay. This auto-repairs the currently-broken documents on the next API restart — no manual SQL.
4. **Summaries are display-formatted, backend untouched.** A Web-side `SummaryResultFormatter` extracts the readable body (strips the prefix line + the structured-context dump) and decides card affordances. "Internally they can stay as they are." LazyGraphRAG fallback results (`lazy-*`) are real passages and keep the standard semantic card treatment.
5. **"View in document" is hidden on summary cards.** A synthesized summary has no single verbatim passage in a document; the current link is dead. The **Sources** list (from `result.Citations`) carries the provenance instead.

## Deliverables

### Workstream A — Ingestion guard-rails (core fix)

#### A1. Write-new-then-swap in the vector store
- `IVectorStore.ReplaceSourceAsync(Guid sourceId, IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken)` — **default implementation** (delete-then-store) so fakes keep compiling (same pattern as `GetChunkCountsAsync`).
- `PgVectorStore` override — delete + insert in **one transaction** (reuse the `StoreBatchAsync` insert path; the old `DeleteBySourceAsync` ran on a separate connection with no transaction). An interruption leaves either the old or the new embeddings, never zero.

#### A2. Reorder `RagsService.IngestAsync`
- Chunk + embed first (build `items`), then call `ReplaceSourceAsync` instead of the current `DeleteBySourceAsync` → `StoreBatchAsync` sequence.

#### A3. `last_ingested_at` marker on `file_metadata`
- `scripts/init.sql` + idempotent migration `2026-08-15-last-ingested-at.sql` (`ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS last_ingested_at TIMESTAMPTZ NULL`).
- `FileMetadata.LastIngestedAt` (`DateTimeOffset?`); `IMetadataRepository.SetLastIngestedAtAsync` (mirror `SetTemplateAsync`); `PostgreSqlMetadataRepository` UPDATE.

#### A4. Stamp the marker on completion
- `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` sets `last_ingested_at = now` on **completion** (success or no-text), **not** on failure.

#### A5. Startup reconciliation sweep
- `IMetadataRepository.GetSourcesMissingIngestionAsync` — `file_id`s where `last_ingested_at IS NULL AND NOT EXISTS (SELECT 1 FROM embeddings e WHERE e.source_id = file_metadata.file_id)` (embeddings is in the same PostgreSQL DB).
- `IngestionJobService.EnqueueRagsRepairForSources(IReadOnlyList<Guid>)` + a new `IngestionJobWorkItem` kind (`RagsRepairSources`) + a runner that iterates the fixed source list calling `EnsureIngestedAsync` (reuse the per-source loop from `RunRagsRepairJobAsync`, minus the query scan).
- `IngestionReconciliationService` — **new** `BackgroundService`: after a short startup delay, call `GetSourcesMissingIngestionAsync`; if any, `EnqueueRagsRepairForSources(...)` and log what it enqueued. Runs once. Registered via `AddHostedService` in `Program.cs`.

#### A6. Fix the current broken state
- Restart the API (the sweep auto-repairs the 3 zero-embedding documents) — or run `POST /api/jobs/rags/repair` once. No manual SQL.

#### A7. Tests
- **RAGS**: `RagsServiceTests` — `IngestAsync` calls `ReplaceSourceAsync` (not delete-then-store); `PgVectorStoreTests` — `ReplaceSourceAsync` deletes + inserts atomically.
- **Repository**: new work-item routing (`RagsRepairForSources_runs_ingestion_for_each_targeted_source`); reconciliation sweep enqueues a targeted repair for the returned source ids; `EnsureIngestedAsync` stamps `last_ingested_at` on success but not on failure.

### Workstream B — Summaries readability + dead "View in document"

#### B1. Display helper (testable, keeps backend untouched)
- `SummaryResultFormatter` (`src/Aletheia.Web/Services/SummaryResultFormatter.cs`) — static helper:
  - `IsSummary(SearchResult)` → `RetrievalStrategy` starts with `"summary-"` (GraphRAG synthesized summaries; LazyGraphRAG fallback results are real `lazy-*` passages and keep the current treatment).
  - `Body(string content)` → strips the `Entity Summary: {label}` / `Community Summary: {name}` prefix line and the trailing `Structured GraphRAG Context` dump, trims.
  - `ShowViewInDocument(SearchResult)` → `false` for summaries (no single verbatim passage; the current link is dead).

#### B2. Search Center card
- `SearchCenter.razor` — for summary results:
  - Render a **"Summaries" badge** (`badge bg-info text-dark` — Bootstrap 5.1 convention, never `text-bg-*`).
  - Render `Body(content)` through **`MarkdownRenderer.ToHtml`** (shared renderer) instead of raw `<p class="card-text">`.
  - Show **Sources** from `result.Citations` (document names) — the summary draws from these.
  - **Hide the "View in document" button** (replaced by the Sources list).
  - Drop the internal `Chunk @result.Chunk.Index from source @result.Chunk.SourceId` footer on summary cards.
  - Semantic / LazyGraphRAG-fallback cards are untouched.
- `SearchCenter.razor.css` — `.summary-body` heading/list styles so a synthesized answer reads like a product, not a document dump.

#### B3. Tests
- `SummaryResultFormatterTests` (`tests/Aletheia.Web.UnitTests`) — `IsSummary` true for `summary-*` / false for `lazy-*` and `semantic`; `Body` strips prefix + context dump; `ShowViewInDocument` false for summaries.

## Acceptance Criteria

- An interrupted re-ingestion leaves the previous embeddings intact (write-new-then-swap); a source is never left with zero embeddings by a partial ingest.
- On API startup, documents with zero embeddings and no `last_ingested_at` are auto-queued for repair and re-ingested — the "Not ingested" flip becomes self-correcting.
- A completed ingestion stamps `last_ingested_at`; a failed one stays NULL so the sweep retries it.
- Summaries search results render a "Summaries" badge, a readable markdown body, a Sources list, and **no** dead "View in document" button; Semantic results keep the working "View in document" link.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- **Durable job queue (backlog item 1)** — explicitly deferred. The in-memory queue stays; the write-new-then-swap + reconciliation sweep make the *data* survive an interruption even though the *job* does not.
- Job stage tracking + resume (backlog item 4).
- Changing the ingestion pipeline's fidelity guarantees (Sprint 70) — the guard-rails make ingestion *resilient*, not *different*.
- Changing how summaries are produced (GraphRAG ingest-time vs LazyGraphRAG query-time behavior is untouched).
- Making the job queue distributed or multi-host.

---

## Implementation Status

**Implemented (2026-08-15).** All items complete; tests green.

### Workstream A — ingestion guard-rails

- **A1 (write-new-then-swap):** `IVectorStore.ReplaceSourceAsync` with a default delete-then-store implementation; `PgVectorStore` overrides it with a single transaction (DELETE by source, then the batch INSERT with `ON CONFLICT` upsert; commit, rollback on error; empty batch → `DeleteBySourceAsync`).
- **A2 (reorder):** `RagsService.IngestAsync` chunks + embeds first, then calls `ReplaceSourceAsync` — the old `DeleteBySourceAsync` → `StoreBatchAsync` sequence is gone.
- **A3 (marker):** `last_ingested_at TIMESTAMPTZ NULL` on `file_metadata` (`init.sql` + idempotent migration `2026-08-15-last-ingested-at.sql`); `FileMetadata.LastIngestedAt`; `IMetadataRepository.SetLastIngestedAtAsync` + `PostgreSqlMetadataRepository` UPDATE.
- **A4 (stamp):** `EnsureIngestedAsync` stamps `last_ingested_at = now` on completion (success or no-text), never on failure.
- **A5 (sweep):** `GetSourcesMissingIngestionAsync` (zero embeddings AND `last_ingested_at IS NULL`); `IngestionJobService.EnqueueRagsRepairForSources` + `RagsRepairSources` work-item kind + fixed-list runner; `IngestionReconciliationService` `BackgroundService` (10s startup delay, runs once) registered in `Program.cs`.
- **A6 (fix current state):** restart the API — the sweep auto-repairs the 3 zero-embedding documents; or run `POST /api/jobs/rags/repair` once.
- **A7 (tests):** RAGS — `IngestAsync_replaces_source_embeddings_atomically` + `IngestAsync_replaces_existing_embeddings_on_reingest` (`RagsServiceTests`), `ReplaceSourceAsync_replaces_embeddings_atomically` (`PgVectorStoreTests`). Repository — `RagsRepairForSources_runs_ingestion_for_each_targeted_source` (`IngestionJobServiceRoutingTests`), `IngestionReconciliationServiceTests` (enqueues targeted repair when sources missing; no-op when nothing missing), `EnsureIngestedAsync_stamps_last_ingested_at_on_success` + `EnsureIngestedAsync_does_not_stamp_last_ingested_at_on_failure`.

### Workstream B — summaries readability

- **B1 (formatter):** `SummaryResultFormatter` — `IsSummary` (`summary-*` prefix), `Body` (strips prefix line + `Structured GraphRAG Context` dump), `ShowViewInDocument` (false for summaries).
- **B2 (card):** `SearchCenter.razor` — "Summaries" badge (`badge bg-info text-dark`), markdown-rendered body via `MarkdownRenderer.ToHtml`, **Sources** list from `result.Citations`, "View in document" hidden on summary cards, `Chunk N from source <guid>` footer dropped on summary cards. Semantic / LazyGraphRAG-fallback cards untouched. `.summary-body` scoped CSS in `SearchCenter.razor.css`.
- **B3 (tests):** `SummaryResultFormatterTests` — `IsSummary` true for `summary-entity`/`summary-community` (case-insensitive), false for `lazy-*`/`semantic`/`semantic-timeout-fallback`/empty/null; `Body` strips entity + community prefixes and the context dump, trims plain content, empty for blank/null; `ShowViewInDocument` false for summaries, true for semantic passages.

**Test counts:** RAGS / Repository / Web — see the sprint commit for the exact deltas; all suites green, `dotnet build Aletheia.slnx` succeeds (0 errors).

**Residual manual (user-side):** `docker compose up -d --build` (fresh DB gets `last_ingested_at` from init.sql; an existing deployment needs the migration `2026-08-15-last-ingested-at.sql` applied once, or the API's schema initializer self-heals at startup). Then **restart the API** — the reconciliation sweep logs the 3 sources and auto-repairs them; hard-refresh `/browse` → rows show **Ingested** (green), not "Not ingested". Hard-refresh `/search` → Summaries results show a "Summaries" badge, a readable markdown body, a Sources list, and no dead "View in document" button; Semantic results keep the working "View in document" link.
