### Sprint 36 - Copilot Small Corpus RAG Latency Fix
**Status:** Completed

#### Objective
Restore responsive Copilot chat performance for small registered corpora by eliminating the artificial large-corpus classification for RFP summary prompts, tightening chat timeouts, and preventing hidden GraphRAG/LazyGraphRAG paths from stalling the RAG-first chat flow.

---

#### Background
Sprint 35 made the normal product surface RAG-first by hiding GraphRAG and LazyGraphRAG UI entry points and routing grounded plans through `AletheiaKnowledgePlugin.SearchRags`. However, prompts such as "summarize the purpose of each of the RFP analysis engagements" still experience long stalls on a two-document corpus. The root causes are:

1. `ChatPlanningService` sets `EstimatedRetrievalCount = 50` for every `CorpusAnalysis` plan, so `ChatExecutionEngine.IsSmallCorpusRequest` always treats the request as a large corpus and bypasses the small-corpus fast path.
2. Production timeout defaults (`MandatoryToolTimeoutSeconds = 1800`, `OverallJobTimeoutSeconds = 3600`, heartbeat watchdog up to 10 minutes) make any retrieval/ingestion/LLM slowdown appear as a long hang before the engine fails or returns.
3. `ChatExecutionEngine` still actively invokes `_graphRagService.GlobalSearchAsync` and `_lazyGraphRagService.GlobalSearchAsync` in the non-tool `CorpusAnalysis` branch and in `InvokeToolAsync` fallback for GraphRAG tools. Although the UI no longer exposes these paths, a regression or plan with a different tool name can still enter the slow graph paths.

---

#### Goals
*   **Small-Corpus Awareness:** Make planning and execution aware of the actual number of registered sources for RFP/scoped-collection prompts, and route tiny corpora through the fast small-corpus path.
*   **Timeout Hygiene:** Reduce production chat timeout defaults so Semantic/Vector RAG calls are bounded to a reasonable interactive duration, while preserving background ingestion timeouts separately.
*   **GraphRAG Fast-Fail:** Ensure any Copilot plan that somehow reaches a GraphRAG/LazyGraphRAG/GlobalGraph tool path fails immediately back to RAGS instead of executing the slow graph services.
*   **Regression Coverage:** Add unit tests for the exact reported prompt with a 2-document corpus, asserting fast completion, correct retrieval strategy, and no graph service invocation.

---

#### Implementation Notes
*   `ChatPlanningService` will continue to emit `AletheiaKnowledgePlugin.SearchRags` for all mandatory repository tool calls. No new tool names are introduced.
*   `ChatExecutionEngine.IsSmallCorpusRequest` will be updated to consider the actual matching registered source count when `IMetadataRepository` is available, so a 2-source corpus is treated as small even when the plan asks for 50 chunks.
*   `ChatExecutionEngine` will add a small-corpus scoped-collection fast path inside `InvokeToolAsync`: when `IsScopedCollectionPrompt` is true and the resolved source count is `<= SmallCorpusDocumentThreshold`, it will run a single bounded per-source retrieval and skip broad retrieval, query variants, and ingestion retries.
*   `RunGlobalSearchAsync` will be updated to fail fast when the prompt/tool plan is from a Copilot chat path: it will log that GraphRAG is hidden and immediately fall back to `RunRagsRetrieveAsync`. This keeps the backend services intact for future reactivation but prevents chat stalling.
*   `Repository.API/appsettings.json` `ChatExecutionEngine` section will be updated:
  - `MandatoryToolTimeoutSeconds`: 1800 -> 180 (3 minutes)
  - `OverallJobTimeoutSeconds`: 3600 -> 600 (10 minutes)
  - `HeartbeatIntervalSeconds`: 30 -> 10
  - `LongWaitHeartbeatIntervalSeconds`: 120 -> 30
  - `HeartbeatWatchdogMissedThreshold`: 20 -> 6
*   `ChatExecutionEngineOptions` defaults will be aligned with the new appsettings values so unit tests reflect production behavior.

---

#### Exit Criteria
*   The reported prompt completes via `AletheiaKnowledgePlugin.SearchRags` with a 2-document corpus in unit tests in under 5 seconds.
*   No GraphRAG or LazyGraphRAG service is invoked during the Copilot chat path for the regression test.
*   `ChatExecutionEngineTests` still passes the existing GraphRAG fallback tests (these now test the fast-fail RAGS fallback path).
*   `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatPlanningServiceTests|FullyQualifiedName~ChatExecutionEngineTests"` passes.
*   `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passes.
*   `dotnet build Aletheia.slnx` passes.
*   `docs/File 02-Current-Sprint.md` points to Sprint 36.

---

#### Validation
*   New test: `Engine_summarizes_rfp_engagements_fast_for_two_document_corpus`.
*   New test: `Engine_fast_fails_graphrag_tool_path_back_to_rags`.
*   Run the full filtered planning/execution test suite.
*   Build the solution and the Web test project.

---

#### Risks
*   Lowering `MandatoryToolTimeoutSeconds` may cause legitimate slow LLM calls to time out. Mitigation: the 3-minute bound applies only to the tool retrieval step; overall job timeout remains 10 minutes for synthesis and progress updates.
*   Fast-failing GraphRAG in chat means future reactivation of GraphRAG chat features must explicitly re-enable the tool branch, which is consistent with Sprint 35's "hide until production-ready" decision.
