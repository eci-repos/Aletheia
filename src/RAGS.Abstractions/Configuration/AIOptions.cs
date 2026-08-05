namespace Aletheia.RAGS.Abstractions.Configuration;

public class AIOptions
{
    public const string SectionName = "AI";

    public string DefaultProvider { get; set; } = "LocalOllama";

    public List<AIProviderOptions> Providers { get; set; } = new();

    /// <summary>Embedding provider: "Simple" (deterministic fallback, 128-dim) or "Ollama" (uses the enabled provider's EmbeddingModel).</summary>
    public string EmbeddingProvider { get; set; } = "Simple";

    /// <summary>Expected embedding dimension for the chosen model (used for the embeddings table schema; default 768 for Ollama models such as nomic-embed-text).</summary>
    public int EmbeddingDimension { get; set; } = 768;
}
