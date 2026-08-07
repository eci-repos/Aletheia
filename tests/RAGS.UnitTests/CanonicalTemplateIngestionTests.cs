using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Domain.UseCases;
using Moq;
using Xunit;

namespace RAGS.UnitTests;

public sealed class CanonicalTemplateIngestionTests
{
    [Fact]
    public async Task EnsureIngestedAsync_proceeds_when_no_canonical_template_matches()
    {
        var download = new Mock<IDownloadUseCase>();
        download.Setup(d => d.DownloadAsync(It.IsAny<DownloadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DownloadResponse>.Failure("download not configured for this test"));
        var extractor = new Mock<IUploadedFileTextExtractor>();
        var rags = new Mock<IRagsService>();
        var indexer = new Mock<IUploadedContentKnowledgeIndexer>();
        var service = new RepositoryKnowledgeSourceIngestionService(
            download.Object,
            extractor.Object,
            rags.Object,
            indexer.Object,
            new DocumentTemplateRegistry());

        // Sprint 59: the canonical gate is softened. A document with no matching template proceeds
        // (ingested as Uncategorized); the failure here is from the download step, not the gate.
        var result = await service.EnsureIngestedAsync(
            new KnowledgeSource(Guid.NewGuid(), "Q3 Financial Report.xlsx", DateTimeOffset.UtcNow));

        Assert.True(result.IsFailure);
        Assert.Contains("download", result.Error, StringComparison.OrdinalIgnoreCase);
        download.Verify(d => d.DownloadAsync(It.IsAny<DownloadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureIngestedAsync_proceeds_when_canonical_template_matches()
    {
        var download = new Mock<IDownloadUseCase>();
        download.Setup(d => d.DownloadAsync(It.IsAny<DownloadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DownloadResponse>.Failure("download not configured for this test"));
        var extractor = new Mock<IUploadedFileTextExtractor>();
        var rags = new Mock<IRagsService>();
        var indexer = new Mock<IUploadedContentKnowledgeIndexer>();
        var service = new RepositoryKnowledgeSourceIngestionService(
            download.Object,
            extractor.Object,
            rags.Object,
            indexer.Object,
            new DocumentTemplateRegistry());

        // Reaches the download step (i.e., passed the canonical gate) even though download fails.
        var result = await service.EnsureIngestedAsync(
            new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow));

        Assert.True(result.IsFailure);
        Assert.Contains("download", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
