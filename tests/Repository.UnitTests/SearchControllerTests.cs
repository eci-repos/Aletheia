using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class SearchControllerTests
{
    [Fact]
    public async Task Search_stamps_chunk_counts_from_vector_store()
    {
        var fileA = CreateFile(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx");
        var fileB = CreateFile(Guid.NewGuid(), "CDF 2026 - 3. RFP Analysis.docx");
        var response = new SearchResponse(new PagedResult<FileMetadata>(new[] { fileA, fileB }, 1, 10, 2));

        var searchUseCase = new Mock<ISearchUseCase>();
        searchUseCase
            .Setup(x => x.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SearchResponse>.Success(response));

        var vectorStore = new Mock<IVectorStore>();
        vectorStore
            .Setup(x => x.GetChunkCountsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, int>>.Success(new Dictionary<Guid, int>
            {
                [fileA.Descriptor.FileId] = 42,
                [fileB.Descriptor.FileId] = 0
            }));

        var controller = new SearchController(searchUseCase.Object, vectorStore.Object, NoActiveJobs());

        var result = await controller.Search(null, 1, 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<SearchResponse>(ok.Value);
        var items = body.Results.Items;
        Assert.Equal(42, items[0].ChunkCount);
        Assert.True(items[0].Ingested);
        Assert.Equal(0, items[1].ChunkCount);
        Assert.False(items[1].Ingested);
    }

    [Fact]
    public async Task Search_marks_missing_sources_as_not_ingested()
    {
        var file = CreateFile(Guid.NewGuid(), "orphan.docx");
        var response = new SearchResponse(new PagedResult<FileMetadata>(new[] { file }, 1, 10, 1));

        var searchUseCase = new Mock<ISearchUseCase>();
        searchUseCase
            .Setup(x => x.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SearchResponse>.Success(response));

        // The vector store returns no counts for this source (no embeddings).
        var vectorStore = new Mock<IVectorStore>();
        vectorStore
            .Setup(x => x.GetChunkCountsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, int>>.Success(new Dictionary<Guid, int>()));

        var controller = new SearchController(searchUseCase.Object, vectorStore.Object, NoActiveJobs());

        var result = await controller.Search(null, 1, 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<SearchResponse>(ok.Value);
        var item = Assert.Single(body.Results.Items);
        Assert.Equal(0, item.ChunkCount);
        Assert.False(item.Ingested);
    }

    [Fact]
    public async Task Search_marks_processing_when_ingestion_job_active()
    {
        var file = CreateFile(Guid.NewGuid(), "mid-flight.docx");
        var response = new SearchResponse(new PagedResult<FileMetadata>(new[] { file }, 1, 10, 1));

        var searchUseCase = new Mock<ISearchUseCase>();
        searchUseCase
            .Setup(x => x.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SearchResponse>.Success(response));

        // The source has partial chunks AND an active ingestion job still writing embeddings.
        var vectorStore = new Mock<IVectorStore>();
        vectorStore
            .Setup(x => x.GetChunkCountsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, int>>.Success(new Dictionary<Guid, int>
            {
                [file.Descriptor.FileId] = 3
            }));

        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(x => x.HasActiveIngestion(file.Descriptor.FileId))
            .Returns(true);

        var controller = new SearchController(searchUseCase.Object, vectorStore.Object, ingestionJobs.Object);

        var result = await controller.Search(null, 1, 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<SearchResponse>(ok.Value);
        var item = Assert.Single(body.Results.Items);
        Assert.Equal(3, item.ChunkCount);
        Assert.True(item.Ingested);
        Assert.True(item.IsProcessing);
    }

    [Fact]
    public async Task Search_returns_bad_request_on_use_case_failure()
    {
        var searchUseCase = new Mock<ISearchUseCase>();
        searchUseCase
            .Setup(x => x.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SearchResponse>.Failure("boom"));

        var controller = new SearchController(searchUseCase.Object, new Mock<IVectorStore>().Object, NoActiveJobs());

        var result = await controller.Search(null, 1, 10, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static IIngestionJobService NoActiveJobs()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(x => x.HasActiveIngestion(It.IsAny<Guid>()))
            .Returns(false);
        return ingestionJobs.Object;
    }

    private static FileMetadata CreateFile(Guid fileId, string fileName)
    {
        return new FileMetadata(
            new FileDescriptor(fileId, fileName),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            1000,
            DateTimeOffset.UtcNow);
    }
}
