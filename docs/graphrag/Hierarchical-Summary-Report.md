# Hierarchical Summary Report

**Sprint-14 | GraphRAG Maturity**

---

## Purpose

This document describes the Hierarchical Summary capability of the Aletheia GraphRAG system. Hierarchical summaries provide multi-level abstractions of the knowledge graph, spanning from individual documents through entities, communities, and knowledge areas to a global graph overview.

---

## Architecture

### Components

| Component | Implementation | Location |
|-----------|---------------|----------|
| Interface | `IHierarchicalSummaryService` | `src/RAGS.Abstractions/Interfaces/IHierarchicalSummaryService.cs` |
| Implementation | `HierarchicalSummaryService` | `src/RAGS.Application/GraphIntelligence/HierarchicalSummaryService.cs` |
| Interface | `IGraphSummaryService` | `src/RAGS.Abstractions/Interfaces/IGraphSummaryService.cs` |
| Model | `GraphNode` | `src/KnowledgeGraph.Abstractions/Models/GraphNode.cs` |
| Model | `GraphCommunity` | `src/KnowledgeGraph.Abstractions/Models/GraphCommunity.cs` |

### Hierarchy Levels

| Level | Method | Description |
|-------|--------|-------------|
| Document | `SummarizeDocumentAsync` | Individual source document summary |
| Entity | `SummarizeEntityAsync` | Delegates to `IGraphSummaryService` |
| Community | `SummarizeCommunityAsync` | Delegates to `IGraphSummaryService` |
| Knowledge Area | `SummarizeKnowledgeAreaAsync` | Subgraph-level summary (2-hop neighborhood) |
| Global | `SummarizeGlobalAsync` | Entire graph overview |

---

## Hierarchy Model

```
Global Graph
  └─ Knowledge Area A
       └─ Community 1
            ├─ Entity A
            ├─ Entity B
            └─ Entity C
       └─ Community 2
            ├─ Entity D
            └─ Entity E
  └─ Knowledge Area B
       └─ Community 3
            ├─ Entity F
            └─ Entity G
       └─ Document X
            └─ Entity H
```

---

## Data Flow

### Document Summary
```
Caller --> HierarchicalSummaryService.SummarizeDocumentAsync
  --> IGraphProvider.GetNodeAsync(documentId)
  --> IGraphProvider.GetNeighborsAsync(documentId)
  --> Semantic Kernel (LLM prompt with document preview + entity neighbors)
  --> Result<string>
```

### Knowledge Area Summary
```
Caller --> HierarchicalSummaryService.SummarizeKnowledgeAreaAsync
  --> IGraphProvider.GetNodeAsync(areaId)
  --> IGraphProvider.GetSubgraphAsync(areaId, depth: 2)
  --> Semantic Kernel (LLM prompt with area label + related entities)
  --> Result<string>
```

### Global Summary
```
Caller --> HierarchicalSummaryService.SummarizeGlobalAsync
  --> _graphSummary.SummarizeGlobalAsync (delegated)
  --> IGraphProvider.GetNodesAsync() + GetEdgesAsync()
  --> Semantic Kernel (LLM prompt with graph statistics + type distributions)
  --> Result<string>
```

---

## Prompt Templates

### Document Summary
```
Document: {Label}
Type: {Type}
Properties:
  - {Key}: {Value}

Content Preview:
{first 2000 characters of content}

Extracted Entities ({Count}):
  - {Label} ({Type})

Generate a concise summary of this document, highlighting its main topics and key entities.
```

### Knowledge Area Summary
```
Knowledge Area: {Label}
Type: {Type}
Properties:
  - {Key}: {Value}

Related Entities ({Count}):
  - {Label} ({Type})

Generate a concise summary of this knowledge area, describing its scope and related concepts.
```

### Global Summary
```
Graph Overview
Total Nodes: {Count}
Total Relationships: {Count}

Node Types:
  - {Type}: {Count}

Relationship Types:
  - {RelationshipType}: {Count}

Generate a high-level summary of this knowledge graph. 
Describe the main domains, key entity types, and overall structure.
```

---

## Integration Points

### Active Consumers

- `GraphRagService.RetrieveAsync` — calls `_hierarchicalSummary.SummarizeEntityAsync()` and `_hierarchicalSummary.SummarizeCommunityAsync()` per resolved entity/community
- `LazyGraphRagService.RetrieveAsync` — same dual invocation pattern
- `GlobalGraphSearchService.SearchAsync` — retrieves both flat (`IGraphSummaryService`) and hierarchical (`IHierarchicalSummaryService`) community summaries for richer synthesis
- `GraphContextBuilder.BuildContextAsync` — includes hierarchical summaries when `Summaries` source flag is set

### Dependency Graph

```
HierarchicalSummaryService
  --> IGraphProvider (Neo4jGraphProvider)
  --> IGraphSummaryService (GraphSummaryService)
      --> ICommunityDetectionService (CommunityDetectionService)
      --> Kernel (Semantic Kernel / Ollama)
  --> Kernel (Semantic Kernel / Ollama)
```

---

## Implementation Details

### Document Summary
- Fetches the document node from Neo4j
- Excludes raw `content` property from metadata (too large)
- Includes a truncated content preview (2000 characters max)
- Lists connected entity neighbors (up to 20)

### Entity Summary
- Delegates directly to `IGraphSummaryService.SummarizeEntityAsync`
- Avoids duplication; ensures single source of truth for entity-level summaries

### Community Summary
- Delegates directly to `IGraphSummaryService.SummarizeCommunityAsync`
- Shares community resolution and member-fetching logic

### Knowledge Area Summary
- Fetches the knowledge area node
- Retrieves a 2-hop subgraph (`GetSubgraphAsync(areaId, depth: 2)`)
- Lists related entities (up to 20), excluding the root node
- Provides scope and concept coverage summary

### Global Summary
- Delegates to `IGraphSummaryService.SummarizeGlobalAsync`
- Operates on all nodes and edges in the graph
- Includes type-grouped statistics for nodes and relationships

---

## Persistence

Hierarchical summaries are computed on demand. No persistent caching layer exists for hierarchical summaries. The underlying graph structure is persisted in Neo4j.

---

## Validation

- Build: ✅ Succeeds (0 errors, 0 warnings)
- Unit tests: ✅ 33 RAGS tests pass
- Runtime: Verified via `GraphRagServiceTests` and `LazyGraphRagServiceTests` mock injections

---

## Status

- ✅ Operational
- ✅ 5-level hierarchy implemented (Document → Entity → Community → Knowledge Area → Global)
- ✅ Delegation pattern ensures no duplication with `IGraphSummaryService`
- ✅ Integrated into retrieval pipelines and global search

---

## Future Enhancements

- Hierarchical summary caching at each level
- Differential updates when subgraphs change
- Multi-document knowledge area synthesis
- Temporal hierarchy (versioned summaries over time)

