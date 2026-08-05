### Sprint 30 - Copilot Evidence Fidelity Regression Fix
**Status:** Completed

#### Objective
Fix Copilot regressions where the execution acceptance panel leaks an internal field name and scoped feature/requirement questions answer from graph/community summaries instead of document-level evidence.

---

#### Background
The acceptance panel rendered `_planStatusMessage` literally because the Blazor component parameter was passed as a string instead of an expression. Separately, prompts such as "Base on CMP 2026 list required features for this engagement" do not always include `RFP`, so planning routed them to global graph/community-summary retrieval. That produced an unusable answer mentioning communities instead of listing the actual required features from the registered document.

---

#### Goals
*   **Acceptance Panel Binding:** Render the real plan status message, never the backing field name.
*   **Scoped Evidence Retrieval:** Route document-scoped requirement/feature/engagement prompts to RAGS document chunks.
*   **No Community Leakage:** Prevent Copilot synthesis from exposing graph/community implementation details when the user asked for document facts.

---

#### Requirements
*   Fix the Blazor parameter binding for `PlanPreview.StatusMessage`.
*   Detect CMP/document-scoped feature and requirement prompts as mandatory repository-tool requests.
*   Prefer `AletheiaKnowledgePlugin.SearchRags` for scoped feature/requirement prompts so raw document chunks are provided to synthesis.
*   Strengthen prompt instructions so community/graph internals are not surfaced to end users as evidence.
*   Add focused tests for planning and UI binding behavior.

---

#### Exit Criteria
*   The acceptance panel shows a friendly status message instead of `_planStatusMessage`.
*   CMP 2026 feature/requirement prompts route to RAGS document evidence, not global graph/community summaries.
*   Handoff documentation is updated for external agents.

---

#### Implementation Summary
*   Fixed the Copilot parent binding from `StatusMessage="_planStatusMessage"` to `StatusMessage="@_planStatusMessage"` so the acceptance panel renders the actual friendly message.
*   Added planner signals for document-scoped and requirement/feature prompts.
*   Routed CMP/document/engagement requirement-feature prompts to `AletheiaKnowledgePlugin.SearchRags` instead of global graph/community search.
*   Extended execution fallback so scoped feature/requirement prompts can hydrate/search matching registered documents even without an explicit `RFP` keyword.
*   Strengthened synthesis instructions to avoid exposing graph communities, community IDs, chunk counts, retrieval strategies, or index internals as user-facing answers.

#### Validation
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 22 tests.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 156 tests.
*   `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj` passed with 91 tests.
*   `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj` passed with 55 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted API/Web containers.
*   `GET http://localhost:8080/health/live`, `GET http://localhost:8080/health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
*   Browser smoke confirmed the Copilot acceptance panel renders `Review the plan and click Run to start.`, does not render `_planStatusMessage`, shows friendly `Corpus analysis`, and displays the `AletheiaKnowledgePlugin.SearchRags` tool for the CMP feature prompt.
