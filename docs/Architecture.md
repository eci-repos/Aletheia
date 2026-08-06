# Architecture

## Overview

Aletheia is an AI-native knowledge and document management platform built on .NET 10. It follows Clean Architecture, Hexagonal Architecture, and Domain-Driven Design principles. The system is organized into vertical slices (modules) for Repository, Knowledge Graph, and RAGS (Retrieval Augmented Generation System), with a shared Foundation layer and Blazor Web UI.

For a presentation-ready walkthrough of CLI operation, WebAssembly, API, Repository, RAGS, Graph, GraphRAG, LazyGraphRAG, and the end-to-end document ingestion to Copilot chat-completion flow, see `docs/Technical-Presentation-Guide.md`.

## Layered Model

Dependencies point inward toward the domain core. Each module follows the same layering pattern.

```
Client/UI (Blazor)
  ↓
API (REST Controllers)
  ↓
Abstractions (Interfaces, DTOs, Models)
  ↓
Application (Use Cases / Orchestrators)
  ↓
Domain (Business Logic Interfaces)
  ↓
Infrastructure (Adapters: PostgreSQL, MinIO, Neo4j, pgvector)
  ↓
Contracts (Cross-cutting abstractions)
  ↓
Foundation (Domain primitives, validation, context, exceptions)
```

## Modules

### Repository (Phases 2–3)

- **Repository.Abstractions**: Contracts (interfaces, DTOs) for file operations.
- **Repository.Domain**: Business workflows for upload, download, search, metadata, versioning.
- **Repository.Application**: Service orchestrator implementing use cases.
- **Repository.Infrastructure.PostgreSQL**: Relational metadata and document indexing.
- **Repository.Infrastructure.MinIO**: Object storage for file payloads.
- **Repository.API**: REST endpoints (Files, Versions, Metadata, Search, Collaboration, Governance).

### Knowledge Graph (Phases 5–6)

- **KnowledgeGraph.Abstractions**: Graph models (`GraphNode`, `GraphEdge`, `GraphPath`) and `IGraphService`.
- **KnowledgeGraph.Application**: Domain events and graph mutation orchestration.
- **KnowledgeGraph.Infrastructure.Neo4j**: Cypher-based graph persistence via Neo4j.

### RAGS — Retrieval Augmented Generation System (Phases 11–12, 15–16, 21)

- **RAGS.Abstractions**: Interfaces for `IRagsService`, `ICopilotService`, `IGraphRagService`, `ILazyGraphRagService`, `IVectorStore`, `IEmbeddingProvider`, models for `Chunk`, `RetrievalRequest`, chat, and ontology.
- **RAGS.Application**: Ingestion, retrieval, summarization, explanation, chat use cases, GraphRAG summary retrieval, global search, and LazyGraphRAG traversal.
- **RAGS.Infrastructure.PgVector**: Vector database adapter using PostgreSQL `pgvector`.
- **RAGS.Infrastructure.PostgreSQL**: Supporting relational stores for RAG sessions.
- **API Controllers**: `RagsController`, `CopilotController`, `GraphRagController`, `LazyGraphRagController`, `OntologyController`, `TaxonomyController`.

#### GraphRAG v2 Intelligence

RAGS v2 adds an index-heavy GraphRAG layer that moves commonly reused reasoning artifacts into the graph index:

- Per-chunk entity and relationship extraction.
- Typed `Entity`, `Source`, and `Community` nodes in Neo4j.
- Typed relationship edges using `GraphEdge.RelationshipType`.
- Entity-to-source `found_in` edges.
- Document, entity, relationship, community, and global summaries.
- Hierarchical community metadata.
- Summary-based retrieval before raw chunk fallback.
- Global search over top-level community summaries.
- Structured context assembly through `IGraphContextBuilder`.

For browser upload and queued GraphRAG ingestion, Phase 21 now uses a faster searchable-first path: RAGS chunks and embeddings are created, lightweight source/chunk graph seed nodes are persisted, and expensive entity/relationship/summary enrichment is deferred to bounded query-time lazy enrichment for relevant chunks. Lazy entity and relationship discoveries are written back through `ILazyEnrichmentKnowledgeSink` so PostgreSQL Taxonomy/Ontology explorers reflect what query-time enrichment has learned.

#### LazyGraphRAG v2 Optimization

LazyGraphRAG follows a different cost model:

