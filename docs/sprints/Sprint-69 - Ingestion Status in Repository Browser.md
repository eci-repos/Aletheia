# Sprint 69 - Ingestion Status in the Repository Browser

**Status:** Active (2026-08-13)

Full authority: this file. Sprint 68 (Query Expansion for Acronyms) is **complete, committed, and pushed** on `origin/master` (`3a77fe5`).

Promotes `docs/backlog/Ingestion-Status-in-Repository-Browser.md` — a user-reported failure. A document (CMP 2026 – 3. RFP Analysis.docx) was uploaded and listed in the Repository Browser, but its ingestion job failed, so it had no embeddings and was invisible to retrieval. The user discovered it only after a long Copilot debugging session. Upload and ingestion are separate steps; nothing in the UI surfaced the difference.

## Objective

Give the Repository Browser a per-file **ingestion status** so a user can see at a glance whether an uploaded document actually made it into retrieval (has embeddings) or silently failed. The ground-truth signal is durable: **a source with ≥1 embedding is ingested** — job status is in-memory and lost on container restart.

## Decisions (from the backlog item, settled 2026-08-13)

1. **Ground truth = embeddings, not job status.** The embeddings table is durable; jobs are in-memory. A source with ≥1 embedding is ingested.
2. **The API layer merges the signal.** `SearchController` (Repository.API) already hosts both modules; it queries `IVectorStore.GetChunkCountsAsync` for the current page's file ids and stamps each `FileMetadata` with `ChunkCount`/`Ingested`. No cross-module dependency is introduced.
3. **`IVectorStore.GetChunkCountsAsync` has a default no-op impl** (returns an empty map) so existing fakes keep compiling; `PgVectorStore` overrides with a grouped `COUNT(*)` query.
4. **UI renders a badge column** — green **Ingested** / amber **Not ingested**, with a tooltip explaining the failure mode.

## Deliverables

### 1. Chunk-count query
- `IVectorStore.GetChunkCountsAsync(IReadOnlyList<Guid> sourceIds, CancellationToken)` — default interface impl returns an empty map (fakes keep compiling).
- `PgVectorStore` override: grouped `SELECT e.source_id, COUNT(*) FROM embeddings e WHERE e.source_id = ANY(@SourceIds) GROUP BY e.source_id`.

### 2. API stamping
- `FileMetadata.ChunkCount` (int?) + computed `Ingested` (`ChunkCount is > 0`).
- `SearchController` injects `IVectorStore`; after a successful search it collects the page's distinct `Descriptor.FileId`s, calls `GetChunkCountsAsync`, and stamps each file (0 when the source is missing from the map).

### 3. UI badge
- `Browse.razor` gains an **Ingestion** column: green `text-bg-success` **Ingested** badge (tooltip: "N chunk(s) embedded — retrievable") or amber `text-bg-warning` **Not ingested** badge (tooltip: "No embeddings found — the ingestion job may have failed or not completed").

### 4. Tests + docs
- `SearchControllerTests` (3): stamps chunk counts from the vector store (42 → Ingested, 0 → Not ingested), marks missing sources as not ingested, returns BadRequest on use-case failure.
- `BrowseBindingTests` (3): Ingestion column header, Ingested/Not ingested badges, tooltip text.
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived.

## Acceptance Criteria

- A file with embeddings shows a green **Ingested** badge; a file without shows an amber **Not ingested** badge with the failure-mode tooltip.
- `SearchController` stamps the current page's files without a cross-module dependency; fakes compile against the default `GetChunkCountsAsync` impl.
- Repository + Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Per-file ingestion *error* surfacing (the Activity panel already shows job errors; the badge links the failure mode in a tooltip only).
- Re-triggering ingestion from the badge (re-upload / repair job remain the manual path).
- Showing ingestion status outside the Repository Browser (Search Center, Copilot, Dashboard).

---

## Implementation Status

**Implemented (2026-08-13).** All 4 items complete; tests green.

### Item 1 — Chunk-count query
- `src/RAGS.Abstractions/Interfaces/IVectorStore.cs`: added `GetChunkCountsAsync(IReadOnlyList<Guid> sourceIds, CancellationToken)` with a **default no-op impl** returning an empty map — existing fakes keep compiling.
- `src/RAGS.Infrastructure.PgVector/VectorStore/PgVectorStore.cs`: override with a grouped `COUNT(*)` query (`SELECT e.source_id, COUNT(*) FROM embeddings e WHERE e.source_id = ANY(@SourceIds) GROUP BY e.source_id`), mapped via a private `ChunkCountRow` record.

