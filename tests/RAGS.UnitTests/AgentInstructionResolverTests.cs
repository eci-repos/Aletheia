using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.AgentInstructions;
using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.Extensions.Options;

namespace RAGS.UnitTests;

public class AgentInstructionResolverTests
{
    [Fact]
    public async Task ResolveAsync_returns_db_override_when_row_exists()
    {
        var settings = new FakeSettingsService();
        settings.SetString(AgentInstructionRoles.SettingKey(AgentInstructionRoles.GraphRagExtractor), "Custom extractor prompt.");
        var resolver = CreateResolver(settings, config: new Dictionary<string, string>
        {
            [AgentInstructionRoles.GraphRagExtractor] = "Config extractor prompt."
        });

        var result = await resolver.ResolveAsync(AgentInstructionRoles.GraphRagExtractor);

        Assert.True(result.IsSuccess);
        Assert.Equal("Custom extractor prompt.", result.Value!.Value);
        Assert.Equal("override", result.Value.Source);
    }

    [Fact]
    public async Task ResolveAsync_returns_config_value_when_no_row()
    {
        var resolver = CreateResolver(new FakeSettingsService(), config: new Dictionary<string, string>
        {
            [AgentInstructionRoles.GraphRagExtractor] = "Config extractor prompt."
        });

        var result = await resolver.ResolveAsync(AgentInstructionRoles.GraphRagExtractor);

        Assert.True(result.IsSuccess);
        Assert.Equal("Config extractor prompt.", result.Value!.Value);
        Assert.Equal("config", result.Value.Source);
    }

    [Fact]
    public async Task ResolveAsync_returns_config_value_after_row_cleared()
    {
        var settings = new FakeSettingsService();
        settings.SetString(AgentInstructionRoles.SettingKey(AgentInstructionRoles.GraphRagExtractor), "Custom extractor prompt.");
        var resolver = CreateResolver(settings, config: new Dictionary<string, string>
        {
            [AgentInstructionRoles.GraphRagExtractor] = "Config extractor prompt."
        });

        settings.Clear(AgentInstructionRoles.SettingKey(AgentInstructionRoles.GraphRagExtractor));
        var result = await resolver.ResolveAsync(AgentInstructionRoles.GraphRagExtractor);

        Assert.True(result.IsSuccess);
        Assert.Equal("Config extractor prompt.", result.Value!.Value);
        Assert.Equal("config", result.Value.Source);
    }

    [Fact]
    public async Task ResolveAsync_ignores_whitespace_row_and_uses_config()
    {
        var settings = new FakeSettingsService();
        settings.SetString(AgentInstructionRoles.SettingKey(AgentInstructionRoles.GraphRagExtractor), "   ");
        var resolver = CreateResolver(settings, config: new Dictionary<string, string>
        {
            [AgentInstructionRoles.GraphRagExtractor] = "Config extractor prompt."
        });

        var result = await resolver.ResolveAsync(AgentInstructionRoles.GraphRagExtractor);

        Assert.True(result.IsSuccess);
        Assert.Equal("Config extractor prompt.", result.Value!.Value);
        Assert.Equal("config", result.Value.Source);
    }

    [Fact]
    public async Task ResolveAsync_fails_for_unknown_role()
    {
        var resolver = CreateResolver(new FakeSettingsService());

        var result = await resolver.ResolveAsync("not.a.role");

        Assert.True(result.IsFailure);
        Assert.Contains("Unknown agent instruction role", result.Error);
    }

    [Fact]
    public async Task ResolveAsync_composes_assistant_persona_from_chat_agent_options()
    {
        var chatAgent = new ChatAgentOptions
        {
            Role = "You are TestBot.",
            RepositoryDescription = "A test repository.",
            Mandate = "Ground answers in documents.",
            NoInformationResponse = "Not found."
        };
        var resolver = CreateResolver(new FakeSettingsService(), chatAgentOptions: chatAgent);

        var result = await resolver.ResolveAsync(AgentInstructionRoles.CopilotAssistant);

        Assert.True(result.IsSuccess);
        Assert.Equal("config", result.Value!.Source);
        Assert.Contains("You are TestBot.", result.Value.Value);
        Assert.Contains("A test repository.", result.Value.Value);
        Assert.Contains("Not found.", result.Value.Value);
    }

    [Fact]
    public async Task ResolveAsync_loads_orchestration_script_from_file()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"orchestration-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(tempFile, "Repository orchestration playbook: search first, then answer.");
            var chatAgent = new ChatAgentOptions { OrchestrationScriptPath = tempFile };
            var resolver = CreateResolver(new FakeSettingsService(), chatAgentOptions: chatAgent);

            var result = await resolver.ResolveAsync(AgentInstructionRoles.CopilotOrchestrator);

            Assert.True(result.IsSuccess);
            Assert.Equal("config", result.Value!.Source);
            Assert.Equal("Repository orchestration playbook: search first, then answer.", result.Value.Value);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ResolveAsync_returns_empty_for_role_without_baseline()
    {
        var resolver = CreateResolver(new FakeSettingsService());

        var result = await resolver.ResolveAsync(AgentInstructionRoles.GraphRagQuery);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value!.Value);
        Assert.Equal("config", result.Value.Source);
    }

    private static AgentInstructionResolver CreateResolver(
        ISettingsService settings,
        Dictionary<string, string>? config = null,
        ChatAgentOptions? chatAgentOptions = null)
    {
        var options = new AgentInstructionsOptions { Roles = config ?? new Dictionary<string, string>() };
        return new AgentInstructionResolver(
            settings,
            Options.Create(options),
            Options.Create(chatAgentOptions ?? new ChatAgentOptions()));
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private readonly Dictionary<string, string> _app = new();

        public void SetString(string key, string value) => _app[key] = value;
        public void Clear(string key) => _app.Remove(key);

        public Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(_app)));

        public Task<Result<bool>> SetAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default)
        {
            _app[key] = value;
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>()));

        public Task<Result<bool>> SetUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> GetBoolAsync(string key, bool defaultValue, string? userId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<bool>.Success(defaultValue));

        public Task<Result<bool>> SetBoolAsync(string key, bool value, string? userId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<string?>> GetStringAsync(string key, string? userId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string?>.Success(_app.TryGetValue(key, out var v) ? v : null));

        public Task<Result<bool>> SetStringAsync(string key, string value, string? userId = null, CancellationToken cancellationToken = default)
        {
            _app[key] = value;
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> ClearAppSettingAsync(string key, CancellationToken cancellationToken = default)
        {
            _app.Remove(key);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
