# Sprint 57 - Search Center Retrieval Quality and Troubleshooting

**Status:** Planned (next after Sprint 56; not yet the active authority - complete and commit Sprint 56 first)

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

Sprint file prepared. No implementation yet.
- Docs added under Sprint 56: `docs/OperationsGuide.md` -> "Search Center Troubleshooting (Sprint 56/57)" with empty-embeddings diagnosis, verification SQL, and example queries.
- Blocking prerequisite: Sprint 56 must be committed/verified before Sprint 57 becomes active (both touch the ingestion pipeline and `RagsService`/`PgVectorStore`).
