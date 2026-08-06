# Appendix A — GraphRAG and LazyGraphRAG: Technical Reference

**Part of the Aletheia End-User Technical Documentation (user-guide).**
Companion reading: `docs/GraphRAG-Implementation-vs-Microsoft-Research.md`, `docs/graphrag/GraphRAG-Maturity-Report.md`, `docs/graphrag/LazyGraphRAG-Maturity-Report.md`, `docs/graphrag/Traversal-Budget-Report.md`, `docs/Phase21-Background-Operations-Handoff.md`.

---

## 1. What GraphRAG Is

GraphRAG (Graph Retrieval-Augmented Generation) is Microsoft Research's index-heavy retrieval pattern: instead of searching raw text chunks alone, the system first builds a **typed knowledge graph** of the corpus — entities, relationships between them, and hierarchical community summaries — and then answers questions by retrieving from that graph's **summaries and structure** rather than (or in addition to) raw chunks.

Aletheia implements the GraphRAG pattern in .NET with a typed graph persisted in **Neo4j** and semantic vectors in **pgvector**. The pattern is:

1. **Source → text units**: documents are split into chunks (shared with standard RAGS).
2. **Entity extraction**: each chunk is analyzed to identify entities (people, organizations, terms, project names, etc.).
3. **Relationship extraction**: directed, typed relationships between entities are identified (e.g., "manages", "part of", "requires").
4. **Typed graph persistence**: entities, relationships, and entity-to-source links (`found_in` edges) are stored in Neo4j with `sourceId`, `sourceName`, `chunkId`, and `chunkIndex` metadata.
5. **Community detection**: a deterministic, Leiden-inspired hierarchical community detection groups related entities into communities.
6. **Summaries**: entity summaries, relationship summaries, and bottom-up community summaries are precomputed and persisted.
7. **Local search**: query entities are resolved, their communities located, and stored entity/community summaries retrieved.
8. **Global search**: a map-reduce over top-level community summaries answers corpus-wide questions ("what are the main themes across all documents?").

## 2. What LazyGraphRAG Is

LazyGraphRAG is Microsoft Research's cost-shifting variant: **indexing stays cheap** (no LLM calls), and the graph exploration happens **at query time** within a strict resource budget.

Aletheia's LazyGraphRAG implementation:

- **Ingestion**: stores chunks and updates a lightweight corpus statistics index (`ICorpusDiscoveryIndex`, TF-IDF/BM25-style statistics). No LLM entity extraction runs during indexing.
- **Query time**: candidate entities are discovered from corpus statistics, a graph is constructed on demand, and traversal uses a **priority-queue best-first search** instead of blind breadth-first search.
- **Budgets**: every query is bounded by `IGraphTraversalBudget` — maximum LLM calls, depth, nodes, relationships, token budget, and execution time — so latency and cost are predictable.
- **Pruning**: low-relevance nodes and relationships are pruned before ranking/synthesis (`ISubgraphPruningService`).
- **Persistent enrichment**: when traversal discovers valuable entities/relationships, they can be persisted so later queries benefit (progressive enrichment).

## 3. Alignment with Microsoft Research Guidelines

Aletheia follows the Microsoft Research reference architecture (per `docs/GraphRAG-Implementation-vs-Microsoft-Research.md`), not a byte-for-byte clone of the Python implementation. The table below summarizes documented alignment:

| Microsoft reference stage | Aletheia implementation | Status |
|---|---|---|
| Source to text units (chunking) | RAGS chunking pipeline | Implemented |
| Entity extraction (per text unit, structured output) | Chunk-level entity extraction | Implemented |
| Relationship extraction (typed, directed) | Typed `GraphEdge.RelationshipType` | Implemented |
| Typed graph persistence | Neo4j via `IGraphProvider` | Implemented |
| Leiden-style hierarchical communities | Deterministic hierarchical community detection (Leiden-inspired local moving) | Implemented |
| Entity / relationship summaries | `IGraphSummaryService` stored node/relationship summaries | Implemented |
| Community summaries (bottom-up) | Persisted `Community` graph nodes | Implemented |
| Local search (entity + community summaries) | Summary candidates preferred when available | Implemented |
| Global search (map-reduce over top-level summaries) | `IGlobalGraphSearchService` | Implemented |
| Structured graph context for synthesis | `IGraphContextBuilder` | Implemented |
| LazyGraphRAG: cheap indexing (no LLM) | `ICorpusDiscoveryIndex` (TF-IDF/BM25) | Implemented |
| LazyGraphRAG: query-time discovery | `LazyEntityDiscoveryService`, `LazyRelationshipDiscoveryService` | Implemented |
| LazyGraphRAG: budgeted traversal | `IGraphTraversalBudget` (LLM calls, depth, nodes, edges, tokens, time) | Implemented |
| LazyGraphRAG: subgraph pruning | `ISubgraphPruningService` | Implemented |

Known divergences (documented in the research-comparison doc): Aletheia does not depend on Microsoft's Python/igraph Leiden stack, keeps its .NET Clean Architecture/provider abstractions, and treats community detection as compatible-with-but-not-identical-to Microsoft's.

## 4. When the Preparation Resources Are Built (Ingestion vs. Query Time)

### 4.1 Standard RAGS (baseline)
- **Ingestion**: chunk + embed every document. No graph work.
- **Query time**: vector similarity (and keyword fallback when below the score floor).