- Ingestion updates chunks and corpus text statistics without LLM entity extraction.
- Query-time discovery uses TF-IDF/BM25-style candidate selection.
- Traversal uses a budgeted best-first search instead of blind BFS.
- `IGraphTraversalBudget` limits LLM calls, depth, nodes, relationships, token budget, and execution time.
- `ISubgraphPruningService` removes low-relevance nodes and relationships before final ranking.

Copilot chat responses include operational telemetry on the assistant message: elapsed seconds, estimated prompt/completion tokens, estimated token throughput, retrieved context count, citation count, retrieval scores, and a retrieval-based alignment confidence estimate. When a plan-based execution completes, the response also includes a plan-versus-actual estimate comparison summary.

The conversational planning system added in Sprints 22.1–22.7 is documented in `docs/Chat-Planning-Architecture-Report.md` and `docs/Copilot-Progress-API-Documentation.md`.

#### Search Center

`Aletheia.Web` exposes Search Center at `/search` as the primary human-facing retrieval workbench. The supported product surface exposes Semantic/Vector RAG as the primary user path against the same Repository-backed knowledge estate:

- Semantic mode calls standard RAGS retrieval over chunks and embeddings and is always visible.
- WRAGS, GraphRAG, and LazyGraphRAG are internal operator modes, hidden from end users unless FeatureFlags:ShowInternalSearch is enabled (default false). When enabled, they are visible in Search Center and the Wiki; when hidden, their API endpoints return HTTP 404. Copilot still uses graph-backed retrieval internally for broad/global corpus prompts, while scoped document prompts continue to prefer Semantic RAGS evidence.
- Direct content ingestion from the page queues background jobs for the visible RAG/WRAGS modes.
- Search results show rank, score, retrieval strategy, citations, chunk/source details, and technical API errors when failures occur.

#### WRAGS Wiki

WRAGS means Wiki Retrieval Augmented Generation System. It is Aletheia's durable LLM Wiki surface over RAGS:

- `/api/wiki` exposes search, recent pages, page lookup, regeneration, queued regeneration, retrieval-as-context, related-page lookup, page history, page edits, and lifecycle status updates.
- PostgreSQL stores generated and edited wiki pages with topic, title, body/summary, source IDs, citations, generation mode, version, lifecycle status, review metadata, related topics, score, rank, retrieval strategy, source/chunk metadata, timestamps, and prior revisions in `wiki_page_history`.
- WRAGS mode searches saved pages first and uses Semantic/Vector RAG as the supported retrieval fallback.
- Users can also force Semantic mode from the Wiki UI.
- The current slice persists generated snapshots, supports `Generated`/`Reviewed`/`Approved`/`NeedsReview`/`Stale` page state, stale warnings, related topics, related pages, editable page bodies, version history, source-change stale detection from Repository metadata, queued regeneration, and use of saved WRAGS pages in Search Center/Copilot retrieval context.

## Foundation (Phases 0–1)

- Domain core: `Entity`, `AggregateRoot`, `ValueObject`, `DomainEvent`
- Shared types: `Result<T>`, `PagedResult<T>`
- Validation: `ValidationResult`, `ValidationException`
- Context: `CorrelationContext`, `SecurityContext`, `TenantContext`
- Audit: `AuditInfo`, `AuditActor`
- Exceptions: `DomainException`, `SecurityException`

## Dependency Rules

- Domain and Foundation projects must not reference infrastructure implementations.
- All cross-module communication uses abstractions (interfaces in `.Abstractions` projects).
- No speculative implementations outside the current sprint scope.

## External Dependencies

| Service        | Technology          | Purpose                          |
|----------------|---------------------|----------------------------------|
| Primary DB     | PostgreSQL + pgvector | Relational data & vector search |
| Graph DB       | Neo4j               | Knowledge graph, typed graph entities, relationships, communities, summaries |
| Object Store   | MinIO               | File blob storage                |
| Web Framework  | ASP.NET Core 10     | REST API & Blazor WebAssembly    |

## API Surface

The `Repository.API` exposes 13 controllers covering document management, RAG operations, knowledge graph interaction, and collaborative features. See `src/Repository.API/Controllers/` for the full list. The conversational planning API surface is documented in `docs/Copilot-Progress-API-Documentation.md`.

## End-to-End Knowledge Flow

