# Sprint 57 - Search Center Retrieval Quality and Troubleshooting

**Status:** Active

## Objective

1. **Make "no results" diagnosable.** When Search Center returns nothing, the user and operator should know *why*: no documents ingested, ingestion blocked/failed, or simply no match - instead of a generic "No results found".
2. **Improve retrieval quality.** Make semantic search match by meaning (real embeddings), never silently return nothing when relevant content exists (score floor + keyword fallback), and support re-embedding the corpus.

## Background

- Field report (2026-08-05): searching **"AI related requirements"** in Search Center returned nothing. Root cause: the `embeddings` table was empty (nothing had been ingested), plus today's embeddings are lexical, not semantic.
- `PgVectorStore.SearchAsync` has **no similarity threshold** (`ORDER BY embedding <=> query LIMIT @TopK`), so *any* embedded chunk would rank in the top-K. Zero results therefore means **zero embeddings**.
- Embeddings today come from `SimpleEmbeddingProvider` (deterministic character + bigram frequency hash into 128-dim). `SemanticKernelEmbeddingService` currently just wraps that fallback - no real model is used for embeddings even though an AI provider (Ollama, `AI:Providers`) is configured for chat.
- Ingestion can silently produce no embeddings: canonical template gate (`DocumentTemplateRegistry` mismatch), no extractable text, failed/queued upload job, or a fresh database. The Activity panel + `/api/jobs` hold the evidence but Search Center does not point users there.
- Sprint 56 (duplicate detection / document updates) added the `content_hash` fingerprint and update/re-ingest path; Sprint 57 builds on that ingest pipeline.

## Deliverables

### 1. Search diagnostics (API + UI)
- Add a lightweight diagnostics surface, e.g. `GET /api/rags/status` (authenticated) returning: embedded chunk count, ingested source count, per-source last upload job status/error, and names skipped by the canonical template gate (from ingestion logs or a tracked counter).
- Search Center UI: when results are empty, show a contextual message:
  - corpus has no ingested/embedded content -> "No documents have been ingested yet. Upload a document and wait for the Activity panel to show Ready, then retry." plus example queries for the registered document.
  - corpus has content but nothing matched -> "No chunks matched 'query'. Try words from your document (e.g., 'Scope of Work') or ask Copilot."
- Structured logging for template-gate skips and extraction failures (already logged in `RepositoryKnowledgeSourceIngestionService`; surface counts via the status endpoint).

### 2. Retrieval quality
- **Real embedding provider (configurable):** implement an Ollama embeddings provider (e.g., `nomic-embed-text` or an equivalent configured via `AI:Providers`) exposed through `IEmbeddingProvider`/`IEmbeddingService`; keep `SimpleEmbeddingProvider` as the fallback when no embedding model is configured. Add a config switch (e.g., `AI:EmbeddingProvider` / per-provider `EmbeddingModel`).
- **Dimension handling + re-embedding:** the `embeddings` table is `vector(128)`. Provide an idempotent dimension migration path (or a normalized wrapper) and a background job (kind `Reembed`, pattern: `IngestionJobService`) that re-generates embeddings for all sources and **replaces** rows (`IVectorStore.DeleteBySourceAsync` + insert), preserving chunk ids or replacing them consistently.
- **Score floor + keyword fallback:** optional `RAGS:MinimumScore` (default 0) - when vector results are empty or below the floor, fall back to keyword search (`file_name` / `content` ILIKE or `to_tsvector`) so users get results instead of silence; surface `RetrievalStrategy` (vector vs keyword) in `SearchResult` (already a field).
- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints and the `/api/jobs` snapshot contract compatible.

### 3. Tests
- Diagnostics: empty-corpus status, template-gate skip reporting.
- Retrieval: score floor filters weak matches; keyword fallback returns results when vector is empty/below floor; embedding provider selection honors config; Reembed job replaces rows for all sources.
- Existing suites (RAGS / Foundation / Repository) remain green; Web C#/Razor compiles.

### 4. Docs
- `docs/OperationsGuide.md`: Search Center troubleshooting section (empty-embeddings diagnosis + example queries) - **added in Sprint 56**; extend with the status endpoint.
- `docs/AdministratorGuide.md`: embedding provider configuration, score floor, re-embed job.
- `docs/Architecture.md`: retrieval pipeline (vector + keyword fallback, re-embedding).
- AGENTS / handoff notes updated.

## Acceptance Criteria

- Searching an empty corpus shows an actionable message ("no documents ingested; check the Activity panel") instead of generic "No results".
- With content ingested, queries sharing words with the documents return results; when vector scores are below the floor (or empty), keyword fallback still returns results with `RetrievalStrategy` indicating the path used.
- A real embedding provider can be enabled by config, and the Reembed job replaces embeddings for all sources without manual SQL.
- RAGS / Foundation / Repository suites green; Web C#/Razor compiles.

## Out of Scope

- Reranker models, cross-lingual embeddings, multi-tenant search.
- Changing GraphRAG/LazyGraphRAG internals or community-summary generation.
- Search personalization/history beyond the existing Recent Context.

---

## Implementation Status (2026-08-05)

Deliverable 1 (Search diagnostics) implemented and locally verified:

