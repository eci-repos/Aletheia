# LazyGraphRAG Architecture Report

**Sprint-15 | LazyGraphRAG Maturity**

**Date:** 2026-07-21

---

## Overview

LazyGraphRAG is Aletheia's implementation of Microsoft LazyGraphRAG — a retrieval architecture that defers nearly all intelligence work to query time, eliminating indexing-time LLM costs.

---

## Core Principle

| Phase | Traditional GraphRAG | LazyGraphRAG |
|-------|---------------------|--------------|
| Ingestion | Entity extraction, relationship extraction, summary generation | Corpus statistics only (TF-IDF, BM25) |
| Retrieval | Graph lookup + expansion | Query-time entity discovery, relationship discovery, graph construction |
| Cost Model | High upfront indexing cost | Near-zero indexing cost; pay-per-query |

---

## Architecture Diagram

```
+----------------------------------------------------------------+
|                        INGESTION PIPELINE                       |
|                                                                 |
|  Document  -->  ChunkingPipeline  -->  RAGS Service (vectors)   |
|                      |                                          |
|                      v                                          |
|              CorpusDiscoveryIndex                               |
|                - TF-IDF metadata                                |
|                - BM25 metadata                                  |
|                - Text statistics                                |
|                - Co-occurrence index                            |
|                      |                                          |
|                      v                                          |
|              In-Memory Lazy Graph (heuristic only)              |
|                - No LLM calls                                   |
|                - No relationship extraction                     |
+----------------------------------------------------------------+
                              |
                              v
+----------------------------------------------------------------+
|                        RETRIEVAL PIPELINE                       |
|                                                                 |
|  Query  -->  Corpus Search (BM25/TF-IDF)  -->  Seed Sources     |
|                                                                 |
|  Query  -->  ILazyEntityDiscoveryService  -->  Entities         |
|              (Semantic Kernel / LLM)                            |
|                                                                 |
|  Query  -->  ILazyRelationshipDiscoveryService --> Relationships|
|              (Semantic Kernel / LLM)                            |
|                                                                 |
|  Build Temporary Graph (discovered + lazy-indexed nodes)       |
|                                                                 |
|  IGraphReasoningService --> LLM-Guided Traversal               |
|    - Edge Selection                                             |
|    - Node Selection                                             |
|    - Expansion Decisions                                        |
|    - Stop Conditions                                            |
|                                                                 |
|  IGraphTraversalBudget --> Cost Enforcement                    |
|    - MaxLLMCalls (5 default)                                    |
|    - MaxDepth (3 default)                                       |
|    - MaxNodes (50 default)                                      |
|    - MaxRelationships (100 default)                             |
|    - MaxTokenBudget (4000 default)                              |
|    - MaxExecutionTime (30s default)                             |
|                                                                 |
|  ISubgraphPruningService --> Remove Low-Relevance Nodes/Edges  |
|    - Relevance threshold scoring                                |
|    - Query-term overlap weighting                               |
|                                                                 |
|  Community Resolution + Summary Retrieval                       |
|                                                                 |
|  IGraphContextBuilder --> Optimized Context                     |
|                                                                 |
|  Semantic Retrieval + Expansion                                 |
|                                                                 |
|  ICitationPathService --> Citations                             |
|                                                                 |
|  Persistent Enrichment --> Save to Graph                        |
+----------------------------------------------------------------+
```

---

## Service Inventory

| Service | Interface | Implementation | Purpose |
|---------|-----------|---------------|---------|
| Corpus Discovery Index | `ICorpusDiscoveryIndex` | `CorpusDiscoveryIndex` | Lightweight statistical indexing |
| Lazy Entity Discovery | `ILazyEntityDiscoveryService` | `LazyEntityDiscoveryService` | Query-time entity extraction |
| Lazy Relationship Discovery | `ILazyRelationshipDiscoveryService` | `LazyRelationshipDiscoveryService` | Query-time relationship extraction |
| Graph Traversal Budget | `IGraphTraversalBudget` | `GraphTraversalBudget` | Resource limits enforcement |
| Subgraph Pruning | `ISubgraphPruningService` | `SubgraphPruningService` | Low-relevance node/edge removal |
| Lazy GraphRAG Service | `ILazyGraphRagService` | `LazyGraphRagService` | Orchestration layer |

---

## Component Details

### CorpusDiscoveryIndex