At runtime, document upload and Copilot chat are connected by a source-attributable knowledge flow:

1. `Aletheia.Web` uploads a document through `POST /api/files/upload`.
2. `Repository.API` stores the source artifact through Repository use cases.
3. MinIO persists the file payload and PostgreSQL persists file metadata.
4. The API queues a background ingestion job and returns an `IngestionJobId`.
5. The worker extracts supported text, calls `IRagsService.IngestAsync`, and stores chunks plus embeddings in pgvector.
6. The worker indexes taxonomy hints and lightweight graph seed nodes for the source and chunks.
7. GraphRAG enriches relevant chunks lazily during retrieval when stored summaries are absent, then syncs discovered entities and relationships into Taxonomy/Ontology.
8. LazyGraphRAG records low-cost corpus statistics for query-time candidate discovery.
9. `Aletheia.Web` polls `/api/jobs` and renders stage, heartbeat, failure details, and approximate progress in the Activity panel.
10. Copilot resolves user document references from registered metadata and aliases.
11. Copilot retrieves source-filtered RAGS chunks, GraphRAG summaries/lazy-enriched context, or LazyGraphRAG pruned context as needed, then sends an augmented prompt to chat completion.
12. Responses return to the WebAssembly app with citations, optional output formatting, and chat-completion stats.

This keeps Repository as the system of record and RAGS as the retrieval-ready semantic memory for ingested documents.

## Deployment Validation Snapshot

The Docker Compose topology has been validated with:

- `aletheia-api` on `http://localhost:8080`
- `aletheia-web` on `http://localhost:8081`
- PostgreSQL, MinIO, and Neo4j healthy
- `/health/live` returning HTTP 200
- `/health/ready` returning HTTP 200
- Authenticated Search Center Semantic, GraphRAG, and LazyGraphRAG retrieval through the Web UI
- Search Center GraphRAG retrieval returning summary-based results such as `summary-entity`
- LazyGraphRAG retrieval honoring traversal budgets without failing when optional enrichment reaches a configured limit
- Background ingestion job status visible through `/api/jobs` and the Web Activity panel

Operationally important runtime fixes are part of the current codebase:

- API container includes `libgssapi-krb5-2` for Npgsql/GSSAPI native dependency resolution.
- `RAGS.Infrastructure.Graph` uses `Neo4j.Driver` `6.2.1` to align with the Neo4j infrastructure provider.
\n## Taxonomy Normalization (Sprint 50)\n\n- **ConfigurableTermNormalizer** (in `RAGS.Application`) loads stop‑words from `appsettings.json` under the `Taxonomy` section and phrase exemptions from `docs/doc-templates/*.md`.\n- The normalizer is registered as a singleton via `builder.Services.AddSingleton<ITermNormalizer, ConfigurableTermNormalizer>();` (see `src/Repository.API/Program.cs`).\n- `UploadedContentKnowledgeIndexer` now extracts topics through a new `ExtractTopics` method that uses the term normalizer to filter stop‑words and preserve exempt phrases.\n- A one‑time migration (`TaxonomyCleanMigration` in `src/Repository.Infrastructure.PostgreSQL/Migrations`) removes existing stop‑word tags from the `taxonomy_tags` table and renames any tag that differs only by case or stop‑word removal, ensuring a clean taxonomy baseline.\n- After migration, taxonomy and ontology pipelines operate only on meaningful terms, improving graph quality and search relevance.\n

## Canonical Document Templates (Sprint 53/54)

- Templates in `docs/doc-templates/*.md` define the **canonical format** for a document kind: the ordered sections (with explanations) every document of that kind must cover (e.g., `3.0 - RFP Analysis`).
- A document's **file name carries the clue** to its canonical (e.g., `CMP 2026 - 3. RFP Analysis.docx` matches canonical `3.0 - RFP Analysis`).
- `DocumentTemplateRegistry` (singleton, `RAGS.Application`) loads the templates at startup, matches documents by token overlap (`TryGetCanonicalName`), and exposes the ordered sections (`TryGetSections`).
- **Ingestion gate**: `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` requires a canonical template match; ingestion stops with a clear error when no canonical is found. The gate covers upload ingestion jobs, hydration, and plugin-triggered ingestion.
- Summaries for template documents open with the document's nature/purpose (deterministic first-chunk injection) and follow the template's section order, each section grounded by its own retrieved evidence (Sprint 53).
- Adding a new document kind requires adding a template under `docs/doc-templates` **before** documents of that kind can be ingested.

