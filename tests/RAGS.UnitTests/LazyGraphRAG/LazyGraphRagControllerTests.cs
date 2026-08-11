using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RAGS.UnitTests.TestSupport;

namespace RAGS.UnitTests.LazyGraphRAG;

public sealed class LazyGraphRagControllerTests
{
    [Fact]
    public async Task IngestAsync_returns_ok_when_service_succeeds()
    {
        var mockService = new Mock<ILazyGraphRagService>();
        mockService
            .Setup(s => s.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate());
        var request = new IngestionRequest(Guid.NewGuid(), "test content");

        var result = await controller.Ingest(request, CancellationToken.None);

        var okResult = Assert.IsType<OkResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task IngestAsync_returns_bad_request_when_service_fails()
    {
        var mockService = new Mock<ILazyGraphRagService>();
        mockService
            .Setup(s => s.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("ingestion failed"));

        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate());
        var request = new IngestionRequest(Guid.NewGuid(), "test content");

        var result = await controller.Ingest(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task Retrieve_returns_ok_with_results_when_service_succeeds()
    {
        var mockService = new Mock<ILazyGraphRagService>();
        var searchResults = new List<SearchResult>
        {
            new(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "chunk 1", 0), 0.95f),
        };
        mockService
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(searchResults));

        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate(showInternalSearch: true));

        var result = await controller.Retrieve("query");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task Retrieve_returns_bad_request_when_service_fails()
    {
        var mockService = new Mock<ILazyGraphRagService>();
        mockService
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Failure("retrieve failed"));

        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate(showInternalSearch: true));

        var result = await controller.Retrieve("query");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task Retrieve_returns_not_found_when_internal_search_hidden()
    {
        var mockService = new Mock<ILazyGraphRagService>();
        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate(showInternalSearch: false));

        var result = await controller.Retrieve("query");

        Assert.IsType<NotFoundObjectResult>(result);
        mockService.Verify(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()), Times.Never);
    }

    [Fact]
    public async Task GlobalSearch_returns_not_found_when_internal_search_hidden()
    {
        var mockService = new Mock<ILazyGraphRagService>();
        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate(showInternalSearch: false));

        var result = await controller.GlobalSearch("query");

        Assert.IsType<NotFoundObjectResult>(result);
        mockService.Verify(s => s.GlobalSearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()), Times.Never);
    }

    [Fact]
    public async Task Retrieve_resolves_themes_to_source_ids_and_passes_them_to_service()
    {
        // Sprint 64: the ?themes= query param is resolved to source ids and flows to the service.
        var sourceA = Guid.NewGuid();
        var mockThemeService = new Mock<IKnowledgeThemeService>();
        mockThemeService
            .Setup(s => s.ResolveSourceIdsAsync(new[] { "Theme A" }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success(new[] { sourceA }));
        var mockService = new Mock<ILazyGraphRagService>();
        mockService
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult>()));
        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate(showInternalSearch: true), mockThemeService.Object);

        var result = await controller.Retrieve("query", themes: "Theme A");

        Assert.IsType<OkObjectResult>(result);
        mockService.Verify(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.Is<IReadOnlyList<Guid>?>(ids => ids != null && ids.Contains(sourceA))), Times.Once);
    }

    [Fact]
    public async Task GlobalSearch_resolves_themes_to_source_ids_and_passes_them_to_service()
    {
        // Sprint 64: the ?themes= query param is resolved to source ids and flows to the service.
        var sourceA = Guid.NewGuid();
        var mockThemeService = new Mock<IKnowledgeThemeService>();
        mockThemeService
            .Setup(s => s.ResolveSourceIdsAsync(new[] { "Theme A" }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success(new[] { sourceA }));
        var mockService = new Mock<ILazyGraphRagService>();
        mockService
            .Setup(s => s.GlobalSearchAsync("query", It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<GlobalSearchResult>.Success(new GlobalSearchResult("answer", new List<string>(), new List<SearchResult>())));
        var controller = new LazyGraphRagController(mockService.Object, new FakeInternalSearchGate(showInternalSearch: true), mockThemeService.Object);

        var result = await controller.GlobalSearch("query", themes: "Theme A");

        Assert.IsType<OkObjectResult>(result);
        mockService.Verify(s => s.GlobalSearchAsync("query", It.IsAny<CancellationToken>(), It.Is<IReadOnlyList<Guid>?>(ids => ids != null && ids.Contains(sourceA))), Times.Once);
    }
}
