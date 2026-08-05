### Sprint 32 - Mandatory Tool Timeout Budget Tuning
**Status:** Completed

#### Objective
Prevent valid Copilot repository questions from failing after 30 seconds when mandatory WRAGS/RAGS retrieval is still alive but slow.

---

#### Background
Sprint 31 fixed silent watchdog stalls by wrapping mandatory repository tool calls in heartbeat-aware execution. The next user scenario, "What opportunities are found for AI based engagements?", now fails cleanly with `Tool invocation timed out after 30 seconds.` This means the watchdog path is fixed, but the timeout budget is too short for corpus-level opportunity/RFP retrieval that may request broad internal evidence before synthesis.

---

#### Goals
*   **Dedicated Tool Timeout:** Mandatory repository tools use a separate, configurable timeout budget instead of the generic step timeout.
*   **Preserve Watchdog Fix:** Tool calls continue emitting heartbeats while long retrieval runs.
*   **Preserve Fast Failures:** Truly hung tool calls still fail with a clear timeout rather than stalling indefinitely.
*   **Documentation:** Keep current sprint and handoff notes synchronized for the next agent.

---

#### Requirements
*   Add a `MandatoryToolTimeoutSeconds` option with a production default longer than `DefaultStepTimeoutSeconds`.
*   Use the new option for `ChatExecutionEngine.InvokeToolAsync`.
*   Update timeout-related tests to verify mandatory tools use the dedicated budget.
*   Update sprint, handoff, admin/operator docs.

---

#### Exit Criteria
*   **Done** Opportunity/RFP corpus prompts no longer fail solely because the mandatory repository tool exceeds the old 30-second generic step timeout.
*   **Done** Hanging mandatory tools still fail by the configured mandatory-tool timeout.
*   **Done** Tests pass and Docker API/Web are rebuilt from the current source.

---

#### Implementation Notes
*   Added `ChatExecutionEngineOptions.MandatoryToolTimeoutSeconds` with a default of 180 seconds.
*   Added explicit `ChatExecutionEngine` configuration in `src/Repository.API/appsettings.json`:
    *   `DefaultStepTimeoutSeconds = 30`
    *   `MandatoryToolTimeoutSeconds = 180`
    *   `OverallJobTimeoutSeconds = 300`
*   `ChatExecutionEngine.InvokeToolAsync` now uses the larger mandatory-tool budget while preserving heartbeat emission and timeout failure behavior.
*   The mandatory-tool timeout is clamped to at least `DefaultStepTimeoutSeconds`, so lowering the dedicated option below the default step budget cannot accidentally shorten tool calls.
*   The heartbeat regression test now proves a mandatory RAGS call can outlive the generic 1-second step timeout and still succeed when the mandatory-tool budget allows it.

---

#### Validation
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 157 tests.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests after rerun. The first parallel attempt hit a file-lock while `dotnet build` was writing `Aletheia.Web.dll`; it was not a test failure.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
