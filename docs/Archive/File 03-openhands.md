File: openhands.md
# OpenHands Instructions

You are developing Aletheia.

Follow all documents in the following order:

1. 00-Aletheia-Charter.md
2. 01-Aletheia-WorkPlan.md
3. 02-Current-Sprint.md

The Charter is authoritative.

If conflicts exist:

Current Sprint overrides Work Plan.

Work Plan overrides assumptions.

Charter overrides everything.

---

# Execution Rules

Always:

- Build incrementally
- Commit small working units
- Keep the solution compiling
- Write tests
- Update documentation

Never:

- Skip phases
- Build future phases early
- Implement speculative features
- Bypass abstractions
- Introduce infrastructure dependencies into Domain projects

---

# Required Architecture

Use:

- Clean Architecture
- Hexagonal Architecture
- DDD
- SOLID
- Dependency Injection

All dependencies must resolve through interfaces.

---

# Provider Rules

Implement only the currently approved provider.

Future providers should be represented by abstractions and TODO backlog items.

Do not create production implementations for future providers unless explicitly instructed.

---

# Documentation Rules

For every completed feature update:

- README
- Architecture diagrams
- API documentation (when applicable)

---

# Testing Rules

Every feature requires:

- Unit tests

Every API requires:

- Integration tests

Do not close work items with failing tests.

---

# Build Rules

The solution must always:

- Build successfully
- Pass tests
- Run locally

---

# Completion Rules

A work item is complete only when:

- Code complete
- Tests passing
- Documentation updated
- Acceptance criteria satisfied

Working software always takes priority over speculative extensibility.

If uncertain, choose the simplest architecture that satisfies current requirements while preserving abstraction boundaries.
This package should be the initial handoff to OpenHands and will provide significantly better results than a single monolithic architecture prompt.

---

# Phase 21 Takeover Notes

Current active phase:

- Phase 21 - RAGS v2 Intelligence and Background Operations

Before making changes, read:

1. `docs/File 00-Aletheia-Charter.md`
2. `docs/File 01-Aletheia-WorkPlan.md`
3. `docs/File 02-Current-Sprint.md`
4. `docs/Phase21-Background-Operations-Handoff.md`

The first background-ingestion slice is implemented and validated. The lazy-enrichment slice is also implemented: uploads seed graph chunks without full document-wide LLM summarization, GraphRAG retrieval lazily enriches relevant chunks, and Copilot responses expose completion stats. WRAGS durability and maturity are implemented too: generated/edited wiki pages persist in PostgreSQL, `/wiki` can search/edit/show history/queue regeneration, pages have `Generated`/`Reviewed`/`Approved`/`NeedsReview`/`Stale` lifecycle controls, stale warnings, source-change stale detection, related topics, related-page lookup, and WRAGS participates in Search Center/Copilot retrieval context.

Sprints 22 through 26 added Copilot plan-before-run execution, background chat progress, telemetry, strict Aletheia Knowledge Estate grounding, mandatory repository tool calls for RFP/domain prompts, Semantic Kernel plugin exposure for knowledge tools, and UI telemetry for retrieval strategy/tool usage. Sprint 26 closure also added tests for live Kernel plugin registration, the ten-year RFP scenario, and fallback from missing GraphRAG communities to semantic RAGS. Continue from the known maturity work in the handoff file rather than rebuilding these paths from scratch.

Sprint 27 added lazy scoped WRAGS coverage. For prompts such as "all RFPs" or "registered RFP opportunities," Copilot should identify matching registered sources, retrieve bounded context per source, and pass that verified context into synthesis instead of re-running a single best/latest-source retrieval.

Sprint 28 added context-scoped Graph Explorer behavior. Upload and Search Center now record recent graph context in browser storage, and Graph Explorer should default to selected recent documents/searches with an explicit full-graph toggle. Preserve this context-first graph workflow as the graph grows; the full graph is for intentional/debug exploration.

Sprint 29 added Copilot session fidelity and panel controls. Copilot now preserves visible chat/session state, pending plan/progress, telemetry, active job ID, draft input, output format, and execution-panel layout in Web client state plus browser storage. The execution panel is resizable/collapsible, and plan/progress labels should remain human-readable instead of raw enum/internal names.

Sprint 30 fixed a Copilot evidence-fidelity regression. The acceptance panel must bind the plan status message as an expression, not render `_planStatusMessage`. CMP/document/engagement prompts asking for required features, requirements, or capabilities must route to `AletheiaKnowledgePlugin.SearchRags` document chunks; do not answer those prompts from graph/community summaries or expose community IDs/chunk counts to end users.

