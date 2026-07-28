using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Aletheia.Security.Services;
using Dapper;
using Npgsql;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Security;

public sealed class PostgreSqlUserStore : IUserStore
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlUserStore(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AddAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                user_id as ""UserId"",
                username as ""Username"",
                email as ""Email"",
                display_name as ""DisplayName"",
                password_hash as ""PasswordHash"",
                password_salt as ""PasswordSalt"",
                is_enabled as ""IsEnabled"",
                created_at as ""CreatedAt""
            FROM security_users
            WHERE user_id = @UserId
            LIMIT 1";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(sql, new { UserId = userId }).ConfigureAwait(false);
        return row is null ? null : await MapAsync(connection, row).ConfigureAwait(false);
    }

    public async Task<UserRecord?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                user_id as ""UserId"",
                username as ""Username"",
                email as ""Email"",
                display_name as ""DisplayName"",
                password_hash as ""PasswordHash"",
                password_salt as ""PasswordSalt"",
                is_enabled as ""IsEnabled"",
                created_at as ""CreatedAt""
            FROM security_users
            WHERE normalized_username = @NormalizedUsername
            LIMIT 1";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(sql, new { NormalizedUsername = Normalize(username) }).ConfigureAwait(false);
        return row is null ? null : await MapAsync(connection, row).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                user_id as ""UserId"",
                username as ""Username"",
                email as ""Email"",
                display_name as ""DisplayName"",
                password_hash as ""PasswordHash"",
                password_salt as ""PasswordSalt"",
                is_enabled as ""IsEnabled"",
                created_at as ""CreatedAt""
            FROM security_users
            ORDER BY username";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = (await connection.QueryAsync<UserRow>(sql).ConfigureAwait(false)).ToList();
        var users = new List<UserRecord>(rows.Count);
        foreach (var row in rows)
        {
            users.Add(await MapAsync(connection, row).ConfigureAwait(false));
        }

        return users.AsReadOnly();
    }

    public async Task<bool> RemoveAsync(string userId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM security_users WHERE user_id = @UserId";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.ExecuteAsync(sql, new { UserId = userId }).ConfigureAwait(false);
        return rows > 0;
    }

    private async Task UpsertAsync(UserRecord user, CancellationToken cancellationToken)
    {
        const string userSql = @"
            INSERT INTO security_users (
                user_id, username, normalized_username, email, display_name,
                password_hash, password_salt, is_enabled, created_at)
            VALUES (
                @UserId, @Username, @NormalizedUsername, @Email, @DisplayName,
                @PasswordHash, @PasswordSalt, @IsEnabled, @CreatedAt)
            ON CONFLICT (user_id)
            DO UPDATE SET
                username = EXCLUDED.username,
                normalized_username = EXCLUDED.normalized_username,
                email = EXCLUDED.email,
                display_name = EXCLUDED.display_name,
                password_hash = EXCLUDED.password_hash,
                password_salt = EXCLUDED.password_salt,
                is_enabled = EXCLUDED.is_enabled";

        const string deleteRolesSql = "DELETE FROM security_user_roles WHERE user_id = @UserId";
        const string insertRoleSql = @"
            INSERT INTO security_user_roles (user_id, role)
            VALUES (@UserId, @Role)
            ON CONFLICT DO NOTHING";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(userSql, new
        {
            user.UserId,
            user.Username,
            NormalizedUsername = Normalize(user.Username),
            user.Email,
            user.DisplayName,
            user.PasswordHash,
            user.PasswordSalt,
            user.IsEnabled,
            user.CreatedAt
        }, transaction).ConfigureAwait(false);

        await connection.ExecuteAsync(deleteRolesSql, new { user.UserId }, transaction).ConfigureAwait(false);

        foreach (var role in user.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            await connection.ExecuteAsync(insertRoleSql, new { user.UserId, Role = role }, transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UserRecord> MapAsync(NpgsqlConnection connection, UserRow row)
    {
        const string rolesSql = @"
            SELECT role
            FROM security_user_roles
            WHERE user_id = @UserId
            ORDER BY role";

        var roles = (await connection.QueryAsync<string>(rolesSql, new { row.UserId }).ConfigureAwait(false)).ToList();
        return new UserRecord(
            row.UserId,
            row.Username,
            row.Email,
            row.DisplayName,
            row.PasswordHash,
            row.PasswordSalt,
            roles,
            row.IsEnabled,
            row.CreatedAt);
    }

    private static string Normalize(string username) => username.Trim().ToUpperInvariant();

    private sealed record UserRow
    {
        public string UserId { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public string PasswordSalt { get; init; } = string.Empty;
        public bool IsEnabled { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
