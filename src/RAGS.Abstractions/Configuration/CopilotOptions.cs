namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class CopilotOptions
{
    public const string SectionName = "Copilot";

    public Dictionary<string, string[]> KnowledgeAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DefaultAreas { get; set; } = new();

    public int RetrievalTopK { get; set; } = 8;

    public string DefaultAnswerProfile { get; set; } = string.Empty;

    public Dictionary<string, CopilotAnswerProfileOptions> AnswerProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CopilotAnswerProfileOptions
{
    public List<string> MatchTerms { get; set; } = new();

    public List<string> Areas { get; set; } = new();

    public string OutputFormat { get; set; } = "markdown";

    public bool RequireCitations { get; set; } = true;

    public List<string> Instructions { get; set; } = new();
}
