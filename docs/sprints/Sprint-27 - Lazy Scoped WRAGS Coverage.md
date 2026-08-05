### Sprint 27 - Lazy Scoped WRAGS Coverage
**Status:** Completed

#### Objective
Make Copilot treat WRAGS/Repository as the full default knowledge scope for repository questions while avoiding slow full-corpus exhaustive enrichment. For prompts such as "summarize all RFPs" or "registered RFP opportunities in the past 10 years," Copilot must lazily identify the requested source set, retrieve bounded context per matching source, and synthesize from that verified scope.

---

#### Background
Sprint 26 forced mandatory knowledge-tool calls and added fallbacks when GraphRAG has no communities. A remaining gap is scope fidelity: broad RFP questions can still collapse to the single best/latest source during synthesis, because the verified tool context is not always carried into the final Copilot prompt. The desired behavior is not expensive full exhaustive mode; it is lazy exhaustive coverage over the user's requested scope.

---

#### Goals
*   **Lazy Scope Detection:** Detect prompts asking for all/listed/registered category coverage, especially RFP opportunities.
*   **Per-Source Coverage:** Find matching WRAGS/Repository sources from metadata/tags, hydrate only those sources when needed, and retrieve bounded top chunks per source.
*   **Verified Context Synthesis:** Pass the verified scoped retrieval set into synthesis so Copilot does not re-retrieve only the latest/best source.
*   **Coverage Reporting:** Add progress messages and telemetry that make source coverage visible.

---

#### Requirements

##### 1. Scoped WRAGS/RFP Retrieval
*   When a mandatory repository prompt asks for all/list/registered RFPs, identify matching registered sources from Repository metadata.
*   Retrieve a small bounded context set per source instead of running full GraphRAG enrichment across the estate.
*   Hydrate only matching sources if their vector context is missing.

##### 2. Synthesis Context Fidelity
*   `ChatExecutionEngine` must pass the verified tool retrieval results to `ICopilotService`.
*   `SemanticKernelCopilotService` must honor provided retrieval context and avoid replacing it with a separate single-source retrieval.
*   The augmented prompt must instruct the model that the provided context represents the requested WRAGS scope.

##### 3. Validation
*   Add a regression test where two registered RFP sources are returned in the mandatory context and final synthesis receives both.
*   Existing GraphRAG fallback and source hydration tests must continue to pass.

---

#### Exit Criteria
*   **✓** "Summarize registered RFP opportunities in the past 10 years" can include all matching registered RFP sources without full-corpus enrichment.
*   **✓** Final synthesis receives the same verified context collected by the mandatory tool path.
*   **✓** Progress messages indicate scoped source coverage when metadata candidates are used.
*   **✓** RAGS and Web unit tests pass; solution build passes; local API/Web containers are rebuilt.

---

#### Completion Notes
*   `ChatExecutionEngine` expands scoped RFP/list prompts across matching registered Repository metadata sources using bounded per-source retrieval and optional source hydration.
*   `ChatExecutionEngine` passes verified mandatory-tool retrieval context into synthesis through `ChatRequestOptions.RetrievalResults`.
*   `SemanticKernelCopilotService` honors provided scoped retrieval context and avoids re-running retrieval when verified context is supplied.
*   `RetrievalAugmentedPromptBuilder` supports a scope instruction explaining that the context represents lazy scoped WRAGS coverage.
*   `ChatExecutionEngineTests.Engine_passes_lazy_scoped_rfp_context_to_synthesis` verifies both RFP sources reach synthesis.
*   `SemanticKernelCopilotServiceTests.ChatAsync_uses_provided_scoped_context_without_retrieving_again` verifies provided context is used without a second best-match retrieval.
*   Follow-up hardening clamps zero retrieval estimates before constructing `RetrievalRequest`, preventing `TopK must be greater than zero` failures.
*   Follow-up hardening classifies prompts such as "on the CMP 2026 list all found features required for AI" as lazy scoped corpus requests.
*   Follow-up UI hardening resets the right-side plan/progress/telemetry panel for every new request, clears stale telemetry, and waits for explicit **Run** on every plan instead of auto-starting low-cost plans.
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj` passed with 153 tests.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj` passed with 10 tests.
*   `dotnet build Aletheia.slnx` passed with the existing AngleSharp NU1902 warning.
*   `docker compose up -d --build api web` rebuilt and restarted the local containers; `/health/live`, `/health/ready`, and `/copilot` returned 200.
