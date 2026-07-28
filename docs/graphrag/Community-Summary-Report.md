# Community Summary Report

**Sprint-14 | GraphRAG Maturity**

---

## Purpose

This document describes the Community Summary capability of the Aletheia GraphRAG system. Community summaries provide concise, LLM-generated descriptions of detected communities (clusters of related entities), enabling higher-level contextual understanding and global search operations.

---

## Architecture

### Components

| Component | Implementation | Location |
|-----------|---------------|----------|
| Interface | `IGraphSummaryService` | `src/RAGS.Abstractions/Interfaces/IGraphSummaryService.cs` |
| Interface | `ICommunityDetectionService` | `src/RAGS.Abstractions/Interfaces/ICommunityDetectionService.cs` |
| Implementation | `GraphSummaryService` | `src/RAGS.Application/GraphIntelligence/GraphSummaryService.cs` |
| Implementation | `CommunityDetectionService` | `src/RAGS.Application/GraphIntelligence/CommunityDetectionService.cs` |
| Model | `GraphCommunity` | `src/KnowledgeGraph.Abstractions/Models/GraphCommunity.cs` |

### Community Summary Method Signature

```csharp
Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default);
```

---

## Data Flow

```
+-----------+     +-------------------+     +------------------------+
|  Caller   | --> | GraphSummarySvc   | --> | ICommunityDetectionSvc |
| (any svc) |     |                   |     | GetCommunityAsync      |
+-----------+     |                   |     +------------------------+
                  |                   |                |
                  |                   |                v
                  |                   |     +------------------------+
                  |                   |     | Fallback: DiscoverAsync|
                  |                   |     +------------------------+
                  |                   |                |
                  |                   |                v
                  |                   |     +------------------------+
                  |                   +---> | IGraphProvider         |
                  |                         | GetNodeAsync(per member)|
                  |                         +------------------------+
                  |                                   |
                  |                                   v
                  |                   +-------------------------------+
                  |                   | Semantic Kernel (Ollama/SK)   |
                  |                   | IChatCompletionService        |
                  |                   +-------------------------------+
                  |                                   |
                  |<----------------------------------+
                  v
          Result<string>.Success(summary)
```

### Pipeline Steps

1. **Resolve Community** — Attempt `ICommunityDetectionService.GetCommunityAsync(communityId)`.
2. **Discovery Fallback** — If the community is not found in cache, trigger `DiscoverAsync()` to re-detect all communities and find the matching ID.
3. **Validate Members** — Verify the community exists and has at least one member.
4. **Retrieve Member Nodes** — For each `MemberId`, call `IGraphProvider.GetNodeAsync()` to fetch node details.
5. **Prompt Construction** — Build a structured prompt including:
   - Community name and description
   - Member count
   - Type-grouped member list (up to 10 per type)
6. **LLM Summarization** — Generate a concise summary describing what unifies the community.
7. **Fallback** — If LLM is unavailable, return a structured text summary.

---

## Community Detection Algorithm

The `CommunityDetectionService` implements the **Label Propagation Algorithm (LPA)**:

1. **Initialize** — Each node starts with its own label.
2. **Iterate** — For up to 20 iterations:
   - Shuffle node order deterministically (seed = 42).
   - Each node adopts the most common label among its neighbors.
   - Tie-breaking is random but deterministic.
3. **Converge** — Stop when no labels change or max iterations reached.
4. **Group** — Nodes with the same label form a community.

This algorithm is parameter-free and discovers communities without requiring pre-knowledge of community count.

---

## Prompt Template

```
Community: {Name}
Description: {Description}

Members ({Count}):
  {Type} ({Count}): {Label1}, {Label2}, ...

Generate a concise summary describing this community. 
What unifies these members? What are the key themes or functions?
```

---

## Integration Points

### Active Consumers

- `GraphRagService.RetrieveAsync` — calls `_graphSummary.SummarizeCommunityAsync()` and `_hierarchicalSummary.SummarizeCommunityAsync()` per resolved community
- `LazyGraphRagService.RetrieveAsync` — same dual invocation pattern
- `GraphContextBuilder.BuildContextAsync` — includes community context when `Communities` source flag is set
- `GlobalGraphSearchService.SearchAsync` — performs **map-reduce** over all community summaries
- `HierarchicalSummaryService.SummarizeCommunityAsync` — delegates to `_graphSummary.SummarizeCommunityAsync()`

### Map-Reduce in Global Search

```csharp
foreach (var community in communities)
{
    var summary = await _graphSummary.SummarizeCommunityAsync(community.Id, ct);
    var hierarchical = await _hierarchicalSummary.SummarizeCommunityAsync(community.Id, ct);
}
// Synthesize all community summaries into a global answer
```

---

## Persistence

Community summaries are computed on demand. The `CommunityDetectionService` maintains an in-memory cache (`_lastDiscovered`) of communities discovered during the most recent `DiscoverAsync()` call. Community assignments are persisted in Neo4j via the `communityId` property on nodes:

```cypher
(:Entity {id: $id, label: $label, communityId: $communityId})
```

---

## Validation

- Build: ✅ Succeeds (0 errors, 0 warnings)
- Unit tests: ✅ 33 RAGS tests pass
- Algorithm: Deterministic with seed=42; converges on all tested graph sizes

---

## Status

- ✅ Operational
- ✅ Label Propagation Algorithm implemented and deterministic
- ✅ Integrated into GraphRAG, LazyGraphRAG, Context Builder, and Global Search
- ✅ Member node resolution from graph provider

---

## Future Enhancements

- Persist community summaries in Neo4j as `:CommunitySummary` nodes
- Support additional community detection algorithms (Louvain, Leiden)
- Temporal community evolution tracking

