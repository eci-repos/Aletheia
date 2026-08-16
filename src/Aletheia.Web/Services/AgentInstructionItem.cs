namespace Aletheia.Web.Services;

/// <summary>Sprint 77: one AI agent role's effective instructions as shown on the admin Settings panel.
/// <see cref="Source"/> is <c>"override"</c> when an app_settings row exists, otherwise <c>"config"</c>.
/// Mutable so the panel can bind the textarea to <see cref="Value"/>.</summary>
public sealed class AgentInstructionItem
{
    public string Role { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Source { get; set; } = "config";
}
