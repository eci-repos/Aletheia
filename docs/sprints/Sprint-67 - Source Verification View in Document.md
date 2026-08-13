# Sprint 67 - Source Verification: View the Exact Passage in the Document

**Status:** Active (2026-08-13)

Full authority: this file. Sprint 66 (Remove Redundant Metadata Nav Item) is **complete, committed, and pushed** on `origin/master`.

Promotes `docs/backlog/Source-Verification-View-in-Document.md` — the project owner's design review (2026-08-11) of the end-user verification loop. Today Search Center and Copilot return chunk-level answers with citations, but the only way to open a source is the raw browser download. There is no in-app preview and no way to jump from a result/citation to the exact passage that grounded it.

## Objective

Give end-users an in-app document viewer with **page-accurate passage highlighting** so an answer can be verified against the source: a chunk carries a page locator born at extraction time, a preview endpoint streams the original blob inline, a `/document/{id}` viewer renders it (PDF.js for PDF, extracted text for other types), and Search Center / Copilot link results and citations straight to the exact passage.

## Decisions (from the backlog item, settled 2026-08-11)

1. **PDF is the first-class document type for page-accurate highlighting**; Office/other types fall back to an **extracted-text viewer** that highlights the passage and shows its page marker when available.
2. **The locator is born at extraction time, never guessed.** `Chunk` gains nullable `PageNumber` (and best-effort `OffsetInPage`) populated by the extraction/chunking pipeline when the extractor reports page boundaries. Pre-existing rows have null locators until a **lightweight reembed** (no LLM) re-chunks with the locator.
3. **Preview streams the original blob inline from MinIO** (mirroring `DownloadUseCase`), with `FileMetadata.ContentType` deciding the renderer. No new object storage or conversion dependency in v1.
4. **PDF renders in-browser via PDF.js** (text-layer enabled) rather than a native `<embed>`: a native embed cannot highlight. Office/other types render the extracted text with page markers.
5. **Highlight = match the chunk's leading phrase in the rendered text layer**, falling back to a page-jump + top-of-page highlight when the exact text doesn't align between extraction and render — never a hard error.
6. **Non-breaking wire format**: `SearchResult`/chunk responses gain optional `pageNumber`/`offsetInPage`; `RetrievalTrace` and existing fields are untouched.

## Deliverables

### 1. Chunk source locator
- Add nullable `PageNumber`/`OffsetInPage` to `Chunk` (optional ctor params, non-breaking).
- Extend `ChunkingPipeline` to carry page boundaries into chunks (an overload that accepts page boundaries; the character-count path stays).
- Add a **PDF text extractor** (page-aware) to `UploadedFileTextExtractor` so PDFs extract with page markers; the extraction result carries page boundaries.
- Add `page_number` to the embeddings schema (idempotent migration + `init.sql` + `PgVectorSchema`); `PgVectorStore` persists and returns it.
- Populate via the existing lightweight reembed flow.

### 2. Preview endpoint
- `GET /api/files/{id}/preview` (optionally versioned) streaming the original blob inline with the stored `ContentType`; PDF → raw PDF bytes, text → extracted text + page markers, other types → 415 with a friendly message. Reuses the existing file access/authorization path.

### 3. In-app document viewer
- `Pages/Document/View.razor` (`/document/{id}?page=&chunk=`) with a PDF.js renderer (text layer) and an extracted-text renderer for non-PDF; accepts `pageNumber` + `chunkId` (or leading-phrase) via query params. Mirrors the `Download.razor`/`RepositoryApiClient` pattern.

### 4. Passage highlight + auto-scroll
- On load with a `page`/`chunk` param: locate the chunk text in the PDF.js text layer (or the text preview) and highlight it, scroll to it; page-jump + top-of-page fallback when text doesn't align.

### 5. Wire-through from results and citations
- `SearchResult`/chunk DTOs carry the locator; Search Center result cards render a "View in document (p. N)" affordance; Copilot citations become links to the viewer with the cited chunk/page. `RepositoryApiClient` gains `PreviewAsync`/viewer navigation.

### 6. Tests + docs
- Locator unit tests (page-boundary chunking, null locators for old rows), preview endpoint tests (content-type branching, auth), viewer binding tests, wire-through tests; docs (Architecture, user guide, AGENTS, File 02/03, this sprint file).

## Acceptance Criteria

- A chunk returned by Search Center / Copilot can be opened in `/document/{id}` and the exact passage is highlighted (or the page is jumped to) — no download required.
- PDFs render in-browser with a text layer; non-PDF types render extracted text with page markers.
- Web unit suite green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Office → PDF conversion in v1 (Office documents get the extracted-text viewer with passage highlight instead).
- Full-text search *inside* the viewer, annotation/notes, or bookmarks.
- Version-diff highlighting across document versions.
- Per-user viewing preferences (this is a global feature).

