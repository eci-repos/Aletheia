# Sprint 79 - Graph Orphan-Nodes Toggle

**Status:** Active (2026-08-17)

Full authority: this file. Sprint 78 (Right Rail Strip Width) is **complete, committed, and pushed** on `origin/master` (`6a1b1ea`).

Promotes `docs/backlog/Graph-Orphan-Nodes-Toggle.md` — the project-owner-directed **Web-only** Graph Explorer pass: orphan nodes (zero edges) clutter the graph view, and the owner asked for a way to toggle them on and off, **off by default**.

## Objective

One small Web-only Graph Explorer filter pass (no API/backend changes, no schema migration, no `wwwroot/index.html` JS changes):

1. **Orphan toggle + filter.** Add a "Show orphan nodes (technical)" checkbox to the Graph Explorer's `.context-mode` block (next to "Show chunk nodes (technical)"), **off by default** — orphans hidden. Degree is computed locally per node as the count of edges where the node is `SourceId` or `TargetId`; a node with degree 0 is an orphan. A new `ApplyOrphanFilter()` runs from `ApplyGraphScope` **after** `ApplyChunkFilter()` and filters `_nodes`/`_edges`; `_pathFrom`/`_pathTo` are cleared if they reference a removed node (same pattern as `ApplyChunkFilter`).
2. **Tests.** Web binding tests: checkbox present + off by default + the orphan filter runs after the chunk filter; a logic test on a public static `FilterOrphans` helper verifies degree-0 nodes are dropped (and edges to dropped nodes are dropped).
3. **Docs.** AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived when complete.

## Decisions (from the backlog item, settled 2026-08-17)

1. **Client-side orphan filter, off by default.** The checkbox mirrors the existing `_showChunkNodes` toggle (`ToggleChunkNodesAsync` handler + `ApplyChunkFilter` pattern).
2. **Degree computed locally — no API change.** The page already holds `_allNodes`/`_allEdges`; degree is computed per node as the count of edges where the node is `SourceId` or `TargetId` (computed on the current post-context/post-chunk `_edges`, so "orphan" means *isolated in the rendered view* — a node whose only edges were to filtered chunk nodes is an orphan). A node with degree 0 is an orphan.
3. **Web-only.** No API, backend, schema, or JS changes. Cytoscape already renders isolated nodes fine; the existing `.context-mode .form-check` CSS already styles a new checkbox.

## Deliverables

### 1. Orphan toggle + filter (`src/Aletheia.Web/Pages/GraphExplorer.razor`)
- Field: `private bool _showOrphanNodes;` (no initializer → `false`, off by default).
- Checkbox in `.context-mode`: `Show orphan nodes (technical)` with `checked="@_showOrphanNodes"` + `@onchange="ToggleOrphanNodesAsync"` — while in there, fix the pre-existing label nesting so each `.form-check` label is properly closed (the full-graph and chunk checkboxes were nested labels).
- Handler `ToggleOrphanNodesAsync(ChangeEventArgs)` — sets `_showOrphanNodes` and re-runs `ApplyGraphScope()` (mirror of `ToggleChunkNodesAsync`).
- `ApplyGraphScope` calls `ApplyOrphanFilter()` immediately after `ApplyChunkFilter()`.
- `public static (List<GraphNode> Nodes, List<GraphEdge> Edges) FilterOrphans(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)` — degree per node from edges (`SelectMany` SourceId+TargetId, group by id); keeps nodes with degree > 0 and edges whose **both** endpoints are kept (matches `FilterGraph`/`ApplyChunkFilter` conventions; public static so it is unit-testable like `FilterGraph`).
- Private `ApplyOrphanFilter()` — no-op when `_showOrphanNodes` is true or `_nodes`/`_edges` are null; otherwise `(_nodes, _edges) = FilterOrphans(_nodes, _edges)` and clear `_pathFrom`/`_pathTo` when they reference a removed node.

