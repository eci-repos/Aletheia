### Sprint 31 - Copilot Tool Heartbeats and Session Reset
**Status:** Completed

#### Objective
Fix Copilot execution stalls during mandatory RAGS retrieval and add a clear/reset control so users can start a fresh conversation and execution panel state.

---

#### Background
The prompt "Based on CMP 2026 list required features for this engagement" now routes to document-level RAGS evidence, but the mandatory repository tool call can block without emitting heartbeats. The execution watchdog then marks the job failed as stalled. The Copilot page also lacks an operator-visible way to clear the current conversation and panels after testing or after a failed job.

---

#### Goals
*   **Tool Heartbeats:** Mandatory repository tool calls must emit heartbeats during long RAGS/GraphRAG/LazyGraphRAG/global retrieval.
*   **Step Timeout:** Long tool retrieval must fail with a clear tool timeout/error instead of a watchdog stall.
*   **Fresh Conversation Control:** Copilot must expose a reset/new-chat action that clears chat messages, draft input, pending plan, progress, telemetry, active job reference, and persisted browser state.

---

#### Requirements
*   Wrap mandatory tool calls in heartbeat-aware execution with a bounded timeout.
*   Preserve the Sprint 30 routing rule: CMP/document/engagement feature or requirement prompts use `AletheiaKnowledgePlugin.SearchRags`.
*   Add a Copilot UI button to reset local session and execution panel state.
*   Add focused tests for heartbeat-protected mandatory RAGS retrieval and Copilot state clearing.
*   Update current sprint, sprint archive, and handoff documentation.

---

#### Exit Criteria
*   **Done** Mandatory RAGS tool retrieval emits heartbeats and does not fail only because the watchdog sees no activity.
*   **Done** A hanging mandatory RAGS call fails through the tool step timeout path rather than watchdog stall.
*   **Done** Copilot has a visible reset/new-chat control that clears local chat/panel state.
*   **Done** Documentation is updated for external agents.

---

#### Implementation Notes
*   `ChatExecutionEngine.InvokeToolAsync` now wraps mandatory `SearchRags`, `SearchGraphRag`, `SearchLazyGraphRag`, and `SearchGlobalGraph` tool work in heartbeat-aware execution with the configured default step timeout.
*   RAGS fallback query variants and per-source scoped retrieval also use the same tool-call heartbeat path, so scoped document feature/requirement prompts keep reporting progress while retrieving evidence.
*   Hanging mandatory tool calls now fail the **Call repository tool** step with a clear timeout message instead of relying on the heartbeat watchdog.
*   Copilot now exposes **New chat** in the conversation header. It clears messages, draft input, pending plan, progress, telemetry, active job state, panel status, and browser-persisted Copilot state.
*   Copilot progress polling now uses a local cancellation token for each polling loop, preventing stale pollers from observing a newly assigned token after reset.

---

#### Validation
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 157 tests.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests.
*   `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj --no-restore` passed with 91 tests.
*   `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj --no-restore` passed with 55 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
*   Browser smoke on `http://localhost:8081/copilot` confirmed **New chat** renders, clears `aletheia.copilot.session.v1`, and returns the page to the empty-conversation state.
