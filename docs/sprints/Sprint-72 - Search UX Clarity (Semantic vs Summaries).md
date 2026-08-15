# Sprint 72 - Search UX Clarity (Semantic vs Summaries)

**Status:** Active (2026-08-15)

Full authority: this file. Sprint 71 (Lexicon Governance and Glossary Surface) is **complete, committed, and pushed** on `origin/master` (`1d5b06c`).

Promotes `docs/backlog/Search-UX-Clarity-Semantic-vs-Summaries.md` — the project-owner-directed follow-up from the 2026-08-15 product/UX review. Three clarity gaps in the search surfaces: (1) the Browse "Search files..." box does not say what it searches; (2) the GraphRAG/LazyGraphRAG summary search is invisible to end users (mode buttons gated behind `FeatureFlags:ShowInternalSearch`, default false); (3) when summaries get created and how they are managed is opaque, which matters on large KBs where summaries are load-bearing.

## Objective

Make the search surfaces self-explanatory without adding a new surface or changing how summaries are produced:

- **Browse search states what it does** — a caption under the box ("Searches file metadata (file name) — not document content.") plus an info icon explaining the fields it matches and pointing to Search Center for content search.
- **Summaries search becomes a first-class user mode** — Search Center exposes an always-visible **Semantic / Summaries** toggle. "Graph" / "LazyGraph" never appear in user-facing copy; the backend resolves the Summaries mode (GraphRAG-first, LazyGraphRAG-fallback).
- **Summary creation/management is de-murkified** — a user-facing "when do summaries exist" story (info icon on the Summaries mode) plus an admin-only status block in Search Center showing graph summary coverage and a "Re-cluster communities" action.

## Decisions (from the backlog item, settled 2026-08-15)

1. **Two user-facing search modes, gentle naming.** Search Center exposes **Semantic** (exact passages) and **Summaries** (higher-level synthesized answers). "Graph" / "LazyGraph" never appear in user-facing copy. The backend resolves the Summaries mode: prefer pre-built GraphRAG community summaries when they exist, fall back to LazyGraphRAG query-time traversal when they don't. WRAGS/LazyGraphRAG remain internal operator modes behind `ShowInternalSearch`.
2. **Info icon on both modes.** A click/hover info icon explains each mode for the curious — e.g. *"Summaries are generated from the connections between your documents. On large knowledge bases they may take time to appear."* — without cluttering the default view.
3. **Browse search states what it does.** A short caption under the box — *"Searches file metadata (file name) — not document content."* — plus an info icon explaining the fields it matches and pointing to Search Center for content search.
4. **De-murkify summary creation/management.** One user-facing story ("summaries exist once the graph has been built; they may take time on large KBs") plus an operator/admin path: an admin-only graph-summaries status block in Search Center (coverage counts + re-cluster action). Operator vocabulary stays admin-side.

## Deliverables

### 1. Summaries retrieval service (GraphRAG-first, LazyGraphRAG-fallback)
- `ISummariesRetrievalService` (`RAGS.Abstractions.Interfaces`) — `RetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken, IReadOnlyList<Guid>? sourceIds = null)`.
- `SummariesRetrievalService` (`RAGS.Application/GraphRAG`) — calls `IGraphRagService.RetrieveAsync`; returns its results when present, otherwise falls back to `ILazyGraphRagService.RetrieveAsync` (empty or failure). Both engines already carry the Sprint 64 `sourceIds` theme scope.

### 2. Summaries status service + snapshot
- `ISummariesStatusService` (`RAGS.Abstractions.Interfaces`) — `GetAsync(CancellationToken)` returning `Result<SummariesStatusSnapshot>`.
- `SummariesStatusSnapshot` / `SourceSummaryStatus` (`RAGS.Abstractions.Models`) — graph-level counts (GraphExists, NodeCount, EntityCount, CommunityCount, SummarizedCommunityCount, SourceCount) + per-source breakdown (SourceId, SourceName, EntityCount, CommunityCount, SummarizedCommunityCount).
- `SummariesStatusService` (`RAGS.Application/GraphRAG`) — reads all graph nodes/edges via `IGraphProvider`, classifies Source/Entity/Community/Chunk nodes, builds an entity→source map, aggregates per-source via `has_member` edges, counts summarized communities via a non-empty `summary` property.

### 3. SummariesController (Repository.API)
- `[Route("api/summaries")]`, `[Authorize]`. `GET retrieve` (query, topK, themes) — **NOT gated by `ShowInternalSearch`** (user-facing mode); resolves themes→sourceIds via `IKnowledgeThemeService` and calls `ISummariesRetrievalService`. `GET status` — `[Authorize(Roles = RoleDefinitions.Administrator)]` (operator surface).
- DI: both services registered as singletons in `Program.cs` (they depend on the singleton `IGraphProvider`).

