### Sprint 29 - Copilot Session Fidelity and Panel Controls
**Status:** Completed

#### Objective
Fix Copilot usability issues around execution-plan display, panel control, and chat session continuity while preserving Sprint 24-28 grounding/progress behavior.

---

#### Background
The Copilot page currently keeps active chat state inside the page component. Navigating away and returning recreates the component, causing the visible session to disappear even when the background execution still exists. The execution-plan acceptance area also exposes raw enum-style values, and the Copilot layout needs operator control over the conversation/execution panel width.

---

#### Goals
*   **Readable Plan Status:** Replace raw/internal plan status display with human-readable labels.
*   **Resizable Copilot Panels:** Let the operator drag the conversation/execution split and collapse/restore the execution panel.
*   **Session Continuity:** Preserve the active Copilot session, plan/progress state, telemetry, and layout preferences across Web navigation and browser refresh.

---

#### Requirements

##### 1. Plan Preview Polish
*   Display friendly mode/status labels instead of raw enum/internal variable names.
*   Keep Run/Revise/Cancel behavior intact.

##### 2. Copilot Layout Controls
*   Add a drag handle between conversation and execution panels on desktop.
*   Add a close/collapse control for the execution panel and a restore control when collapsed.
*   Keep mobile layout usable without draggable overlap.

##### 3. Persistent Copilot State
*   Introduce Web client state storage for the current Copilot session.
*   Persist chat messages, output format, pending plan, progress, telemetry, active job ID, panel width, and collapsed state.
*   Restore persisted state when returning to `/copilot`, then resume polling for active work.

##### 4. Validation
*   Add focused Web unit tests for friendly plan labels and Copilot state serialization.
*   Run Web/RAGS tests, full solution build, Docker rebuild, and Copilot smoke test.

---

#### Exit Criteria
*   Copilot plan preview no longer displays raw/internal status labels.
*   Copilot execution panel can be resized and collapsed/restored.
*   Copilot chat/session state survives navigation and browser refresh.
*   Handoff documentation is updated.

---

#### Implementation Summary
*   Added `CopilotStateService` to preserve the active Copilot session, draft input, output format, pending plan, progress, telemetry, active job ID, plan message, and panel layout in Web client memory plus browser `localStorage`.
*   Copilot now restores saved session state on first render and resumes progress polling when a pending plan or active job exists.
*   Copilot plan preview now displays friendly mode/status labels such as "Corpus analysis" and "Ready for review" instead of raw enum-style names.
*   Copilot progress badges now use friendly labels such as "Completed" and "Done".
*   Copilot execution panel can be resized with a drag handle and collapsed/restored from the conversation header.
*   Static asset query strings were bumped so the refreshed container serves the updated Copilot styles/scripts.

#### Validation
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 21 tests.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests.
*   `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
*   `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
*   Browser smoke confirmed persisted Copilot messages survive navigation away/back, execution panel collapse persists, and drag resize changes/stores panel width.
