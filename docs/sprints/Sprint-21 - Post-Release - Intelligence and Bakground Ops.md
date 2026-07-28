# Current Sprint

Sprint: Post-Release RAGS v2 - Intelligence and Background Operations

Status: Active

## Objective

Begin the v2 RAGS initiative after v1.0 release certification by moving GraphRAG toward an index-heavy model and making long-running ingestion observable and independent from browser request timeouts.

The active scope is Phase 21 only.

Authorized work:

- Replace flat community detection with hierarchical GraphRAG community detection.
- Generate graph summaries during ingestion.
- Extract entities and relationships per chunk.
- Persist typed entity nodes and typed relationship edges in Neo4j.
- Use stored entity and community summaries during retrieval.
- Implement global map-reduce search over top-level community summaries.
- Assemble structured GraphRAG prompts through `IGraphContextBuilder`.
- Optimize LazyGraphRAG for minimal indexing cost.
- Shift LazyGraphRAG candidate discovery to query-time TF-IDF/BM25 statistics.
- Implement budgeted best-first LazyGraphRAG traversal with LLM edge guidance where available.
- Prune low-relevance LazyGraphRAG subgraphs before final ranking.
- Move long-running document ingestion into background jobs, with upload indexing using lightweight graph seed persistence by default.
- Shift expensive GraphRAG entity, relationship, and summary enrichment toward bounded query-time lazy enrichment for touched chunks.
- Sync query-time lazy GraphRAG entity and relationship discoveries back into PostgreSQL Taxonomy/Ontology so the UI explorers stay useful.
- Return a job identifier quickly for long-running ingestion requests.
- Provide job status, stage, heartbeat, timing metadata, and progress feedback through the API and UI.
- Report coarse-grained progress for extraction, chunking, embeddings, graph persistence, community detection, summarization, and final indexing.
- Return Copilot chat completion telemetry: elapsed seconds, estimated token throughput, context/citation counts, and heuristic alignment confidence.
- Keep feedback useful but not noisy through stage transitions and periodic heartbeat updates.
- Preserve current abstractions and production hardening guarantees.

---

# Authority

The repository is the source of truth.

Phase 21 supersedes the previous v1.0 release-freeze sprint.

Completed v1.0 functionality must not be regressed.

---

# Deliverables

- Updated GraphRAG ingestion path
- Hierarchical community metadata
- Index-time summary persistence
- Typed graph persistence
- Summary-based retrieval
- Global map-reduce search mode
- Structured context assembly
- LazyGraphRAG stats-only ingestion
- LazyGraphRAG query-time candidate discovery
- LazyGraphRAG best-first traversal and pruning
- Background ingestion job orchestration
- Lightweight upload graph seed indexing
- Query-time lazy GraphRAG enrichment for relevant chunks
- Lazy enrichment write-back to Taxonomy/Ontology
- Copilot chat completion telemetry and UI stats
- Job status and progress APIs
- UI progress/feedback panel for long-running work
- Periodic heartbeat reporting for active ingestion/enrichment jobs
- Focused unit tests

---

# Implementation Snapshot

Completed in this sprint so far:

