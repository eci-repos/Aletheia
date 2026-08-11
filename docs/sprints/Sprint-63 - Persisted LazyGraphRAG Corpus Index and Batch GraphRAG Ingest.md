# Sprint 63 - Persisted LazyGraphRAG Corpus Index and Batch GraphRAG Ingest

**Status:** Active (2026-08-11)

Full authority: this file. Sprint 62 (GraphRAG Soft Deadline and Reembed Parity) is **complete, committed, and pushed** (`26995d9` on `origin/master`). Its optional Docker smoke test (reembed speed + `semantic-timeout-fallback` trace under LLM saturation) is user-side and can run in parallel.

Promotes backlog items 2 and 3 from `docs/backlog/GraphRAG-LazyGraphRAG-Enhancements.md` — the two remaining parked items from the GraphRAG/LazyGraphRAG enhancement backlog.

## Objective

Two infrastructure hardening items for the GraphRAG / LazyGraphRAG retrieval paths, delivered in one pass because they both touch the ingestion/retrieval data path:

1. **Persist the LazyGraphRAG corpus index to PostgreSQL** — the in-memory `CorpusDiscoveryIndex` (singleton) is lost on restart and invisible to a second instance, so a fresh instance sees an empty corpus and LazyGraphRAG candidate selection degrades until the corpus is re-discovered. Persist term frequency / doc frequency / avg doc length so the corpus survives restart and multi-instance.
2. **Batch GraphRAG ingest** — the full graph-intelligence ingest path (`UploadedContentKnowledgeIndexer.IndexAsync` / `GraphRagService.IngestAsync`) is serial N+1: per-chunk LLM extraction and per-chunk Neo4j writes, plus community re-clustering that is O(graph) on every upload. Batch the Neo4j writes (UNWIND), bound LLM concurrency, and gate community re-clustering so large-document ingest is fast and cheap.

## Background

- **Corpus index (item 2):** `LazyGraphRagService` maintains an in-memory `CorpusDiscoveryIndex` (term frequency, doc frequency, avg doc length) used for statistical candidate selection during retrieval. It is a singleton, so a container restart or a second API instance starts with an empty corpus and must re-discover it from scratch.
- **Batch ingest (item 3):** the full `IndexAsync` path (used by repair, chat hydration, and — before Sprint 62 — reembed) runs per chunk: entity discovery + node summaries + relationship extraction + community detection + community summaries, each a serial LLM call, and each graph write a separate Neo4j round-trip. Community re-clustering runs on every upload and is O(graph). Sprint 62 moved reembed to the lightweight path, but repair/chat hydration still pay the full cost.

## Deliverables

### 1. Persist the LazyGraphRAG corpus index (item 2)
- New PostgreSQL tables (migration + `init.sql` in sync) for the corpus index: term frequency, document frequency, and corpus statistics (avg doc length, doc count). Follow the existing RAGS PostgreSQL repository pattern (Dapper + `PostgreSqlConnectionFactory`).
- New `ICorpusIndexRepository` → `PostgreSqlCorpusIndexRepository`; wire into `LazyGraphRagService` so the corpus index is loaded at startup and persisted incrementally as it is discovered/updated during retrieval.
- The in-memory index remains the hot path; persistence is a write-through/load-on-start so restart and multi-instance see the same corpus.

### 2. Batch GraphRAG ingest (item 3)
- **UNWIND-based Neo4j writes:** add a batch write path to the graph provider (or a batch helper) so chunk/entity/relationship/community writes for a document are sent as `UNWIND` Cypher statements instead of N+1 round-trips.
- **Bounded-concurrency LLM extraction:** run per-chunk entity/relationship extraction with bounded concurrency (e.g. `Parallel.ForEachAsync` with a small `MaxDegreeOfParallelism` or a `SemaphoreSlim`) instead of fully serial.
- **Gate community re-clustering:** only re-run community detection when the graph has materially changed (e.g. new source added) rather than on every upload; keep the existing behavior for the first ingest of a source.

### 3. Tests
- RAGS.UnitTests / Repository.UnitTests: corpus-index repository round-trip (persist → load → same stats), restart-survival semantics, batch-write path produces the same graph as the serial path, bounded concurrency does not exceed the limit, community re-clustering is gated.
- Existing suites remain green.

### 4. Docs
- `docs/Architecture.md`, `docs/OperationsGuide.md`, `docs/Development-Guidelines.md`, AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, sprint handoff updated. Backlog items 2 + 3 statuses updated.

## Acceptance Criteria

- A LazyGraphRAG retrieval after a restart (or on a second instance) sees the persisted corpus — candidate selection does not start from an empty corpus.
- The corpus index is persisted incrementally as it is discovered/updated, with no regression to the in-memory hot path.
- A large-document full ingest issues batched (UNWIND) Neo4j writes and bounded-concurrency LLM calls instead of serial N+1; the resulting graph is equivalent to the serial path.
- Community re-clustering is gated so it does not run O(graph) on every upload.
- Repository / RAGS / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Theme-aware graph retrieval (Canonical backlog item 5).
- New queue providers, session stores, or changes to the chat approval/settings surface.
- Changing the lightweight reembed path (Sprint 62) or the soft-deadline behavior (Sprint 62).

---

## Implementation Status (2026-08-11)

**Implementation complete.** Both items implemented, tested, and documented. Pending: commit + push.

