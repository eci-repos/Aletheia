# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Aletheia is an enterprise knowledge platform: document storage/governance, semantic retrieval (RAG), knowledge graph exploration, GraphRAG, LazyGraphRAG, and RAG-augmented Copilot chat. Built on .NET 10 (SDK `10.0.302`, pinned in `global.json`) following Clean Architecture / Hexagonal / DDD. Currently in the post-release RAGS v2 sprint.

**Read this documentation order before implementation:** `docs/File 00-Aletheia-Charter.md` → `docs/File 01-Aletheia-WorkPlan.md` → `docs/File 02-Current-Sprint.md` → `AGENTS.md`. `docs/File 02-Current-Sprint.md` is the **active implementation authority** — work is authorized only when the current sprint (or a sprint file it references) describes it. The current sprint overrides AGENTS.md and historical sprint/handoff files on conflict.

## Commands

```powershell
dotnet build Aletheia.slnx
dotnet test Aletheia.slnx
```

Per-suite tests (run the specific project — the full solution test can be slow):

```powershell
dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj
dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj
```

- Single test: add `--filter "FullyQualifiedName~GraphTraversalBudgetTests"` (xUnit; `--filter` also accepts `ClassName`/`Name`).
- Integration tests (`tests/Repository.IntegrationTests`) need live PostgreSQL + Neo4j — spin up the compose stack first.
- Coverage: `dotnet test <csproj> --collect:"XPlat Code Coverage"` writes `cobertura` under `tests/*/TestResults/`. Coverage note: `Aletheia.Foundation.Domain.DomainEvent` line 3 is uncovered (line rate caps at 99.47%); Repository-only coverage requires filtering to `Aletheia.Repository*` classes. Target coverage is 80%.
- Test framework: xUnit + Moq.

**Run locally** (Docker Compose, full production-like topology):

```powershell
docker compose up -d --build
```

| Endpoint | URL |
|---|---|
| Web UI (Blazor WASM) | `http://localhost:8081` |
| API | `http://localhost:8080` |
| MinIO Console | `http://localhost:9001` |
| Neo4j Browser | `http://localhost:7474` |

Infrastructure runs as containers (PostgreSQL+pgvector `:5432`, MinIO `:9000`, Neo4j `:7687`); the API/web build from `src/Repository.API/Dockerfile` and `src/Aletheia.Web/Dockerfile`. The default AI provider is Ollama at `http://host.docker.internal:11434` (model `gpt-oss:120b-cloud`) — no cloud LLM dependency for local dev. Copy `.env.example` → `.env` for production values; local defaults in `docker-compose.yml` work out of the box.

## Architecture

Dependencies point inward: Blazor Web → REST Controllers → Abstractions → Application → Domain → Infrastructure (PostgreSQL, MinIO, Neo4j) → Contracts → Foundation. See `docs/Architecture.md` for the full layered model and `docs/Technical-Presentation-Guide.md` for an end-to-end ingestion→chat walkthrough.

**Modules** (in `src/`, each following the layered pattern):

- **Repository** — file lifecycle. `Repository.Abstractions` (contracts/DTOs) → `Repository.Domain` (use case interfaces) → `Repository.Application` (orchestrators) → `Repository.Infrastructure.PostgreSQL` (metadata/search/security/versioning) and `Repository.Infrastructure.MinIO` (blobs) → `Repository.API` (REST controllers).
- **KnowledgeGraph** — `Abstractions` (GraphNode/GraphEdge/GraphPath, IGraphService) → `Application` (graph mutation) → `Infrastructure.Neo4j`.
- **RAGS** — retrieval. `RAGS.Abstractions` (interfaces + models: `Chunk`, `RetrievalRequest`, trace, budget) → `RAGS.Application` (ingestion, retrieval, GraphRAG, LazyGraphRAG, Copilot, Wiki, DocumentBriefs) → `RAGS.Infrastructure.PgVector` (vector store) + `RAGS.Infrastructure.PostgreSQL` (wiki/taxonomy/ontology) + `RAGS.Infrastructure.Graph` (Neo4j provider) → API controllers (`RagsController`, `CopilotController`, `GraphRagController`, `LazyGraphRagController`, `WikiController`, `OntologyController`, `TaxonomyController`).
- **Aletheia.Web** — Blazor WebAssembly SPA (`Pages/`, `Services/`; the main surfaces are Search Center at `/search`, Upload, Wiki, GraphExplorer, Copilot). Talks to the API via `RepositoryApiClient` with a `BearerTokenHandler`; per-page scoped services (`CopilotStateService`, `SearchScopeStateService`) persist to localStorage.
- **Aletheia.Foundation / Aletheia.Contracts / Aletheia.Security** — shared primitives (`Result<T>`, `PagedResult<T>`, domain base classes), cross-cutting abstractions, and auth (JWT, users/roles, token services).

