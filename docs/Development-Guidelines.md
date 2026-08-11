# Development Guidelines

## Architecture Standards

- Follow Clean Architecture, Hexagonal Architecture, and DDD.
- Keep dependencies pointing inward.
- Domain and Foundation projects must not reference infrastructure implementations.
- Use abstractions for all external dependencies.

## Build & Test

- Target framework: .NET 10.0
- Build:
  ```powershell
  dotnet build Aletheia.slnx
  ```
- Test:
  ```powershell
  dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj
  ```
- Coverage target: 80% minimum.

## Documentation

- Update README, Architecture, and Roadmap for every completed feature.
- Keep documentation aligned with the current sprint scope.

## Coding Standards

- Keep changes minimal and focused on approved phases.
- Prefer small, testable units.
- Avoid speculative features and future-phase implementations.

## Canonical Templates & Knowledge Themes

- Every document is matched to a canonical template in `docs/doc-templates` when one exists (the file name carries the clue, e.g. `CMP 2026 - 3. RFP Analysis.docx` -> `3.0 - RFP Analysis`). Since Sprint 59 the gate is **softened**: a document with no matching template is still ingested (RAGS + knowledge index + graph seed) with `template_status = Uncategorized`, so a new document kind is never lost. Template-dependent features (document briefs, per-section retrieval, theme) wait until the row is `Canonical`.
- Every template file declares its knowledge theme **set** on the **first line**: `Theme: <Theme>` (e.g. `Theme: Analysis`, or `Theme: Analysis, As-Built` for multiple). Missing or unknown themes resolve to `Uncategorized`. `file_metadata.theme` is a `text[]` set; a document in multiple themes is matched by any and counted in each.
- Themes drive the knowledge filter: the end-user picks themes in Copilot (session-level, Sprint 58) and in Search Center (shared scope, Sprint 59). Since Sprint 64 the Search Center scope applies to **semantic** search and to the internal **graph modes** (GraphRAG / LazyGraphRAG / global-graph) — the graph controllers accept `?themes=` and the graph services take an optional `sourceIds` scope (see the Sprint 64 section below). New document kinds still require a template (and themes) for the full experience, but can be ingested as `Uncategorized` first and promoted later via `POST /api/knowledge/reevaluate`.
- **Theme scope on graph retrieval (Sprint 64)**: `IGraphRagService`/`ILazyGraphRagService` `RetrieveAsync`/`GlobalSearchAsync` and `IGlobalGraphSearchService.SearchAsync` take an optional `IReadOnlyList<Guid>? sourceIds = null` **after** `cancellationToken` — null means no scope. Use `GraphThemeScope` (`RAGS.Application/GraphRAG`) to resolve a node's source id and filter nodes/communities to an allowlist (communities use match-any semantics). When adding or updating a test fake, the full parameter list must match the interface (C# requires implementations to match optional params too).
- When adding a new document kind: write its template under `docs/doc-templates`, upload a document of that kind, then run re-evaluation (Search Center admin panel or the API) to promote existing uncategorized rows and generate their document briefs.

## GraphRAG / LazyGraphRAG (Sprint 60)

- **Traversal budgets are per-request** — never register a shared `IGraphTraversalBudget` singleton. `LazyGraphRagService` holds a template and calls `CreatePerRequest()` inside `RetrieveAsync`; `GraphRagService` constructs one inline (or via an injectable `Func<IGraphTraversalBudget> budgetFactory` ctor param for tests). Guard shared mutable state (e.g. `_indexedSources`) with a lock.
- **Token accounting** records real SemanticKernel usage via `TokenUsageHelper.GetTotalTokens(ChatMessageContent?)` (reads `Metadata`). `GraphTraversalBudget.RecordTokens` records actual consumption even when it breaches the budget and returns `updated <= MaxTokenBudget` — do not "cap and ignore", or the token budget silently stops firing `IsExceeded()`.
- **Hard deadline**: every `RetrieveAsync` should flow a linked `CancellationTokenSource.CancelAfter(MaxExecutionTime)` token through all LLM/traversal calls. On the GraphRAG path the deadline is a **soft** signal since Sprint 62: when it fires without caller cancellation, degrade to a best-effort plain semantic retrieval under a short secondary deadline and return Success with trace strategy `semantic-timeout-fallback` + steps `deadline-exceeded`/`semantic-fallback` (caller cancellation and other exceptions still fail). The degrade must cover **both** a thrown `OperationCanceledException` **and** a returned `Failure` from `RagsService.RetrieveAsync` — `PgVectorStore` converts a cancelled vector search into a returned `Failure("Vector search failed. The operation was canceled.")`, so check the deadline condition whenever a semantic retrieval returns a Failure, not just in the catch block (see `GraphRagService.RunSemanticTimeoutFallbackAsync`). New retrieval paths should follow this degrade-don't-fail contract.
- **Noise entities**: never persist `keyword` / `statistical-candidate` entities to the graph — filter with `NoiseEntityFilter.IsNoise` before persistence.
- **Retrieval trace**: populate `SearchResult.Trace` (`RetrievalTrace`) with the fired strategy, LLM calls, tokens, traversed nodes/relationships, pruning ratio, elapsed ms, and step labels. The Web Search Center renders it on each GraphRAG / LazyGraphRAG result card; keep the addition non-breaking.