- **No LLM Calls**
- Computes TF-IDF and BM25 scores per document
- Tokenizes with stop-word removal
- Supports corpus-wide search by BM25 score
- Stores only `Dictionary<Guid, DocumentIndex>` in memory

### LazyEntityDiscoveryService

- Wraps `IEntityExtractionService` for query-time invocation
- `DiscoverAtQueryTimeAsync` → Semantic Kernel `IChatCompletionService`
- Falls back to simple keyword extraction if LLM unavailable
- `PersistAsync` saves entities to `IGraphProvider`

### LazyRelationshipDiscoveryService

- Accepts query + entity list
- Uses Semantic Kernel to identify semantic relationships
- Returns `ExtractedRelationship` objects
- Falls back to co-occurrence relationships if LLM unavailable
- `PersistAsync` saves edges to `IGraphProvider`

### GraphTraversalBudget

- Thread-safe via `Interlocked`
- Tracks: LLM calls, node visits, relationship traversals, tokens, elapsed time
- All traversal loops check `_budget.IsExceeded()` before continuing
- Reset per-retrieval via `_budget.Reset()`

### SubgraphPruningService

- Computes relevance scores per node:
  - Label exact match: +1.0
  - Label containment: +0.7
  - Query term overlap: +0.15 per term
  - Property overlap: +0.05 per term
- Filters edges to only connect retained nodes
- Default threshold: 0.25

---

## Data Flow (Query Lifecycle)

```
1. User Query
       |
       v
2. CorpusDiscoveryIndex.SearchCorpus(query)
       |--> Seed document IDs (BM25-ranked)
       |
       v
3. ILazyEntityDiscoveryService.DiscoverAtQueryTimeAsync(query)
       |--> List<ExtractedEntity>
       |
       v
4. ILazyRelationshipDiscoveryService.DiscoverAtQueryTimeAsync(query, entities)
       |--> List<ExtractedRelationship>
       |
       v
5. BuildTemporaryGraph(entities, relationships)
       |--> Merges discovered entities with lazy-indexed co-occurrence graph
       |
       v
6. IGraphReasoningService.SelectEntitiesAsync(query)
       |--> LLM-guided entity selection
       |
       v
7. Budget-limited neighbor expansion (depth <= MaxDepth)
       |--> IGraphProvider.GetNeighborsAsync()
       |
       v
8. ISubgraphPruningService.PruneNodesAsync(nodes, query)
       |--> Removes low-relevance nodes/edges
       |
       v
9. Community resolution + summary retrieval
       |
       v
10. IGraphContextBuilder.BuildContextAsync(query, ...)
       |
       v
11. Semantic retrieval + expansion (seed docs + entity labels)
       |
       v
12. Citation building
       |
       v
13. Persistent enrichment (save new entities/relationships)
       |
       v
14. Return Result<IReadOnlyList<SearchResult>>
```

---

## File Locations

| File | Path |
|------|------|
| ICorpusDiscoveryIndex | `src/RAGS.Abstractions/Interfaces/ICorpusDiscoveryIndex.cs` |
| CorpusDiscoveryIndex | `src/RAGS.Application/LazyGraphRAG/CorpusDiscoveryIndex.cs` |
| ILazyRelationshipDiscoveryService | `src/RAGS.Abstractions/Interfaces/ILazyRelationshipDiscoveryService.cs` |
| LazyRelationshipDiscoveryService | `src/RAGS.Application/LazyGraphRAG/LazyRelationshipDiscoveryService.cs` |
| IGraphTraversalBudget | `src/RAGS.Abstractions/Interfaces/IGraphTraversalBudget.cs` |
| GraphTraversalBudget | `src/RAGS.Application/LazyGraphRAG/GraphTraversalBudget.cs` |
| ISubgraphPruningService | `src/RAGS.Abstractions/Interfaces/ISubgraphPruningService.cs` |
| SubgraphPruningService | `src/RAGS.Application/LazyGraphRAG/SubgraphPruningService.cs` |
| LazyGraphRagService | `src/RAGS.Application/LazyGraphRAG/LazyGraphRagService.cs` |
| DI Registration | `src/Repository.API/Program.cs` |

---

## Validation

- Build: ✅ 0 errors, 0 warnings
- Tests: ✅ 167/167 passing
- No LLM calls during ingestion: ✅ Verified (IngestAsync only calls CorpusDiscoveryIndex)

---

*Report generated by OpenHands agent on behalf of the user.*
