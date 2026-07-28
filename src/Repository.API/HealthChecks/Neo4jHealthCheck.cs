using Microsoft.Extensions.Diagnostics.HealthChecks;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.Repository.API.HealthChecks;

public sealed class Neo4jHealthCheck : IHealthCheck
{
    private readonly IGraphProvider _graphProvider;

    public Neo4jHealthCheck(IGraphProvider graphProvider)
    {
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _graphProvider.GraphExistsAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return HealthCheckResult.Healthy("Neo4j is reachable.");
            }
            return HealthCheckResult.Unhealthy("Neo4j responded with failure.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Neo4j is unreachable.", ex);
        }
    }
}