Sprint 31 fixed Copilot mandatory-tool stalls and session reset. Mandatory repository tool calls now run through heartbeat-aware, step-timeout-bounded execution, including RAGS fallback query variants and scoped per-source retrieval. If the prompt `Based on CMP 2026 list required features for this engagement` stalls during retrieval, validate the `Tool call` heartbeat path before changing watchdog settings. Copilot also has a **New chat** action that clears browser-local conversation, draft, plan, progress, telemetry, active job reference, and panel status.

Sprint 32 tuned mandatory repository tool timeouts. The prompt `What opportunities are found for AI based engagements?` exposed that 30 seconds was too short for corpus-level opportunity/RFP retrieval even though the job was alive. Mandatory tools now use `ChatExecutionEngine:MandatoryToolTimeoutSeconds`; preserve this separation from the generic `DefaultStepTimeoutSeconds`.

Sprint 33 fixed the follow-up watchdog/cadence mismatch for mandatory tools. `Tool call` must use the normal `HeartbeatIntervalSeconds` cadence, not `LongWaitHeartbeatIntervalSeconds`; otherwise production can wait 120 seconds for the first tool heartbeat while the watchdog fails active jobs too early. `RunWithHeartbeatAsync` emits an immediate heartbeat and records the in-memory watchdog timestamp before progress-store writes. Production defaults are now `MandatoryToolTimeoutSeconds = 1800`, `OverallJobTimeoutSeconds = 3600`, and `HeartbeatWatchdogMissedThreshold = 20`.

Sprint 34 fixed WRAGS orchestration/queue throughput. Chat execution is bounded-concurrent via `ChatExecutionEngineOptions.MaxConcurrentChatJobs` (production default/config `3`), so one long mandatory tool call cannot strand later requests in `Queued`. Scoped collection prompts such as RFP lists, registered opportunities, and CMP feature/requirement questions now skip broad repository retrieval when matching registered sources are found in metadata; Copilot hydrates/searches those sources directly through bounded per-source WRAGS retrieval.

Sprint 35 narrowed the normal product surface to reliable RAG/WRAGS. Search Center shows only `Semantic` and `WRAGS`; WRAGS Wiki shows only `WRAGS` and `Semantic`; Copilot mandatory repository plans now select `AletheiaKnowledgePlugin.SearchRags`. GraphRAG and LazyGraphRAG backend APIs remain in the solution for future reactivation, but do not re-expose them in the UI until a future sprint explicitly makes them performant enough. Taxonomy, ontology, vocabularies, and related data entities remain supported through the RAG/WRAGS path.

Sprint 38 fixes a Copilot mandatory repository tool invocation hang. The engine was emitting heartbeats and timing out after 180 seconds without ever invoking the registered Semantic Kernel plugin function. The fix introduces a dedicated `IChatToolInvoker` that the engine uses to invoke `AletheiaKnowledgePlugin`/`RepositoryTool` functions through the registered kernel, with normalization for both plugin/function naming styles and bounded step timeouts. The manual fallback path is retained for scoped-source hydration when the plugin returns no results.

Sprint 39 adds a configuration-driven Chat Agent persona. The agent's role, repository description, mandate, tool names, and behavior flags now live in the `ChatAgent` config section (`appsettings.json`, `.env`, and Docker environment variables). `SemanticKernelChatService` prepends a system prompt built from config; `RetrievalAugmentedPromptBuilder` and `SemanticKernelCopilotService` consume the same options; and `ChatExecutionEngine` enforces repository lookup before answer synthesis when `RequireRepositoryLookupBeforeAnswer` is true. Defaults are conservative and mirror prior hard-coded wording to minimize behavior change.

Sprint 41 recovers the chat-agent orchestration path. Hidden GraphRAG/LazyGraphRAG/global graph tool plans must be normalized to the configured active repository search tool before invocation, and Copilot progress/activity must show prompt acceptance, repository tool dispatch, internal context verification, and synthesis handoff. Do not treat SQL credential changes or longer timeouts as the primary fix for these failures unless Activity proves the prompt and tool call reached the expected layer.

Sprint 42 adds an external Copilot RAGS orchestration playbook at `src/Repository.API/Prompts/copilot-rags-orchestration.md`, configured by `ChatAgent:OrchestrationScriptPath`. If Taxonomy/metadata finds `RFP` or another scope but `SearchRags` returns no chunks, treat it as RAGS index drift and queue `POST /api/jobs/rags/repair?query=RFP` before changing timeouts or assuming no repository context exists.

Sprint 43 fixes the `RFP`/`Rfp`/legacy `Rpf` split. Use `KnowledgeTermNormalizer` for acronym labels. Fresh ingestion should create one canonical `RFP` Taxonomy/Ontology concept and `found_in` relationships from that concept to each matching source document.

