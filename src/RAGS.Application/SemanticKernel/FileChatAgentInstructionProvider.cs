using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aletheia.RAGS.Application.SemanticKernel;

/// <summary>Sprint 77: the Copilot orchestration playbook provider. Resolves the
/// <c>copilot.orchestrator</c> role through <see cref="IAgentInstructionResolver"/> (app_settings
/// override → config baseline → orchestration script file). When no resolver is present (backward
/// compat), loads the orchestration script file directly.</summary>
public sealed class FileChatAgentInstructionProvider : IChatAgentInstructionProvider
{
    private readonly ChatAgentOptions _options;
    private readonly IAgentInstructionResolver? _resolver;
    private readonly ILogger<FileChatAgentInstructionProvider> _logger;
    private readonly object _gate = new();
    private string? _cachedInstructions;
    private bool _loaded;

    public FileChatAgentInstructionProvider(
        IOptions<ChatAgentOptions>? options = null,
        IAgentInstructionResolver? resolver = null,
        ILogger<FileChatAgentInstructionProvider>? logger = null)
    {
        _options = options?.Value ?? new ChatAgentOptions();
        _resolver = resolver;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileChatAgentInstructionProvider>.Instance;
    }

    public async Task<string> GetInstructionsAsync(CancellationToken cancellationToken = default)
    {
        if (_resolver is not null)
        {
            var resolved = await _resolver
                .ResolveAsync(AgentInstructionRoles.CopilotOrchestrator, cancellationToken)
                .ConfigureAwait(false);
            if (resolved.IsSuccess && !string.IsNullOrWhiteSpace(resolved.Value!.Value))
            {
                return resolved.Value.Value;
            }
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return _cachedInstructions ?? string.Empty;
            }

            _loaded = true;
            _cachedInstructions = LoadInstructions();
            return _cachedInstructions;
        }
    }

    private string LoadInstructions()
    {
        if (string.IsNullOrWhiteSpace(_options.OrchestrationScriptPath))
        {
            return string.Empty;
        }

        foreach (var path in CandidatePaths(_options.OrchestrationScriptPath.Trim()))
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

        _logger.LogInformation("Chat agent orchestration script {Path} was not found.", _options.OrchestrationScriptPath);
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
