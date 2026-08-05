### Sprint 37 - Copilot RAG Retrieval Boundedness and Observability
**Status:** Completed.

#### Objective
Eliminate the remaining Copilot chat stalls caused by unbounded RAGS retrieval and synchronous source hydration, and add observability so operators can see exactly which sub-step is slow.

---

#### Background
After Sprint 36, the prompt "summarize the purpose of each of the RFP analysis engagements" no longer routes into GraphRAG and now correctly selects the small-corpus fast path. However, the `Call repository tool` step still times out after 180 seconds with the error:

> Mandatory repository tool failed: Tool invocation timed out after 180 seconds.

The documents are visible in Repository, Graph Explorer, and Ontology, so metadata and graph seed data exist. The remaining likely stalls are:

1. **Unbounded pgvector search** — `PgVectorStore` orders the entire `embeddings` table by distance before applying `LIMIT`. Without an HNSW/IVFFlat index, this becomes a full table scan as the corpus grows, and even on a small table can stall if the database connection/command timeout is not set.
2. **No command timeout on vector queries** — `NpgsqlConnection` defaults to no command timeout, so a hung query waits forever until the parent cancellation token fires at 180s.
3. **Synchronous source hydration inside the chat tool path** — `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` downloads the file from MinIO, extracts text, embeds chunks, writes to pgvector, and indexes. On a slow MinIO/PostgreSQL connection or a large file this can consume the entire tool budget.
4. **No progress messages inside the retrieval/hydration sub-steps** — the UI shows "Call repository tool" for the entire 180 seconds with no indication whether embedding, search, hydration, or DB access is slow.

---

#### Goals
*   **Bounded Vector Search:** Add explicit `CommandTimeout` to all `PgVectorStore` queries and ensure retrieval fails fast with a clear error if PostgreSQL is slow rather than silently hanging.
*   **Vector Indexing:** Create a PostgreSQL HNSW (or IVFFlat fallback) index on the `embeddings.embedding` column during schema initialization so similarity search is approximate and bounded.
*   **Hydration Budgeting:** Cap source hydration inside the chat tool path to a much shorter `HydrationTimeoutSeconds`, emit progress messages before/after hydration, and allow retrieval to continue with already-indexed chunks if hydration times out.
*   **Observability:** Add `ILogger`-based and `IProgressStore`-based milestones inside `RagsService.RetrieveAsync`, `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync`, and the small-corpus fast path so the UI can display "Generating embedding", "Querying vector store", "Hydrating source X", etc.
*   **Regression Coverage:** Add tests proving a hanging vector store or ingestion service fails within a small timeout and reports the correct sub-step, and tests proving the HNSW index SQL is emitted by schema initialization.

---

#### Implementation Notes
*   Add a new `PgVectorOptions` config section with `CommandTimeoutSeconds` (default 30) and `VectorIndexType` (`hnsw` default, `ivfflat` fallback).
*   Update `PgVectorStore` constructor to accept `IOptions<PgVectorOptions>` and apply `connection.CommandTimeout` before each query.
*   Create `RAGS.Infrastructure.PgVector.Schema.PgVectorSchemaInitializer` (hosted service) that runs on startup and creates the `embeddings` table plus the HNSW index if they do not exist. Register it in `Program.cs`.
*   Update `ChatExecutionEngineOptions` with `HydrationTimeoutSeconds` (default 30). In `RetrieveSmallCorpusScopedCollectionResultsAsync` and `RetrieveScopedCollectionResultsAsync`, wrap each `EnsureIngestedAsync` call in a linked CTS with the hydration timeout and append a progress message if hydration is skipped/timed out.
*   Add `ILogger<RagsService>` to `RagsService` and log each retrieval stage (embedding generation start, vector search start, result count).
*   Add `ILogger<RepositoryKnowledgeSourceIngestionService>` and log download/extraction/ingestion/indexing stages.
*   Update `ChatExecutionEngine` to append progress messages for "Generating embedding", "Querying vector store", and "Hydrating source" when running the tool path.
*   Keep existing synchronous APIs compatible; only add timeout/observability behavior.

---

#### Exit Criteria
*   `PgVectorStore` vector queries use a bounded command timeout and fail fast with a clear error if PostgreSQL does not respond.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passes, including new tests for bounded retrieval and hydration timeout.
*   `dotnet build Aletheia.slnx` passes.
*   `docs/File 02-Current-Sprint.md` points to Sprint 37.
*   A new test confirms HNSW index creation SQL is emitted by the schema initializer.

---

#### Validation
*   New test: `RagsService_retrieve_fails_fast_when_embedding_provider_hangs`.
*   New test: `PgVectorStore_search_uses_command_timeout`.
*   New test: `ChatExecutionEngine_hydration_timeout_reports_hydration_substep`.
*   Run full RAGS unit tests.
*   Build solution.

---

#### Risks
*   Adding HNSW requires the `pgvector` extension version to support it (v0.5.0+). The initializer will fall back to IVFFlat if HNSW is unavailable.
*   A 30-second command timeout may fail legitimate but slow vector searches on very large corpora; this is acceptable for the chat path because the alternative is an opaque 180-second hang.
*   Hydration timeout may cause retrieval to use stale chunks. This is acceptable because the prompt will still get some context, and the ingestion job can complete in the background.
