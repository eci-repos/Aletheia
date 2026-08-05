# Aletheia GraphRAG vs. Microsoft Research

## Executive Summary

Aletheia's RAGS v2 work moves the platform materially closer to Microsoft Research's GraphRAG and LazyGraphRAG patterns.

The biggest change is that GraphRAG is no longer just graph-enhanced vector search. It can build typed graph intelligence, store summaries, retrieve from entity and community summaries, and support global search over top-level community summaries. Phase 21 also adds a searchable-first lazy enrichment path for uploads: cheap source/chunk graph seeds are persisted first, and expensive entity/relationship/summary enrichment runs only for relevant chunks at query time. LazyGraphRAG follows the Microsoft cost-shifting idea more closely by keeping ingestion cheap and using query-time discovery, budgeted best-first traversal, and pruning.

This is still not a byte-for-byte clone of Microsoft's Python implementation. Aletheia keeps its .NET clean architecture, provider abstractions, Repository system-of-record boundary, Neo4j graph provider, pgvector semantic store, and Semantic Kernel model orchestration.

---

## Microsoft GraphRAG Reference Pattern

Microsoft GraphRAG is index-heavy and query-light.

| Stage | Microsoft pattern | Aletheia v2 status |
| --- | --- | --- |
| Source to text units | Split documents into chunks | Implemented through RAGS chunking |
| Entity extraction | Extract entities per text unit with LLM structured output | Implemented at chunk level during GraphRAG ingest |
| Relationship extraction | Extract directed relationships per text unit | Implemented at chunk level with typed `GraphEdge.RelationshipType` |
| Typed graph persistence | Persist entities, relationships, and source links | Implemented in Neo4j through `IGraphProvider` |
| Community detection | Leiden-style hierarchical communities | Implemented as deterministic hierarchical community detection with Leiden-inspired local moving |
| Entity summaries | Precompute human-readable entity summaries | Implemented through `IGraphSummaryService` and stored node summaries |
| Relationship summaries | Precompute relationship summaries | Implemented as relationship summary metadata |
| Community summaries | Precompute bottom-up community summaries | Implemented for detected communities and persisted as `Community` graph nodes |
| Local search | Retrieve entity and community summaries around query entities | Implemented; summary candidates are preferred when available |
| Global search | Map-reduce over top-level community summaries | Implemented through `IGlobalGraphSearchService` |
| Structured context | Format graph abstractions for synthesis | Implemented through `IGraphContextBuilder` |

## Aletheia GraphRAG v2 Pipeline

### Ingestion

`GraphRagService.IngestAsync` now performs:

1. Standard RAGS ingestion for chunks and embeddings.
2. Source node persistence in Neo4j.
3. Per-chunk entity extraction.
4. Typed `Entity` node persistence with `sourceId`, `sourceName`, `chunkId`, and `chunkIndex` metadata.
5. Entity-to-source `found_in` edges.
6. Typed relationship edge persistence.
7. Document, entity, relationship, and community summary generation.
8. Hierarchical community detection and community summary persistence.

For Repository uploads and queued GraphRAG ingestion jobs, Aletheia now uses `UploadedContentKnowledgeIndexer.IndexLightweightAsync` instead of running the full summary-heavy path during upload. That path records taxonomy hints, source nodes, chunk nodes, and `has_chunk` edges; it marks chunks as pending lazy enrichment so the graph is searchable quickly.

### Retrieval

`GraphRagService.RetrieveAsync` now:

1. Resolves query entities through graph-aware reasoning.
2. Resolves communities for matching entities.
3. Reads stored entity and community summaries where available.
4. Uses `IGraphContextBuilder` to assemble structured context.
5. Ranks summary candidates and semantic fallback candidates with citations.
6. If stored summaries are absent, lazily enriches the top retrieved chunks with bounded entity extraction, relationship extraction, and entity summaries, then marks those chunks as `lazyEnriched`.
7. Returns results marked with retrieval strategies such as `summary-entity` and `summary-community`.

### Global Search

`GraphRagService.GlobalSearchAsync` delegates to global graph search, which:

1. Detects communities.
2. Selects top-level communities.
3. Maps each selected community summary against the query.
4. Reduces mapped answers into a broad corpus-level response.

## Microsoft LazyGraphRAG Reference Pattern

Microsoft LazyGraphRAG shifts cost from indexing to retrieval.

| Stage | Microsoft pattern | Aletheia v2 status |
| --- | --- | --- |
| Indexing | Store text units and cheap text statistics; avoid LLM calls | Implemented with `ICorpusDiscoveryIndex` using TF-IDF/BM25-style statistics |
| Query entity discovery | Identify candidates at query time | Implemented via query-time discovery from corpus statistics |
| Traversal | LLM-guided or relevance-guided best-first traversal | Implemented as budgeted best-first traversal with optional reasoning guidance |
| Budgets | Limit LLM calls, depth, nodes, edges, tokens, and time | Implemented through `IGraphTraversalBudget` |
| Pruning | Remove low-relevance subgraph elements before synthesis | Implemented through `ISubgraphPruningService` |
| Final context | Synthesize from pruned graph context | Implemented through ranked summary/context candidates |

