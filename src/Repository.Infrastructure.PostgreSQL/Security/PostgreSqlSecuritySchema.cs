using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Npgsql;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Security;

internal static class PostgreSqlSecuritySchema
{
    private const string Sql = @"
        CREATE TABLE IF NOT EXISTS security_users (
            user_id TEXT PRIMARY KEY,
            username TEXT NOT NULL,
            normalized_username TEXT NOT NULL UNIQUE,
            email TEXT NOT NULL DEFAULT '',
            display_name TEXT NOT NULL DEFAULT '',
            password_hash TEXT NOT NULL,
            password_salt TEXT NOT NULL,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS security_user_roles (
            user_id TEXT NOT NULL REFERENCES security_users(user_id) ON DELETE CASCADE,
            role TEXT NOT NULL,
            PRIMARY KEY (user_id, role)
        );

        CREATE TABLE IF NOT EXISTS security_refresh_tokens (
            token_hash TEXT PRIMARY KEY,
            user_id TEXT NOT NULL REFERENCES security_users(user_id) ON DELETE CASCADE,
            expires_at TIMESTAMPTZ NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            is_revoked BOOLEAN NOT NULL DEFAULT FALSE
        );

        CREATE INDEX IF NOT EXISTS idx_security_refresh_tokens_user_id ON security_refresh_tokens(user_id);
        CREATE INDEX IF NOT EXISTS idx_security_refresh_tokens_expires_at ON security_refresh_tokens(expires_at);";

    public static async Task EnsureAsync(PostgreSqlConnectionFactory connectionFactory, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var batch in Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            using var command = new NpgsqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
