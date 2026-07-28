using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.RAGS.Application.Pipelines;

namespace RAGS.UnitTests;

public class RagsServiceTests
{
    [Fact]
    public async Task IngestAsync_stores_embeddings_for_chunks()
    {
        var vectorStore = new FakeVectorStore();
        var service = CreateService(vectorStore);
        var sourceId = Guid.NewGuid();

        var result = await service.IngestAsync(new IngestionRequest(sourceId, new string('a', 3000)));

        Assert.True(result.IsSuccess);
        Assert.True(vectorStore.StoredItems.Count > 0);
        Assert.All(vectorStore.StoredItems, item => Assert.Equal(sourceId, item.Chunk.SourceId));
    }

    [Fact]
    public async Task IngestAsync_deletes_existing_embeddings_before_storing()
    {
        var vectorStore = new FakeVectorStore();
        var service = CreateService(vectorStore);
        var sourceId = Guid.NewGuid();

        await service.IngestAsync(new IngestionRequest(sourceId, new string('a', 3000)));
        await service.IngestAsync(new IngestionRequest(sourceId, new string('b', 3000)));

        Assert.Contains(sourceId, vectorStore.DeletedSources);
    }

    [Fact]
    public async Task RetrieveAsync_returns_search_results()
    {
        var vectorStore = new FakeVectorStore();
        var service = CreateService(vectorStore);
        var sourceId = Guid.NewGuid();

        await service.IngestAsync(new IngestionRequest(sourceId, "hello world semantic search test"));

        var result = await service.RetrieveAsync(new RetrievalRequest("search", 3));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task RetrieveAsync_returns_failure_when_embedding_generation_fails()
    {
        var vectorStore = new FakeVectorStore();
        var failingProvider = new FailingEmbeddingProvider();
        var service = new RagsService(new ChunkingPipeline(), failingProvider, vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("query", 3));

        Assert.True(result.IsFailure);
    }

    private static RagsService CreateService(FakeVectorStore vectorStore)
    {
        return new RagsService(new ChunkingPipeline(), new Aletheia.RAGS.Application.Providers.SimpleEmbeddingProvider(), vectorStore);
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public List<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> StoredItems { get; } = new();
        public List<Guid> DeletedSources { get; } = new();

        public Task<Result> StoreAsync(Guid chunkId, ReadOnlyMemory<float> vector, Chunk chunk, CancellationToken cancellationToken = default)
        {
            StoredItems.Add((chunkId, vector, chunk));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> StoreBatchAsync(IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items)
            {
                StoredItems.Add(item);
            }
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> SearchAsync(ReadOnlyMemory<float> vector, int topK, CancellationToken cancellationToken = default)
        {
            var results = StoredItems.Take(topK).Select(i => new SearchResult(i.Chunk, 0.95f)).ToList();
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(results));
        }

        public Task<Result> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
        {
            DeletedSources.Add(sourceId);
            StoredItems.RemoveAll(i => i.Chunk.SourceId == sourceId);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FailingEmbeddingProvider : IEmbeddingProvider
    {
        public int VectorDimension => 128;

        public Task<Result<ReadOnlyMemory<float>>> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ReadOnlyMemory<float>>.Failure("embedding failed"));
        }
    }
}
