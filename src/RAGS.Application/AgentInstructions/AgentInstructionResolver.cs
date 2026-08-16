using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aletheia.RAGS.Application.AgentInstructions;

/// <summary>Sprint 77: precedence resolver for AI agent instructions. A role's effective instructions
/// are the <c>app_settings</c> row (<c>agent.instructions.&lt;role&gt;</c>) when one exists, otherwise
/// the config baseline — the <c>AgentInstructions</c> section value, or the role-specific baseline
/// (the composed Copilot persona for <c>copilot.assistant</c>, the orchestration script file for
/// <c>copilot.orchestrator</c>). Row-existence is the "modified" marker; clearing the row (DELETE)
/// returns the role to its config baseline.</summary>
public sealed class AgentInstructionResolver : IAgentInstructionResolver
{
    private readonly ISettingsService _settings;
    private readonly AgentInstructionsOptions _options;
    private readonly ChatAgentOptions _chatAgentOptions;
    private readonly ILogger<AgentInstructionResolver> _logger;

    public AgentInstructionResolver(
        ISettingsService settings,
        IOptions<AgentInstructionsOptions>? options = null,
        IOptions<ChatAgentOptions>? chatAgentOptions = null,
        ILogger<AgentInstructionResolver>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _options = options?.Value ?? new AgentInstructionsOptions();
        _chatAgentOptions = chatAgentOptions?.Value ?? new ChatAgentOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentInstructionResolver>.Instance;
    }

    public async Task<Result<AgentInstructionResolution>> ResolveAsync(string role, CancellationToken cancellationToken = default)
    {
        if (!AgentInstructionRoles.IsKnown(role))
        {
            return Result<AgentInstructionResolution>.Failure($"Unknown agent instruction role '{role}'.");
        }

        var key = AgentInstructionRoles.SettingKey(role);
        var stored = await _settings.GetStringAsync(key, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (stored.IsFailure)
        {
            return Result<AgentInstructionResolution>.Failure(stored.Error ?? "Agent instruction lookup failed.");
        }

        if (!string.IsNullOrWhiteSpace(stored.Value))
        {
            return Result<AgentInstructionResolution>.Success(new(role, stored.Value, "override"));
        }

        return Result<AgentInstructionResolution>.Success(new(role, ResolveConfigBaseline(role), "config"));
    }

    private string ResolveConfigBaseline(string role)
    {
        if (_options.Roles.TryGetValue(role, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return role switch
        {
            AgentInstructionRoles.CopilotAssistant => ComposeAssistantPersona(),
            AgentInstructionRoles.CopilotOrchestrator => LoadOrchestrationScript(),
            _ => string.Empty
        };
    }

    /// <summary>The Copilot assistant persona composed from <c>ChatAgentOptions</c> — the config
    /// baseline for <c>copilot.assistant</c> when the <c>AgentInstructions</c> section has no value.</summary>
    private string ComposeAssistantPersona()
    {
        return $"""
            {_chatAgentOptions.Role}
            {_chatAgentOptions.RepositoryDescription}
            {_chatAgentOptions.Mandate}
            When the user's question concerns repository content such as projects, RFPs, contracts, requirements, wiki pages, teams, schedules, rules, or mandates, you must ground your answer exclusively in retrieved repository documents.
            You have no access to general internet knowledge, LLM training data, market data, or external facts.
            If the requested information is not present in the retrieved context, respond with: {_chatAgentOptions.NoInformationResponse}
            """;
    }

    /// <summary>The orchestration script file — the config baseline for <c>copilot.orchestrator</c>.</summary>
    private string LoadOrchestrationScript()
    {
        if (string.IsNullOrWhiteSpace(_chatAgentOptions.OrchestrationScriptPath))
        {
            return string.Empty;
        }

        foreach (var path in CandidatePaths(_chatAgentOptions.OrchestrationScriptPath.Trim()))
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to load chat agent orchestration script from {Path}.", path);
            }
        }

        _logger.LogInformation("Chat agent orchestration script {Path} was not found.", _chatAgentOptions.OrchestrationScriptPath);
        return string.Empty;
    }

    private static IEnumerable<string> CandidatePaths(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            yield return configuredPath;
            yield break;
        }

        yield return Path.Combine(AppContext.BaseDirectory, configuredPath);
        yield return Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
    }
}
