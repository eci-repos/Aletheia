### Sprint 33 - Mandatory Tool Heartbeat Cadence Fix
**Status:** Completed

#### Objective
Stop active mandatory repository tool calls from being failed by the heartbeat watchdog while retrieval is still running.

---

#### Background
The prompt "What opportunities are found for AI based engagements?" failed again after about 114 seconds with `Engine heartbeat watchdog detected a stalled job (no heartbeat for more than 90s).` Sprint 31 added tool-call heartbeats and Sprint 32 added a longer mandatory-tool timeout, but `Tool call` was still using the long-wait heartbeat interval. In production that interval is 120 seconds, while the watchdog threshold is 90 seconds, so the watchdog can fire before the first long-wait tool heartbeat.

---

#### Goals
*   **Frequent Tool Heartbeats:** Mandatory repository tool calls emit heartbeats on the normal heartbeat cadence, not the long-wait cadence.
*   **Long Safety Timeout:** Production mandatory-tool and overall job timeouts are long safety nets, not normal execution limits.
*   **Preserve Watchdog:** Keep the watchdog enabled for genuinely stalled jobs.
*   **Handoff Ready:** Update current sprint and operational handoff documentation.

---

#### Exit Criteria
*   **Done** Active mandatory tool calls cannot be killed merely because the first long-wait heartbeat is later than the watchdog threshold.
*   **Done** Production config gives long-running repository retrieval enough time while still allowing operator cancellation.
*   **Done** Tests and Docker API/Web validation pass.

---

#### Implementation Notes
*   `ChatExecutionEngine.SelectHeartbeatInterval` now treats `Tool call` like `Synthesis`, `Global search`, and `RAGS retrieval`, using `HeartbeatIntervalSeconds` instead of `LongWaitHeartbeatIntervalSeconds`.
*   Production `ChatExecutionEngine` configuration now uses:
    *   `HeartbeatIntervalSeconds = 30`
    *   `LongWaitHeartbeatIntervalSeconds = 120`
    *   `HeartbeatWatchdogMissedThreshold = 20`
    *   `MandatoryToolTimeoutSeconds = 1800`
    *   `OverallJobTimeoutSeconds = 3600`
*   This fixes the observed mismatch where `Tool call` waited 120 seconds for its first heartbeat while the watchdog failed jobs after 90 seconds without a heartbeat.
*   `RunWithHeartbeatAsync` now emits an immediate heartbeat and records the in-memory watchdog timestamp before appending to the progress store, so a slow progress-write path cannot make the job look stalled.
*   The watchdog threshold now respects the configured long-wait cadence and the missed-heartbeat threshold. With current production settings, it is a 10-minute safety net.
*   Timeouts remain as long server-side safety nets for hung dependencies or abandoned background jobs. Operators can still cancel visible jobs from the UI.

---

#### Validation
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 158 tests.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 24 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted the local API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
*   Verified the live API container has `MandatoryToolTimeoutSeconds = 1800`, `OverallJobTimeoutSeconds = 3600`, `HeartbeatIntervalSeconds = 30`, `LongWaitHeartbeatIntervalSeconds = 120`, and `HeartbeatWatchdogMissedThreshold = 20`.
