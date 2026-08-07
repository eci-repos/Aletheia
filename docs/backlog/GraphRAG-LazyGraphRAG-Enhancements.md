# Backlog: GraphRAG / LazyGraphRAG Enhancements

**Status:** Proposed (parked until promoted)
**Created:** 2026-08-06
**Source:** Review of `GraphRagService.cs`, `LazyGraphRagService.cs`, `GraphTraversalBudget.cs`, `CorpusDiscoveryIndex.cs`, `SubgraphPruningService.cs`, `GraphRagResultRanker.cs`, `Neo4jGraphProvider.cs`, and the GraphIntelligence services.

Items here are **not** authorized work. An item becomes authorized only when the current sprint file promotes it. This document tracks candidate improvements so they are not lost; keep the Status column and this file current as work progresses.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Per-request `GraphTraversalBudget`** — replace the shared-singleton budget (Program.cs) with one constructed per `RetrieveAsync` call; also make `LazyGraphRagService._indexedSources` thread-safe. | Concurrent requests currently corrupt each other's budget via `Reset()`. | ~1–2 hrs | Proposed |
| 2 | **Persist the LazyGraphRAG corpus index to PostgreSQL** — term frequency / doc frequency / avg doc length survive restart and multi-instance. | Current in-memory `CorpusDiscoveryIndex` (singleton) is lost on restart; second instance sees an empty corpus. | ~0.5–1 day | Proposed |
| 3 | **Batch GraphRAG ingest** — `UNWIND`-based Neo4j writes per chunk, bounded-concurrency LLM extraction, and gate community re-clustering (currently O(graph) on every upload). | Serial N+1 LLM + Neo4j round-trips make large-document ingest extremely slow/costly. | ~1–1.5 days | Proposed |
| 4 | **Real token accounting + hard deadline** — wire SemanticKernel usage into `RecordTokens` (currently dead code) and `CancellationTokenSource.CancelAfter(MaxExecutionTime)`. | Token budget is never enforced; a single slow LLM call blows the 30s budget. | ~2–3 hrs | Proposed |
| 5 | **Stop noise-entity persistence** — do not persist `keyword` / `statistical-candidate` terms as graph nodes; keep them retrieval-only. | Entity extraction LLM fallback and LazyGraphRAG statistical candidates pollute the graph. | ~1–2 hrs | Proposed |
| 6 | **Per-query retrieval trace** — expose LLM calls, tokens, nodes/edges traversed, pruning ratio, and which fallback strategy produced the answer. | GraphRAG `RetrieveAsync` is a long fallback cascade; the fired path is currently opaque. | ~2–4 hrs | Proposed |

**Suggested sequencing:** quick wins (1, 4, 5, 6) in one pass, then 2 and 3 as separate focused pieces.

**Total (agent):** ~3–4 working days including build/test verification and Docker smoke.
