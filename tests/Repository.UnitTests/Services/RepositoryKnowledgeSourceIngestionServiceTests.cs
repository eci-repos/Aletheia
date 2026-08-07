using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Repository.UnitTests.Services;

/// <summary>
/// Guards Sprint 58 persistence: when a document passes the canonical template gate,
/// the ingestion service records the template name + knowledge theme on file_metadata.
/// </summary>
public sealed class RepositoryKnowledgeSourceIngestionServiceTests
{
    [Fact]
    public async Task EnsureIngestedAsync_persists_template_and_theme()
    {
        var sourceId = Guid.NewGuid();
        var source = new KnowledgeSource(sourceId, "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow);

        var metadataRepository = new Mock<IMetadataRepository>();
        metadataRepository
            .Setup(x => x.SetTemplateAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var service = CreateService(metadataRepository.Object);

        var result = await service.EnsureIngestedAsync(source);

        Assert.True(result.IsSuccess);
        metadataRepository.Verify(
            x => x.SetTemplateAsync(sourceId, "3.0 - RFP Analysis", new[] { "Analysis" }, "Canonical", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureIngestedAsync_ingests_uncategorized_document_when_no_template_matches()
    {
        var sourceId = Guid.NewGuid();
        var source = new KnowledgeSource(sourceId, "Q3 Financial Report.xlsx", DateTimeOffset.UtcNow);

        var metadataRepository = new Mock<IMetadataRepository>();
        metadataRepository
            .Setup(x => x.SetTemplateAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var service = CreateService(metadataRepository.Object);

        // Sprint 59: the canonical gate is softened - the document is ingested as Uncategorized
        // instead of being refused, so a new document kind is never lost.
        var result = await service.EnsureIngestedAsync(source);

        Assert.True(result.IsSuccess);
        metadataRepository.Verify(
            x => x.SetTemplateAsync(sourceId, null, null, "Uncategorized", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureIngestedAsync_enqueues_brief_for_canonical_but_not_uncategorized()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        var lazyJobs = new Lazy<IIngestionJobService>(() => ingestionJobs.Object);

        // Canonical document -> document brief is enqueued.
        var canonicalSource = new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow);
        var canonicalService = CreateService(new Mock<IMetadataRepository>().Object, lazyJobs);
        await canonicalService.EnsureIngestedAsync(canonicalSource);
        ingestionJobs.Verify(x => x.EnqueueDocumentBriefs(canonicalSource.SourceId, canonicalSource.SourceName), Times.Once);

        // Uncategorized document -> no brief (no template sections to structure one).
        ingestionJobs.Invocations.Clear();
        var uncategorizedSource = new KnowledgeSource(Guid.NewGuid(), "Q3 Financial Report.xlsx", DateTimeOffset.UtcNow);
        var uncategorizedService = CreateService(new Mock<IMetadataRepository>().Object, lazyJobs);
        await uncategorizedService.EnsureIngestedAsync(uncategorizedSource);
        ingestionJobs.Verify(x => x.EnqueueDocumentBriefs(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    private static RepositoryKnowledgeSourceIngestionService CreateService(
        IMetadataRepository metadataRepository,
        Lazy<IIngestionJobService>? ingestionJobs = null)
    {
        var download = new Mock<IDownloadUseCase>();
        download
            .Setup(x => x.DownloadAsync(It.IsAny<DownloadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DownloadResponse>.Success(
                new DownloadResponse(
                    new FileMetadata(
                        new FileDescriptor(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx"),
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        1024,
                        DateTimeOffset.UtcNow),
                    new MemoryStream("RFP scope and requirements text."u8.ToArray()))));

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
            .Setup(x => x.IndexAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        return new RepositoryKnowledgeSourceIngestionService(
            download.Object,
            textExtractor.Object,
            ragsService.Object,
            knowledgeIndexer.Object,
            templateRegistry: new DocumentTemplateRegistry(),
            metadataRepository: metadataRepository,
            ingestionJobs: ingestionJobs);
    }
}