using Npgsql;

namespace Repository.IntegrationTests.Fixtures;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;
    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? "Host=localhost;Port=5432;Database=aletheia;Username=aletheia;Password=aletheia";

        try
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var initScript = await File.ReadAllTextAsync("../../../../init.sql").ConfigureAwait(false);
            await ExecuteAsync(connection, initScript).ConfigureAwait(false);

            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        try
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await ExecuteAsync(connection, @"
                DROP TABLE IF EXISTS security_refresh_tokens CASCADE;
                DROP TABLE IF EXISTS security_user_roles CASCADE;
                DROP TABLE IF EXISTS security_users CASCADE;
                DROP TABLE IF EXISTS file_metadata CASCADE;").ConfigureAwait(false);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        foreach (var batch in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            using var command = new NpgsqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
