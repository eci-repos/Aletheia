namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>Sprint 77: the effective instructions for one agent role after precedence resolution
/// (app_settings override → config baseline). <see cref="Source"/> is <c>"override"</c> when an
/// <c>app_settings</c> row exists, otherwise <c>"config"</c> — row-existence is the "modified" marker.</summary>
public sealed record AgentInstructionResolution(string Role, string Value, string Source);
