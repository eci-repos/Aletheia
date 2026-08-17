# Backlog: Graph Orphan-Nodes Toggle

**Status:** **Complete** — items 1 + 2 implemented (Sprint 79, 2026-08-17). Archived.
**Created:** 2026-08-16
**Source:** Project-owner request (2026-08-16) — "in the Graph can 'orphan' nodes be toggled on and off (off by default) if so, create a backlog item to manage this."

## Problem

- **Orphan nodes (zero edges) exist in the graph data.** Several code paths create nodes with no relationships: a `Source` node is created unconditionally on upload (`UploadedContentKnowledgeIndexer.cs` ~line 383) and is an orphan when chunking yields no chunks and no entities are extracted; query-time discovered entities are persisted with **no edges at all** (`LazyEntityDiscoveryService.cs` lines 73–83); a community node can be left with zero edges after its members are `DETACH DELETE`d (`CommunityDetectionService.cs` lines 130–158). The graph API returns every node regardless of degree (`Neo4jGraphProvider.GetNodesAsync` — `MATCH (n) ... RETURN n`, no relationship filter), so orphans render as isolated circles in the Graph Explorer.
- **There is no way to hide them.** The Graph Explorer has a "Show chunk nodes (technical)" toggle (`_showChunkNodes` in `Pages/GraphExplorer.razor`) but no degree/orphan filtering. Orphans clutter the view — especially after a re-ingest or when many query-time entities were discovered — and there is no way to tell "isolated node" from "node that belongs to a cluster" at a glance.

## Decisions (proposed approach)

1. **Client-side orphan filter, off by default.** Add a "Show orphan nodes (technical)" checkbox to the Graph Explorer's `.context-mode` block (next to "Show chunk nodes (technical)"), **off by default** — orphans hidden. When on, orphans render. Mirrors the existing `_showChunkNodes` toggle (`ToggleChunkNodesAsync` handler + `ApplyChunkFilter` pattern).
2. **Degree computed locally — no API change.** The page already holds `_allNodes`/`_allEdges`; compute degree per node as the count of edges where the node is `SourceId` or `TargetId`. A node with degree 0 is an orphan. A new `ApplyOrphanFilter()` runs from `ApplyGraphScope` **after** `ApplyChunkFilter()` and filters `_nodes`/`_edges`; clear `_pathFrom`/`_pathTo` if they reference a removed node (same pattern as `ApplyChunkFilter` lines 454–462).
3. **Web-only.** No API, backend, or schema changes. No `wwwroot/index.html` JS changes — Cytoscape already renders isolated nodes fine; the existing `.context-mode .form-check` CSS already styles a new checkbox.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Orphan toggle + filter** — `_showOrphanNodes` bool (default `false`), "Show orphan nodes (technical)" checkbox in `.context-mode`, `ToggleOrphanNodesAsync` handler, `ApplyOrphanFilter()` (degree computed from `_edges`; drops degree-0 nodes + their edges; clears `_pathFrom`/`_pathTo` if needed). | The explicit ask: hide zero-edge clutter, off by default. | ~0.25 day | Proposed |
| 2 | **Tests + docs** — Web binding test: checkbox present + off by default + filter drops degree-0 nodes (and the path selects are cleared when they reference a removed node); AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The Graph Explorer filter contract is locked down. | ~0.25 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — one small Graph Explorer filter pass (both are `GraphExplorer.razor` + binding tests).

**Total (agent):** ~0.5 working day including build/test verification — a single small sprint.

## Out of Scope

- Server-side orphan detection/counting — the `GraphHealth.OrphanNodes` property exists but is hardcoded to 0 (`GraphAnalyticsService.ComputeGraphHealthAsync` line 65, `// TODO: compute orphan nodes`); that is a separate analytics concern.
- Auto-repairing/removing orphan nodes — `GraphAdminService.RepairGraphAsync` is a no-op (`// TODO: Implement graph repair`); a separate concern.
- Changing graph node/edge data, API contracts, or backend behavior — Web-only (no schema migration).
- Any change to the existing "Show chunk nodes (technical)" toggle or the drag-group/zoom behavior.
