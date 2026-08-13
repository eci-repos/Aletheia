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
