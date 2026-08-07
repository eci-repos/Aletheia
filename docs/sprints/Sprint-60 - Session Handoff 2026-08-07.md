# Sprint 60 - Session Handoff (2026-08-07)

Status: **All four deliverables implemented; tests green; uncommitted.**

## What is done (working tree, uncommitted)

- **D1 - Per-request `GraphTraversalBudget`**: `IGraphTraversalBudget.CreatePerRequest()` + read-only counters (`LlmCalls`, `TokensConsumed`, `NodesVisited`, `RelationshipsTraversed`); `GraphTraversalBudget` implements them (`Volatile`/`Interlocked`). `LazyGraphRagService` keeps the injected budget as `_budgetTemplate` (optional ctor param moved to the end) and calls `CreatePerRequest()` per `RetrieveAsync`; `_indexedSources` guarded by `lock (_indexedSourcesLock)`. `GraphRagService.RetrieveAsync` constructs `new GraphTraversalBudget()` inline. `AddSingleton<IGraphTraversalBudget>` removed from `Repository.API/Program.cs`.
- **D2 - Real token accounting + hard deadline**: `TokenUsageHelper.GetTotalTokens(ChatMessageContent?)` reads `Metadata` (provider-agnostic input/output/total key sets + nested `"Usage"` + reflection over provider usage objects). Wired into `EntityExtractionService.DiscoverAsync` and `LazyRelationshipDiscoveryService.DiscoverAtQueryTimeAsync`. `RecordTokens` now records actual consumption even when it breaches the budget and returns `updated <= MaxTokenBudget`, so `IsExceeded()` halts traversal (token budget is no longer dead code — test updated to assert 120 recorded against a 100 budget). Both `RetrieveAsync` paths use `CreateLinkedTokenSource` + `CancelAfter(MaxExecutionTime)`; all LLM/traversal calls flow the deadline token `ct`.
- **D3 - Stop noise-entity persistence**: `NoiseEntityFilter.IsNoise` (`keyword` / `statistical-candidate`). Applied in `LazyEntityDiscoveryService.PersistAsync`, `LazyGraphRagService.PersistDiscoveryAsync` (also drops relationships with noise endpoints), `GraphRagService.IngestAsync`, `GraphRagService.EnsureQueryTimeEnrichmentAsync`. Noise entities stay retrieval-only.
- **D4 - Per-query retrieval trace**: `RetrievalTrace` model + settable `SearchResult.Trace` (additive, non-breaking). LazyGraphRAG reports real per-request budget counters + pruning ratio + step chain; GraphRAG reports approximate `llmCalls` + budget tokens + step chain (per-call token accounting for GraphSummary/HierarchicalSummary/GraphReasoning/RelationshipExtraction is a documented follow-up). Web `SearchCenter.razor` renders the trace block on each result card.
- **Tests**: RAGS.UnitTests 251 -> **265**. New: `GraphTraversalBudgetTests` (6), `LazyGraphRagServiceTests` (+3: per-request budget isolation, 5 concurrent retrievals, trace populated), `LazyEntityDiscoveryServiceTests` (+3 noise), `GraphRagServiceTests` (+2: keyword not persisted, trace populated). All mocks updated for the new `IGraphTraversalBudget? budget` / `CancellationToken` interface params.
- **Docs**: sprint file implementation status, File 02, File 03, AGENTS.md (new "GraphRAG / LazyGraphRAG Budget, Tokens, and Trace" section), Architecture.md, OperationsGuide.md, Development-Guidelines.md, backlog item statuses.

## Verification

- `dotnet build Aletheia.slnx` succeeds (pre-existing AngleSharp NU1902 warning only).
- RAGS.UnitTests 265 passed / Repository.UnitTests 121 passed / Repository.IntegrationTests 8 passed / Foundation.UnitTests 55 passed.
- Aletheia.Web.UnitTests: 6 failures are **pre-existing** (verified identical on a clean HEAD worktree — `CopilotStateService` session-key `v1` vs `v2`, `RepositoryApiClientUploadTests` x4, Wiki mode-buttons); unrelated to Sprint 60.

## Next

1. Docker smoke test (optional): build images, upload a document, run GraphRAG / LazyGraphRAG searches in Search Center and confirm each result card shows the retrieval trace; verify concurrent retrievals behave (per-request budgets).
2. Commit (in the user's terminal): `git add -A` then commit D1-D4 + tests + docs as Sprint 60 implementation.
