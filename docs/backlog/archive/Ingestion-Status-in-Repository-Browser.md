# Backlog: Ingestion Status in the Repository Browser

**Status:** **Implemented (Sprint 69, 2026-08-13) — archived.** All 4 items delivered; see `docs/sprints/Sprint-69 - Ingestion Status in Repository Browser.md` "Implementation Status". This file is the design record; the sprint file is the implementation authority.
**Created:** 2026-08-13
**Source:** User-reported failure. A document (CMP 2026 – 3. RFP Analysis.docx) was uploaded and listed in the Repository Browser but its ingestion job failed, so it had no embeddings and was invisible to retrieval — the user discovered it only after a long Copilot debugging session. Upload and ingestion are separate steps; nothing in the UI surfaces the difference.

## Problem

The Repository Browser lists uploaded files, but **upload ≠ ingested**. Ingestion (extract → chunk → embed) is a background job that can fail, leaving a file in Browse with no embeddings — and therefore never retrievable. There is no per-file signal in the UI, so a failed ingestion is invisible until a search/Copilot question silently misses the document.

## Decisions made (2026-08-13)

1. **The ground-truth signal is "does this source have embeddings?", not job status.** Jobs are in-memory and lost on container restart; the embeddings table is durable. A source with ≥1 embedding is ingested.
2. **The API layer merges the signal** — `SearchController` (Repository.API) already hosts both modules; it queries `IVectorStore.GetChunkCountsAsync` for the current page's file ids and stamps each `FileMetadata` with `ChunkCount`/`Ingested`. No cross-module dependency is introduced.
3. **`IVectorStore.GetChunkCountsAsync` has a default no-op impl** (returns an empty map) so existing fakes keep compiling; `PgVectorStore` overrides with a grouped `COUNT(*)` query.
4. **UI renders a badge column** — green **Ingested** / amber **Not ingested**, with a tooltip explaining the failure mode.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Chunk-count query** — `IVectorStore.GetChunkCountsAsync(IReadOnlyList<Guid> sourceIds)` (default no-op) + `PgVectorStore` grouped `COUNT(*)` override. | The durable ground truth for "ingested". | ~0.5 day | Proposed |
| 2 | **API stamping** — `FileMetadata.ChunkCount` (int?) + `Ingested` (computed); `SearchController` injects `IVectorStore` and stamps the current page's files. | Exposes the signal on the existing search response. | ~0.5 day | Proposed |
| 3 | **UI badge** — `Browse.razor` Ingestion column with green/amber badges + tooltip. | Surfaces the failure at the moment it matters (right after upload). | ~0.25 day | Proposed |
| 4 | **Tests + docs** — `SearchControllerTests` (stamping, missing sources, failure), `BrowseBindingTests` (column + badges); docs updated. | Locks down the merge and the UI contract. | ~0.5 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 first** — the query and the API stamping.
- **Item 3 next** — the badge consumes the stamped field.
- **Item 4 alongside** — tests with each, docs last.

**Total (agent):** ~1 working day including build/test verification.

## Out of Scope

- Per-file ingestion *error* surfacing (the Activity panel already shows job errors; the badge links the failure mode in a tooltip only).
- Re-triggering ingestion from the badge (re-upload / repair job remain the manual path).
- Showing ingestion status outside the Repository Browser (Search Center, Copilot, Dashboard).
