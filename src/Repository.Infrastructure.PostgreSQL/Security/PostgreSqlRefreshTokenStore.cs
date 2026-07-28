using System.Security.Cryptography;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Aletheia.Security.Authentication;
using Dapper;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Security;

public sealed class PostgreSqlRefreshTokenStore : IRefreshTokenStore
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlRefreshTokenStore(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AddAsync(RefreshTokenEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO security_refresh_tokens (token_hash, user_id, expires_at, created_at, is_revoked)
            VALUES (@TokenHash, @UserId, @ExpiresAt, @CreatedAt, @IsRevoked)
            ON CONFLICT (token_hash)
            DO UPDATE SET
                user_id = EXCLUDED.user_id,
                expires_at = EXCLUDED.expires_at,
                created_at = EXCLUDED.created_at,
                is_revoked = EXCLUDED.is_revoked";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql, new
        {
            TokenHash = HashToken(entry.Token),
            entry.UserId,
            entry.ExpiresAt,
            entry.CreatedAt,
            entry.IsRevoked
        }).ConfigureAwait(false);
    }

    public async Task<RefreshTokenEntry?> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                user_id as ""UserId"",
                expires_at as ""ExpiresAt"",
                created_at as ""CreatedAt"",
                is_revoked as ""IsRevoked""
            FROM security_refresh_tokens
            WHERE token_hash = @TokenHash
            LIMIT 1";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QueryFirstOrDefaultAsync<TokenRow>(sql, new { TokenHash = HashToken(token) }).ConfigureAwait(false);
        return row is null
            ? null
            : new RefreshTokenEntry(token, row.UserId, row.ExpiresAt, row.IsRevoked, row.CreatedAt);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE security_refresh_tokens SET is_revoked = TRUE WHERE token_hash = @TokenHash";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql, new { TokenHash = HashToken(token) }).ConfigureAwait(false);
    }

    public async Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE security_refresh_tokens SET is_revoked = TRUE WHERE user_id = @UserId";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql, new { UserId = userId }).ConfigureAwait(false);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM security_refresh_tokens WHERE expires_at < NOW()";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private sealed record TokenRow
    {
        public string UserId { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public bool IsRevoked { get; init; }
    }
}