## Duplicate Upload Detection and Document Update Flow (Sprint 56)

### Content fingerprinting
Every upload is hashed server-side (SHA-256 of the raw bytes, computed over the temporary upload file in `FilesController`) **before** any storage write. The hash is carried on `UploadRequest.ContentHash` -> `FileMetadata.ContentHash` and persisted in the `file_metadata.content_hash` column (`init.sql` + idempotent migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-05-file-metadata-content-hash.sql`). A b-tree index (`idx_file_metadata_content_hash`) supports duplicate lookups.

### Duplicate trap
`IDuplicateDetectionService` (RAGS-adjacent application service in `Aletheia.Repository.Application`, registered as a singleton) resolves the most recent row with the same content hash via `IMetadataRepository.FindByContentHashAsync`. When a new upload's hash matches an existing row, the API returns **HTTP 409 Conflict** with a structured payload (`duplicate`, `noChange`, `message`, `existingFileId`, `existingFileName`, `existingUploadedAt`, `existingVersion`) and stores/ingests nothing. The Web Upload page renders a "Duplicate - already exists" badge and an Activity warning and skips ingestion tracking. The same hash posted to the same document via the update path is reported as a no-change conflict.

### Document update (new version of an existing document)
`POST /api/files/upload` accepts an optional `existingFileId`. When present:
1. The current (unversioned) metadata row is resolved; if missing, the API returns 400.
2. If the new content hash equals the current row's hash, the upload is trapped as a no-change conflict (no new version).
3. Otherwise `IVersioningUseCase.CreateVersionAsync` snapshots the current state into a named version row, then the new blob is stored under the same `fileId` (MinIO object name is `fileId/fileName`; the blob is replaced) and the unversioned metadata row is upserted with the new content hash, size, and content type.
4. An ingestion job is enqueued with the **same sourceId**. `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` now applies replace semantics: before ingestion it clears prior knowledge-index rows (`UploadedContentKnowledgeIndexer.DeleteSourceAsync`) and graph nodes (`IGraphProvider.DeleteSourceAsync`, implemented for Neo4j as a DETACH DELETE on `n.sourceId`), and `RagsService.IngestAsync` already replaces embeddings. The automatic document-brief trigger regenerates the Wiki brief for the updated content.
5. Version history (`GET /api/versions`) lists the prior versions; versioned downloads share the single blob per `fileId` (metadata-level versioning - a documented limitation).

### Existing duplicate cleanup (admin)
`GET /api/files/duplicates` (role-gated to Administrator) returns every `file_metadata` row whose `content_hash` is shared by more than one row, newest first, so operators can review and manually remove duplicate artifacts with the existing DELETE flow. No automatic deletion.

### API contract notes
- 409 payload is machine-readable; the Web client (`RepositoryApiClient.UploadAsync`) maps it to `UploadClientResult` (`IsDuplicate`, `NoChange`, `DuplicateMessage`, `ExistingFileId`, `ExistingFileName`).
- Existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints and the `/api/jobs` snapshot contract are unchanged.

### Retrieval Pipeline (Sprint 58: Knowledge Theme Filtering)

The Copilot retrieval pipeline now includes a session-level **knowledge theme stage**:

1. **Session scope**: the user picks themes at session creation (or edits them mid-session). The selection rides `ChatSession.ThemeFilter` -> `ChatPayload` -> `ChatRequestOptions`/`ChatPlanRecord`.
2. **Theme -> source resolution**: `KnowledgeThemeService` (singleton) resolves the theme set to registered source ids from `file_metadata` (persisted `template_name`/`theme`, with a read-time registry fallback for pre-Sprint-58 rows).
3. **Enforcement**: `RetrievalRequest.SourceIds` carries the source set into `RagsService.RetrieveAsync`, which uses `PgVectorStore.SearchBySourcesAsync` for the vector path and a source-set predicate in the keyword fallback (`source_id = ANY(...)`); stores without set support post-filter results. The execution engine intersects the theme set with Sprint 51's single-document scope (a named document outside the themes returns no results) and filters repository-tool results before synthesis.
4. **Catalog**: `GET /api/knowledge/themes` returns themes with registered-document counts for the UI picker (themes with zero documents are included).