### 1. Persist the LazyGraphRAG corpus index (item 2) — DONE
- New `ICorpusIndexRepository` (`src/RAGS.Abstractions/Interfaces/ICorpusIndexRepository.cs`) with `UpsertDocumentAsync(sourceId, termFrequency, documentLength, ct)` and `LoadAsync(ct)`; models `CorpusIndexSnapshot` (with `Documents`) and `CorpusDocumentIndex` (`SourceId`, `DocumentLength`, `TermFrequency`).
- New `PostgreSqlCorpusIndexRepository` (`src/RAGS.Infrastructure.PostgreSQL/CorpusIndex/`) — Dapper + `PostgreSqlConnectionFactory`, following the `PostgreSqlWikiPageRepository` pattern. `UpsertDocumentAsync` runs a transaction: upsert `lazygraphrag_corpus_documents` (`ON CONFLICT (source_id) DO UPDATE`), delete + reinsert `lazygraphrag_corpus_terms`. `LoadAsync` LEFT JOINs both tables and reconstructs the snapshot. Document count / avg doc length are derived from the documents table — no separate statistics row.
- New idempotent migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-11-lazygraphrag-corpus-index.sql` + matching tables appended to `scripts/init.sql` (`lazygraphrag_corpus_documents` + `lazygraphrag_corpus_terms` with a term index).
- `CorpusDiscoveryIndex` ctor now takes `(ICorpusIndexRepository? repository = null, ILogger<CorpusDiscoveryIndex>? logger = null)`; when a repository is supplied it loads the persisted corpus at startup (best-effort — a load failure logs a warning and starts empty) and `IndexAsync` does a best-effort write-through upsert (a persistence failure logs a warning and never fails ingestion — the in-memory index stays authoritative).
- `Repository.API/Program.cs` registers `AddSingleton<ICorpusIndexRepository, PostgreSqlCorpusIndexRepository>()` next to the `ICorpusDiscoveryIndex` registration.

### 2. Batch GraphRAG ingest (item 3) — DONE
- **Batched graph writes:** `IGraphProvider` gained `CreateNodesAsync`, `CreateRelationshipsAsync`, and `UpdateNodesAsync` with **default interface implementations** that fall back to per-item calls (so existing test fakes — `MockGraphProvider`, `MemoryGraphProvider` — keep compiling). `Neo4jGraphProvider` implements them with `UNWIND $rows AS row` Cypher: nodes/updates grouped by `BuildNodeLabels(type)` (dynamic labels can't be set per-row), relationships grouped by `NormalizeToken(RelationshipType, "related_to")` (dynamic type can't be set per-row).
- **Bounded-concurrency LLM extraction:** both full-ingest paths (`UploadedContentKnowledgeIndexer.PersistGraphIntelligenceAsync` and `GraphRagService.IngestAsync`) were refactored into 4 phases with `private const int MaxLlmConcurrency = 4`:
  1. Bounded-concurrency per-chunk entity + relationship extraction (`SemaphoreSlim(MaxLlmConcurrency)` + `Task.WhenAll`; within a chunk the relationship pass stays sequential on its entities).
  2. Build all nodes/edges, then one `CreateNodesAsync` + one `CreateRelationshipsAsync` per label/type group.
  3. Bounded-concurrency entity summaries (deduped via `createdEntityIds`).
  4. **Gated community detection** — `SourceNodeExistsAsync` (via `GetNodeAsync`) is checked **before** the source node is created; community detection + bounded-concurrency community summaries + `UpdateNodesAsync` run only when `!sourceExists` (first ingest of a source). Re-ingests of an existing source skip the O(graph) re-cluster; retrieval-time discovery still re-clusters on cache miss.
- `ChunkExtraction` record `(Chunk Chunk, IReadOnlyList<ExtractedEntity> Entities, IReadOnlyList<ExtractedRelationship>? Relationships)` shared by both paths.

### 3. Tests — DONE
- RAGS.UnitTests **281** (was 272): new `CorpusDiscoveryIndexTests` (4 — write-through persists to the repository, constructor loads the persisted corpus so restart sees the same corpus, persistence failure never fails ingestion, load failure starts empty), `PostgreSqlCorpusIndexRepositoryTests` (1 live-DB round-trip — try-connect / `catch { return; }` skip when PostgreSQL is unavailable, idempotent `EnsureSchemaAsync`, cleanup in `finally`), and `GraphRagServiceTests` (+4 — `IngestAsync_uses_batched_graph_writes` via `BatchRecordingGraphProvider`, `IngestAsync_bounded_concurrency_does_not_exceed_limit` via `ConcurrencyTrackingEntityExtractionService`, `IngestAsync_runs_community_detection_for_new_source` + `IngestAsync_skips_community_detection_for_existing_source` via `CountingCommunityDetectionService`). `CreateService` gained an optional `ICommunityDetectionService? communityDetection` param.
- Repository 130 / Foundation 55 / Aletheia.Web.UnitTests 46 unchanged; `dotnet build Aletheia.slnx` succeeds (pre-existing AngleSharp NU1902 warning only).

### 4. Docs — DONE
- `docs/Architecture.md` (corpus-index persistence + batched ingest paragraphs), `docs/OperationsGuide.md` (corpus-index restart note + batched-ingest monitoring), `docs/Development-Guidelines.md` (batch-write + bounded-concurrency + community-gate contract), AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, this sprint file updated. Backlog items 2 + 3 marked promoted.

## Remaining
- **Commit + push** (autonomous per standing instruction).
- Optional Docker smoke test: restart the API container and confirm a LazyGraphRAG retrieval sees the persisted corpus without re-discovery; upload a large document and confirm batched (UNWIND) writes + bounded LLM concurrency in the Neo4j/API logs.
