using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class RagsControllerTests
{
    [Fact]
    public async Task Status_returns_snapshot_when_service_succeeds()
    {
        var snapshot = new RagsStatusSnapshot(
            EmbeddedChunkCount: 42,
            IngestedSourceCount: 3,
            RegisteredDocumentCount: 5,
            UncategorizedIngestCount: 1,
            ExtractionFailureCount: 0,
            UncategorizedIngests: new[] { "Renamed File.pdf" },
            RecentUploadJobs: new List<UploadJobSummary>());
        var statusService = new Mock<IRagsStatusService>();
        statusService
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RagsStatusSnapshot>.Success(snapshot));
        var controller = new RagsController(new Mock<IRagsService>().Object, statusService.Object);

        var result = await controller.Status(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(snapshot, ok.Value);
    }

    [Fact]
    public async Task Status_returns_bad_request_when_service_fails()
    {
        var statusService = new Mock<IRagsStatusService>();
        statusService
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RagsStatusSnapshot>.Failure("db unavailable"));
        var controller = new RagsController(new Mock<IRagsService>().Object, statusService.Object);

        var result = await controller.Status(CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("db unavailable", badRequest.Value!.ToString());
    }

    [Fact]
    public async Task Retrieve_applies_theme_scope_when_themes_provided()
    {
        var sourceId = Guid.NewGuid();
        var ragsService = new Mock<IRagsService>();
        ragsService
            .Setup(x => x.RetrieveAsync(It.IsAny<RetrievalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult>()));
        var themeService = new Mock<IKnowledgeThemeService>();
        themeService
            .Setup(x => x.ResolveSourceIdsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success(new[] { sourceId }));
        var controller = new RagsController(ragsService.Object, new Mock<IRagsStatusService>().Object, themeService.Object);

        await controller.Retrieve("scope of work", 5, "Analysis, As-Built", CancellationToken.None);

        ragsService.Verify(
            x => x.RetrieveAsync(
                It.Is<RetrievalRequest>(r => r.SourceIds != null && r.SourceIds.Count == 1 && r.SourceIds.Contains(sourceId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Retrieve_without_themes_does_not_set_source_ids()
    {
        var ragsService = new Mock<IRagsService>();
        ragsService
            .Setup(x => x.RetrieveAsync(It.IsAny<RetrievalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult>()));
        var themeService = new Mock<IKnowledgeThemeService>();
        var controller = new RagsController(ragsService.Object, new Mock<IRagsStatusService>().Object, themeService.Object);

        await controller.Retrieve("scope of work", 5, themes: null, cancellationToken: CancellationToken.None);

        ragsService.Verify(
            x => x.RetrieveAsync(
                It.Is<RetrievalRequest>(r => r.SourceIds == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        themeService.Verify(x => x.ResolveSourceIdsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
