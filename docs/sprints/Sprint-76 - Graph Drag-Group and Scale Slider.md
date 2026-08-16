# Sprint 76 - Graph Drag-Group and Scale Slider

**Status:** Active (2026-08-16)

Full authority: this file. Sprint 75 (Activity and Chats Right Rail) is **complete, committed, and pushed** on `origin/master` (`4899ab3`).

Promotes `docs/backlog/Graph-Drag-Group-and-Scale-Slider.md` — the project-owner request from the 2026-08-15 session: "would it be possible in the Graph to drag a 'root' node that represents the source document and all children that are solely based on this doc be dragged at the same time?... plan B is good, lets add a scale slider that also allow the user to put a scaling factor since scaling the graph now is difficult." Owner confirmed **Plan B** (flat rendering — no compound/parent nodes) plus the scale slider.

## Objective

One coherent Graph Explorer UX pass (Web-only — no API/backend changes, no schema migration):

1. **Drag-group on source nodes.** In the Graph Explorer, dragging a source-document node (type `Source`) also moves the entity nodes attributed to it via `found_in` edges — but only those **solely based on this doc** (a child found in multiple documents stays put). Flat rendering preserved: the group is delta translation, not hierarchy.
2. **Scale slider + numeric scaling factor.** An explicit zoom control in the Graph Explorer toolbar: a range slider (25%–300%) plus a numeric factor input, wired to `cy.zoom({ level })`. The existing zoom-threshold label logic re-runs automatically at the new scale. A **Fit** button resets the view (already present in the toolbar).
3. **Tests + docs.** Web binding tests lock down the `initGraph` drag-group contract and the zoom control; AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived when complete.

## Decisions (from the backlog item, settled 2026-08-15)

1. **Custom drag-group on source nodes (Plan B, flat rendering).** Keep the current flat graph look — no compound/parent nodes. In `initGraph`, hook the Cytoscape `grab`/`drag`/`free` events: on grab of a `Source` node, compute the group = the node itself + the nodes connected to it via `found_in` edges whose **only** source connection is this document. On each drag tick, translate every group member by the dragged node's position delta; release on `free`. Positions already persist across re-renders via the existing `preservePositions` flow, so a moved cluster stays where the user put it.
2. **Scale slider + numeric factor.** A Graph Explorer control (Blazor, in `GraphExplorer.razor`): a `range` slider (25%–300%) plus a numeric input showing the current factor, wired to `setGraphZoom(factor)` which applies `window.cy.zoom({ level: factor })`. The existing zoom-dependent label rendering (`updateLabels` on the `'zoom'` event) re-runs automatically at the new scale, so the two features compose. A **Fit** button (`cy.fit()`) resets the view.
3. **Web-only.** No API, backend, or schema changes. All behavior lives in `index.html` JS + `GraphExplorer.razor` markup/handlers.

## Deliverables

### 1. Drag-group on source nodes (`wwwroot/index.html` — `initGraph`)
- Edge elements gain `relationshipType: e.relationshipType` in their data (the exclusivity check reads `edge.data('relationshipType')`; `label` alone is the display label).
- `grab`/`drag`/`free` handlers on `window.cy`: on grab of a `SourceDocument` node, `computeDragGroup` returns the node + every non-source node whose `found_in` edges all point to this document; on each drag tick, translate group members by the dragged node's delta from its grab-time position; on `free`, clear the group state.

### 2. Scale slider + numeric factor + Fit (`Pages/GraphExplorer.razor` + `.razor.css`, `index.html`)
- Toolbar gains a `.graph-zoom` control: `#graph-zoom-slider` (range 25–300, step 5) + `#graph-zoom-factor` (number 0.25–3, step 0.05) + a `×` unit. Slider `@oninput` / number `@onchange` update `_zoomFactor` (clamped 0.25–3.0) and call `setGraphZoom`.
- `window.setGraphZoom(factor)` clamps and applies `cy.zoom({ level })`; `window.getGraphZoom()` returns the current zoom. `OnGraphLayoutSettled` and `FitGraphAsync` sync `_zoomFactor` from the graph so the control reflects the actual zoom after layout/fit.
- The existing **Fit** button (`FitGraphAsync` → `fitGraph`) is retained as the view-reset control.

### 3. Tests + docs
- **Web** binding tests: new `GraphExplorerBindingTests` — `initGraph` defines the `grab`/`drag`/`free` handlers, the `found_in` exclusivity check, and `relationshipType` edge data; `index.html` defines `setGraphZoom`/`getGraphZoom`; `GraphExplorer.razor` renders the slider + factor inputs and wires Fit/zoom sync.
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived when complete.

## Acceptance Criteria

- Dragging a source-document node moves its exclusively-`found_in` children with it; a child found in multiple documents stays put; dragging a non-source node moves only that node.
- The toolbar has an explicit zoom control: a range slider (25%–300%) and a numeric scaling factor input that both apply the zoom; the control reflects the graph's actual zoom after load/layout/Fit.
- The existing Fit button resets the view; the zoom-threshold label logic still works at the new scale.
- No API, backend, or schema changes — Web-only.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Compound/parent-node rendering (the flat graph look is preserved — group drag is delta translation, not hierarchy).
- Recursive subtree dragging beyond one hop (a child whose descendants are also exclusively this doc could be an extension).
- Changing graph node/edge data, API contracts, or backend behavior — Web-only (no schema migration).
- Physics simulations or layout algorithm changes — the existing `getGraphLayoutOptions`/`Re-layout` flow is untouched.

---

## Implementation Status

**Implemented (2026-08-16).** All 3 items complete; tests green.

### Item 1 — Drag-group on source nodes
- `wwwroot/index.html` `initGraph`: edge elements now carry `relationshipType: e.relationshipType` in their data. `grab`/`drag`/`free` handlers on `window.cy` implement the group: on grab of a `SourceDocument` node, `computeDragGroup` returns the node + every non-source node whose `found_in` edges all point to this document (`exclusivelyInThisSource`); on each drag tick, group members are translated by the dragged node's delta from its grab-time position; on `free`, the group state is cleared. Positions persist across re-renders via the existing `preservePositions` flow.

### Item 2 — Scale slider + numeric factor + Fit
- `GraphExplorer.razor` toolbar gains a `.graph-zoom` control (`#graph-zoom-slider` range 25–300 + `#graph-zoom-factor` number 0.25–3 + `×` unit); slider `@oninput` / number `@onchange` update `_zoomFactor` (clamped) and call `setGraphZoom`. `index.html` defines `window.setGraphZoom` (clamps + `cy.zoom({ level })`) and `window.getGraphZoom`. `OnGraphLayoutSettled` and `FitGraphAsync` sync `_zoomFactor` from the graph so the control reflects the actual zoom. The existing **Fit** button is retained as the view-reset control. The `'zoom'` event re-runs `updateLabels`, so the zoom-threshold label logic composes automatically.

### Item 3 — Tests + docs
- **Web 139 (+6)**: new `GraphExplorerBindingTests` — `initGraph` defines the `grab`/`drag`/`free` handlers, the `found_in` exclusivity check, and `relationshipType` edge data; `index.html` defines `setGraphZoom`/`getGraphZoom`; `GraphExplorer.razor` renders the slider + factor inputs and wires Fit/zoom sync.
- Foundation 55 / Repository 157 / RAGS 361 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh `/graph` — drag a source-document node (teal) and its exclusively-`found_in` children move with it (a child shared by multiple documents stays put); use the toolbar **Zoom** slider or the numeric factor to scale precisely, and **Fit** to reset the view. No schema migration — Web-only.
