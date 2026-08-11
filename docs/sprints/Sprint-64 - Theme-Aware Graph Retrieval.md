# Sprint 64 - Theme-Aware Graph Retrieval

**Status:** Active (2026-08-11)

Full authority: this file. Sprint 63 (Persisted LazyGraphRAG Corpus Index and Batch GraphRAG Ingest) is **complete, committed, and pushed** (`df7627d` on `origin/master`).

Promotes backlog item 5 from `docs/backlog/archive/Canonical-Form-Themes-Filtering-Enhancements.md` — the last remaining item in that backlog.

## Objective

Extend theme enforcement to the GraphRAG / LazyGraphRAG retrieval paths and to global (community-summary) search. Today theme filtering works only on the semantic RAG path: `RetrievalRequest.SourceIds` → PgVectorStore set predicate. Graph modes are explicitly out of scope for theme filtering (Sprint 58 boundary), so a user scoping Search Center to a theme gets theme-filtered semantic results but unfiltered GraphRAG / LazyGraphRAG / global-graph results — an inconsistent view of the estate.

## Background

- **Theme model:** `file_metadata.theme` is a `text[]` set; `KnowledgeThemeService.ResolveSourceIdsAsync(themes)` resolves theme names → source ids with match-any semantics. The Search Center theme scope (Sprint 59) currently applies to Semantic search only.
- **Graph node model:** entity/chunk/relationship nodes carry `["sourceId"]` in `Properties`; source nodes have `Id == sourceId.ToString()`, `Type == "Source"`, and no `sourceId` property. `GraphCommunity.MemberIds` are node ids, so mapping a community to its sources requires a node→source map (via `IGraphProvider.GetNodesAsync()`).
- **Retrieval paths:**
  - `GraphRagService.RetrieveAsync` — resolves query entities, expands to communities, summarizes, builds context; falls back to semantic `RetrievalRequest(query, topK)` and entity-based multi-hop expansion `RetrievalRequest(node.Label, ...)`.
  - `LazyGraphRagService.RetrieveAsync` — seeds candidates from `_corpusIndex.SearchCorpus(query, topK: 10)`, discovers entities at query time, traverses a temporary graph, resolves communities + summaries; falls back to semantic `RetrievalRequest(query, topK)` and expansion calls.
  - `GlobalGraphSearchService.SearchAsync` — discovers communities, selects top-level communities, summarizes each, builds context, maps/reduces; citations come from `community.MemberIds.Take(5)`.

## Deliverables

### 1. Theme scope on GraphRAG / LazyGraphRAG retrieval
- Add an optional `IReadOnlyList<Guid>? sourceIds = null` parameter (after `cancellationToken`) to `IGraphRagService.RetrieveAsync` / `GlobalSearchAsync` and `ILazyGraphRagService.RetrieveAsync` / `GlobalSearchAsync`, and to `IGlobalGraphSearchService.SearchAsync`. When null, behavior is unchanged (no theme scope).
- New `GraphThemeScope` static helper in RAGS.Application: given a node, resolve its source id (`Properties["sourceId"]`, falling back to `Type == "Source"` → `Id`); given a set of nodes and a source-id allowlist, filter to nodes whose source is in the allowlist.
- `GraphRagService.RetrieveAsync`: filter resolved entities to the allowlist before community resolution; scope the semantic fallback and entity-expansion `RetrievalRequest`s with `sourceIds`.
- `LazyGraphRagService.RetrieveAsync`: filter the corpus seed sources (`SearchCorpus` results) to the allowlist before entity discovery; scope the semantic fallback and expansion `RetrievalRequest`s with `sourceIds`.
- `GlobalGraphSearchService.SearchAsync`: when scoped, build a node→source map (via `IGraphProvider.GetNodesAsync()`) and filter communities to those whose members all (or any — match-any semantics) belong to the allowlist before summarizing.

### 2. API + Web wiring
- `GraphRagController` / `LazyGraphRagController`: accept `?themes=` (comma-separated), resolve via `IKnowledgeThemeService.ResolveSourceIdsAsync` (following the `RagsController` pattern with an optional `IKnowledgeThemeService? themeService = null` ctor param), pass `sourceIds` through. Both `Retrieve` and `GlobalSearch` endpoints.
- `RepositoryApiClient.GraphRagRetrieveAsync` / `LazyGraphRagRetrieveAsync`: accept themes and append `&themes=`.
- `SearchCenter.razor`: pass `_selectedThemes` to the GraphRAG / LazyGraphRAG retrieve calls; update the "Theme scope applies to Semantic search only." note to reflect that graph modes now honor the scope.

