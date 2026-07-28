# Global Search Report

**Sprint-14 | GraphRAG Maturity**

---

## Purpose

This document describes the Global Search capability of the Aletheia GraphRAG system. Global Search answers organization-wide and cross-domain questions by synthesizing information across all detected communities using a map-reduce pattern over community summaries.

---

## Architecture

### Components

| Component | Implementation | Location |
|-----------|---------------|----------|
| Interface | `IGlobalGraphSearchService` | `src/RAGS.Abstractions/Interfaces/IGlobalGraphSearchService.cs` |
| Implementation | `GlobalGraphSearchService` | `src/RAGS.Application/GraphRAG/GlobalGraphSearchService.cs` |
| Model | `GlobalSearchResult` | `src/RAGS.Abstractions/Models/GlobalSearchResult.cs` |
| Interface | `ICommunityDetectionService` | `src/RAGS.Abstractions/Interfaces/ICommunityDetectionService.cs` |
| Interface | `IGraphSummaryService` | `src/RAGS.Abstractions/Interfaces/IGraphSummaryService.cs` |
| Interface | `IHierarchicalSummaryService` | `src/RAGS.Abstractions/Interfaces/IHierarchicalSummaryService.cs` |
| Interface | `IGraphContextBuilder` | `src/RAGS.Abstractions/Interfaces/IGraphContextBuilder.cs` |
| Interface | `ICitationPathService` | `src/RAGS.Abstractions/Interfaces/ICitationPathService.cs` |

### Method Signature

```csharp
Task<Result<GlobalSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
```

---

## Map-Reduce Pattern

Global Search uses a classic map-reduce architecture:

### Map Phase
For each detected community:
1. Retrieve community metadata via `ICommunityDetectionService.DiscoverAsync()`
2. Generate flat summary via `IGraphSummaryService.SummarizeCommunityAsync()`
3. Generate hierarchical summary via `IHierarchicalSummaryService.SummarizeCommunityAsync()`

### Context Enrichment
Invoke `IGraphContextBuilder.BuildContextAsync()` with:
- `GraphContextSources.Communities`
- `GraphContextSources.Summaries`
- `GraphContextSources.Entities`

### Reduce Phase (Synthesis)
Build a master prompt containing:
- The original query
- All community summaries (flat + hierarchical)
- Additional context from the context builder
- Instruction to synthesize an executive-level answer

### Citation Phase
For each community, trace member entities back to source documents via `ICitationPathService.GetEntitySourcesAsync()`, producing a deduplicated citation list.

---

## Data Flow

```
+-------+     +------------------------+     +--------------------------+
| Query | --> | GlobalGraphSearchSvc   | --> | ICommunityDetectionSvc   |
+-------+     |                        |     | DiscoverAsync()          |
              |                        |     +--------------------------+
              |                        |                |
              |                        |                v
              |                        |     +--------------------------+
              |                        |     | Community[]              |
              |                        |     +--------------------------+
              |                        |                |
              |          MAP PHASE     |                v
              |                        |     +--------------------------+
              |                        |     | IGraphSummarySvc         |
              |                        |     | SummarizeCommunityAsync  |
              |                        |     +--------------------------+
              |                        |                |
              |                        |                v
              |                        |     +--------------------------+
              |                        |     | IHierarchicalSummarySvc  |
              |                        |     | SummarizeCommunityAsync  |
              |                        |     +--------------------------+
              |                        |                |
              |      CONTEXT PHASE     |                v
              |                        |     +--------------------------+
              |                        |     | IGraphContextBuilder     |
              |                        |     | BuildContextAsync        |
              |                        |     +--------------------------+
              |                        |                |
              |     REDUCE PHASE       |                v
              |                        |     +--------------------------+
              |                        |     | Semantic Kernel          |
              |                        |     | Synthesize master prompt |
              |                        |     +--------------------------+
              |                        |                |
              |    CITATION PHASE      |                v
              |                        |     +--------------------------+
              |                        |     | ICitationPathService     |
              |                        |     | GetEntitySourcesAsync    |
              |                        |     +--------------------------+
              |                        |                |
              |<-----------------------+
              v
      Result<GlobalSearchResult>
```

---

## GlobalSearchResult Model

```csharp
public sealed class GlobalSearchResult
{
    public string Answer { get; }              // Synthesized executive answer
    public IReadOnlyList<string> Citations { get; }  // Deduplicated source citations
    public IReadOnlyList<SearchResult> SupportingResults { get; }  // Optional chunk-level evidence
}
```

---

## API Endpoints

Both GraphRAG services expose global search via HTTP:

### GraphRAG
```
GET /api/graphrag/global?query={query}
```

### LazyGraphRAG
```
GET /api/lazygraphrag/global?query={query}
```

Both endpoints delegate to `IGraphRagService.GlobalSearchAsync` and `ILazyGraphRagService.GlobalSearchAsync`, respectively, which forward to `IGlobalGraphSearchService.SearchAsync`.

---

## Prompt Template (Synthesis)

```
Query: {query}

Detected {N} communities with summaries.

Community: {Name}
Summary: {Summary}

Hierarchical Perspectives:
- {Name}: {Summary}

Additional Context:
{ContextBuilderOutput}

Synthesize a concise, executive-level answer to the query using the community summaries above.
```

---

## Integration Points

### Active Consumers

- `GraphRagService.GlobalSearchAsync` — delegates directly to `_globalSearch.SearchAsync()`
- `LazyGraphRagService.GlobalSearchAsync` — delegates directly to `_globalSearch.SearchAsync()`
- `GraphRagController.GlobalSearch` — HTTP endpoint exposing global search
- `LazyGraphRagController.GlobalSearch` — HTTP endpoint exposing global search

### Dependency Graph

```
GlobalGraphSearchService
  --> ICommunityDetectionService (CommunityDetectionService)
  --> IGraphSummaryService (GraphSummaryService)
  --> IHierarchicalSummaryService (HierarchicalSummaryService)
  --> IGraphContextBuilder (GraphContextBuilder)
  --> ICitationPathService (CitationPathService)
  --> Kernel? (optional Semantic Kernel for synthesis)
```

---

## Use Cases

| Use Case | Example Query |
|----------|---------------|
| Organization-wide | "What are the main risk factors across all projects?" |
| Cross-domain | "How does our supply chain interact with regulatory compliance?" |
| Executive summary | "Summarize Q3 findings in 3 sentences." |
| Knowledge synthesis | "What do we know about vendor X across all documents?" |

---

## Validation

- Build: ✅ Succeeds (0 errors, 0 warnings)
- Unit tests: ✅ 33 RAGS tests pass (including mock global search service)
- API endpoints: ✅ Registered in both controllers
- DI wiring: ✅ `IGlobalGraphSearchService` registered in `Program.cs`

---

## Status

- ✅ Operational
- ✅ Map-reduce over community summaries implemented
- ✅ Dual summary retrieval (flat + hierarchical)
- ✅ Citation tracing via `ICitationPathService`
- ✅ LLM synthesis with deterministic fallback
- ✅ HTTP endpoints exposed for both GraphRAG and LazyGraphRAG

---

## Future Enhancements

- Streaming global search (SSE/WebSockets)
- Community ranking by relevance to query
- Hybrid global + local search blending
- Cached community summary index for sub-second global responses
