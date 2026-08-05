### Sprint 28 - Context Scoped Graph Explorer
**Status:** Completed

#### Objective
Make the Knowledge Graph usable as the corpus grows by defaulting graph exploration to a recent, user-selected context instead of rendering the full repository graph.

---

#### Background
The full graph view is useful for debugging and small corpora, but it becomes visually crowded even with a few documents. As more WRAGS documents, entities, relationships, and communities are loaded, the graph needs a context-first workflow. The user should be able to focus on the last loaded documents and recent search requests, then select what should drive the graph view.

---

#### Goals
*   **Recent Context Memory:** Track the last 10 uploaded documents and last 10 Search Center queries in the Web client.
*   **Graph Scope Panel:** Add a right-side panel to Graph Explorer showing recent documents and searches.
*   **Context-Scoped Rendering:** Let users select recent context items and render only matching graph nodes, connected edges, and a small 1-hop neighborhood.
*   **Full Graph Escape Hatch:** Keep an explicit full-graph mode for advanced/debug exploration.

---

#### Requirements

##### 1. Recent Context
*   Record successful uploads as document context items.
*   Record Search Center searches as query context items.
*   Persist recent context in browser storage so it survives page refresh.

##### 2. Graph Explorer UX
*   Add a context panel with selected item checkboxes, "select all", "clear", and "full graph" controls.
*   Default Graph Explorer to selected/recent context when available.
*   Display filtered and total node/edge counts so the operator knows what scope is being rendered.

##### 3. Validation
*   Add focused tests for recent-context trimming/deduplication and graph context matching behavior.
*   Run RAGS/Web tests and full build.
*   Rebuild local API/Web containers and smoke test the graph page.

---

#### Exit Criteria
*   **✓** Graph Explorer can focus on recent uploaded documents and searches without loading the full graph by default.
*   **✓** Users can choose recent context items or switch to full graph intentionally.
*   **✓** Recent context survives refresh using browser storage.
*   **✓** Handoff documentation is updated.

---

#### Implementation Summary
*   Added `RecentGraphContextService` in the Web client to persist recent uploaded documents and Search Center queries in browser `localStorage`, capped at 10 items per kind.
*   Upload records successful document uploads into recent graph context.
*   Search Center records completed searches into recent graph context.
*   Graph Explorer now renders a right-side Recent Context panel with refresh, select all, clear, and explicit full-graph controls.
*   Graph Explorer defaults to recent selected context when available and filters nodes/edges to matching context plus a 1-hop neighborhood. Full graph remains available for advanced/debug exploration.
*   Graph Explorer displays filtered and total node/edge counts.

#### Validation
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 15 tests.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests on rerun. First run had one timing-shaped `Engine_honors_step_timeouts` assertion where the job was still `Running`; no Sprint 28 path was involved.
*   `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
*   `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/graph` returned `200`.
*   Browser smoke on `http://localhost:8081/graph` confirmed the rendered Recent Context panel and full-graph control are present.
