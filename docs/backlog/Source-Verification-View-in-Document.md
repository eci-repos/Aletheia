# Backlog: Source Verification — View the Exact Passage in the Document

**Status:** **Proposed** — not yet promoted to a sprint. No work authorized.
**Created:** 2026-08-11
**Source:** Design review with the project owner of the end-user verification loop. Today Search Center and Copilot return chunk-level answers with citations, but the only way to open a source is the raw browser download (`Download.razor` → `DownloadUseCase` → MinIO stream). There is no in-app preview and no way to jump from a result/citation to the exact passage that grounded it: `Chunk` carries only `Id/SourceId/Content/Index` (no page/locator), `ChunkingPipeline` slices text by character count (1000/200) with no page awareness, `SearchResult` wraps the chunk + `Citations`, and `FileMetadata.ContentType` is available to branch a viewer.

## Problem

A knowledge worker gets a good answer but cannot **verify it in the source**:

- Search and chat results quote extracted chunks, but opening the source means downloading the whole file and manually hunting for the passage.
- Copilot citations are opaque `[1]` markers — no way to jump from a citation to the page/paragraph it came from.
- There is no in-app document preview at all, so verification requires leaving the platform (download → open in external tool).
- End-users read verification as the trust loop: *"an answer is only as good as proving it to me."* Without it, retrieved answers are treated as unverified summaries.

## Decisions made (2026-08-11)

1. **PDF is the first-class document type for page-accurate highlighting** (the corpus is largely PDF/office exports); Office and other types fall back to an **extracted-text viewer** that highlights the passage and shows its page marker when available.
2. **The locator is born at extraction time, never guessed.** `Chunk` gains nullable `PageNumber` (and best-effort `OffsetInPage`) populated by the extraction/chunking pipeline when the extractor reports page boundaries. Pre-existing rows have null locators until a **lightweight reembed** (no LLM) re-chunks with the locator — the existing `POST /api/jobs/rags/reembed` flow already supports this.
3. **Preview streams the original blob inline from MinIO** (mirroring `DownloadUseCase`), with `FileMetadata.ContentType` deciding the renderer. No new object storage or conversion dependency in v1.
4. **PDF renders in-browser via PDF.js** (text-layer enabled) rather than a native `<embed>`: a native embed cannot highlight. Office/other types render the extracted text with page markers.
5. **Highlight = match the chunk's leading phrase in the rendered text layer**, falling back to a page-jump + top-of-page highlight when the exact text doesn't align between extraction and render — never a hard error.
6. **Non-breaking wire format**: `SearchResult`/chunk responses gain optional `pageNumber`/`offsetInPage`; `RetrievalTrace` and existing fields are untouched.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Chunk source locator** — add nullable `PageNumber`/`OffsetInPage` to `Chunk`; extend `ChunkingPipeline` (and the PDF text extractor feeding it) to carry page boundaries into chunks; add `page_number` to the embeddings schema (idempotent migration + `init.sql`); populate via lightweight reembed. | Without a locator there is no way to "show me where in the document." This is the foundation everything else consumes. | ~1 day | Proposed |
| 2 | **Preview endpoint** — `GET /api/files/{id}/preview` (optionally versioned) streaming the original blob inline with the stored `ContentType`; PDF → raw PDF bytes, text → extracted text + page markers, other types → 415 with a friendly message. Reuses the existing file access/authorization path. | The viewer needs a streaming surface that is distinct from download (inline, not attachment). | ~0.5 day | Proposed |
| 3 | **In-app document viewer** — `Pages/Document/View.razor` (`/document/{id}?page=&chunk=`) with a PDF.js renderer (text layer) and an extracted-text renderer for non-PDF; accepts `pageNumber` + `chunkId` (or leading-phrase) via query params. Mirrors the `Download.razor`/`RepositoryApiClient` pattern. | Gives end-users the first in-platform document surface; everything links into it. | ~1–1.5 days | Proposed |
| 4 | **Passage highlight + auto-scroll** — on load with a `page`/`chunk` param: locate the chunk text in the PDF.js text layer (or the text preview) and highlight it, scroll to it; page-jump + top-of-page fallback when text doesn't align. | This is the verification payoff: the user sees the exact passage the answer quoted, not just the page. | ~0.5–1 day | Proposed |
| 5 | **Wire-through from results and citations** — `SearchResult`/chunk DTOs carry the locator; Search Center result cards render a "View in document (p. N)" affordance; Copilot citations become links to the viewer with the cited chunk/page. `RepositoryApiClient` gains `PreviewAsync`/viewer navigation. | Without the wire-through the feature is invisible — this is where the end-user actually discovers it. | ~0.5–1 day | Proposed |
| 6 | **Tests + docs** — locator unit tests (page-boundary chunking, null locators for old rows), preview endpoint tests (content-type branching, auth), viewer binding tests, wire-through tests; docs (Architecture, user guide, AGENTS, File 02/03, sprint file when promoted). | Verification is a correctness claim; the locator and highlight logic must be locked down. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 first** — the locator (foundation) and the preview endpoint (viewer's I/O) are independent prerequisites.
- **Item 3 next** — the viewer renders the preview; it can land before highlighting.
- **Item 4 after 3** — highlight consumes the viewer's text layer.
- **Item 5 last** — the wire-through makes it discoverable; do it once the surface exists.
- **Item 6 alongside each** — locator/preview tests with items 1–2, viewer/highlight tests with 3–4, wire-through tests with 5.

**Total (agent):** ~4–5 working days including build/test verification and a Docker smoke pass.

## Out of Scope

- Office → PDF conversion in v1 (needs a new dependency; Office documents get the extracted-text viewer with passage highlight instead).
- Full-text search *inside* the viewer, annotation/notes, or bookmarks.
- Version-diff highlighting across document versions.
- Per-user viewing preferences (this is a global feature).
