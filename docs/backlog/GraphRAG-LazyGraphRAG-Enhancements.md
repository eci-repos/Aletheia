# Backlog: GraphRAG / LazyGraphRAG Enhancements

**Status:** Items 1, 4, 5, 6 promoted to Sprint 60 and **implemented** (committed `c6c3e48`, pushed 2026-08-08); items 2, 3, 7, 8 still parked. Items 7 and 8 come from the Sprint 60 Docker smoke test (2026-08-10).
**Created:** 2026-08-06
**Source:** Review of `GraphRagService.cs`, `LazyGraphRagService.cs`, `GraphTraversalBudget.cs`, `CorpusDiscoveryIndex.cs`, `SubgraphPruningService.cs`, `GraphRagResultRanker.cs`, `Neo4jGraphProvider.cs`, and the GraphIntelligence services.

Items here are **not** authorized work. An item becomes authorized only when the current sprint file promotes it. This document tracks candidate improvements so they are not lost; keep the Status column and this file current as work progresses.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Per-request `GraphTraversalBudget`** — replace the shared-singleton budget (Program.cs) with one constructed per `RetrieveAsync` call; also make `LazyGraphRagService._indexedSources` thread-safe. | Concurrent requests currently corrupt each other's budget via `Reset()`. | ~1–2 hrs | **Implemented** (Sprint 60, 2026-08-07) |
| 2 | **Persist the LazyGraphRAG corpus index to PostgreSQL** — term frequency / doc frequency / avg doc length survive restart and multi-instance. | Current in-memory `CorpusDiscoveryIndex` (singleton) is lost on restart; second instance sees an empty corpus. | ~0.5–1 day | Proposed |
| 3 | **Batch GraphRAG ingest** — `UNWIND`-based Neo4j writes per chunk, bounded-concurrency LLM extraction, and gate community re-clustering (currently O(graph) on every upload). | Serial N+1 LLM + Neo4j round-trips make large-document ingest extremely slow/costly. | ~1–1.5 days | Proposed |
| 4 | **Real token accounting + hard deadline** — wire SemanticKernel usage into `RecordTokens` (currently dead code) and `CancellationTokenSource.CancelAfter(MaxExecutionTime)`. | Token budget is never enforced; a single slow LLM call blows the 30s budget. | ~2–3 hrs | **Implemented** (Sprint 60, 2026-08-07) |
| 5 | **Stop noise-entity persistence** — do not persist `keyword` / `statistical-candidate` terms as graph nodes; keep them retrieval-only. | Entity extraction LLM fallback and LazyGraphRAG statistical candidates pollute the graph. | ~1–2 hrs | **Implemented** (Sprint 60, 2026-08-07) |
| 6 | **Per-query retrieval trace** — expose LLM calls, tokens, nodes/edges traversed, pruning ratio, and which fallback strategy produced the answer. | GraphRAG `RetrieveAsync` is a long fallback cascade; the fired path is currently opaque. | ~2–4 hrs | **Implemented** (Sprint 60, 2026-08-07) |
| 7 | **Reembed indexer parity** — `POST /api/jobs/rags/reembed` / `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` run the full `UploadedContentKnowledgeIndexer.IndexAsync` (~100+ serial cloud LLM calls per doc: entity discovery + node summaries + relationship extraction + community detection + community summaries), while file uploads use `IndexLightweightAsync`. Make reembed honor the lightweight path (or parallelize / batch the graph-intelligence calls). | A 3-doc corpus reembed took 40+ minutes against a cloud model (Sprint 60 smoke test, 2026-08-10); operators cannot reasonably wait on re-embedding after a provider/dimension change. | ~0.5–1 day | Proposed |
| 8 | **GraphRAG soft deadline / best-partial result** — under LLM saturation (Ollama serializes cloud-model calls), concurrent GraphRAG retrievals hit the 30s `CancelAfter(MaxExecutionTime)` before the final semantic fallback and return HTTP 400 `Vector search failed. The operation was canceled.` Surface the deadline as a soft signal: return the best partial result with a timeout notice, or degrade to semantic retrieval without a hard failure. | Concurrent GraphRAG under load hard-fails the whole request instead of degrading to best-available (Sprint 60 smoke test, 2026-08-10); rough UX edge on the documented hard-timeout behavior. | ~2–4 hrs | Proposed |

**Suggested sequencing:** quick wins (1, 4, 5, 6) in one pass, then 2 and 3 as separate focused pieces; 7 and 8 are small follow-ups that can ride along with any GraphRAG-focused sprint.

**Total (agent):** ~3–4 working days including build/test verification and Docker smoke.
