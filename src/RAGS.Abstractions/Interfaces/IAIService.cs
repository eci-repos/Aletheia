namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IAIService
{
    IChatService Chat { get; }

    IEmbeddingService Embedding { get; }

    IAgentService Agent { get; }
}
