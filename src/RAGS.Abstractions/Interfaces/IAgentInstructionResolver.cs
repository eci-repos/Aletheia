using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>Sprint 77: resolves a role's effective AI agent instructions. Precedence is
/// <c>app_settings</c> row (override) → config baseline (<c>AgentInstructions</c> section, or the
/// role-specific baseline such as the composed Copilot persona / orchestration file).</summary>
public interface IAgentInstructionResolver
{
    Task<Result<AgentInstructionResolution>> ResolveAsync(string role, CancellationToken cancellationToken = default);
}
