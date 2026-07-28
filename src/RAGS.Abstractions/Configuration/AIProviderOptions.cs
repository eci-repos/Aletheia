namespace Aletheia.RAGS.Abstractions.Configuration;

public class AIProviderOptions
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string DefaultModel { get; set; } = string.Empty;
}
