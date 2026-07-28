# Sprint: Semantic Kernel Migration

## Objective

Replace Copilot-specific AI integrations with Microsoft Semantic Kernel (SK) and establish Semantic Kernel as the default AI orchestration framework for Aletheia.

## Requirements

### Semantic Kernel

- Add Semantic Kernel as the primary AI framework.
- Route all AI operations through Semantic Kernel.
- Remove direct dependencies on Copilot-specific services where possible.
- Preserve existing AI functionality through abstraction layers.

### AI Abstractions

Ensure all AI functionality is exposed through interfaces:

- IAIService
- IChatService
- IEmbeddingService
- IAgentService

No business logic may directly reference:

- Copilot
- Ollama
- OpenAI
- Kimi
- Semantic Kernel implementation classes

### Configuration

Create a configuration section supporting multiple AI providers.

Example:

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

### Default Behavior

When no provider is explicitly selected:

- Use Semantic Kernel.
- Use Ollama.
- Use model `kimi-k2.7-code:cloud`.

### Dependency Injection

Create centralized registration:

```csharp
services.AddAletheiaAI(...)
```

Register all AI services through DI.

### Migration

Identify and replace:

- Copilot service registrations
- Copilot configuration
- Copilot abstractions
- Copilot references

with Semantic Kernel equivalents.

### Validation

Execute:

```bash
dotnet restore
dotnet build
dotnet test
```

Resolve all issues created by the migration.

## Deliverables

- Semantic Kernel integrated.
- Copilot no longer the default AI implementation.
- Multi-provider configuration model implemented.
- Ollama configured as default provider.
- `kimi-k2.7-code:cloud` configured as default model.
- AI services available through abstractions and DI.
- Successful build and test execution.
- Migration summary documenting all changes.

## Out of Scope

- New GraphRAG functionality.
- New Repository functionality.
- New HXP functionality.
- New Taxonomy/Ontology functionality.

Focus exclusively on Semantic Kernel migration and AI provider configuration.
