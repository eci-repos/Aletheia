# Semantic Kernel Migration Summary

## Overview

Replaced Copilot-specific AI integrations with Microsoft Semantic Kernel (SK) as the default AI orchestration framework for Aletheia.

## Changes

### New Abstractions (`src/RAGS.Abstractions/Interfaces/`)

- **`IAIService`** – Composite facade exposing `Chat`, `Embedding`, and `Agent` services.
- **`IChatService`** – Chat operations via LLM.
- **`IAgentService`** – Agent operations: `SummarizeAsync`, `ExplainAsync`, `DiscoverAsync`.
- **`IEmbeddingService`** – Embedding generation (mirrors `IEmbeddingProvider` for backward compatibility).

### New Configuration (`src/RAGS.Abstractions/Configuration/`)

- **`AIOptions`** – Root configuration section (`AI` in appsettings).
  - `DefaultProvider`: `"LocalOllama"`
  - `Providers`: list of `AIProviderOptions`
- **`AIProviderOptions`** – Per-provider config:
  - `Name`, `Type` (`"Ollama"`), `Enabled`, `Endpoint`, `ApiKey`, `DefaultModel`

### New Implementations (`src/RAGS.Application/SemanticKernel/`)

- **`SemanticKernelChatService`** – SK-based chat using `IChatCompletionService`.
- **`SemanticKernelAgentService`** – SK-based agent with RAG grounding.
- **`SemanticKernelAIService`** – Composite implementation of `IAIService`.
- **`SemanticKernelCopilotService`** – Adapter implementing `ICopilotService` by delegating to `IChatService` and `IAgentService`.

### DI Registration (`src/RAGS.Application/Configuration/`)

- **`AIServiceCollectionExtensions.AddAletheiaAI(IConfiguration)`**
  - Reads `AI` configuration section.
  - Builds `Microsoft.SemanticKernel.Kernel` with Ollama connector.
  - Registers all AI abstractions.
  - Provides backward-compatible `ICopilotService` registration.
  - Preserves `IEmbeddingProvider` 128-dim deterministic provider for pgvector compatibility.

### Updated Files

- **`src/Repository.API/Program.cs`**
  - Replaced manual Copilot/embedding registrations with `services.AddAletheiaAI(builder.Configuration)`.
  - Removed `using Aletheia.RAGS.Application.Copilot;`.

- **`src/RAGS.Application/Providers/SimpleEmbeddingProvider.cs`**
  - Now implements both `IEmbeddingProvider` and `IEmbeddingService`.

- **`src/RAGS.Application/RAGS.Application.csproj`**
  - Added packages:
    - `Microsoft.SemanticKernel` 1.15.0
    - `Microsoft.SemanticKernel.Connectors.Ollama` 1.19.0-alpha
    - `Microsoft.Extensions.Configuration` 8.0.0
    - `Microsoft.Extensions.Configuration.Binder` 8.0.1
    - `Microsoft.Extensions.DependencyInjection` 8.0.0
    - `Microsoft.Extensions.Options` 8.0.2
    - `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0

### Removed Files

- **`src/RAGS.Application/Copilot/CopilotService.cs`**
  - Old hardcoded-response implementation no longer registered.

### Preserved Backward Compatibility

- `ICopilotService` interface retained.
- `CopilotController` at `/api/copilot/*` unchanged.
- `IEmbeddingProvider` interface retained.
- `SimpleEmbeddingProvider` deterministic 128-dim embeddings preserved.

## Default Configuration

When no provider is configured, the system defaults to:

```json
{
  "AI": {
    "DefaultProvider": "LocalOllama",
    "Providers": [
      {
        "Name": "LocalOllama",
        "Type": "Ollama",
        "Enabled": true,
        "Endpoint": "http://localhost:11434",
        "DefaultModel": "kimi-k2.7-code:cloud"
      }
    ]
  }
}
```

## Build & Test Results

- **Build**: Succeeded (0 warnings, 0 errors)
- **Foundation.UnitTests**: 55/55 passed
- **Repository.UnitTests**: 79/79 passed
- **RAGS.UnitTests**: 32/33 passed (1 pre-existing PgVector integration test failure requiring live PostgreSQL)

## Out of Scope (per Sprint)

No changes to GraphRAG, Repository, HXP, Taxonomy, or Ontology functionality.
