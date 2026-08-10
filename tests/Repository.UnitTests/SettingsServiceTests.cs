using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Application.Settings;

namespace Repository.UnitTests;

public class SettingsServiceTests
{
    [Fact]
    public async Task GetBoolAsync_returns_default_when_setting_missing()
    {
        var service = CreateService();

        var result = await service.GetBoolAsync("copilot.requireApproval", true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task SetBoolAsync_then_GetBoolAsync_round_trips_app_scope()
    {
        var service = CreateService();

        var set = await service.SetBoolAsync("copilot.requireApproval.force", true);
        var get = await service.GetBoolAsync("copilot.requireApproval.force", false);

        Assert.True(set.IsSuccess);
        Assert.True(get.IsSuccess);
        Assert.True(get.Value);
    }

    [Fact]
    public async Task SetBoolAsync_then_GetBoolAsync_round_trips_user_scope()
    {
        var service = CreateService();

        var set = await service.SetBoolAsync("copilot.requireApproval", false, "user-1");
        var get = await service.GetBoolAsync("copilot.requireApproval", true, "user-1");

        Assert.True(set.IsSuccess);
        Assert.True(get.IsSuccess);
        Assert.False(get.Value);
    }

    [Fact]
    public async Task User_settings_are_isolated_between_users()
    {
        var service = CreateService();
        await service.SetBoolAsync("copilot.requireApproval", false, "user-1");

        var other = await service.GetBoolAsync("copilot.requireApproval", true, "user-2");

        Assert.True(other.IsSuccess);
        Assert.True(other.Value); // user-2 has no preference → default true
    }

    [Fact]
    public async Task GetAppSettingsAsync_returns_cached_values()
    {
        var service = CreateService();
        await service.SetAppSettingAsync("theme", "dark", "admin");

        var result = await service.GetAppSettingsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("dark", result.Value!["theme"]);
    }

    [Fact]
    public async Task GetUserSettingsAsync_returns_cached_values()
    {
        var service = CreateService();
        await service.SetUserSettingAsync("user-1", "copilot.requireApproval", "false");

        var result = await service.GetUserSettingsAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("false", result.Value!["copilot.requireApproval"]);
    }

    [Fact]
    public async Task GetBoolAsync_parses_invalid_value_as_default()
    {
        var service = CreateService();
        await service.SetAppSettingAsync("copilot.requireApproval.force", "not-a-bool");

        var result = await service.GetBoolAsync("copilot.requireApproval.force", false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task SetAppSettingAsync_rejects_empty_key()
    {
        var service = CreateService();

        var result = await service.SetAppSettingAsync("", "value");

        Assert.True(result.IsFailure);
    }

    private static SettingsService CreateService()
    {
        return new SettingsService(new InMemorySettingsRepository());
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _app = new();
        private readonly Dictionary<string, Dictionary<string, string>> _users = new();

        public Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(_app)));
        }

        public Task<Result<bool>> UpsertAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default)
        {
            _app[key] = value;
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default)
        {
            var values = _users.TryGetValue(userId, out var user) ? user : new Dictionary<string, string>();
            return Task.FromResult(Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(values)));
        }

        public Task<Result<bool>> UpsertUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                user = new Dictionary<string, string>();
                _users[userId] = user;
            }

            user[key] = value;
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
