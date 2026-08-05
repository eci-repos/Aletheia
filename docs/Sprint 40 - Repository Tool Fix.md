### Sprint 40 - Fix repository tool timeout and enable config/secret setup
**Status:** In Progress — Provide connection string, JWT secret, enable debug logging, verify RAG tool works.

---

#### Objective
Ensure the mandatory repository tool (`AletheiaKnowledgePlugin.SearchRags`) can complete successfully by supplying required configuration (PostgreSQL pgvector connection, JWT secret) and adding diagnostics.

#### Goals
* Add `PgVector.ConnectionString` and `Authentication.Jwt.Secret` to `appsettings.json` (done).
* Enable `Debug` logging for `Aletheia.RAGS` (done).
* Run the API locally to confirm it starts.
* Execute a Copilot request (`list the AI features as required by the CMP 2026 project`) and verify it returns results within the step timeout.
* Update `File 02-Current-Sprint.md` to reflect Sprint 40 as current.
* Commit changes.

---

#### Validation
* `dotnet build Aletheia.slnx` succeeds.
* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passes.
* Running the API (`dotnet run --urls http://localhost:8081` in `src/Repository.API`) starts without error.
* Using the Copilot UI (or direct SDK call) returns the expected feature list within <180 s.

---

#### Risks
* Using default dev passwords; must not be shipped to production.
* Debug logging may expose internal details; ensure it is limited to development.
