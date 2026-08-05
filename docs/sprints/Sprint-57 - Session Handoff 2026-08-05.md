# Sprint 57 - Session Handoff (2026-08-05)

Status: **Deliverable 1 (Search diagnostics) implemented and verified; uncommitted.**

## What is done (uncommitted, working tree)

- **`GET /api/rags/status`** (RagsController, authenticated):
  - `RagsStatusService`/`IRagsStatusService` + `RagsStatusSnapshot`/`UploadJobSummary` (Repository.API).
  - Counts via PostgreSQL/Dapper: `embeddings` rows, distinct `source_id`, `file_metadata` rows.
  - Recent upload jobs: last 10 `UploadIngestion` jobs from `IngestionJobService.List(50)`.
- **`IngestionDiagnostics`/`IIngestionDiagnostics`** (singleton): `TemplateGateSkipCount`, `ExtractionFailureCount`, bounded recent gate-skip list; wired into `RepositoryKnowledgeSourceIngestionService` (optional ctor dep, recorded at the canonical-gate and extraction-failure branches).
- **Search Center UI** (`SearchCenter.razor`):
  - Empty results -> contextual message via `EmptyStateMessage` property: corpus-empty ("No documents have been ingested yet... check Activity panel" + example queries) vs no-match ("ask Copilot").
  - Operator RAGS status chip (counts + recent gate skips) when `FeatureFlags:ShowInternalSearch=true`.
  - `RepositoryApiClient.GetRagsStatusAsync()` + `RagsStatusClientSnapshot` records.
- **Tests**: Repository.UnitTests 107 passed (IngestionDiagnosticsTests x3, RagsControllerTests x2). RAGS 225, Foundation 55 green. Web C#/Razor CoreCompile 0 errors.

## Last verification

- `dotnet build src/Repository.API/Repository.API.csproj --no-restore -m:1 -nodeReuse:false -p:NuGetAudit=false` -> succeeded.
- `dotnet build src/Aletheia.Web/Aletheia.Web.csproj --no-restore -t:CoreCompile -m:1 -nodeReuse:false -p:NuGetAudit=false` -> 0 errors.
- `dotnet test tests/Repository.UnitTests/...` -> 107 passed; `tests/RAGS.UnitTests/...` -> 225 passed; `tests/Aletheia.Foundation.UnitTests/...` -> 55 passed.

## Environment caveats (this sandbox)

- `.git` read-only here; commit/push in the user's terminal.
- Full WASM build fails locally (task host `ComputeWasmBuildAssets`); use the CoreCompile workaround. Web.UnitTests not buildable in sandbox (pre-existing).
- Docker engine pipe access-denied from sandbox; `docker compose` runs in the user's terminal.
- Offline build/test: `--no-restore -m:1 -nodeReuse:false -p:NuGetAudit=false`.

## Next

1. Commit Deliverable 1 (Sprint 57).
2. Deliverable 2: `RAGS:MinimumScore` + keyword fallback (ILIKE/to_tsvector) with `RetrievalStrategy` surfaced.
3. Deliverable 3: Ollama embedding provider + dimension migration + Reembed job.
4. Docker smoke test: upload doc -> ingest -> search empty vs populated corpus -> status endpoint.

## Deliverable 2 (same day, implemented)

- `RetrievalOptions` (`RAGS:MinimumScore`, default 0) + appsettings `"RAGS": { "MinimumScore": 0 }`; registered in Program.cs.
- `IVectorStore.SearchKeywordAsync` default "not supported"; PgVectorStore implements ILIKE over embeddings.content + file_metadata.file_name (newest first, strategy "keyword").
- `RagsService.RetrieveAsync` fallback when vector empty or best score < MinimumScore; `SearchResult.RetrievalStrategy` "semantic"/"keyword".
- RAGS.UnitTests 229 (+4), Repository 107, Foundation 55; Web CoreCompile 0 errors. Docs updated (AdministratorGuide, OperationsGuide).
- NOTE: RagsService ctor gained optional `IOptions<RetrievalOptions>` before logger - positional callers that passed logger as 4th arg would need updating (none found).

## Deliverable 3 (same day, implemented)

- `OllamaEmbeddingProvider` (RAGS.Application) - POST /api/embed, parses embeddings[0], tracks actual dimension.
- Config: `AI:EmbeddingProvider` (Simple|Ollama), `AI:EmbeddingDimension` (768), `AI:Providers[*].EmbeddingModel` (nomic-embed-text); provider selection with Simple fallback in `AIServiceCollectionExtensions`.
- `PgVectorSchema` dimension auto-migration (format_type check + ALTER + index drop/recreate) in BuildSqlScript and EnsureCreatedAsync; PgVectorSchemaTests ivfflat assertion updated to `DoesNotContain("CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_hnsw")`.
- Reembed: `IngestionJobEngine.Reembed`, `EnqueueReembed()` (kind ReembedIngestion), `RunReembedJobAsync` (per-source EnsureIngestedAsync, progress/heartbeat), `POST /api/jobs/rags/reembed`, `RepositoryApiClient.ReembedAsync`, Search Center admin "Re-embed all documents".
- Tests: RAGS 234, Repository 108, Foundation 55 green; Web CoreCompile 0 errors.
- Caveats: OllamaEmbeddingProvider expects the Ollama /api/embed contract ({"model", "input"} -> {"embeddings": [[...]]}); if the model dimension differs from AI:EmbeddingDimension, the provider logs a warning and the schema must match (run reembed after setting the right dimension).