1. **`GET /api/rags/status`** (RagsController, authenticated) - `RagsStatusService`/`IRagsStatusService` (Repository.API, singletons in Program.cs) returns `RagsStatusSnapshot`: EmbeddedChunkCount, IngestedSourceCount, RegisteredDocumentCount (PostgreSQL counts via Dapper), TemplateGateSkipCount, ExtractionFailureCount + recent gate skips (from `IngestionDiagnostics`), and the last 10 `UploadIngestion` jobs (from `IngestionJobService.List`).
2. **Ingestion diagnostics counters** - `IIngestionDiagnostics`/`IngestionDiagnostics` (singleton); `RepositoryKnowledgeSourceIngestionService` records template-gate skips and extraction failures (optional ctor dependency, wired in DI).
3. **Search Center UI** - empty results now show a contextual message: corpus-empty -> "No documents have been ingested yet... check the Activity panel" + example queries; otherwise "No results found for 'query'... ask Copilot". A RAGS status chip (counts + recent gate skips) renders for operators when `FeatureFlags:ShowInternalSearch=true`. `RepositoryApiClient.GetRagsStatusAsync()` added.
4. **Tests** - Repository.UnitTests 107 passed (IngestionDiagnosticsTests x3, RagsControllerTests x2); RAGS 225, Foundation 55 green; Web C#/Razor CoreCompile 0 errors.
5. **Docs** - OperationsGuide status endpoint, AdministratorGuide API table updated.

Remaining in Sprint 57: Deliverable 2 (score floor + keyword fallback), Deliverable 3 (real embedding provider + Reembed job), Docker smoke test, commit.

## Deliverable 2 - Score Floor and Keyword Fallback (2026-08-05, implemented)

- `RetrievalOptions` (`RAGS:MinimumScore`, default 0) registered in Program.cs; `"RAGS": { "MinimumScore": 0 }` added to appsettings.json.
- `IVectorStore.SearchKeywordAsync(query, topK)` added with a default "not supported" implementation; `PgVectorStore` implements it (PostgreSQL `ILIKE` over `embeddings.content` and `file_metadata.file_name`, newest first, `RetrievalStrategy` = "keyword").
- `RagsService.RetrieveAsync` falls back to keyword search when vector results are empty or the best vector score is below `RAGS:MinimumScore`; strategy surfaced via `SearchResult.RetrievalStrategy` ("semantic" vs "keyword"). Backward compatible (default 0 => fallback only on empty vector results).
- Tests: RAGS.UnitTests 229 passed (+4: empty-vector fallback, below-floor fallback, above-floor keeps vector, unsupported-keyword keeps vector). Repository 107, Foundation 55 green; Web CoreCompile 0 errors.
- Docs: AdministratorGuide (Retrieval Options), OperationsGuide (Keyword Fallback).

## Deliverable 3 - Real Embedding Provider and Reembed Job (2026-08-05, implemented)

- `OllamaEmbeddingProvider` (RAGS.Application/Providers): calls Ollama `/api/embed` with the configured model; parses `embeddings[0]`; tracks the actual dimension after first call; clear failure messages.
- Config: `AI:EmbeddingProvider` ("Simple" default | "Ollama"), `AI:EmbeddingDimension` (default 768), `AI:Providers[*].EmbeddingModel` (LocalOllama -> nomic-embed-text in appsettings). `AIServiceCollectionExtensions` selects Ollama when configured (with Simple fallback when misconfigured).
- Dimension migration: `PgVectorSchema` now emits an idempotent DO-block that checks `format_type(atttypid, atttypmod)` of `embeddings.embedding` and ALTERs the column to the provider dimension (dropping the vector index first; recreated after). Applied in both `BuildSqlScript` and `EnsureCreatedAsync`.
- Reembed job: `IngestionJobEngine.Reembed`, `IIngestionJobService.EnqueueReembed()` (kind `ReembedIngestion`), `RunReembedJobAsync` (reuses `LoadRepairSourcesAsync` + `EnsureIngestedAsync` per source with heartbeat/progress), `POST /api/jobs/rags/reembed`, `RepositoryApiClient.ReembedAsync`, Search Center admin "Re-embed all documents" button.
- Tests: RAGS.UnitTests 234 passed (+5 OllamaEmbeddingProvider; PgVectorSchema ivfflat assertion adjusted for the DROP reference); Repository 108 (+1 JobsControllerTests); Foundation 55 green; Web CoreCompile 0 errors.
- Docs: AdministratorGuide (embedding config + reembed), OperationsGuide (re-embedding).

## Defect Fix - Ingestion job routing regression (2026-08-05)

- **Symptom:** uploads stored fine but the `UploadIngestion` background job immediately went to "Document brief" and failed with `no retrieved evidence is available`; no text extraction/chunking/embedding ever ran, so Search Center stayed empty.
- **Root cause:** commit `9cdc131` inserted the `Reembed` branch into `IngestionJobService.RunJobAsync` between the `DocumentBriefs` `if` and its body, orphaning the body into an unconditional bare block. Every job that reached that point (including `Rags` uploads) ran `RunDocumentBriefsJobAsync`; `RunUploadedFileJobAsync` became unreachable (compiler warning CS0162).
- **Fix:** restored proper if/body pairing so `DocumentBriefs`, `Reembed`, and upload (`Rags`) jobs each route to their own handler.
- **Regression test:** `Repository.UnitTests.Services.IngestionJobServiceRoutingTests.UploadedFileJob_runs_ingestion_then_queues_document_brief` enqueues an uploaded file through the real background service and asserts the job ends `Succeeded`/`Indexed`, the text extractor + RAGS ingest ran exactly once, and the document brief only runs as its own queued job afterward. Added `<InternalsVisibleTo Include="Repository.UnitTests" />` to `Repository.API.csproj`.
- **Results:** Repository.UnitTests 109 (+1), RAGS 234, Foundation 55 green; Web CoreCompile 0 errors.
