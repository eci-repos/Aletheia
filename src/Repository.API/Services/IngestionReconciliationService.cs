using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aletheia.Repository.API.Services;

/// <summary>
/// Sprint 73: startup reconciliation sweep. On boot, finds registered documents with zero
/// embeddings — the signature of an interrupted ingestion (the old delete-then-insert race plus the
/// in-memory job queue being lost on an API restart) — and enqueues a targeted repair for exactly
/// those sources. The candidate set is embeddings-only (not gated on last_ingested_at): a source
/// with a stale marker but zero embeddings is still a repair candidate, matching the Browser's
/// "Ingested" ground truth. Runs once; the durable job queue is a documented follow-up.
/// </summary>
public sealed class IngestionReconciliationService : BackgroundService
{
    private readonly IMetadataRepository _metadataRepository;
    private readonly IIngestionJobService _ingestionJobs;
    private readonly ILogger<IngestionReconciliationService> _logger;
    private readonly TimeSpan _startupDelay;

    public IngestionReconciliationService(
        IMetadataRepository metadataRepository,
        IIngestionJobService ingestionJobs,
        ILogger<IngestionReconciliationService> logger,
        TimeSpan? startupDelay = null)
    {
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _ingestionJobs = ingestionJobs ?? throw new ArgumentNullException(nameof(ingestionJobs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _startupDelay = startupDelay ?? TimeSpan.FromSeconds(10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API come up and the schema initializers finish before querying.
        try
        {
            await Task.Delay(_startupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var result = await _metadataRepository.GetSourcesMissingIngestionAsync(stoppingToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                // A query failure must not read as "nothing to do" — the sweep would silently
                // never self-heal (e.g. the last_ingested_at migration not yet applied).
                _logger.LogError("Ingestion reconciliation sweep could not query for sources missing ingestion: {Error}", result.Error);
                return;
            }

            if (result.Value is null || result.Value.Count == 0)
            {
                _logger.LogInformation("Ingestion reconciliation: no registered documents missing ingestion.");
                return;
            }

            _logger.LogWarning(
                "Ingestion reconciliation: {Count} registered document(s) have zero embeddings; enqueuing targeted RAGS repair.",
                result.Value.Count);

            var snapshot = _ingestionJobs.EnqueueRagsRepairForSources(result.Value);
            _logger.LogInformation("Ingestion reconciliation: targeted RAGS repair enqueued as job {JobId}.", snapshot.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion reconciliation sweep failed.");
        }
    }
}
