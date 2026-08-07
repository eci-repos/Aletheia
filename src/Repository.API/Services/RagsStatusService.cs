using System.Data;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace Aletheia.Repository.API.Services;

public interface IRagsStatusService
{
    Task<Result<RagsStatusSnapshot>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record RagsStatusSnapshot(
    int EmbeddedChunkCount,
    int IngestedSourceCount,
    int RegisteredDocumentCount,
    long UncategorizedIngestCount,
    long ExtractionFailureCount,
    IReadOnlyList<string> UncategorizedIngests,
    IReadOnlyList<UploadJobSummary> RecentUploadJobs);

public sealed record UploadJobSummary(
    Guid JobId,
    string Status,
    string Stage,
    string? Error,
    Guid SourceId,
    string? SourceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed class RagsStatusService : IRagsStatusService
{
    private const string StatusFailedMessage = "RAGS status retrieval failed.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly IIngestionJobService _ingestionJobs;
    private readonly IIngestionDiagnostics _diagnostics;

    public RagsStatusService(
        PostgreSqlConnectionFactory connectionFactory,
        IIngestionJobService ingestionJobs,
        IIngestionDiagnostics diagnostics)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _ingestionJobs = ingestionJobs ?? throw new ArgumentNullException(nameof(ingestionJobs));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<Result<RagsStatusSnapshot>> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var chunkCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM embeddings", cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var sourceCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(DISTINCT source_id) FROM embeddings", cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var documentCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM file_metadata", cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var recentUploadJobs = _ingestionJobs.List(50)
                .Where(job => string.Equals(job.Kind, "UploadIngestion", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(job => job.CreatedAt)
                .Take(10)
                .Select(job => new UploadJobSummary(
                    job.JobId,
                    job.Status,
                    job.Stage,
                    job.Error,
                    job.SourceId,
                    job.SourceName,
                    job.CreatedAt,
                    job.CompletedAt))
                .ToList();

            var snapshot = new RagsStatusSnapshot(
                chunkCount,
                sourceCount,
                documentCount,
                _diagnostics.UncategorizedIngestCount,
                _diagnostics.ExtractionFailureCount,
                _diagnostics.UncategorizedIngests,
                recentUploadJobs);

            return Result<RagsStatusSnapshot>.Success(snapshot);
        }
        catch (Exception ex)
        {
            return Result<RagsStatusSnapshot>.Failure($"{StatusFailedMessage} {ex.Message}");
        }
    }
}
