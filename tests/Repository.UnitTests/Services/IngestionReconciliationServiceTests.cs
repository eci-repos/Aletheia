using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Repository.UnitTests.Services;

/// <summary>
/// Sprint 73: the startup reconciliation sweep must enqueue a targeted RAGS repair for exactly the
/// registered documents that never completed ingestion (zero embeddings + last_ingested_at NULL), and
/// must stay quiet when nothing is missing.
/// </summary>
public class IngestionReconciliationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_enqueues_targeted_repair_for_sources_missing_ingestion()
    {
        var missing = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var metadataRepository = new Mock<IMetadataRepository>();
        metadataRepository
            .Setup(x => x.GetSourcesMissingIngestionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success(missing));

        var enqueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(x => x.EnqueueRagsRepairForSources(It.IsAny<IReadOnlyList<Guid>>()))
            .Returns(() =>
            {
                enqueued.TrySetResult();
                return CreateSnapshot("RagsRepairSources");
            });

        var service = new IngestionReconciliationService(
            metadataRepository.Object,
            ingestionJobs.Object,
            NullLogger<IngestionReconciliationService>.Instance,
            startupDelay: TimeSpan.Zero);

        await service.StartAsync(CancellationToken.None);
        await enqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        ingestionJobs.Verify(
            x => x.EnqueueRagsRepairForSources(It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(missing))),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_enqueue_when_nothing_missing()
    {
        var metadataRepository = new Mock<IMetadataRepository>();
        var queryCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metadataRepository
            .Setup(x => x.GetSourcesMissingIngestionAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                queryCalled.TrySetResult();
                return Task.FromResult(Result<IReadOnlyList<Guid>>.Success(new List<Guid>()));
            });

        var ingestionJobs = new Mock<IIngestionJobService>();

        var service = new IngestionReconciliationService(
            metadataRepository.Object,
            ingestionJobs.Object,
            NullLogger<IngestionReconciliationService>.Instance,
            startupDelay: TimeSpan.Zero);

        await service.StartAsync(CancellationToken.None);
        await queryCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        ingestionJobs.Verify(
            x => x.EnqueueRagsRepairForSources(It.IsAny<IReadOnlyList<Guid>>()),
            Times.Never);
    }

    private static IngestionJobSnapshot CreateSnapshot(string kind)
    {
        var now = DateTimeOffset.UtcNow;
        return new IngestionJobSnapshot(
            Guid.NewGuid(),
            kind,
            "test job",
            "Queued",
            "Queued",
            0,
            0,
            0,
            "queued",
            Guid.Empty,
            null,
            now,
            null,
            now,
            null,
            null);
    }
}
