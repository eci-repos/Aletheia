using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Microsoft.Extensions.Hosting;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Security;

public sealed class PostgreSqlSecuritySchemaInitializer : IHostedService
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlSecuritySchemaInitializer(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => PostgreSqlSecuritySchema.EnsureAsync(_connectionFactory, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