### 3. Tests
- RAGS.UnitTests: theme-scoped GraphRAG retrieval filters entities/communities; theme-scoped LazyGraphRAG retrieval filters corpus seeds; `GlobalGraphSearchService` theme-scoped search filters communities; controllers pass themes through to the service.
- Existing suites remain green.

### 4. Docs
- `docs/Architecture.md`, `docs/OperationsGuide.md`, `docs/Development-Guidelines.md`, AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, sprint handoff updated. Backlog item 5 status updated.

## Acceptance Criteria

- A Search Center theme scope filters GraphRAG / LazyGraphRAG retrieval results and global-graph (community summary) results to documents in the selected themes.
- No theme scope → behavior identical to pre-Sprint-64 (graph modes unfiltered).
- Semantic fallback and entity-expansion `RetrievalRequest`s inside graph retrieval carry the same `sourceIds` scope, so a graph query that degrades to semantic retrieval stays theme-scoped.
- Repository / RAGS / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- New theme model changes (multi-theme, backfill — already done in Sprint 59).
- Copilot session theme filter changes (already theme-scoped via `RetrievalRequest.SourceIds`).
- Wiki / WRAGS theme scoping (Wiki stays curated).
- Changing the per-request traversal budget, token accounting, or soft-deadline behavior (Sprint 60/62).

---

## Implementation Status (2026-08-11)

**Complete.** All deliverables implemented, tested, committed, and pushed.

- **Deliverable 1 — Theme scope on graph retrieval:** `sourceIds` params added to `IGraphRagService` / `ILazyGraphRagService` (`RetrieveAsync` / `GlobalSearchAsync`) and `IGlobalGraphSearchService.SearchAsync` (all optional, after `cancellationToken`). New `GraphThemeScope` static helper (`TryGetSourceId`, `IsInScope`, `FilterNodes`, `ToAllowSet`, `CommunityHasMemberInScope`). `GraphRagService.RetrieveAsync` filters resolved entities + multi-hop expansion nodes to the allowlist and scopes semantic fallback / entity-expansion `RetrievalRequest`s; `LazyGraphRagService.RetrieveAsync` filters corpus seed sources and scopes semantic fallback / expansion requests; `GlobalGraphSearchService.SearchAsync` builds a node→source map via `IGraphProvider.GetNodesAsync()` and filters communities with match-any semantics (returns `Failure("No communities in the selected themes.")` when scoped and empty).
- **Deliverable 2 — API + Web wiring:** `GraphRagController` / `LazyGraphRagController` accept `?themes=` (comma-separated) on `Retrieve` and `GlobalSearch`, resolve via optional `IKnowledgeThemeService? themeService = null` ctor param (RagsController pattern), pass `sourceIds` through. `RepositoryApiClient.GraphRagRetrieveAsync` / `LazyGraphRagRetrieveAsync` accept themes and append `&themes=`. `SearchCenter.razor` passes `_selectedThemes` to graph-mode retrieve calls; the WRAGS note now reads "Theme scope does not apply to WRAGS search."
- **Deliverable 3 — Tests:** RAGS suite 289 green (was 281). New tests: `GraphRagServiceTests.RetrieveAsync_theme_scope_filters_resolved_entities`, `SearchAsync_theme_scope_filters_communities_by_member_source`, `LazyGraphRagServiceTests.RetrieveAsync_theme_scope_flows_source_ids_to_semantic_retrieval`, `RetrieveAsync_theme_scope_filters_corpus_seed_sources`, plus controller themes pass-through tests in `GraphRagControllerTests` / `LazyGraphRagControllerTests` (mock `IKnowledgeThemeService`). All fakes updated for the new signatures.
- **Deliverable 4 — Docs:** this file, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, AGENTS.md, CLAUDE.md, `docs/Architecture.md`, `docs/OperationsGuide.md`, `docs/Development-Guidelines.md`, and the backlog item updated.
- **Verification:** `dotnet build Aletheia.slnx` succeeds (only pre-existing AngleSharp NU1902 warning); RAGS 289 / Repository 130 / Foundation 55 / Web 46 all green.
