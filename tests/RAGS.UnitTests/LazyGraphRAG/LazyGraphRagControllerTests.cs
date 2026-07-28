using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

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

        var controller = new LazyGraphRagController(mockService.Object);
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

        var controller = new LazyGraphRagController(mockService.Object);
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
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(searchResults));

        var controller = new LazyGraphRagController(mockService.Object);

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
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Failure("retrieve failed"));

        var controller = new LazyGraphRagController(mockService.Object);

        var result = await controller.Retrieve("query");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }
}
