using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;

namespace Aletheia.Repository.API.HealthChecks;

public sealed class MinioHealthCheck : IHealthCheck
{
    private readonly IMinioClient _minioClient;

    public MinioHealthCheck(IMinioClient minioClient)
    {
        _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var buckets = await _minioClient.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("MinIO is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MinIO is unreachable.", ex);
        }
    }
}