### 2. Tests (`tests/Aletheia.Web.UnitTests/GraphExplorerTests.cs` + `GraphExplorerBindingTests.cs`)
- `GraphExplorerTests.FilterOrphans_drops_degree_zero_nodes` (and edges to dropped nodes are dropped; degree-1+ nodes kept).
- `GraphExplorerBindingTests.GraphExplorer_has_orphan_toggle_off_by_default` — checkbox text, handler, `_showOrphanNodes` declared without an initializer (false), and `ApplyOrphanFilter();` appears **after** `ApplyChunkFilter();` in `ApplyGraphScope`.
- `GraphExplorerBindingTests.GraphExplorer_orphan_filter_clears_path_selects_for_removed_nodes` — the path-clearing guard pattern mirrors `ApplyChunkFilter` (`!keptIds.Contains(_pathFrom)` / `!keptIds.Contains(_pathTo)` inside `ApplyOrphanFilter`).

### 3. Docs
- AGENTS (new Sprint 79 section), CLAUDE (Current state), File 02/03, this sprint file; backlog item archived when complete.

## Acceptance Criteria

- The `.context-mode` block shows **Explore full graph**, **Show chunk nodes (technical)**, and **Show orphan nodes (technical)**, the last **unchecked by default**.
- With the toggle off, nodes with zero edges in the current view are not rendered (nor are any edges that would dangle); toggling it on restores them.
- The path From/To selects are cleared if they reference a node removed by the filter.
- Web + Repository + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Server-side orphan detection/counting — `GraphHealth.OrphanNodes` is hardcoded to 0 (`GraphAnalyticsService.ComputeGraphHealthAsync`, `// TODO: compute orphan nodes`); a separate analytics concern.
- Auto-repairing/removing orphan nodes — `GraphAdminService.RepairGraphAsync` is a no-op; a separate concern.
- Any change to the existing "Show chunk nodes (technical)" toggle or the drag-group/zoom behavior.
- Any API, backend, schema, or `wwwroot/index.html` change.

---

## Implementation Status

**Implemented (2026-08-17).** All 3 items complete; tests green.

### Item 1 — Orphan toggle + filter
- `GraphExplorer.razor`: `private bool _showOrphanNodes;` (off by default); "Show orphan nodes (technical)" checkbox in `.context-mode` (`checked="@_showOrphanNodes"` + `ToggleOrphanNodesAsync`); `ToggleOrphanNodesAsync(ChangeEventArgs)` mirrors `ToggleChunkNodesAsync`. The `.context-mode` label nesting was fixed (the full-graph + chunk checkboxes were previously nested `<label>`s — now three properly-closed sibling `.form-check` labels).
- `ApplyGraphScope` calls `ApplyOrphanFilter()` immediately after `ApplyChunkFilter()`.
- `public static FilterOrphans(nodes, edges)` computes degree per node from the current edges (`SelectMany` over `SourceId`/`TargetId`, grouped by id) and keeps nodes with degree > 0 plus edges whose both endpoints are kept.
- Private `ApplyOrphanFilter()` — no-op when the toggle is on or `_nodes`/`_edges` are null; otherwise applies `FilterOrphans` and clears `_pathFrom`/`_pathTo` when they reference a removed node.

### Item 2 — Tests
- **Web 151 (+5)**: `GraphExplorerTests` — `FilterOrphans_drops_degree_zero_nodes_and_keeps_edges_between_kept_nodes` (degree-0 node dropped, degree-1+ nodes kept), `FilterOrphans_keeps_all_nodes_when_every_node_has_an_edge` (no orphans → no change), `FilterOrphans_drops_edges_that_reference_a_node_outside_the_node_set` (edges to a dropped/absent endpoint are dropped). `GraphExplorerBindingTests` — `GraphExplorer_has_orphan_toggle_off_by_default` (checkbox text + handler + `_showOrphanNodes` declared without an initializer + `ApplyOrphanFilter();` appears after `ApplyChunkFilter();`) + `GraphExplorer_orphan_filter_clears_path_selects_for_removed_nodes` (path-clearing guard mirrors `ApplyChunkFilter`, scoped to the method body). Web count 146 → 151.
- Foundation 55 / Repository 172 / RAGS 369 unchanged; build 0 errors; docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh `/graph` — the `.context-mode` block lists three checkboxes; **Show orphan nodes (technical)** is off by default, so isolated circles (zero-edge sources/entities/communities) are hidden; check it to render them again. No schema migration — Web-only.
