namespace Aletheia.RAGS.Abstractions.Configuration;

/// <summary>Sprint 77: canonical registry of AI agent roles. "Role" means agent role (the LLM task),
/// not user role — this is not RBAC. The registry is the single enumeration the API validates against
/// and the Settings panel lists; unknown roles are rejected at the API boundary.</summary>
public static class AgentInstructionRoles
{
    public const string CopilotAssistant = "copilot.assistant";
    public const string CopilotOrchestrator = "copilot.orchestrator";
    public const string GraphRagExtractor = "graphrag.extractor";
    public const string GraphRagSummarizer = "graphrag.summarizer";
    public const string GraphRagQuery = "graphrag.query";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CopilotAssistant,
        CopilotOrchestrator,
        GraphRagExtractor,
        GraphRagSummarizer,
        GraphRagQuery
    };

    public static bool IsKnown(string role) => All.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>app_settings</c> key for a role's override row (mirrors the config section key).</summary>
    public static string SettingKey(string role) => $"agent.instructions.{role}";
}