**External dependencies:** PostgreSQL+pgvector (relational + vector search), Neo4j (knowledge graph), MinIO (object storage), Semantic Kernel (LLM calls).

**End-to-end flow:** upload (`POST /api/files/upload`) → blob in MinIO + metadata in PostgreSQL → background ingestion job (`IngestionJobService`, surfaced via `/api/jobs`) → text extraction → RAGS chunks+embeddings in pgvector → taxonomy hints + lightweight graph seed nodes → GraphRAG lazy enrichment during retrieval (entities synced into Taxonomy/Ontology) → Copilot retrieves source-filtered/theme-filtered context and synthesizes a cited answer. Repository is the system of record; RAGS is the retrieval-ready semantic memory.

## Key patterns & gotchas

- **GraphTraversalBudget is per-request, never a singleton.** `GraphRagService.RetrieveAsync` constructs one inline; `LazyGraphRagService` holds a template and calls `IGraphTraversalBudget.CreatePerRequest()`. Do **not** register `IGraphTraversalBudget` in DI (`Repository.API/Program.cs` has an explicit comment about this). `LazyGraphRagService._indexedSources` is guarded by `lock`.
- **Token accounting is real, not capped.** `TokenUsageHelper.GetTotalTokens(ChatMessageContent?)` (RAGS.Application/GraphIntelligence) reads actual SemanticKernel usage from `ChatMessageContent.Metadata`. `RecordTokens` records actual consumption **even past the budget** and returns `updated <= MaxTokenBudget` so `IsExceeded()` halts traversal — never cap-and-ignore, or the token budget silently becomes dead code.
- **Hard deadline.** Both `RetrieveAsync` paths flow `CancellationTokenSource.CreateLinkedTokenSource(...)` + `CancelAfter(MaxExecutionTime)` through every LLM/traversal call. Keep the budget + `ct` parameters when touching discovery interfaces.
- **Noise entities never persist.** `NoiseEntityFilter.IsNoise` (`keyword`/`statistical-candidate`). Always filter extracted entities with it before persisting (`LazyEntityDiscoveryService.PersistAsync`, `LazyGraphRagService.PersistDiscoveryAsync`, `GraphRagService.IngestAsync`, `EnsureQueryTimeEnrichmentAsync`).
- **Retrieval trace** (`RetrievalTrace` in RAGS.Abstractions) rides on `SearchResult.Trace` (settable, additive — keep non-breaking); Search Center renders it per result card.
- **Singleton rule:** `IGraphProvider` is a singleton, so every service depending on it must be a singleton too (`GraphSummaryService`, `HierarchicalSummaryService`, `CommunityDetectionService`, `CitationPathService`, `GraphContextBuilder`, `GraphAdminService`).
- **`Result<T>`** (`Aletheia.Foundation.Shared`): use `Result.Success()/Failure(...)`, check `IsSuccess`/`Value` before proceeding. Controllers return `BadRequest(new { error = result.Error })` on failure and use `[Route("api/...")]` attribute routing.
- **Graph model:** `GraphEdge.RelationshipType` (not `Type`); source nodes use `Type == "Source"`; entity→source edges use `RelationshipType == "found_in"`; `GraphNode` metadata keys `"sourceId"`, `"sourceName"`, `"communityId"`.
- **Wiki vs WRAGS:** the user-facing surface is **Wiki**; "WRAGS" is internal code/docs only. Wiki document briefs are per-document plain-language pages (`generated_from = 'document-brief'`). Wiki search/recent excludes GraphRAG community summaries.
- **Internal search gating:** Semantic RAG is the primary user path; WRAGS/GraphRAG/LazyGraphRAG are internal operator modes gated by `FeatureFlags:ShowInternalSearch` (default false) via `InternalSearchGate` (single source of truth). Gated endpoints return 404; the Web UI hides controls.
- **Canonical templates & themes:** `docs/doc-templates/*.md` define document kinds (first line declares a `Theme: A, B` set). Since Sprint 59 the ingestion gate is **softened** — a document with no matching template ingests anyway as `template_status = Uncategorized` (no document brief). Add a template, then promote rows via `POST /api/knowledge/reevaluate` (`GET /api/knowledge/uncategorized` lists them). `KnowledgeThemeService` matches a row when **any** of its themes is in the requested set.
- **Duplicate uploads:** uploads are SHA-256 fingerprinted (`file_metadata.content_hash`); exact duplicates return HTTP 409 with a machine-readable payload. `POST /api/files/upload?existingFileId=` updates a document (same blob, new metadata + version snapshot, replace-on-reingest). `GET /api/files/duplicates` is admin-only.
- **In-memory chat state:** chat plans and execution jobs live in-memory (`InMemoryChatPlanRepository`, `InMemoryChatProgressStore`) — a container restart invalidates browser-restored plans; the client falls back to the plan preview and stops polling after 3 no-progress polls. `GET /api/copilot/plans/{id}/progress` returns 200 with `JobId == Guid.Empty` for a plan with no execution job yet; "plan not found" is a 404.
- **DB schema:** `scripts/init.sql` is the fresh-install schema; incremental schema changes are idempotent migrations under `src/Repository.Infrastructure.PostgreSQL/Migrations/` (named `YYYY-MM-DD-*.sql`). Keep both in sync.
- **Graph Explorer render contract:** `window.initGraph(containerId, nodes, edges, dotNetRef, preservePositions)` in `wwwroot/index.html`; `GraphExplorer.razor` owns a `DotNetObjectReference<GraphExplorer>` and exposes `[JSInvokable] OnGraphLayoutSettled()`.