### 4.2 GraphRAG
- **Ingestion (index-heavy)**: full pipeline — source nodes, chunk nodes, entity extraction, relationship extraction, `found_in` edges, community detection, and entity/relationship/community summaries.
- **Upload path (searchable-first)**: Repository uploads use `IndexLightweightAsync` — taxonomy hints, source nodes, chunk nodes, and `has_chunk` edges are persisted immediately, and chunks are marked **pending lazy enrichment** so the document is searchable quickly. The expensive summary-heavy enrichment is deferred until query time for relevant chunks (lazy enrichment), then the chunk is marked `lazyEnriched`.
- **Query time**: resolve entities → resolve communities → read stored summaries (when present) → assemble structured context with `IGraphContextBuilder` → fall back to semantic chunk evidence with citations; lazily enrich the top chunks when summaries are absent.

### 4.3 LazyGraphRAG
- **Ingestion (cheap)**: chunks + corpus statistics only. **No LLM calls**.
- **Query time (the working phase)**: candidate entity discovery → best-first traversal under `IGraphTraversalBudget` → pruning → ranked context; discovered entities/relationships may be persisted for future queries.

### 4.4 What this means operationally
- A freshly uploaded document is **searchable via Semantic RAGS immediately** after the upload job completes.
- Graph structures and summaries grow **progressively** — at ingestion for GraphRAG, and on-demand for LazyGraphRAG — rather than blocking uploads on expensive summarization.
- Background jobs (ingestion, re-embedding, document briefs) run through the queued job system with `/api/jobs` progress; see the Operations Guide for repair/health commands.

## 5. Why These Modes Are Beneficial

| Mode | Best for | Benefit |
|---|---|---|
| Standard RAGS (Semantic) | Precise, scoped questions about specific documents | Fast, deterministic, source-scoped, citations |
| GraphRAG | Corpus-wide and cross-document synthesis | Answers "what are the main themes/entities across the estate?" via community summaries and global search; typed relationships surface connections plain vectors miss |
| LazyGraphRAG | Large corpora with cost/latency sensitivity | Near-zero ingestion LLM cost; predictable per-query budgets; progressive enrichment |
| Combination | Copilot broad/global prompts | Graph modes participate in broad retrieval with Semantic fallback; scoped document prompts use source-scoped Semantic evidence |

Additional documented benefits:
- **Structured context + citations**: `IGraphContextBuilder` formats graph abstractions so answers are grounded and cited (`summary-entity`, `summary-community`, `semantic`, `keyword` retrieval strategies).
- **Predictable cost**: traversal budgets make token consumption and latency bounded per query.
- **Operational observability**: Copilot responses carry elapsed time, estimated token throughput/counts, context and citation counts, retrieval scores, and a heuristic alignment-confidence estimate.

## 6. Where Users and Operators See These Modes

- **Search Center** exposes Semantic, WRAGS, GraphRAG, and LazyGraphRAG modes. GraphRAG/LazyGraphRAG (and raw Wiki/WRAGS) are **internal operator modes**, hidden from end users unless `FeatureFlags:ShowInternalSearch` is enabled (default false); gated endpoints return HTTP 404 when hidden.
- **Copilot** uses graph-backed retrieval internally for broad/global corpus prompts and prefers source-scoped Semantic RAGS evidence for specific-document prompts.
- **WRAGS (the Wiki)**: the durable, user-facing Wiki surface over this stack — generated/edited pages with lifecycle status, history, related topics, and source-change stale detection. Community summaries stay internal; document briefs are the user-facing Wiki content.

## 7. Current Positioning and Known Gaps

**Positioning** (per the research-comparison doc): Standard RAG is production-ready; GraphRAG and LazyGraphRAG are close architectural alignments for their respective patterns; evaluation against Microsoft-style baselines is not yet quantified on a larger benchmark.

**Documented gaps and suggested next steps** (from the research-comparison and maturity docs):

| Gap | Suggested next step |
|---|---|
| Full Microsoft Leiden parity not required today | Optional provider adapter / formal quality benchmark if exact parity is needed |
| Long-running GraphRAG ingest job state is in-memory only | Durable job persistence, cancellation, retry, deeper progress instrumentation |
| Token/confidence telemetry are estimates, not provider-grade | Replace token estimates with model-provider usage data; calibrate confidence against an evaluation set |
| No full retrieval-trace UI | Add a retrieval-trace panel (entity → community → summary) in Search Center/Copilot |
| WRAGS backlinks / diff UX basic | Graph-derived backlinks, editorial diff visualization, benchmarked wiki-as-context quality scoring |
| No repeatable GraphRAG/LazyGraphRAG benchmark | Add benchmark corpora/questions and scoring harness |
| Summary refresh policy not operator-facing | Admin controls for summary regeneration and stale-summary detection |

## 8. Possible Next Steps (Product Roadmap Input)

1. **Benchmark & evaluation harness** — repeatable corpora/questions to quantify answer quality, recall, latency, and cost vs. Microsoft-style baselines.
2. **Durable background jobs** — PostgreSQL-backed job state with cancellation/retry for graph ingest, re-embedding, and brief generation.
3. **Provider-grade telemetry** — model-provider token usage accounting and calibrated confidence.
4. **Retrieval trace UX** — inspect the entity/community/summary path behind every answer.
5. **Summary lifecycle management** — operator-facing regeneration and stale-summary detection aligned with document updates (Sprint 56) and re-embedding (Sprint 57).
6. **Graph-derived Wiki backlinks + diff visualization** — connect GraphRAG structure to the user-facing Wiki.
7. **Theme-aware graph retrieval (Sprint 58 follow-up)** — apply knowledge-theme filtering to GraphRAG/LazyGraphRAG global/broad paths (currently out of scope; RAGS paths are theme-enforced).

---

*Sources: `docs/GraphRAG-Implementation-vs-Microsoft-Research.md`, `docs/graphrag/*` maturity and traversal-budget reports, `docs/Phase21-Background-Operations-Handoff.md`, AGENTS.md.*