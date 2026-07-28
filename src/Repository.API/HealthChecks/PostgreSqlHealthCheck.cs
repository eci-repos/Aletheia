using Microsoft.Extensions.Diagnostics.HealthChecks;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;

namespace Aletheia.Repository.API.HealthChecks;

public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlHealthCheck(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", ex);
        }
    }
}