### 4. Search Center Semantic/Summaries modes + info icons + admin status
- `SearchCenter.razor`: always-visible **Semantic** / **Summaries** mode buttons (WRAGS/GraphRAG/LazyGraphRAG stay behind `@if (ShowInternalSearch)`); `SetMode` allows `"semantic" or "summaries"` when internal search is off. Info icon (`&#9432;`) with per-mode `ModeInfoTitle`/`ModeInfoDetail`/`ToggleModeInfo`. Summaries mode calls `RepositoryApiClient.SummariesRetrieveAsync` (themes forwarded).
- Admin block (`<AuthorizeView Roles="Administrator">`): **Graph summaries** status card — coverage counts from `GetSummariesStatusAsync` + a **Re-cluster communities** button (`DetectClustersAsync` → `POST /api/communities/detect`). Copy states summaries are generated from the connections between documents — pre-built during ingestion, or on demand during a Summaries search.

### 5. Browse search caption + info icon
- `Browse.razor`: caption under the search box — "Searches file metadata (file name) — not document content." — plus an info icon (`&#9432;`) with `ToggleSearchInfo`/`_showSearchInfo` explaining the metadata fields and pointing to Search Center (which "matches by meaning across document chunks" and whose **Summaries** mode answers from the higher-level connections between documents).

### 6. Tests + docs
- **RAGS** (+14): `SummariesRetrievalServiceTests` (4 — uses graphrag when present, falls back when empty, falls back when fails, forwards sourceIds), `SummariesStatusServiceTests` (4 — empty graph, counts communities/summarized, per-source aggregation via has_member, community counted once per source), `SummariesControllerTests` (6 — retrieve OK/fail/themes, status OK, status admin-only attribute, retrieve not admin-only).
- **Web** (+9): `SearchCenterBindingTests` (6 — Semantic/Summaries buttons, internal modes gated, mode info icon, summaries-mode explanation, admin graph-summaries block, summaries-mode calls SummariesRetrieveAsync) + `BrowseBindingTests` (3 — metadata-filter caption, info icon, points to Search Center).
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived.

## Acceptance Criteria

- The Browse search box states it is a metadata (file name) filter, with an info icon explaining the fields and pointing to Search Center for content search.
- A normal user can choose **Semantic** or **Summaries** in Search Center without `ShowInternalSearch`; "Graph" / "LazyGraph" never appear in user-facing copy.
- The Summaries mode returns pre-built graph summaries when they exist and falls back to query-time traversal otherwise; theme scope is forwarded.
- An admin sees graph-summary coverage in Search Center and can re-cluster communities.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- A new dedicated surface for browsing summaries (the Search Center mode toggle is the surface).
- Renaming the internal GraphRAG/LazyGraphRAG services, controllers, or API routes (internal code/docs may keep the terms; only user-facing copy changes).
- Changing how summaries are produced (GraphRAG ingest-time vs LazyGraphRAG query-time behavior is untouched).
- Per-document summary status in the Repository Browser (the Sprint 69 Ingestion column pattern) — documented follow-up; this sprint ships the admin graph-level status block instead.
- Making summary generation distributed or multi-host.

---

## Implementation Status

**Implemented (2026-08-15).** All 6 items complete; tests green.

### Item 1 — Summaries retrieval service
- `ISummariesRetrievalService` + `SummariesRetrievalService` (`RAGS.Application/GraphRAG`): GraphRAG-first, LazyGraphRAG-fallback. `GraphRagService.RetrieveAsync` already has an internal fallback chain (summaries → lazy enrichment → graph-aware → semantic), so the service-level fallback triggers only on empty/failure.

### Item 2 — Summaries status service
- `ISummariesStatusService` + `SummariesStatusService` + `SummariesStatusSnapshot`/`SourceSummaryStatus`. Community summaries are persisted as a `summary` property on Community nodes during GraphRAG ingest, making the coverage counts valid.

### Item 3 — SummariesController
- `[Route("api/summaries")]`; `GET retrieve` (user-facing, not gated by `ShowInternalSearch`, themes→sourceIds) + `GET status` (Administrator). Both services registered as singletons in `Program.cs`.

### Item 4 — Search Center modes + info icons + admin status
- Always-visible **Semantic** / **Summaries** buttons; WRAGS/GraphRAG/LazyGraphRAG stay behind `ShowInternalSearch`. Per-mode info icon. Admin **Graph summaries** block with coverage counts + **Re-cluster communities** button.

### Item 5 — Browse search caption + info icon
- Caption "Searches file metadata (file name) — not document content." + info icon explaining the metadata fields and pointing to Search Center.

### Item 6 — Tests + docs
- **RAGS 359 (+14)**: `SummariesRetrievalServiceTests` (4), `SummariesStatusServiceTests` (4), `SummariesControllerTests` (6).
- **Web 100 (+9)**: `SearchCenterBindingTests` (6) + `BrowseBindingTests` (3).
- Repository 151 / Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh `/search` (Semantic/Summaries toggle + admin Graph summaries block) and `/browse` (search caption + info icon) for a live visual check. No schema migration — this sprint is API + Web only.
