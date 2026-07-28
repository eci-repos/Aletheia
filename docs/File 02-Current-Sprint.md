# Current Sprint

Sprint: Sprint 25 - Agentic Tooling for Domain-Specific Grounding

Status: Active

## Objective

Formalize the **agentic tool-calling framework** for the Copilot by auditing and exposing existing service resources as **Semantic Kernel Plugins**. This ensures that domain-specific queries (e.g., "RFP") trigger a mandatory "fetch" from the local **Aletheia Knowledge Estate** instead of relying on model training data.

---

## Background

Previous phases established a robust foundation for AI orchestration, including the migration to **Semantic Kernel** in Phase 11 and the completion of the **Graph SDK** in Phases 12–13. However, current Copilot behavior indicates that while the retrieval logic exists, it is not being consistently invoked as a mandatory "agentic tool" when the model identifies domain-specific entities. This sprint focuses on "catching up" with existing codebases to expose retrieval services as callable tools for the AI agent.

---

## Authority

The repository is the source of truth. This sprint must utilize the existing **Clean Architecture** and **Semantic Kernel** abstractions established in Sprint 11 and Sprint 22.

---

## Goals

- Audit existing AI resources in `RAGS.Abstractions` and `KnowledgeGraph.Abstractions` for conversion into Semantic Kernel Plugins
- Formalize the `AletheiaKnowledgePlugin` / `RepositoryToolPlugin` by wrapping existing `IRagsService`, `IGraphRagService`, `ILazyGraphRagService`, `IGlobalGraphSearchService`, `IKnowledgeSourceResolver`, and `IKnowledgeSourceIngestionService` methods as `[KernelFunction]` calls
- Configure `IChatPlanningService` to recognize "RFP" as a high-priority repository entity that requires a mandatory tool call
- Update system instructions to prioritize tool outputs over parametric model knowledge to eliminate hallucinations regarding repository statistics

---

## Architecture

Extend:

```text
RAGS.Application
RAGS.Abstractions
KnowledgeGraph.Application
```

---

## Core Abstractions

### AletheiaKnowledgePlugin

Exposed functions:

```csharp
[KernelFunction]
[Description("Search the local RAGS vector index for relevant chunks.")]
Task<IReadOnlyList<SearchResult>> SearchRagsAsync(string query, int topK, CancellationToken cancellationToken = default)

[KernelFunction]
[Description("Search the GraphRAG community summaries for corpus-level answers.")]
Task<GlobalSearchResult> SearchGraphRagAsync(string query, CancellationToken cancellationToken = default)

[KernelFunction]
[Description("Search the LazyGraphRAG index for corpus-level answers.")]
Task<GlobalSearchResult> SearchLazyGraphRagAsync(string query, CancellationToken cancellationToken = default)

[KernelFunction]
[Description("Run a global graph search across the entire Aletheia Knowledge Estate.")]
Task<GlobalSearchResult> SearchGlobalGraphAsync(string query, CancellationToken cancellationToken = default)

[KernelFunction]
[Description("Resolve the most relevant knowledge source for a user query.")]
Task<KnowledgeSource?> ResolveKnowledgeSourceAsync(string userMessage, CancellationToken cancellationToken = default)

[KernelFunction]
[Description("Ensure a resolved knowledge source is ingested into the search index.")]
Task<bool> EnsureSourceIngestedAsync(Guid sourceId, CancellationToken cancellationToken = default)
```

### ChatExecutionPlan / ChatPlanRecord

Added properties:

```text
RequiresToolCall
ToolName
ToolArguments
```

### IChatPlanningService

For RFP and other domain-specific queries, `CreatePlanAsync` must set:

```text
Mode = CorpusAnalysis or TimelineAnalysis
RequiresToolCall = true
ToolName = "AletheiaKnowledgePlugin.SearchRags" or "AletheiaKnowledgePlugin.SearchGraphRag"
ToolArguments = { "query", "topK" }
```

### ChatExecutionEngine

When `RequiresToolCall` is true, the engine must:

```text
1. Begin "Retrieving context" step
2. Invoke the named tool/plugin function
3. Convert tool results into SearchResult context
4. Complete "Retrieving context" step
5. Continue to synthesis using the tool output as grounding context
```

### ChatTelemetryService

Telemetry must include:

```text
ToolName
ToolInvocationCount
RetrievalStrategy used
```

---

## Tool Rules

- All plugin functions must be decorated with `[KernelFunction]` and `[Description]` attributes.
- Plugin functions must accept primitive or string arguments so Semantic Kernel can invoke them via planner/function calling.
- Plugin results must include citations or source references.
- The Copilot system prompt must instruct the model to call a repository tool whenever a domain-specific term is detected.

---

## Validation

Scenario test:

```text
Ask: "Summarize registered RFP opportunities in the past 10 years."
```

Success metric:

```text
The Execution Plan identifies a tool call to the local repository.
The final response summarizes only the 2 RFPs in the WRAGS repository with accompanying citations.
```

Telemetry metric:

```text
The Telemetry Panel indicates that the response was generated using the Repository Plugin rather than general knowledge.
```

Build/test:

```text
dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj
```

---

## Implementation Notes

Added in `RAGS.Application`:

- `AletheiaKnowledgePlugin` class with `[KernelFunction]` methods wrapping `IRagsService`, `IGraphRagService`, `ILazyGraphRagService`, and `IGlobalGraphSearchService`.
- `RepositoryToolPlugin` alias registration so both names resolve to the same plugin instance.

Added in `RAGS.Abstractions`:

- `ChatExecutionPlan` and `ChatPlanRecord` extended with `RequiresToolCall`, `ToolName`, and `ToolArguments`.
- `ChatExecutionTelemetry` extended with `ToolName` and `ToolInvocationCount`.

Updated in `RAGS.Application`:

- `ChatPlanningService` detects domain-specific entities (RFP, WRAGS, procurement, last N years) and emits tool-call plans.
- `ChatExecutionEngine` invokes the configured tool before synthesis and converts results into grounded context.
- `ChatTelemetryService` reports the tool name and invocation count.
- `RetrievalAugmentedPromptBuilder` and `SemanticKernelCopilotService` treat tool output as authoritative context.

Registered in `AIServiceCollectionExtensions`:

- `AletheiaKnowledgePlugin` as a singleton plugin.

---

## Exit Criteria

- Existing RAGS and Graph services are successfully exposed as SK Plugins.
- "RFP" queries reliably trigger local tool calls.
- Telemetry confirms the use of internal tools for domain-specific answers.
- The model no longer provides external statistics for repository-specific terms.
- Build and all unit tests pass.
