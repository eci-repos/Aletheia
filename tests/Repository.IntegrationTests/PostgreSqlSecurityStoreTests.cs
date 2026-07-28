using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Aletheia.Repository.Infrastructure.PostgreSQL.Security;
using Aletheia.Security.Authentication;
using Aletheia.Security.Services;
using Npgsql;
using Repository.IntegrationTests.Fixtures;

namespace Repository.IntegrationTests;

public sealed class PostgreSqlSecurityStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSecurityStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserStore_persists_users_and_roles_across_instances()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var factory = new PostgreSqlConnectionFactory(_fixture.ConnectionString);
        var firstStore = new PostgreSqlUserStore(factory);
        var userId = Guid.NewGuid().ToString("N");
        var username = $"pg-user-{Guid.NewGuid():N}";
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var user = new UserRecord(
            userId,
            username,
            "pg-user@example.test",
            "PostgreSQL User",
            "hash",
            "salt",
            new[] { "Reader", "Administrator", "Reader" },
            isEnabled: true,
            createdAt: createdAt);

        await firstStore.AddAsync(user);

        var secondStore = new PostgreSqlUserStore(factory);
        var saved = await secondStore.GetByUsernameAsync(username.ToUpperInvariant());

        Assert.NotNull(saved);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal(username, saved.Username);
        Assert.Equal("pg-user@example.test", saved.Email);
        Assert.True(saved.IsEnabled);
        Assert.Contains("Administrator", saved.Roles);
        Assert.Contains("Reader", saved.Roles);
        Assert.Equal(2, saved.Roles.Count);

        saved.IsEnabled = false;
        saved.Roles.Clear();
        saved.Roles.Add("Auditor");
        await secondStore.UpdateAsync(saved);

        var updated = await firstStore.GetByIdAsync(userId);

        Assert.NotNull(updated);
        Assert.False(updated.IsEnabled);
        Assert.Equal(new[] { "Auditor" }, updated.Roles);
    }

    [Fact]
    public async Task RefreshTokenStore_hashes_tokens_and_persists_revocation()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var factory = new PostgreSqlConnectionFactory(_fixture.ConnectionString);
        var userStore = new PostgreSqlUserStore(factory);
        var userId = Guid.NewGuid().ToString("N");
        var username = $"pg-token-user-{Guid.NewGuid():N}";
        await userStore.AddAsync(new UserRecord(
            userId,
            username,
            "pg-token-user@example.test",
            "PostgreSQL Token User",
            "hash",
            "salt"));

        var token = $"refresh-token-{Guid.NewGuid():N}";
        var firstTokenStore = new PostgreSqlRefreshTokenStore(factory);
        await firstTokenStore.AddAsync(new RefreshTokenEntry(token, userId, DateTimeOffset.UtcNow.AddHours(1)));

        var storedTokenHash = await GetStoredTokenHashAsync(factory, userId);
        Assert.False(string.IsNullOrWhiteSpace(storedTokenHash));
        Assert.NotEqual(token, storedTokenHash);

        var secondTokenStore = new PostgreSqlRefreshTokenStore(factory);
        var saved = await secondTokenStore.GetAsync(token);

        Assert.NotNull(saved);
        Assert.Equal(userId, saved.UserId);
        Assert.True(saved.IsValid);

        await secondTokenStore.RevokeAsync(token);

        var revoked = await firstTokenStore.GetAsync(token);
        Assert.NotNull(revoked);
        Assert.True(revoked.IsRevoked);
        Assert.False(revoked.IsValid);
    }

    private static async Task<string?> GetStoredTokenHashAsync(PostgreSqlConnectionFactory factory, string userId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT token_hash FROM security_refresh_tokens WHERE user_id = @UserId LIMIT 1",
            connection);
        command.Parameters.AddWithValue("UserId", userId);

        return await command.ExecuteScalarAsync() as string;
    }
}