## Documentation is mandatory

Keeping `AGENTS.md`, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, handoff/log files, and the relevant sprint file under `docs/` current is a **standing requirement** on every change — never leave documentation describing a stale state. Backlog/proposed work lives in `docs/backlog/`; a backlog item is not authorized work until the current sprint promotes it. Add a new document kind's canonical template to `docs/doc-templates` as part of the work.

## Current state

- **Sprint 61** ("Chat Approval Prompt and Admin Settings") is **complete** (2026-08-11) — see `docs/File 02-Current-Sprint.md` and `docs/sprints/Sprint-61 - Chat Approval Prompt and Admin Settings.md`. It promoted all 5 items from `docs/backlog/Chat-Approval-Prompt-and-Admin-Settings.md`, all implemented, committed, and pushed: item 1 (`4d10561` — modal approval prompt, `PlanPreview` in a centered overlay above the Activity/Chats panels, auto-expand collapsed Execution column on submit), items 2+3+4 (`793fc52` — server-side settings foundation `app_settings`/`user_settings` + singleton `SettingsService` + `GET/PUT /api/settings` + `copilot.requireApproval` per-user preference with a "Don't ask again" checkbox + `copilot.requireApproval.force` admin override), item 5 (`f8f5292` — admin `/settings` page: My Preferences + Administrator-gated Global Settings card, admin-only NavMenu entry). **No next sprint file exists yet — no new work is authorized** until one is created. Sprint 60 (`c6c3e48`) is complete; its optional Docker smoke test was **run 2026-08-10** (retrieval traces verified end-to-end on LazyGraphRAG + GraphRAG, per-request budgets clean under 5 concurrent LazyGraphRAG retrievals, concurrent GraphRAG hits the 30s hard deadline under LLM saturation, reembed verified working but slow — full `IndexAsync`). The compose topology now env-maps the internal-search gate: `SHOW_INTERNAL_SEARCH=true docker compose up -d api` (default `false`).
- All unit suites are green: RAGS 270 / Repository 129 / Foundation 55 / **Aletheia.Web.UnitTests 46**. The 6 pre-existing `Aletheia.Web.UnitTests` failures (CopilotStateService session-key `v1`/`v2`, `RepositoryApiClientUploadTests` ×4, Wiki mode-buttons) were fixed 2026-08-10 — all were **stale tests, no code regressions** (fake `HttpClient` missing the `BaseAddress` production sets, an intentional Sprint 58 storage-key bump to `v2`, and the WRAGS→Wiki button rename). `Repository.IntegrationTests` (8) needs live PostgreSQL + Neo4j.
