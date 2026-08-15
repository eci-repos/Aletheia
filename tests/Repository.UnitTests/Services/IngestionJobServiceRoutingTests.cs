using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Repository.UnitTests.Services;

/// <summary>
/// Guards the background job routing in <see cref="IngestionJobService"/>: an uploaded file must
/// run the text-extraction/chunking/embedding pipeline, and the document brief must only run as its
/// own queued job after ingestion succeeds. Regression test for the Sprint 57 Reembed insertion
/// that orphaned the DocumentBriefs branch and sent upload jobs straight to brief generation
/// (which then failed with "no retrieved evidence is available").
/// </summary>
public class IngestionJobServiceRoutingTests
{
    [Fact]
    public async Task UploadedFileJob_runs_ingestion_then_queues_document_brief()
    {
        var sourceId = Guid.NewGuid();
        const string sourceName = "CMP 2022 - 3. RFP Analysis.docx";

        var textExtractor = new Mock<IUploadedFileTextExtractor>();
        textExtractor
            .Setup(x => x.ExtractAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UploadedFileTextExtraction>.Success(
                new UploadedFileTextExtraction(true, "RFP scope and requirements text.", "TextExtracted")));

        var ragsService = new Mock<IRagsService>();
        ragsService
            .Setup(x => x.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var knowledgeIndexer = new Mock<IUploadedContentKnowledgeIndexer>();
        knowledgeIndexer
            .Setup(x => x.IndexLightweightAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IIngestionProgressSink?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var documentBriefService = new Mock<IDocumentBriefService>();
        documentBriefService
            .Setup(x => x.RegenerateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WikiPage>.Success(
                new WikiPage(Guid.NewGuid(), "RFP Analysis", sourceName, "brief")));

        var tempPath = Path.Combine(Path.GetTempPath(), $"aletheia-upload-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempPath, "RFP scope and requirements text.");

        try
        {
            var service = new IngestionJobService(
                textExtractor.Object,
                ragsService.Object,
                Mock.Of<IGraphRagService>(),
                Mock.Of<ILazyGraphRagService>(),
                Mock.Of<IWragsWikiService>(),
                knowledgeIndexer.Object,
                Mock.Of<IMetadataRepository>(),
                Mock.Of<IKnowledgeSourceIngestionService>(),
                documentBriefService.Object,
                NullLogger<IngestionJobService>.Instance);

            var enqueued = service.EnqueueUploadedFile(sourceId, sourceName, "text/plain", tempPath, 0);

            await service.StartAsync(CancellationToken.None);
            try
            {
                await WaitForTerminalAsync(service, enqueued.JobId, TimeSpan.FromSeconds(10));

                var uploadJob = service.Get(enqueued.JobId);
                Assert.NotNull(uploadJob);
                Assert.Equal("Succeeded", uploadJob!.Status);
                Assert.Equal("Indexed", uploadJob.Stage);

                // The upload pipeline actually ran (not the document brief branch).
                textExtractor.Verify(
                    x => x.ExtractAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<Stream>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
                ragsService.Verify(
                    x => x.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()),
                    Times.Once);

                // The brief is queued as its own job only after ingestion succeeded.
                var briefJob = service.List(20)
                    .SingleOrDefault(job => job.Kind == "DocumentBriefs" && job.SourceId == sourceId);
                Assert.NotNull(briefJob);
                await WaitForTerminalAsync(service, briefJob!.JobId, TimeSpan.FromSeconds(10));

                documentBriefService.Verify(
                    x => x.RegenerateAsync(sourceId, sourceName, It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task RagsRepairForSources_runs_ingestion_for_each_targeted_source()
    {
        var sourceId = Guid.NewGuid();
        const string sourceName = "CMP 2026 RFP.pdf";

        var metadataRepository = new Mock<IMetadataRepository>();
        metadataRepository
            .Setup(x => x.GetByFileIdAsync(sourceId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileMetadata?>.Success(
                new FileMetadata(new FileDescriptor(sourceId, sourceName), "application/pdf", 100, DateTimeOffset.UtcNow)));

        var knowledgeSourceIngestion = new Mock<IKnowledgeSourceIngestionService>();
        knowledgeSourceIngestion
            .Setup(x => x.EnsureIngestedAsync(It.IsAny<KnowledgeSource>(), It.IsAny<CancellationToken>(), It.IsAny<KnowledgeIndexMode>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var service = new IngestionJobService(
            Mock.Of<IUploadedFileTextExtractor>(),
            Mock.Of<IRagsService>(),
            Mock.Of<IGraphRagService>(),
            Mock.Of<ILazyGraphRagService>(),
            Mock.Of<IWragsWikiService>(),
            Mock.Of<IUploadedContentKnowledgeIndexer>(),
            metadataRepository.Object,
            knowledgeSourceIngestion.Object,
            Mock.Of<IDocumentBriefService>(),
            NullLogger<IngestionJobService>.Instance);

        var enqueued = service.EnqueueRagsRepairForSources(new[] { sourceId });

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForTerminalAsync(service, enqueued.JobId, TimeSpan.FromSeconds(10));

            var job = service.Get(enqueued.JobId);
            Assert.NotNull(job);
            Assert.Equal("Succeeded", job!.Status);
            Assert.Equal("RagsRepairSources", job.Kind);

            // The targeted repair ran EnsureIngestedAsync for exactly the enqueued source.
            knowledgeSourceIngestion.Verify(
                x => x.EnsureIngestedAsync(
                    It.Is<KnowledgeSource>(s => s.SourceId == sourceId && s.SourceName == sourceName),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<KnowledgeIndexMode>()),
                Times.Once);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitForTerminalAsync(
        IIngestionJobService service,
        Guid jobId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = service.Get(jobId);
            if (snapshot is not null && snapshot.Status is not ("Queued" or "Running"))
            {
                return;
            }

            await Task.Delay(50);
        }

        var last = service.Get(jobId);
        Assert.Fail(
            $"Job {jobId} did not reach a terminal state within {timeout}; last status: {last?.Status ?? "unknown"}.");
    }
}

