namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>Provides the Copilot orchestration playbook (Sprint 77: resolved through
/// <see cref="IAgentInstructionResolver"/> for the <c>copilot.orchestrator</c> role, falling back to
/// the orchestration script file).</summary>
public interface IChatAgentInstructionProvider
{
    Task<string> GetInstructionsAsync(CancellationToken cancellationToken = default);
}
