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
            .Setup(x => x.SetTemplateAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var service = CreateService(metadataRepository.Object);

        var result = await service.EnsureIngestedAsync(source);

        Assert.True(result.IsSuccess);
        metadataRepository.Verify(
            x => x.SetTemplateAsync(sourceId, "3.0 - RFP Analysis", "Analysis", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureIngestedAsync_does_not_persist_when_no_template_matches()
    {
        var source = new KnowledgeSource(Guid.NewGuid(), "Q3 Financial Report.xlsx", DateTimeOffset.UtcNow);

        var metadataRepository = new Mock<IMetadataRepository>();
        var service = CreateService(metadataRepository.Object);

        var result = await service.EnsureIngestedAsync(source);

        Assert.True(result.IsFailure);
        metadataRepository.Verify(
            x => x.SetTemplateAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static RepositoryKnowledgeSourceIngestionService CreateService(IMetadataRepository metadataRepository)
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
            metadataRepository: metadataRepository);
    }
}