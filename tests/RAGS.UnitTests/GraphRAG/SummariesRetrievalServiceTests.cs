using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.GraphRAG;
using Moq;

namespace RAGS.UnitTests.GraphRAG;

public sealed class SummariesRetrievalServiceTests
{
    private static SearchResult Result(string content) =>
        new(new Chunk(Guid.NewGuid(), Guid.NewGuid(), content, 0), 0.9f);

    [Fact]
    public async Task RetrieveAsync_uses_graphrag_results_when_present()
    {
        var graphResults = new List<SearchResult> { Result("graph summary") };
        var graphRag = new Mock<IGraphRagService>();
        graphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(graphResults));
        var lazyGraphRag = new Mock<ILazyGraphRagService>();

        var service = new SummariesRetrievalService(graphRag.Object, lazyGraphRag.Object);

        var result = await service.RetrieveAsync("query");

        Assert.True(result.IsSuccess);
        Assert.Same(graphResults, result.Value);
        lazyGraphRag.Verify(
            s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()),
            Times.Never);
    }

    [Fact]
    public async Task RetrieveAsync_falls_back_to_lazygraphrag_when_graphrag_empty()
    {
        var graphRag = new Mock<IGraphRagService>();
        graphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult>()));
        var lazyResults = new List<SearchResult> { Result("lazy summary") };
        var lazyGraphRag = new Mock<ILazyGraphRagService>();
        lazyGraphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(lazyResults));

        var service = new SummariesRetrievalService(graphRag.Object, lazyGraphRag.Object);

        var result = await service.RetrieveAsync("query");

        Assert.True(result.IsSuccess);
        Assert.Same(lazyResults, result.Value);
    }

    [Fact]
    public async Task RetrieveAsync_falls_back_to_lazygraphrag_when_graphrag_fails()
    {
        var graphRag = new Mock<IGraphRagService>();
        graphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Failure("graphrag failed"));
        var lazyResults = new List<SearchResult> { Result("lazy summary") };
        var lazyGraphRag = new Mock<ILazyGraphRagService>();
        lazyGraphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(lazyResults));

        var service = new SummariesRetrievalService(graphRag.Object, lazyGraphRag.Object);

        var result = await service.RetrieveAsync("query");

        Assert.True(result.IsSuccess);
        Assert.Same(lazyResults, result.Value);
    }

    [Fact]
    public async Task RetrieveAsync_forwards_source_ids_to_both_engines()
    {
        var sourceA = Guid.NewGuid();
        var graphRag = new Mock<IGraphRagService>();
        graphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult>()));
        var lazyGraphRag = new Mock<ILazyGraphRagService>();
        lazyGraphRag
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult> { Result("lazy") }));

        var service = new SummariesRetrievalService(graphRag.Object, lazyGraphRag.Object);

        await service.RetrieveAsync("query", sourceIds: new[] { sourceA });

        graphRag.Verify(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.Is<IReadOnlyList<Guid>?>(ids => ids != null && ids.Contains(sourceA))), Times.Once);
        lazyGraphRag.Verify(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.Is<IReadOnlyList<Guid>?>(ids => ids != null && ids.Contains(sourceA))), Times.Once);
    }
}