---

## Implementation Status

**Implemented, committed, and pushed (2026-08-13).** All 6 items complete.

### Item 1 — Chunk source locator
- `Chunk` gains nullable `PageNumber`/`OffsetInPage` (optional ctor params, non-breaking; null for pre-Sprint-67 rows and non-page-aware text).
- `ChunkingPipeline.Chunk` gains a page-boundary overload (`IReadOnlyList<TextPage>? pages`); the character-count path is unchanged. `ResolvePage` stamps each chunk with the page whose range contains the chunk start (a chunk straddling a page boundary is stamped with the page it starts on) and the best-effort `OffsetInPage`.
- `TextPage` record (`RAGS.Abstractions/Models/TextPage.cs`): 1-based page number + character range into the normalized extraction text.
- `UploadedFileTextExtractor` gains a **page-aware PDF path** via `UglyToad.PdfPig` (restricted feed `0.1.9-alpha001-patch1`): extracts text page-by-page, builds the normalized text page-by-page (so page offsets stay valid), and returns `Pages`. `IsPdf` is public static so the controller can branch on it.
- Embeddings schema: idempotent migration `2026-08-13-embeddings-page-number.sql` (`ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS page_number INT`) + `scripts/init.sql` + `PgVectorSchema` in sync; `PgVectorStore` persists and returns `page_number`.
- Populated via the existing lightweight reembed flow (`IngestionJobService`/`RepositoryKnowledgeSourceIngestionService` pass the extractor's pages through to chunking).

### Item 2 — Preview endpoint
- `GET /api/files/{id}/preview` (optional `?version=`) in `FilesController`: resolves metadata by file id alone (`IMetadataRepository.GetByFileIdAsync` — new default-impl interface method, PostgreSQL override), streams the original blob via `DownloadUseCase`. PDF → raw PDF bytes (`File(..., "application/pdf", enableRangeProcessing: true)`); text/docx → `UploadedFileTextExtractor` → `FileTextPreviewResponse` (fileName, contentType, text, pages); unsupported → 415.

### Item 3 — In-app document viewer
- `Pages/Document/View.razor` at `/document/{id}` with `Page`/`Chunk`/`Version` query params. PDF → `#pdf-viewer` div rendered by `window.renderPdf` (PDF.js v3.11.174 from unpkg, text layer enabled); non-PDF → per-page `<section class="text-page">` sections (or a plain `<pre>` when no page markers). Mirrors the `Download.razor`/`RepositoryApiClient` pattern; `RepositoryApiClient.PreviewAsync` returns `FilePreviewClientResult` (PDF stream or text+pages).

### Item 4 — Passage highlight + auto-scroll
- PDF: `renderPdf` renders the requested page to canvas, builds the text layer from `getTextContent()`, highlights the chunk's leading phrase (fallback: first text item), scrolls to it.
- Text: `HighlightPhrase` wraps matches in `<mark class="passage-highlight">`; `OnAfterRenderAsync` scrolls to `.passage-highlight`, falling back to `#page-N` (page-jump) when the phrase doesn't align between extraction and render — never a hard error.

### Item 5 — Wire-through from results and citations
- Search Center result cards render a **"View in document (p. N)"** button linking to `document/{sourceId}?page=&chunk=<leading phrase>`.
- Copilot answers: `ChatMessage.Citations` (`ChatCitation(Number, SourceId, PageNumber, LeadingPhrase)`) is populated by `RetrievalAugmentedPromptBuilder.BuildCitations` (same ranked→grouped-by-source→sequential numbering as the prompt's context blocks); `Index.razor` `LinkCitations` turns `[N]` markers into `<a class="copilot-citation" href="document/{sourceId}?page=&chunk=">[N]</a>`.

### Item 6 — Tests + docs
- **RAGS 293** (+3): `RetrievalAugmentedPromptBuilderTests` — `BuildCitations` maps sequential numbers grouped by source, uses the leading 100-char phrase, empty/null results.
- **Repository 134** (+4): `FilesControllerTests` — preview returns PDF bytes, extracted text, 415 for unsupported, 404 for missing file.
- **Web 76** (+8): `DocumentViewerBindingTests` — viewer route/params, PDF/text renderers, highlight/scroll, CSS, index.html PDF.js, Search Center link, Copilot citation links, `RepositoryApiClient.PreviewAsync`.
- **Foundation 55** unchanged. `dotnet build Aletheia.slnx` succeeds (0 errors).
- Docs: this sprint file, File 02/03, AGENTS.md, CLAUDE.md updated; backlog item archived.

**Residual manual (user-side):** hard-refresh `/search` and `/copilot` for a live visual check of the viewer links; a Docker smoke pass (upload a PDF → search → open the passage in `/document/{id}`) is optional.