- GraphRAG v2 index-time intelligence and query-time summary retrieval.
- LazyGraphRAG query-time candidate discovery, budgeted traversal, and pruning.
- Background ingestion job orchestration for uploads and direct Search Center ingestion.
- Upload and queued GraphRAG ingestion now seed RAGS/vector data plus lightweight graph chunk nodes; expensive graph enrichment is deferred to query time.
- GraphRAG retrieval can lazily enrich the top retrieved chunks with bounded entity extraction, relationship extraction, and entity summaries.
- Lazy GraphRAG discoveries now write back to Taxonomy/Ontology through `ILazyEnrichmentKnowledgeSink`.
- Copilot responses now include elapsed time, estimated token rate, estimated tokens, context/citation counts, retrieval scores, and heuristic alignment confidence.
- `/api/jobs` status/progress endpoints.
- Web Activity panel polling and job progress display.
- Search Center supports Semantic, WRAGS, GraphRAG, and LazyGraphRAG retrieval modes with queued ingestion, result counts, retrieval strategy labels, citations, expansion controls, and visible technical failure details.
- WRAGS is now the project name for the LLM Wiki surface: durable wiki pages over RAGS, GraphRAG, and LazyGraphRAG knowledge.
- WRAGS now persists generated wiki pages in PostgreSQL through `/api/wiki`, including topic, title, summary, citations, source IDs, retrieval strategy, version, lifecycle status, review metadata, related topics, score, rank, source/chunk metadata, and timestamps.
- The WRAGS Web UI page at `/wiki` provides recent durable pages, topic search, GraphRAG-first wiki generation, LazyGraphRAG/Semantic fallback modes, citations, source/chunk details, retrieval strategy labels, versions, lifecycle controls, stale warnings, related topics, related-page backlinks, and regeneration controls.
- WRAGS maturity now includes page lifecycle states (`Generated`, `Reviewed`, `Approved`, `NeedsReview`, `Stale`), `reviewed_by`/`reviewed_at` persistence, stale flags/reasons, source-change stale detection, related-topic extraction, related-page lookup, editable page bodies, version history, background regeneration jobs, and API endpoints for status, edit, history, retrieval, and related pages.
- Search Center now includes WRAGS mode so durable wiki pages can participate as retrieval context beside Semantic, GraphRAG, and LazyGraphRAG.
- Copilot now merges saved WRAGS wiki pages into retrieval context when relevant, favoring reviewed/approved durable knowledge without auto-generating wiki pages during chat.
- LazyGraphRAG traversal budget counters now stop at configured limits instead of incrementing past the limit and failing retrieval after optional enrichment work.
- Documentation updates for architecture, administration, deployment, operations, and technical presentation.

Current handoff details are maintained in `docs/Phase21-Background-Operations-Handoff.md`.

---

# Exit Criteria

- Build succeeds
- RAGS unit tests pass
- Repository unit tests pass
- Existing GraphRAG retrieval behavior remains compatible
- New v2 index-time intelligence behavior is covered by tests
- Query-time GraphRAG prefers pre-computed summaries when available
- Global search uses top-level community summaries for corpus-level answers
- LazyGraphRAG ingestion performs no LLM extraction work
- LazyGraphRAG traversal is budgeted, best-first, and pruned
- Upload indexing avoids full document-wide LLM graph summarization by default
- Query-time GraphRAG enrichment marks touched chunks as enriched for reuse
- Query-time entity and relationship discoveries are visible through `/api/taxonomy` and `/api/ontology`
- Copilot displays useful completion stats after each assistant answer
- Search Center can retrieve through Semantic, WRAGS, GraphRAG, and LazyGraphRAG modes from the Web UI without console errors or hidden API failures
- WRAGS Wiki page is available from navigation and can search, display, edit, queue regeneration, review, approve, mark stale, show history, and show related durable PostgreSQL-backed wiki pages
- Upload and GraphRAG ingestion can continue after the initial HTTP request returns
- UI shows current ingestion stage, heartbeat age, and approximate completion
- Long-running jobs expose failure details instead of leaving the user guessing

Known maturity work that remains after the first background-operations slice:

- Persist job state durably instead of keeping it in API memory only.
- Add job cancellation, retry, and cleanup controls.
- Add deeper per-chunk/per-community progress from GraphRAG services.
- Replace estimated token counts with provider-reported usage when the chat provider exposes it.
- Calibrate alignment confidence with evaluation data; current confidence is retrieval-score/citation based.
- Add integration tests around upload queueing and job lifecycle APIs.
- Consider SSE/WebSocket progress streaming if 10-second polling is not enough.
- Persist job state durably for WRAGS regeneration and ingestion jobs; current background job queue is still process-local.
- Add richer graph-derived backlinks and editorial diff visualization beyond current related-page/topic matching.

---

# Out Of Scope

Do NOT:

- Replace provider abstractions.
- Add a new graph database provider.
- Rewrite LazyGraphRAG beyond compatibility needs.
- Change unrelated user-facing APIs.
