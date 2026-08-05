# Sprint 57 - Search Center Retrieval Quality and Troubleshooting

**Status:** Active

Full authority: `docs/sprints/Sprint-57 - Search Center Retrieval Quality and Troubleshooting.md` (created 2026-08-05). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 56 (Duplicate Upload Detection and Document Update Flow) is **complete, committed, and pushed**: commits `5366696` (implementation + docs) and `e34bba7` (Sprint 57 prep docs) are on `origin/master` (HEAD `e34bba7`). Remaining Sprint 56 verification: Docker smoke test (duplicate trap + update flow) - can run in parallel with Sprint 57 work.

## Objective

1. **Make "no results" diagnosable** - when Search Center returns nothing, tell the user *why* (no documents ingested, ingestion blocked/failed, or no match) instead of a generic "No results found".
2. **Improve retrieval quality** - real embeddings (configurable), score floor + keyword fallback so relevant content is never silently missed, and a Reembed background job.

## Authorized Work (summary - see sprint file for details)

1. **Search diagnostics**: `GET /api/rags/status` (embedded chunk count, ingested source count, per-source last upload job status/error, template-gate skips); Search Center UI empty-state messaging (empty corpus vs no match, pointers to Activity panel + example queries); structured logging for gate skips.
2. **Retrieval quality**: configurable real embedding provider (Ollama) with SimpleEmbeddingProvider fallback (`AI:EmbeddingProvider`); dimension migration path + `Reembed` background job (kind `Reembed`, replaces embeddings per source via DeleteBySourceAsync); optional `RAGS:MinimumScore` + keyword (ILIKE/to_tsvector) fallback with `RetrievalStrategy` surfaced.
3. **Tests**: diagnostics (empty corpus, gate skips); retrieval (score floor, keyword fallback, provider selection, Reembed); existing suites green; Web C#/Razor compiles.
4. **Docs**: OperationsGuide troubleshooting (added under Sprint 56) + status endpoint; AdministratorGuide embedding config + Reembed job; Architecture retrieval pipeline; AGENTS/handoff.

## Acceptance Criteria

- Empty-corpus search shows an actionable message ("no documents ingested; check the Activity panel") instead of generic "No results".
- With content ingested, word-sharing queries return results; keyword fallback returns results when vector scores are empty/below floor, with `RetrievalStrategy` indicating the path.
- Real embedding provider configurable; Reembed replaces embeddings for all sources without manual SQL.
- RAGS / Foundation / Repository suites green; Web C#/Razor compiles.

## Out of Scope

- Rerankers, cross-lingual embeddings, multi-tenant search; GraphRAG/LazyGraphRAG internals; community summaries.

---

## Progress (2026-08-05)

- Sprint 57 sprint file created (`docs/sprints/Sprint-57 - Search Center Retrieval Quality and Troubleshooting.md`); status flipped to Active after Sprint 56 was committed/pushed.
- **Deliverable 1 (Search diagnostics) implemented**:
  - `GET /api/rags/status` (authenticated) - `RagsStatusService` + `IngestionDiagnostics` (singletons); returns chunk/source/document counts, template-gate + extraction-failure counters, recent gate skips, last 10 `UploadIngestion` jobs.
  - `RepositoryKnowledgeSourceIngestionService` records template-gate skips and extraction failures.
  - Search Center empty-state messaging (empty corpus vs no match, Activity-panel pointer, example queries) + operator RAGS status chip when `FeatureFlags:ShowInternalSearch=true`; `RepositoryApiClient.GetRagsStatusAsync()`.
  - Tests: Repository.UnitTests 107 (IngestionDiagnosticsTests x3, RagsControllerTests x2); RAGS 225, Foundation 55 green; Web CoreCompile 0 errors.
  - Docs: OperationsGuide + AdministratorGuide updated.
- Remaining: Deliverable 2 (score floor + keyword fallback), Deliverable 3 (real embeddings + Reembed job), Docker smoke test, commit.


### Deliverable 2 - Score floor + keyword fallback (2026-08-05)

- `RAGS:MinimumScore` (default 0) via `RetrievalOptions`; `IVectorStore.SearchKeywordAsync` (default not-supported; PgVectorStore implements ILIKE over content + file name); `RagsService.RetrieveAsync` falls back to keyword when vector results are empty or below the floor; `RetrievalStrategy` = "keyword" surfaced. Backward compatible.
- RAGS.UnitTests 229 passed (+4); Repository 107, Foundation 55 green; Web CoreCompile 0 errors.
- Remaining: Deliverable 3 (real embeddings + Reembed job), Docker smoke test, commit.

### Deliverable 3 - Real embedding provider + Reembed job (2026-08-05)

- `OllamaEmbeddingProvider` (/api/embed) + `AI:EmbeddingProvider`/`AI:EmbeddingDimension`/`AI:Providers[*].EmbeddingModel` config with Simple fallback; appsettings updated (LocalOllama -> nomic-embed-text).
- `PgVectorSchema` auto-migrates `embeddings.embedding` column dimension (idempotent DO-block, drops/recreates the vector index).
- Reembed background job (kind `ReembedIngestion`): `POST /api/jobs/rags/reembed`, per-source `EnsureIngestedAsync` with progress; Search Center admin "Re-embed all documents" button.
- RAGS.UnitTests 234 (+5), Repository 108 (+1), Foundation 55 green; Web CoreCompile 0 errors.
- Remaining: Docker smoke test, commit.