## Aletheia LazyGraphRAG v2 Pipeline

`LazyGraphRagService` now treats ingestion and retrieval differently:

- Ingestion stores chunks and updates corpus statistics without performing LLM entity extraction.
- Retrieval discovers candidate entities at query time from TF-IDF/BM25 signals.
- Traversal uses a priority queue instead of blind BFS.
- Traversal is bounded by depth, nodes, relationships, LLM calls, token budget, and timeout.
- Low-relevance nodes and relationships are pruned before ranking.
- Entity and community summaries can be pulled into final context where available.

## Current Positioning

| Capability | Position versus Microsoft research |
| --- | --- |
| Standard RAG | Production-ready baseline semantic retrieval with citations |
| GraphRAG | Close architectural alignment for index-heavy graph summarization and local/global summary retrieval |
| LazyGraphRAG | Close architectural alignment for low-cost indexing and budgeted query-time graph exploration |
| WRAGS Wiki | Durable LLM Wiki surface over the RAGS-first retrieval stack with PostgreSQL-backed generated/edited pages, version history, lifecycle controls, related topics/pages, stale warnings, retrieval participation, and queued regeneration controls |
| Search Center | Web UI exposes Semantic, WRAGS, GraphRAG, and LazyGraphRAG modes over the same corpus. Graph modes participate in broad/global retrieval with Semantic fallback; scoped document prompts continue using source-scoped Semantic RAGS evidence. |
| Community detection | Functional hierarchical implementation, but not a direct dependency on Microsoft GraphRAG's Python/igraph Leiden stack |
| Evaluation | Needs a larger benchmark set to quantify answer quality, recall, latency, and cost against Microsoft-style baselines |
| Operations | Docker stack validated; long-running upload/Search Center ingestion now queues background jobs with `/api/jobs` progress |
| Runtime observability | Copilot responses now include elapsed time, estimated token rate, estimated token counts, context/citation counts, retrieval scores, and heuristic alignment confidence |

## Remaining Gaps

| Gap | Impact | Suggested next step |
| --- | --- | --- |
| Full Microsoft Leiden parity | Current implementation is compatible with Aletheia abstractions but not the exact Microsoft/Python graph stack | Add a formal quality benchmark or optional provider adapter if exact parity becomes required |
| Long-running GraphRAG ingest | Uploads now avoid full summary-heavy enrichment by default, but job state is in-memory only | Add durable job persistence, cancellation, retry, and deeper progress instrumentation |
| Token and confidence telemetry | Copilot stats are useful estimates, not provider-grade accounting or calibrated quality scores | Replace token estimates with model-provider usage data and calibrate confidence against an evaluation set |
| Retrieval trace UX | Results include strategy/citations, but users cannot yet inspect the full entity-community-summary trace | Add a retrieval trace panel in Search Center/Copilot |
| WRAGS maturity | WRAGS persists generated/edited page snapshots with history, lifecycle state, queued regeneration, source-aware stale detection, related-page lookup, and retrieval participation, but graph-derived backlinks and editorial diff UX are still basic | Add graph-derived backlinks, diff visualization, and benchmarked wiki-as-context quality scoring |
| Evaluation harness | Unit tests cover mechanics; research-level quality needs repeatable corpora/questions | Add GraphRAG/LazyGraphRAG benchmark datasets and scoring |
| Summary refresh policy | Summaries are generated during ingestion; update/expiration policy is not yet operator-facing | Add admin controls for summary regeneration and stale-summary detection |

## Bottom Line

Aletheia is now much closer to Microsoft Research's GraphRAG initiative than the earlier implementation. The core conceptual gaps that mattered most are closed: precomputed summaries, hierarchical communities, summary-based retrieval, global search, minimal-cost LazyGraphRAG ingestion, budgeted traversal, and pruning.

The remaining work is mostly maturity work: exact algorithm parity where needed, evaluation, UI traceability, durable async ingestion operations, and operational tuning.

## References

- Microsoft Research, "From Local to Global: A Graph RAG Approach to Query-Focused Summarization": https://arxiv.org/abs/2404.16130
- Microsoft GraphRAG project: https://www.microsoft.com/en-us/research/project/graphrag/
- Microsoft GraphRAG open source: https://github.com/microsoft/graphrag
- Microsoft Research Blog, "LazyGraphRAG: Setting a new standard for quality and cost": https://www.microsoft.com/en-us/research/blog/lazygraphrag-setting-a-new-standard-for-quality-and-cost/
