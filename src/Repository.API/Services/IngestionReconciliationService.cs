using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aletheia.Repository.API.Services;

/// <summary>
/// Sprint 73: startup reconciliation sweep. On boot, finds registered documents that never
/// completed a RAGS ingestion pass (last_ingested_at IS NULL) AND have zero embeddings — the
/// signature of an interrupted ingestion (the old delete-then-insert race plus the in-memory job
/// queue being lost on an API restart) — and enqueues a targeted repair for exactly those sources.
/// Runs once; the durable job queue is a documented follow-up.
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
            if (result.IsFailure || result.Value is null || result.Value.Count == 0)
            {
                _logger.LogInformation("Ingestion reconciliation: no registered documents missing ingestion.");
                return;
            }

            _logger.LogWarning(
                "Ingestion reconciliation: {Count} registered document(s) never completed ingestion (zero embeddings, last_ingested_at NULL); enqueuing targeted RAGS repair.",
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
