namespace Aletheia.RAGS.Abstractions.Configuration;

public class AIOptions
{
    public const string SectionName = "AI";

    public string DefaultProvider { get; set; } = "LocalOllama";

    public List<AIProviderOptions> Providers { get; set; } = new();
}
