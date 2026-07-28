File: 01-Aletheia-WorkPlan.md
# Aletheia Work Plan

## Phase 0 - Project Initialization

Deliver:

- Solution structure
- Repository structure
- Documentation baseline
- CI/CD baseline

Exit Criteria:

- Build succeeds
- Repository organized
- Documentation available

---

## Phase 1 - Foundation Platform

Projects:

- Aletheia.Foundation
- Aletheia.Contracts

Deliver:

- Entity
- AggregateRoot
- ValueObject
- DomainEvent
- Result<T>
- Validation
- Audit Models
- Tenant Models
- Exceptions

Exit Criteria:

- Unit tests pass
- Foundation documented

---

## Phase 2 - Repository Contracts

Deliver:

- IRepositoryService
- IStorageProvider
- IMetadataRepository
- IVersioningService
- IAuditService
- DTO Contracts

Exit Criteria:

- Contracts complete

---

## Phase 3 - Repository Core

Deliver:

- Upload Use Cases
- Download Use Cases
- Search Use Cases
- Metadata Use Cases
- Versioning Use Cases

Exit Criteria:

- Business layer fully tested

---

## Phase 4 - Repository Infrastructure

Providers:

- PostgreSQL
- MinIO

Deliver:

- Metadata Storage
- File Storage
- Versioning

Exit Criteria:

- Upload and Download operational

---

## Phase 5 - Repository API

Deliver:

- REST APIs
- Swagger
- Integration Tests

Exit Criteria:

- API operational

---

## Phase 6 - Initial HXP

Deliver:

- Authentication
- Dashboard
- Upload
- Download
- Browse
- Metadata Editing

Exit Criteria:

- User manages repository content

---

## Phase 7 - RAGS Contracts

Deliver:

- IRagsService
- IEmbeddingProvider
- IVectorStore
- ITaxonomyProvider
- IOntologyProvider

Exit Criteria:

- Contracts complete

---

## Phase 8 - RAGS Core

Deliver:

- Ingestion
- Chunking
- Embedding Workflow
- Retrieval Workflow

Exit Criteria:

- Semantic retrieval operational

---

## Phase 9 - Vector Search

Provider:

- pgvector

Deliver:

- Embedding Storage
- Similarity Search

Exit Criteria:

- Semantic search operational

---

## Phase 10 - HXP Semantic Search

Deliver:

- Search Center
- Semantic Search UI
- Citation Experience

Exit Criteria:

- User semantic search operational

---

## Phase 11 - Taxonomy Platform

Deliver:

- Categories
- Taxonomies
- Taxonomy Search

Exit Criteria:

- Taxonomy operational

---

## Phase 12 - Ontology Platform

Deliver:

- Entities
- Relationships
- Ontology Models

Exit Criteria:

- Ontology operational

---

## Phase 13 - Knowledge Graph

Provider:

- Neo4j

Deliver:

- Graph Storage
- Graph Queries
- Traversal

Exit Criteria:

- Knowledge graph operational

---

## Phase 14 - Graph Visualization

Deliver:

- Graph Viewer
- Relationship Explorer

Exit Criteria:

- Graph exploration operational

---

## Phase 15 - GraphRAG

Deliver:

- Entity Resolution
- Graph Retrieval
- Context Expansion

Exit Criteria:

- GraphRAG operational

---

## Phase 16 - LazyGraphRAG

Deliver:

- Incremental Graph Construction
- Retrieval-Time Expansion

Exit Criteria:

- LazyGraphRAG operational

---

## Phase 17 - AI Copilot

Deliver:

- Chat Experience
- Summaries
- Explanations
- Knowledge Discovery

Exit Criteria:

- AI assistant operational

---

## Phase 18 - Collaboration

Deliver:

- Comments
- Notes
- Collections
- Shared Workspaces

Exit Criteria:

- Collaboration operational

---

## Phase 19 - Governance

Deliver:

- RBAC
- Audit
- Retention
- Compliance Hooks

Exit Criteria:

- Governance operational

---

## Phase 20 - Production Hardening

Deliver:

- Load Testing
- Security Testing
- Final Documentation
- Deployment Guides

Exit Criteria:

- Release Candidate RC1

---

## Phase 21 - RAGS v2 Intelligence and Background Operations

Deliver:

- Hierarchical GraphRAG community detection
- Index-time entity, relationship, and community summaries
- Chunk-level entity and relationship extraction
- Typed entity nodes and relationship edges in Neo4j
- Summary-based query-time retrieval
- Map-reduce global search over top-level community summaries
- Structured graph context assembly for synthesis
- LazyGraphRAG minimal-cost ingestion using TF-IDF/BM25 statistics only
- Budgeted best-first LazyGraphRAG traversal
- LazyGraphRAG subgraph pruning before answer generation
- Background jobs for long-running document ingestion
- Lightweight upload graph seed indexing that avoids document-wide LLM graph enrichment by default
- Bounded query-time GraphRAG enrichment for relevant chunks
- Copilot chat completion telemetry for elapsed time, estimated token throughput, context/citation counts, and heuristic confidence
- Job status APIs with stage, heartbeat, elapsed time, failure details, and approximate progress
- UI feedback panel for active, completed, and failed long-running jobs
- Periodic progress updates every few minutes and at important stage transitions

Exit Criteria:

- GraphRAG retrieval can use pre-computed graph intelligence
- Typed graph persistence remains compatible with existing abstractions
- Broad corpus-level questions can be answered from community summaries
- LazyGraphRAG ingestion avoids LLM entity and relationship extraction
- LazyGraphRAG traversal honors LLM, node, edge, and pruning budgets
- Upload ingestion returns searchable chunks and graph seed nodes without full summary generation
- GraphRAG can lazily enrich relevant chunks during retrieval and reuse marked-enriched chunks
- Copilot responses expose useful completion stats in the API/UI
- Long-running ingestion returns a job identifier quickly instead of relying on one browser request
- Operators can see whether ingestion is alive, what stage is active, and roughly what remains
- Unit tests pass