## Server-Side Settings (Sprint 61)

- **Layers**: `ISettingsRepository` (Abstractions) → `PostgreSqlSettingsRepository` (Infrastructure.PostgreSQL, Dapper `ON CONFLICT` upsert) → `ISettingsService` (Abstractions) → `SettingsService` (Application). The service is a **singleton** with in-memory caching; writes go through to the repository and update the cache, so the cache never goes stale within a process.
- **Schema**: `app_settings` (global, admin-managed) and `user_settings` (per-user, `(user_id, key)` PK). Keep `scripts/init.sql` and the idempotent migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-10-app-user-settings.sql` in sync.
- **API**: `GET/PUT /api/settings` (Administrator) and `GET/PUT /api/settings/me` (authenticated). The caller's user id is the JWT `NameIdentifier` claim.
- **Typed accessors**: `GetBoolAsync/SetBoolAsync(key, defaultValue, userId?)` — null `userId` = app/global scope. Missing/invalid values fall back to the default.
- **Chat approval policy**: setting keys live in `Aletheia.RAGS.Abstractions.Configuration.ChatApprovalSettings` (shared with the Web client — never hard-code the strings in two places). `ChatPlanApprovalService.CreatePlanAsync` takes the caller's `userId` and applies: `base && (userPrefersApproval || adminOverride)`. The Web modal's "Don't ask again" writes `copilot.requireApproval = false`; the client auto-approves + executes when a plan comes back with `RequiresApproval = false`.
- **Settings page**: `Pages/Settings/Index.razor` (`/settings`) renders **My Preferences** (own `copilot.requireApproval` toggle, any authenticated user) + a **Global Settings (Administrator)** card (`copilot.requireApproval.force`) behind `AuthorizeView Roles="Administrator"`; toggles load on init and save on change through `GET/PUT /api/settings/me` + `/api/settings`. The NavMenu **Settings** entry is admin-only. Gating mirrors the API: the UI hides admin surfaces for non-admins, the API enforces the role.

## Persisted LazyGraphRAG Corpus Index and Batch GraphRAG Ingest (Sprint 63)

- **Corpus index persistence**: `ICorpusIndexRepository` (`UpsertDocumentAsync(sourceId, termFrequency, documentLength, ct)` + `LoadAsync(ct)`) → `PostgreSqlCorpusIndexRepository` (Dapper + `PostgreSqlConnectionFactory`, transaction upsert of `lazygraphrag_corpus_documents` + delete/reinsert `lazygraphrag_corpus_terms`; LEFT JOIN load). Keep `scripts/init.sql` and the idempotent migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-11-lazygraphrag-corpus-index.sql` in sync. `CorpusDiscoveryIndex` ctor takes `(ICorpusIndexRepository? repository = null, ILogger? logger = null)` — loads the persisted corpus at startup and persists write-through on `IndexAsync`. **Both are best-effort**: a load/persist failure logs a warning and never fails ingestion; the in-memory index stays authoritative. Register `AddSingleton<ICorpusIndexRepository, PostgreSqlCorpusIndexRepository>()` in `Program.cs`.
- **Batch graph writes**: prefer `IGraphProvider.CreateNodesAsync` / `CreateRelationshipsAsync` / `UpdateNodesAsync` for multi-write ingest paths. They have **default interface implementations** that fall back to per-item calls, so existing fakes keep compiling — but new fakes should override the batch methods to assert batching. `Neo4jGraphProvider` implements them with `UNWIND $rows AS row` Cypher, grouping nodes/updates by `BuildNodeLabels(type)` and relationships by `NormalizeToken(RelationshipType, "related_to")` (dynamic labels/types can't be set per-row).
- **Bounded-concurrency ingest**: full-ingest paths run 4 phases with `MaxLlmConcurrency = 4` (`SemaphoreSlim` + `Task.WhenAll`): (1) bounded per-chunk entity + relationship extraction (relationship pass stays sequential within a chunk), (2) one `CreateNodesAsync` + one `CreateRelationshipsAsync` per label/type group, (3) bounded entity summaries (deduped), (4) **gated community detection** — check `SourceNodeExistsAsync` **before** creating the source node and run community detection + community summaries + `UpdateNodesAsync` only when `!sourceExists`. Re-ingests of an existing source skip the O(graph) re-cluster; retrieval-time discovery still re-clusters on cache miss.