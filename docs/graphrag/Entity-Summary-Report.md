# Entity Summary Report

**Sprint-14 | GraphRAG Maturity**

---

## Purpose

This document describes the Entity Summary capability of the Aletheia GraphRAG system. Entity summaries provide concise, LLM-generated descriptions of individual entities in the knowledge graph based on their properties, relationships, and neighborhood structure.

---

## Architecture

### Components

| Component | Implementation | Location |
|-----------|---------------|----------|
| Interface | `IGraphSummaryService` | `src/RAGS.Abstractions/Interfaces/IGraphSummaryService.cs` |
| Implementation | `GraphSummaryService` | `src/RAGS.Application/GraphIntelligence/GraphSummaryService.cs` |
| Model | `GraphNode` | `src/KnowledgeGraph.Abstractions/Models/GraphNode.cs` |
| Provider | `Neo4jGraphProvider` | `src/RAGS.Infrastructure.Graph/Providers/Neo4jGraphProvider.cs` |

### Entity Summary Method Signature

```csharp
Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default);
```

---

## Data Flow

```
+-----------+     +-----------------+     +-------------------+     +-------------+
|  Caller   | --> | GraphSummarySvc | --> | IGraphProvider    | --> |  Neo4j DB   |
| (any svc) |     |                 |     | GetNodeAsync      |     |             |
+-----------+     |                 |     | GetNeighborsAsync |     +-------------+
                  |                 |     +-------------------+
                  |                 |
                  |                 |  +-------------------------------+
                  |                 +->| Semantic Kernel (Ollama/SK)   |
                  |                    | IChatCompletionService        |
                  |                    +-------------------------------+
                  |                                 |
                  |                                 v
                  |<---------------------------  Summary String
                  |
                  v
          Result<string>.Success(summary)
```

### Pipeline Steps

1. **Retrieve Entity Node** — `IGraphProvider.GetNodeAsync(entityId)` fetches the node with its label, type, and properties.
2. **Retrieve Neighbors** — `IGraphProvider.GetNeighborsAsync(entityId)` fetches connected entities.
3. **Retrieve Edges** — All edges touching the entity are filtered to build a relationship list.
4. **Prompt Construction** — A structured prompt is built including:
   - Entity label and type
   - Properties (filtered for non-null values)
   - Connected entities (up to 20)
   - Relationships (up to 20)
5. **LLM Summarization** — Semantic Kernel `IChatCompletionService` generates a concise summary.
6. **Fallback** — If the LLM is unavailable, a structured text summary is returned without external calls.

---

## Prompt Template

```
Entity: {Label}
Type: {Type}
Properties:
  - {Key}: {Value}

Connected Entities ({Count}):
  - {Neighbor.Label} ({Neighbor.Type})

Relationships ({Count}):
  - {RelationshipType}: {SourceId} -> {TargetId}

Generate a concise, factual summary of this entity based on the information above. 
Include its type, key properties, and significant relationships.
```

---

## Integration Points

### Active Consumers

- `GraphRagService.RetrieveAsync` — calls `_graphSummary.SummarizeEntityAsync()` per resolved entity
- `LazyGraphRagService.RetrieveAsync` — calls `_graphSummary.SummarizeEntityAsync()` per resolved entity
- `GraphContextBuilder.BuildContextAsync` — includes entity summaries when `Summaries` source flag is set
- `HierarchicalSummaryService.SummarizeEntityAsync` — delegates to `_graphSummary.SummarizeEntityAsync()`

### Dependency Graph

```
GraphSummaryService
  --> IGraphProvider (Neo4jGraphProvider)
  --> ICommunityDetectionService (CommunityDetectionService)
  --> Kernel (Semantic Kernel / Ollama)
```

---

## Persistence

Entity summaries are **computed on demand** during retrieval. The summary string is returned as a `Result<string>` to the caller but is **not persisted as a separate node property** in the current implementation. Future hardening may cache summaries in the graph metadata.

The underlying entity data is stored in Neo4j with the following structure:

```cypher
(:Entity {id: $id, label: $label, type: $type, ...properties})
```

---

## Validation

- Build: ✅ Succeeds (0 errors, 0 warnings)
- Unit tests: ✅ 33 RAGS tests pass
- Runtime: Verified via `GraphRagServiceTests` mock infrastructure

---

## Status

- ✅ Operational
- ✅ Wired into both GraphRAG and LazyGraphRAG retrieval pipelines
- ✅ Integrated with Context Builder and Citation Builder

---

## Future Enhancements

- Persistent summary caching in Neo4j node properties
- Batch entity summarization during ingestion
- Confidence scoring per summary

