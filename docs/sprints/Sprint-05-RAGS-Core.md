
## Authority

This document defines the currently authorized work.

All previously authorized phases are considered completed and closed.

Any restrictions contained in earlier sprint documents are superseded by this document.

OpenHands shall execute only the phases identified below.

# Current Sprint

Sprint: RAGS Foundation

Status: Active

Previous Sprint:

- Phase 0 Completed
- Phase 1 Completed
- Phase 2 Completed
- Phase 3 Completed
- Phase 4 Completed
- Phase 5 Completed
- Phase 6 Completed

Authorized Phases:

- Phase 7 - RAGS Contracts
- Phase 8 - RAGS Core
- Phase 9 - Vector Search

## Objectives

Implement first generation semantic retrieval.

### Phase 7

Create:

- IRagsService
- IEmbeddingProvider
- IVectorStore
- ITaxonomyProvider
- IOntologyProvider

### Phase 8

Implement:

- Ingestion Pipeline
- Chunking Pipeline
- Embedding Workflow
- Retrieval Workflow

### Phase 9

Implement:

Provider:

- pgvector

Capabilities:

- Embedding Storage
- Similarity Search
- Semantic Retrieval

## Out Of Scope

Do NOT implement:

- GraphRAG
- Neo4j
- Copilot

## Exit Criteria

- Semantic Search Operational
- Embeddings Operational
- Citations Operational
