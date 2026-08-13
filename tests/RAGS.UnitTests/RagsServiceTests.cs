using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.RAGS.Application.Pipelines;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task RetrieveAsync_uses_keyword_fallback_when_vector_returns_no_results()
    {
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>(),
            KeywordResults = new List<SearchResult>
            {
                CreateKeywordResult("keyword chunk")
            }
        };
        var service = CreateService(vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("requirements", 3));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("keyword chunk", item.Chunk.Content);
        Assert.Equal("keyword", item.RetrievalStrategy);
    }

    [Fact]
    public async Task RetrieveAsync_uses_keyword_fallback_when_best_vector_score_below_minimum()
    {
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>
            {
                new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "weak vector match", 0), 0.5f)
            },
            KeywordResults = new List<SearchResult>
            {
                CreateKeywordResult("lexical match")
            }
        };
        var service = new RagsService(
            new ChunkingPipeline(),
            new Aletheia.RAGS.Application.Providers.SimpleEmbeddingProvider(),
            vectorStore,
            Options.Create(new RetrievalOptions { MinimumScore = 0.9 }));

        var result = await service.RetrieveAsync(new RetrievalRequest("requirements", 3));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("lexical match", item.Chunk.Content);
        Assert.Equal("keyword", item.RetrievalStrategy);
    }

    [Fact]
    public async Task RetrieveAsync_keeps_vector_results_when_score_at_or_above_minimum()
    {
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>
            {
                new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "strong vector match", 0), 0.95f)
            },
            KeywordResults = new List<SearchResult>
            {
                CreateKeywordResult("lexical match")
            }
        };
        var service = new RagsService(
            new ChunkingPipeline(),
            new Aletheia.RAGS.Application.Providers.SimpleEmbeddingProvider(),
            vectorStore,
            Options.Create(new RetrievalOptions { MinimumScore = 0.9 }));

        var result = await service.RetrieveAsync(new RetrievalRequest("requirements", 3));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("strong vector match", item.Chunk.Content);
        Assert.Equal("semantic", item.RetrievalStrategy);
    }

    [Fact]
    public async Task RetrieveAsync_keeps_vector_results_when_keyword_search_not_supported()
    {
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>
            {
                new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "only vector match", 0), 0.95f)
            },
            KeywordSearchSupported = false
        };
        var service = CreateService(vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("requirements", 3));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("only vector match", item.Chunk.Content);
    }

    [Fact]
    public async Task RetrieveAsync_filters_results_to_source_set_for_unfiltered_store()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>
            {
                new SearchResult(new Chunk(Guid.NewGuid(), sourceA, "from source A", 0), 0.95f),
                new SearchResult(new Chunk(Guid.NewGuid(), sourceB, "from source B", 0), 0.93f)
            }
        };
        var service = CreateService(vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("query", 5, sourceIds: new[] { sourceA }));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(sourceA, item.Chunk.SourceId);
        Assert.Equal("from source A", item.Chunk.Content);
    }

    [Fact]
    public async Task RetrieveAsync_keyword_fallback_honors_source_set()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>(),
            KeywordResults = new List<SearchResult>
            {
                new SearchResult(new Chunk(Guid.NewGuid(), sourceA, "keyword A", 0), 0.9f, retrievalStrategy: "keyword"),
                new SearchResult(new Chunk(Guid.NewGuid(), sourceB, "keyword B", 0), 0.9f, retrievalStrategy: "keyword")
            }
        };
        var service = CreateService(vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("requirements", 5, sourceIds: new[] { sourceA }));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(sourceA, item.Chunk.SourceId);
        Assert.Equal("keyword", item.RetrievalStrategy);
    }

    [Fact]
    public async Task RetrieveAsync_returns_empty_when_source_set_is_empty()
    {
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>
            {
                new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "any result", 0), 0.95f)
            }
        };
        var service = CreateService(vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("query", 5, sourceIds: Array.Empty<Guid>()));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task RetrieveAsync_embeds_expanded_query_for_acronyms()
    {
        var vectorStore = new FakeVectorStore();
        var recordingProvider = new RecordingEmbeddingProvider();
        var service = new RagsService(new ChunkingPipeline(), recordingProvider, vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("AI features", 3));

        Assert.True(result.IsSuccess);
        Assert.Equal("AI Artificial Intelligence features", recordingProvider.LastEmbeddedText);
    }

    [Fact]
    public async Task RetrieveAsync_keyword_fallback_uses_original_query()
    {
        var vectorStore = new FakeVectorStore
        {
            SearchOverride = _ => new List<SearchResult>(),
            KeywordResults = new List<SearchResult>
            {
                CreateKeywordResult("keyword chunk")
            }
        };
        var recordingProvider = new RecordingEmbeddingProvider();
        var service = new RagsService(new ChunkingPipeline(), recordingProvider, vectorStore);

        var result = await service.RetrieveAsync(new RetrievalRequest("AI", 3));

        Assert.True(result.IsSuccess);
        // The keyword fallback must search the literal acronym, not the expanded phrase (ILIKE match).
        Assert.Equal("AI", vectorStore.LastKeywordQuery);
    }

    private static SearchResult CreateKeywordResult(string content)
    {
        return new SearchResult(
            new Chunk(Guid.NewGuid(), Guid.NewGuid(), content, 0),
            0.9f,
            retrievalStrategy: "keyword");
    }

    private static RagsService CreateService(FakeVectorStore vectorStore)
    {
        return new RagsService(new ChunkingPipeline(), new Aletheia.RAGS.Application.Providers.SimpleEmbeddingProvider(), vectorStore);
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public List<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> StoredItems { get; } = new();
        public List<Guid> DeletedSources { get; } = new();
        public Func<int, IReadOnlyList<SearchResult>>? SearchOverride { get; set; }
        public IReadOnlyList<SearchResult>? KeywordResults { get; set; }
        public bool KeywordSearchSupported { get; set; } = true;
        public string? LastKeywordQuery { get; private set; }

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
            if (SearchOverride is not null)
            {
                return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(SearchOverride(topK)));
            }

            var results = StoredItems.Take(topK).Select(i => new SearchResult(i.Chunk, 0.95f)).ToList();
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(results));
        }

        public Task<Result> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
        {
            DeletedSources.Add(sourceId);
            StoredItems.RemoveAll(i => i.Chunk.SourceId == sourceId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> SearchKeywordAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            if (!KeywordSearchSupported)
            {
                return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Failure("Keyword search is not supported by this store."));
            }

            LastKeywordQuery = query;
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(KeywordResults ?? new List<SearchResult>()));
        }
    }

    private sealed class RecordingEmbeddingProvider : IEmbeddingProvider
    {
        public int VectorDimension => 128;
        public string? LastEmbeddedText { get; private set; }

        public Task<Result<ReadOnlyMemory<float>>> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            LastEmbeddedText = text;
            return Task.FromResult(Result<ReadOnlyMemory<float>>.Success(new ReadOnlyMemory<float>(new float[128])));
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
