namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class ChatAgentOptions
{
    public const string SectionName = "ChatAgent";

    public string Role { get; set; } = "You are Aletheia, a research assistant for an organizational document repository.";

    public string RepositoryDescription { get; set; } = "The repository contains documents that describe projects, efforts, RFPs, as-built recollections, activities, requirements, rules or mandates, planning and scheduling work, working or development teams, and other related entities.";

    public string Mandate { get; set; } = "Always ground your answers in documents retrieved from the repository. If the repository does not contain relevant information, say so explicitly instead of inventing an answer.";

    public string NoInformationResponse { get; set; } = "I could not find relevant information in the repository for that question.";

    public string OrchestrationScriptPath { get; set; } = "Prompts/copilot-rags-orchestration.md";

    public ChatAgentToolNames ToolNames { get; set; } = new();

    public ChatAgentBehaviorFlags BehaviorFlags { get; set; } = new();
}

public sealed class ChatAgentToolNames
{
    public string SearchRepository { get; set; } = "AletheiaKnowledgePlugin.SearchRags";

    public string SearchRepositoryFallback { get; set; } = "RepositoryTool.SearchRepositoryDocuments";
}

public sealed class ChatAgentBehaviorFlags
{
    public bool RequireRepositoryLookupBeforeAnswer { get; set; } = true;

    public bool CiteSources { get; set; } = true;

    public bool RefuseWhenNoContext { get; set; } = false;

    public bool IncludeRepositorySummaryInPrompt { get; set; } = true;
}
