# Roadmap

## Completed Phases

### Phase 0 – Foundation Setup
- Shared kernel (`Aletheia.Foundation`): `Entity`, `AggregateRoot`, `ValueObject`, `DomainEvent`, `Result<T>`, `PagedResult<T>`
- Validation, context, audit, and exception primitives

### Phase 1 – Foundation Testing
- Unit tests for all Foundation components
- Coverage target: 80% minimum

### Phase 2 – Repository Contracts
- `Repository.Abstractions`: service and provider interfaces, DTOs for upload, download, and search

### Phase 3 – Repository Core
- `Repository.Domain` and `Repository.Application`: use cases for upload, download, search, metadata, versioning
- Service orchestrator and unit tests

### Phase 4 – Repository Integration
- `Repository.Infrastructure.PostgreSQL` and `Repository.Infrastructure.MinIO`
- `Repository.API` with Files, Versions, Metadata, and Search controllers
- Integration tests with PostgreSQL and MinIO

### Phase 5 – Knowledge Graph Models
- `KnowledgeGraph.Abstractions`: `GraphNode`, `GraphEdge`, `GraphPath`, `IGraphService`

### Phase 6 – Knowledge Graph Infrastructure
- `KnowledgeGraph.Infrastructure.Neo4j`: Cypher-based graph persistence
- `KnowledgeGraphController` and graph sync integration

### Phase 7 – UI Design
- Blazor server application scaffolding (`Aletheia.Web`)
- Repository homepage and shared layout components

### Phase 8 – Repository UI Pages
- Blazor pages for document upload, download, search, and version history

### Phase 9 – Collaboration & Governance
- Collaboration and Governance controllers
- Corresponding Blazor UI pages

### Phase 10 – RAGS Setup
- `RAGS.Abstractions`: `IRagsService`, `IVectorStore`, `IEmbeddingProvider`, `Chunk`, `RetrievalRequest`
- `RAGS.Application`: ingestion and retrieval use cases
- `RAGS.Infrastructure.PgVector`: vector search with `pgvector`

### Phase 11 – RAGS Features
- Summarization, explanation, and response synthesis
- `RagsController` and Blazor UI integration

### Phase 12 – Copilot Integration
- `ICopilotService`, chat models, `CopilotController`
- Blazor chat UI

### Phase 13 – AI Core
- LLM abstraction, prompt management, multimodal embedding support

### Phase 14 – AI Infrastructure
- LLM provider infrastructure, model configuration, prompt versioning

### Phase 15 – Agentic Search & GraphRAG
- `IGraphRagService`, agentic graph search, `GraphRagController`
- GraphRAG Blazor UI

### Phase 16 – LazyGraphRAG
- `ILazyGraphRagService`, lazy evaluation graph RAG, `LazyGraphRagController`
- LazyGraphRAG Blazor UI

## Current Sprint

### Phase 21 - RAGS v2 Intelligence and Background Operations
- GraphRAG hierarchical community detection
- Per-chunk entity and relationship extraction
- Typed `Entity`, `Source`, and `Community` graph persistence
- Entity, relationship, document, community, and global summaries
- Summary-based GraphRAG retrieval
- Map-reduce global search over top-level community summaries
- Structured prompt context through `IGraphContextBuilder`
- LazyGraphRAG TF-IDF/BM25 corpus statistics at ingestion time
- LazyGraphRAG query-time candidate discovery
- Budgeted best-first traversal and subgraph pruning
- Docker/UI validation for authenticated GraphRAG Search Center flows
- Background ingestion jobs for upload and lightweight graph seed indexing
- Query-time lazy GraphRAG enrichment for relevant chunks
- Copilot chat completion stats with elapsed time, estimated token throughput, context/citation counts, and heuristic confidence
- Job progress APIs with stage, heartbeat, elapsed time, failures, and approximate completion
- UI feedback panel for active and completed long-running jobs
