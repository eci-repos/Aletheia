using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.Planning;
using Aletheia.RAGS.Application.Providers;
using Aletheia.RAGS.Application.SemanticKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Aletheia.RAGS.Application.Configuration;

public static class AIServiceCollectionExtensions
{
    public static IServiceCollection AddAletheiaAI(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AIOptions.SectionName).Get<AIOptions>() ?? new AIOptions();

        var provider = options.Providers.FirstOrDefault(p =>
            p.Name == options.DefaultProvider && p.Enabled)
            ?? options.Providers.FirstOrDefault(p => p.Enabled);

        if (provider is null)
        {
            provider = new AIProviderOptions
            {
                Name = "LocalOllama",
                Type = "Ollama",
                Enabled = true,
                Endpoint = "http://localhost:11434",
                DefaultModel = "gpt-oss:120b-cloud"
            };
        }

        services.Configure<AIOptions>(configuration.GetSection(AIOptions.SectionName));
        services.Configure<CopilotOptions>(configuration.GetSection(CopilotOptions.SectionName));
        services.Configure<ChatPlanningOptions>(configuration.GetSection(ChatPlanningOptions.SectionName));
        services.Configure<ChatExecutionEngineOptions>(configuration.GetSection(ChatExecutionEngineOptions.SectionName));
        services.Configure<ChatAgentOptions>(configuration.GetSection(ChatAgentOptions.SectionName));

        // Chat planning abstractions
        services.AddSingleton<IChatPlanningService, ChatPlanningService>();
        services.AddSingleton<IChatPlanRepository, InMemoryChatPlanRepository>();
        services.AddSingleton<IChatPlanApprovalService, ChatPlanApprovalService>();
        services.AddSingleton<IChatProgressStore, InMemoryChatProgressStore>();
        services.AddSingleton<IChatTelemetryService, ChatTelemetryService>();
        services.AddSingleton<IDocumentTemplateRegistry, DocumentTemplateRegistry>();
        services.AddSingleton<ChatExecutionEngine>();
        services.AddSingleton<IChatExecutionService>(sp => sp.GetRequiredService<ChatExecutionEngine>());
        services.AddSingleton<IChatExecutionEngine>(sp => sp.GetRequiredService<ChatExecutionEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<IChatExecutionEngine>());

        // Build Semantic Kernel
        var kernelBuilder = Kernel.CreateBuilder();

        if (provider.Type.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable SKEXP0070
            kernelBuilder.AddOllamaChatCompletion(
                modelId: provider.DefaultModel,
                endpoint: new Uri(provider.Endpoint ?? "http://localhost:11434"));
#pragma warning restore SKEXP0070

            services.Configure<Microsoft.SemanticKernel.Connectors.Ollama.OllamaPromptExecutionSettings>(settings =>
            {
                settings.Temperature = 0.3f;
                settings.TopP = 0.9f;
                settings.NumPredict = provider.MaxOutputTokens ?? 8_192;
                settings.ExtensionData ??= new Dictionary<string, object>();
                settings.ExtensionData["num_ctx"] = provider.ContextLength ?? 128_000;
                if (provider.RequestTimeoutSeconds.HasValue)
                {
                    settings.ExtensionData["timeout"] = provider.RequestTimeoutSeconds.Value;
                }
            });
        }

        // Agentic knowledge tools / plugins
        services.AddSingleton<Aletheia.RAGS.Application.SemanticKernel.AletheiaKnowledgePlugin>();
        services.AddSingleton<Aletheia.RAGS.Application.SemanticKernel.RepositoryToolPlugin>(
            sp => new Aletheia.RAGS.Application.SemanticKernel.RepositoryToolPlugin(
                sp.GetRequiredService<Aletheia.RAGS.Application.SemanticKernel.AletheiaKnowledgePlugin>()));
        services.AddSingleton(sp =>
        {
            var kernel = kernelBuilder.Build();
            kernel.Plugins.AddFromObject(
                sp.GetRequiredService<Aletheia.RAGS.Application.SemanticKernel.AletheiaKnowledgePlugin>(),
                "AletheiaKnowledgePlugin");
            kernel.Plugins.AddFromObject(
                sp.GetRequiredService<Aletheia.RAGS.Application.SemanticKernel.RepositoryToolPlugin>(),
                "RepositoryTool");
            return kernel;
        });

        // Tool invoker used by the chat execution engine to call registered repository tools.
        services.AddSingleton<IChatToolInvoker, Aletheia.RAGS.Application.Planning.KernelChatToolInvoker>();

        // Embedding: keep deterministic provider for 128-dim pgvector compatibility
        services.AddSingleton<SimpleEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProvider>(sp => sp.GetRequiredService<SimpleEmbeddingProvider>());
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<SimpleEmbeddingProvider>());

        // Semantic Kernel AI services
        services.AddSingleton<IChatAgentInstructionProvider, FileChatAgentInstructionProvider>();
        services.AddSingleton<IKnowledgeSourceResolver, MetadataKnowledgeSourceResolver>();
        services.AddSingleton<IChatService, SemanticKernelChatService>();
        services.AddSingleton<IAgentService, SemanticKernelAgentService>();
        services.AddSingleton<IAIService, SemanticKernelAIService>();

        // Backward-compatible Copilot service via Semantic Kernel
        services.AddSingleton<ICopilotService, SemanticKernelCopilotService>();

        return services;
    }
}
