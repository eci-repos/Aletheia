namespace Aletheia.RAGS.Abstractions.Configuration;

/// <summary>Sprint 77: config-seeded baseline prompts for AI agent roles. The admin Settings panel
/// writes per-role overrides into <c>app_settings</c>; a role's effective instructions are the
/// <c>app_settings</c> row when one exists, otherwise this config value (see
/// <see cref="Aletheia.RAGS.Application.AgentInstructions.AgentInstructionResolver"/>). Keys mirror
/// the DB keys (<c>agent.instructions.&lt;role&gt;</c>) so precedence resolution is a pure lookup.</summary>
public sealed class AgentInstructionsOptions
{
    public const string SectionName = "AgentInstructions";

    /// <summary>Baseline prompt per role key (see <see cref="AgentInstructionRoles"/>). A role with no
    /// configured value falls back to its role-specific baseline (composed persona / orchestration file).</summary>
    public Dictionary<string, string> Roles { get; set; } = new();
}