Takeover observation: the prior Kimi K2.7 Code cloud agent made useful progress on mandatory tool-calling, strict grounding, RFP intent planning, plugin exposure, and telemetry, but stopped short of production validation. It missed the small-corpus GraphRAG failure path where `SearchGraphRag` returned `No communities detected in the graph.`, left Sprint 26 documentation partially reconciled, and allowed misleading progress state when a mandatory tool failed. Future agents should validate the real user scenario end-to-end against a small corpus, confirm fallback behavior before closing a sprint, and update handoff/current-sprint docs in the same pass as code changes.

Important constraints:

- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints compatible unless the sprint explicitly changes them.
- Preserve the `/api/jobs` snapshot contract used by the Web Activity panel.
- Do not introduce a new queue provider or database unless it is part of the current Phase 21 maturity work.
- Keep job progress concise: stage transitions plus coarse heartbeats are preferred over noisy per-token logs.
- Preserve the searchable-first upload path unless the sprint explicitly reopens full index-time enrichment.
- Treat Copilot `AlignmentConfidence` as a retrieval heuristic, not a calibrated correctness score.
- Preserve mandatory repository tool calls for high-grounding prompts such as RFP, registered opportunities, procurement, repository-wide timelines, and corpus summaries.
- Preserve the mandatory-tool fallback: if GraphRAG/global graph has no communities, summaries, or usable context, Copilot should fall back to `AletheiaKnowledgePlugin.SearchRags` before failing synthesis.
- Preserve registered-source hydration in mandatory fallback: for RFP prompts, if broad semantic RAGS returns no context, try RFP query variants and hydrate matching Repository metadata candidates before failing.
- Preserve Sprint 27 synthesis fidelity: verified mandatory-tool retrieval context must be passed to `ICopilotService` via `ChatRequestOptions.RetrievalResults` for scoped WRAGS/list prompts.
- Preserve ProgressPanel visibility for retrieval strategy, tool name, and tool invocation count.
- Preserve Sprint 28 Graph Explorer scoping: recent document/search context should remain the default graph exploration path, with full graph behind an explicit operator choice.
- Preserve Sprint 29 Copilot session fidelity: navigating away from `/copilot` and returning must not clear the visible chat/session, and the execution panel resize/collapse controls should continue to persist in browser storage.
- Preserve Sprint 30 scoped evidence routing: feature/requirement prompts for named documents or engagements should retrieve document-level RAGS evidence, not global graph/community summaries.
- Preserve Sprint 31 mandatory-tool heartbeats: repository tool calls must emit progress while retrieval is running and should fail by configured step timeout, not by heartbeat watchdog, when a retrieval dependency hangs.
- Preserve Sprint 31 reset semantics: **New chat** clears browser-local Copilot state and panels, but should not silently cancel a server-side background job.
- Preserve Sprint 32 timeout separation: slow mandatory RAGS/WRAGS tool retrieval should be tuned through `MandatoryToolTimeoutSeconds`, not by weakening the watchdog or globally stretching every execution step.
- Preserve Sprint 33 heartbeat cadence: `Tool call` must heartbeat on the normal cadence so the watchdog sees active retrieval progress.
- Preserve Sprint 34 bounded-concurrent execution and scoped-source retrieval: do not reintroduce a single inline chat worker, and do not force a broad RAGS/GraphRAG pass before per-source retrieval when metadata already identifies the scoped registered documents.
- Sprint 44 supersedes the Sprint 35 RAG-first hide decision: normal UI should expose Semantic/Vector RAG, WRAGS, GraphRAG, and LazyGraphRAG. Keep scoped RFP/CMP/document prompts on Semantic RAGS document evidence, and use graph modes for broad/global graph workflows with fallback.
- Sprint 48 is the current source-identity rule: do not pass flat mixed-source context to synthesis. Group retrieval by `SourceId`, preserve source names/IDs in prompt blocks, and forbid facts from one document being used to describe another.
- Preserve Sprint 41 chat-agent observability: Copilot Activity should show prompt snippets, tool dispatch/normalization, internal context verification, and synthesis handoff. Hidden graph tools must be normalized before invocation, not just reported differently in telemetry.
- Preserve the current WRAGS API surface: `/api/wiki/search`, `/api/wiki/recent`, `/api/wiki/retrieve`, `/api/wiki/pages/{id}`, `/api/wiki/pages/{id}/history`, `/api/wiki/pages/{id}/status`, `/api/wiki/pages/{id}/related`, `/api/wiki/regenerate`, and `/api/wiki/regenerate/job`.

Recommended takeover target:

- Durable PostgreSQL-backed job state, followed by cancellation/retry controls, integration tests, provider-backed token usage telemetry, graph-derived WRAGS backlinks, editorial diff visualization, and quality scoring for wiki-as-context retrieval.
- Remaining next-agent work should focus on durable PostgreSQL-backed chat/job state, cancellation/retry controls beyond the current in-memory path, deeper integration tests, provider-reported token usage telemetry when available, graph-derived WRAGS backlinks, editorial diff visualization, and quality scoring for wiki-as-context retrieval.
