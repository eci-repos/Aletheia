namespace Aletheia.RAGS.Abstractions.Configuration;

public class AIProviderOptions
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string DefaultModel { get; set; } = string.Empty;

    public int? ContextLength { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? RequestTimeoutSeconds { get; set; }

    public int? BatchSize { get; set; }

    /// <summary>Model used for embeddings when this provider is selected for embedding (e.g., nomic-embed-text).</summary>
    public string? EmbeddingModel { get; set; }
}
