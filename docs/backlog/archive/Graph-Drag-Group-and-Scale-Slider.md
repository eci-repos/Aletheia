# Backlog: Graph Drag-Group and Scale Slider

**Status:** **Complete** — all 3 items implemented (Sprint 76, 2026-08-16). Archived.
**Created:** 2026-08-15
**Source:** Project-owner request (2026-08-15) — "would it be possible in the Graph to drag a 'root' node that represents the source document and all children that are solely based on this doc be dragged at the same time?... plan B is good, lets add a scale slider that also allow the user to put a scaling factor since scaling the graph now is difficult. make a backlog item and keep it to work on it later."

## Problem

- **Dragging a source document does not move its subtree.** In the Graph Explorer (`Pages/GraphExplorer.razor` + `window.initGraph` in `wwwroot/index.html`, Cytoscape.js 3.26.0), a source-document node (type `Source`) and the entity nodes attributed to it (connected via `found_in` edges) are independent. Dragging the document node leaves its children behind, so rearranging a document's cluster means dragging every node one by one.
- **Scaling the graph is difficult.** There is no zoom control in the UI — scaling is only via Cytoscape's default mouse-wheel/pinch/box-selection behavior (finicky, imprecise, and not discoverable). The user wants an explicit scale slider plus a numeric scaling factor input.

## Decisions (proposed approach)

1. **Custom drag-group on source nodes (Plan B, flat rendering).** Keep the current flat graph look — no compound/parent nodes (which would render children inside a container box). In `initGraph`, hook the Cytoscape grab/drag/free events: on grab of a `Source` node, compute the group = the node itself + the nodes connected to it via `found_in` edges whose **only** source connection is this document (a child found in multiple documents stays put, since it is not "solely based on this doc"). On each drag tick, translate every group member by the dragged node's position delta; release on `free`. Positions already persist across re-renders via the existing `preservePositions` flow, so a moved cluster stays where the user put it.
2. **Scale slider + numeric factor.** Add a Graph Explorer control (Blazor, in `GraphExplorer.razor`): a `range` slider (e.g. 25%–300%) plus a numeric input showing the current factor, wired to a new JS function (e.g. `setGraphZoom(factor)`) that applies `window.cy.zoom({ level: factor })`. The existing zoom-dependent label rendering (`updateLabels` on the `'zoom'` event, `index.html` lines ~413–447 — labels show/hide by zoom threshold) re-runs automatically at the new scale, so the two features compose. Include a **Fit** button (`cy.fit()`) to reset the view.
3. **Web-only.** No API, backend, or schema changes. All behavior lives in `index.html` JS + `GraphExplorer.razor` markup/handlers.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Drag-group on source nodes** — on grab of a `Source` node, drag it plus its exclusively-`found_in` children as one group (grab/drag/free delta translation in `initGraph`). | The explicit ask: move a document's whole cluster in one drag. | ~0.5 day | Proposed |
| 2 | **Scale slider + numeric scaling factor** — range slider (25%–300%) + numeric factor input + Fit button wired to `cy.zoom()`; reuses the existing zoom-threshold label logic. | Precise, discoverable zoom; the current wheel/box-only scaling is hard to control. | ~0.25 day | Proposed |
| 3 | **Tests + docs** — Web binding tests: `initGraph` JS defines the group-drag (grab/free handlers + `found_in` exclusivity check) and `GraphExplorer.razor` has the slider/number/fit controls; AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The Graph Explorer interaction contract is locked down. | ~0.5 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — one coherent Graph Explorer UX pass (both are `index.html` JS + `GraphExplorer.razor`; the zoom plumbing is shared).
- **Item 3** alongside each item, not a trailing batch.

**Total (agent):** ~1–1.25 working days including build/test verification — a single sprint.

## Out of Scope

- Compound/parent-node rendering (the flat graph look is preserved — group drag is delta translation, not hierarchy).
- Recursive subtree dragging beyond one hop (a child whose descendants are also exclusively this doc could be an extension).
- Changing graph node/edge data, API contracts, or backend behavior — Web-only (no schema migration).
- Physics simulations or layout algorithm changes — the existing `getGraphLayoutOptions`/`Re-layout` flow is untouched.
