using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class SemanticKernelAIService : IAIService
{
    public IChatService Chat { get; }

    public IEmbeddingService Embedding { get; }

    public IAgentService Agent { get; }

    public SemanticKernelAIService(
        IChatService chatService,
        IEmbeddingService embeddingService,
        IAgentService agentService)
    {
        Chat = chatService ?? throw new ArgumentNullException(nameof(chatService));
        Embedding = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        Agent = agentService ?? throw new ArgumentNullException(nameof(agentService));
    }
}
