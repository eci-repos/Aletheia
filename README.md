# Aletheia

Aletheia is an enterprise knowledge platform for document storage, governance, semantic retrieval, knowledge graph exploration, GraphRAG, LazyGraphRAG, and RAG-augmented AI Copilot chat.

The platform is in the post-release RAGS v2 sprint. The active work moves GraphRAG closer to Microsoft Research's index-heavy model and moves LazyGraphRAG closer to Microsoft's low-index-cost, query-time traversal model.

## Current Sprint

Sprint 58 - Session Knowledge Theme Filtering (active). See docs/File 02-Current-Sprint.md and docs/sprints/.

## Solution Layout

- `Aletheia.slnx` - solution file
- `src/Aletheia.Foundation` - foundational domain abstractions
- `src/Aletheia.Contracts` - cross-cutting contract abstractions
- `src/Aletheia.Security` - authentication, authorization, user, role, and token services
- `src/Aletheia.Web` - Blazor WebAssembly user experience
- `src/KnowledgeGraph.*` - graph abstractions, application services, and Neo4j infrastructure
- `src/RAGS.*` - RAGS, GraphRAG, LazyGraphRAG, embeddings, ontology, taxonomy, and AI Copilot services
- `src/Repository.Abstractions` - repository contracts and DTOs
- `src/Repository.Domain` - repository use case interfaces
- `src/Repository.Application` - repository use case implementations
- `src/Repository.API` - authenticated REST API surface
- `src/Repository.Infrastructure.MinIO` - file object storage adapter
- `src/Repository.Infrastructure.PostgreSQL` - metadata, search, security, and versioning persistence
- `tests/Aletheia.Foundation.UnitTests` - unit tests for foundation types
- `tests/Repository.UnitTests` - unit tests for repository contracts and use cases
- `tests/RAGS.UnitTests` - RAGS, GraphRAG, LazyGraphRAG, and Copilot unit tests
- `tests/Repository.IntegrationTests` - API integration tests
- `tests/Aletheia.LoadTests` - command-line load test harness
- `docs` - project documentation
- `docker-compose.yml` - local production-like runtime topology

## Documentation

- `docs/File 00-Aletheia-Charter.md` - charter and guiding principles
- `docs/File 01-Aletheia-WorkPlan.md` - phased delivery plan
- `docs/File 02-Current-Sprint.md` - active sprint scope
- `docs/Architecture.md` - architecture overview
- `docs/Technical-Presentation-Guide.md` - technical audience briefing, end-to-end ingestion to chat completion, CLI/API/Web/RAGS/GraphRAG guide
- `docs/GraphRAG-Implementation-vs-Microsoft-Research.md` - current GraphRAG/LazyGraphRAG alignment against Microsoft Research patterns
- `docs/Roadmap.md` - sprint-aligned roadmap
- `docs/Development-Guidelines.md` - development standards

## Run Locally

```powershell
docker compose up -d --build
```

Default local endpoints:

- Web UI: `http://localhost:8081`
- API: `http://localhost:8080`
- MinIO Console: `http://localhost:9001`
- Neo4j Browser: `http://localhost:7474`

Recent Docker validation confirmed API readiness, Web UI login, authenticated Search Center retrieval across Semantic/WRAGS/GraphRAG/LazyGraphRAG, and WRAGS wiki lifecycle behavior. Long-running ingestion and WRAGS regeneration now return a background job id and report stage, heartbeat, progress, and failures through `/api/jobs` and the Web Activity panel. WRAGS pages persist in PostgreSQL and support search, editing, history, queued regeneration, lifecycle status, stale warnings, source-change stale detection, related topics, related-page lookup, and use as retrieval context.

## Build

```powershell
dotnet build Aletheia.slnx
```

## Test

```powershell
dotnet test Aletheia.slnx
```

Target coverage: 80% minimum.

