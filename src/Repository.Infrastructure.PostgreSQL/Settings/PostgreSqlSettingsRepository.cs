using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Settings;

public sealed class PostgreSqlSettingsRepository : ISettingsRepository
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlSettingsRepository(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rows = await connection.QueryAsync<SettingRow>(
                "SELECT key, value FROM app_settings").ConfigureAwait(false);

            return Result<IReadOnlyDictionary<string, string>>.Success(rows.ToDictionary(r => r.Key, r => r.Value));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure($"App settings retrieval failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> UpsertAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                @"INSERT INTO app_settings (key, value, updated_at, updated_by)
                  VALUES (@Key, @Value, NOW(), @UpdatedBy)
                  ON CONFLICT (key) DO UPDATE
                      SET value = EXCLUDED.value, updated_at = NOW(), updated_by = EXCLUDED.updated_by",
                new { Key = key, Value = value, UpdatedBy = updatedBy }).ConfigureAwait(false);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"App setting save failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rows = await connection.QueryAsync<SettingRow>(
                "SELECT key, value FROM user_settings WHERE user_id = @UserId",
                new { UserId = userId }).ConfigureAwait(false);

            return Result<IReadOnlyDictionary<string, string>>.Success(rows.ToDictionary(r => r.Key, r => r.Value));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure($"User settings retrieval failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> UpsertUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                @"INSERT INTO user_settings (user_id, key, value, updated_at)
                  VALUES (@UserId, @Key, @Value, NOW())
                  ON CONFLICT (user_id, key) DO UPDATE
                      SET value = EXCLUDED.value, updated_at = NOW()",
                new { UserId = userId, Key = key, Value = value }).ConfigureAwait(false);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"User setting save failed: {ex.Message}");
        }
    }

    private sealed class SettingRow
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
