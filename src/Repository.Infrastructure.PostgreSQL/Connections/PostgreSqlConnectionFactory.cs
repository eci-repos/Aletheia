using Npgsql;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Connections;

public sealed class PostgreSqlConnectionFactory
{
    private readonly string _connectionString;

    public PostgreSqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