### Item 2 — API stamping
- `src/Repository.Abstractions/Models/FileMetadata.cs`: added `int? ChunkCount` and computed `bool Ingested => ChunkCount is > 0`.
- `src/Repository.API/Controllers/SearchController.cs`: injected `IVectorStore`; after a successful search, `StampChunkCountsAsync` collects the page's distinct `Descriptor.FileId`s, calls `GetChunkCountsAsync`, and stamps each file (0 when the source is missing from the map). No cross-module dependency — the controller already sits above both modules.

### Item 3 — UI badge
- `src/Aletheia.Web/Pages/Browse.razor`: added `<th>Ingestion</th>` column and a badge cell — green `text-bg-success` **Ingested** (tooltip "N chunk(s) embedded — retrievable") or amber `text-bg-warning` **Not ingested** (tooltip "No embeddings found — the ingestion job may have failed or not completed").

### Item 4 — Tests + docs
- **Repository 137** (+3): `SearchControllerTests` — stamps chunk counts from the vector store (42 → Ingested, 0 → Not ingested), marks missing sources as not ingested, returns BadRequest on use-case failure.
- **Web 79** (+3): `BrowseBindingTests` — Ingestion column header, Ingested/Not ingested badges, tooltip text.
- RAGS 302 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors).

**Residual manual (user-side):** hard-refresh `/browse`; the CMP 2026 – 3. RFP Analysis.docx row should now show an amber **Not ingested** badge, confirming the diagnosis that its ingestion job failed. Re-upload it (or run a repair job) to turn it green.

---

## Post-Sprint 69 refinement — "Processing" state for mid-ingestion sources (2026-08-14)

Per a project-owner review, the binary badge (green **Ingested** / amber **Not ingested**) was misleading for a source whose ingestion job is **still running**: it turned green the moment the first embedding was written, so a mid-flight file with partial chunks read as "complete". The badge is now **three-state**, and the tooltips state their scope honestly:

- **Processing** (blue `text-bg-info`): an active (queued or running) ingestion job is still producing embeddings for the source. Signal = `IIngestionJobService.HasActiveIngestion(sourceId)` (new interface member), which matches active **upload/rag/graph/lazy-graph content jobs** for the source **and** treats the **global** re-embed (`ReembedIngestion`) and RAGS-repair (`RagsRepair`) jobs as in-flight for every source (they reprocess all registered documents).
- **Ingested** (green `text-bg-success`): no active job and ≥1 embedding — ground truth stays the durable embeddings table.
- **Not ingested** (amber `text-bg-warning`): no active job and 0 embeddings.

`SearchController` stamps `FileMetadata.IsProcessing` (bool) alongside `ChunkCount`; `Browse.razor` renders the three states. Tooltips now read "…embeddings ready (status reflects embeddings only)" / "…Status reflects embeddings only." — the badge verifies the **embeddings** half of "fully processed"; graph/taxonomy/wiki-brief resource readiness is **not** yet part of the check (documented follow-up, needs a per-source graph-node count).

**Verification:** Repository 138 (+1 — `SearchControllerTests.Search_marks_processing_when_ingestion_job_active`) / Web 81 (+2 — `BrowseBindingTests` Processing badge + embeddings-only scope tooltip) / RAGS 302 (the two `IIngestionJobService` fakes updated for the new member). Build 0 errors.

### Post-Sprint 69 fix — invisible badges (Bootstrap 5.1 has no `text-bg-*` utilities) (2026-08-14)

User-reported: the Ingestion column rendered **blank** (header visible, cells empty) for every file, on every browser/cache-clearing attempt — the data path was fine. Root cause: the badges used `text-bg-success` / `text-bg-warning` / `text-bg-info`, but the vendored `wwwroot/css/bootstrap/bootstrap.min.css` is **v5.1.0** and the `text-bg-*` background/text utilities only exist in **Bootstrap 5.2+**. The unknown class applied no background, and `.badge` defaults to white text → white-on-white invisible cells. `Dashboard.razor` had the same latent bug (`text-bg-light`), masked by its `border` class.

**Fix:** `Browse.razor` badges now use the app's Bootstrap-5.1 convention — `bg-info text-dark` (Processing), `bg-success` (Ingested), `bg-warning text-dark` (Not ingested); `Dashboard.razor` badges use `bg-light text-dark border`. Binding tests updated to assert the working classes. **Gotcha for future Web UI work: never use `text-bg-*` in this app** (Bootstrap 5.1). Web 81 green; build 0 errors. Residual manual (user-side): `docker compose up -d --build`, then hard-refresh `/browse` — the badges are now visible.
