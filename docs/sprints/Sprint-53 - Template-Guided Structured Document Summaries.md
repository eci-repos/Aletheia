# Sprint 53 - Template-Guided Structured Document Summaries

**Status:** Active (implementation complete; pending end-to-end verification)

## Objective

Make document summaries deterministic, structured, and grounded:

1. **Nature first** - every summary must open with the document's stated purpose/theme from its opening/"Project Summary" section (chunk 0), which vector ranking misses.
2. **Template-guided structure** - documents of the same kind share a template in `docs/doc-templates` (e.g., `3.0 - RFP Analysis.md`) that enumerates the ordered sections every such document covers. Summaries must follow that exact order, per section with its own retrieved evidence.
3. **Chunk order persistence** - the vector store must know chunk order so the engine can fetch each document's opening chunks deterministically.

## Background

- The `embeddings` table stores no chunk index, so the engine cannot retrieve "the first chunk" (which contains the title/RFP metadata/Project Summary).
- Vector top-k rarely surfaces the document opening, so summaries become "a compilation of all RFP areas" without the project's nature.
- `docs/doc-templates/*.md` are the canonical section contracts (headings = ordered sections, with explanations).

## Deliverables

1. **Chunk index persistence**
   - `embeddings` gains `chunk_index INT` (created by `PgVectorSchema` + `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` for existing DBs).
   - `PgVectorStore.StoreAsync` / `StoreBatchAsync` persist `chunk.Index`.
   - New `IVectorStore.GetSourceChunksAsync(Guid sourceId, int take, ...)` returning first-N chunks ordered by `chunk_index`.
   - `IRagsService.RetrieveSourceChunksAsync(...)` passthrough (+ test fakes).

2. **DocumentTemplateRegistry** (`Aletheia.RAGS.Application`)
   - Interface `IDocumentTemplateRegistry.TryGetSections(string fileName)` in `RAGS.Abstractions.Interfaces`.
   - Parses `docs/doc-templates/*.md`: ordered `## Heading` sections with a short description (first paragraph after the heading).
   - Template match = file name contains the template name (case-insensitive). Singleton in DI.

3. **Engine - deterministic opening + per-section evidence** (`RetrieveScopedCollectionResultsAsync`)
   - Per source: fetch first 3 chunks via `RetrieveSourceChunksAsync` and merge (they sort first by chunk index).
   - If a template matches the source: for up to 6 sections, run a scoped retrieval with the section title (top 2) and merge; else keep the existing query-variant behavior.
   - Raise the final merged take so section evidence is not truncated.

4. **Prompt scaffold** (`ChatRequestOptions.SectionOutline`, `RetrievalAugmentedPromptBuilder`)
   - `ChatRequestOptions` gains `SectionOutline` (ordered template sections with descriptions).
   - Engine `BuildOptions` populates it from the registry for the retrieved source(s).
   - Prompt builder, when an outline is present, instructs: open with the project's nature/purpose, then cover each section in the given order, stating "not covered" when absent; never invent.

5. **Docs** - orchestration playbook: summaries follow template section order, nature first.

## Acceptance Criteria

- "prepare a summary for each CMP RFP project" yields, per document: nature/purpose first (from the opening chunk), then sections in template order (Project Summary, Bid Opportunities, Scope of Work, ...) with per-section evidence.
- Unit tests: store chunk ordering, template registry parsing/matching, engine opening-chunk injection, per-section retrieval, prompt outline instructions.
- RAGS.UnitTests / Foundation / Repository suites remain green.
- Re-ingest existing documents once so `chunk_index` populates.


## Execution Status (2026-08-03)

Implemented and verified:

- **Chunk order persistence**: `embeddings.chunk_index` column (created by `PgVectorSchema` + `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`); `PgVectorStore.StoreAsync`/`StoreBatchAsync` persist `Chunk.Index`; new `IVectorStore.GetSourceChunksAsync` (ordered by chunk index) with `IRagsService.RetrieveSourceChunksAsync` passthrough; default interface implementations keep existing fakes compiling.
- **DocumentTemplateRegistry** (singleton): parses `docs/doc-templates/*.md` into ordered sections (numbered bold items or `##` headings, with a short description); matches documents by token overlap (e.g., `CMP 2026 - 3. RFP Analysis.docx` matches `3.0 - RFP Analysis`).
- **Engine**: per-source summary retrieval now injects the first 3 opening chunks (nature/Project Summary) and, for template-matched sources, runs per-section scoped retrieval (up to 6 sections, top 2 each); tool path also injects opening chunks; `BuildOptions` populates `ChatRequestOptions.SectionOutline`.
- **Prompt**: when an outline is present, instructs opening with the project's nature/purpose, then sections in template order, stating "not covered" rather than inventing.

Tests (all green):

- RAGS.UnitTests: 207/207 (new: `DocumentTemplateRegistryTests` x4, `Engine_injects_opening_chunks_and_section_outline_for_template_documents`, `PgVectorStoreTests` updated for `chunk_index`).
- Aletheia.Foundation.UnitTests: 55/55; Repository.UnitTests: 91/91; Aletheia.Web C# compiles.

Required manual step:

- Re-ingest the CMP documents once so `chunk_index` populates for existing rows (the ALTER adds the column; existing rows are NULL and sort last until re-ingested).
