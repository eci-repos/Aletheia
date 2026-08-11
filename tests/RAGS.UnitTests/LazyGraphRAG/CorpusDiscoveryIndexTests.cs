using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.LazyGraphRAG;
using Microsoft.Extensions.Logging.Abstractions;

namespace RAGS.UnitTests.LazyGraphRAG;

public sealed class CorpusDiscoveryIndexTests
{
    [Fact]
    public async Task IndexAsync_persists_write_through_to_repository()
    {
        var repository = new FakeCorpusIndexRepository();
        var index = new CorpusDiscoveryIndex(repository, NullLogger<CorpusDiscoveryIndex>.Instance);
        var sourceId = Guid.NewGuid();

        var result = await index.IndexAsync("Alpha works with Beta on the project.", sourceId);

        Assert.True(result.IsSuccess);
        var upserted = Assert.Single(repository.Upserted);
        Assert.Equal(sourceId, upserted.SourceId);
        Assert.True(upserted.TermFrequency.ContainsKey("alpha"));
        Assert.True(upserted.TermFrequency.ContainsKey("works"));
        // Stopwords ("with", "on", "the") and short words are filtered, so the
        // sentence yields 4 tokens: alpha, works, beta, project.
        Assert.Equal(4, upserted.DocumentLength);
    }

    [Fact]
    public void Constructor_loads_persisted_corpus_so_restart_sees_same_corpus()
    {
        var sourceId = Guid.NewGuid();
        var repository = new FakeCorpusIndexRepository
        {
            Snapshot = new CorpusIndexSnapshot
            {
                Documents = new[]
                {
                    new CorpusDocumentIndex
                    {
                        SourceId = sourceId,
                        DocumentLength = 5,
                        TermFrequency = new Dictionary<string, int>
                        {
                            ["alpha"] = 2,
                            ["beta"] = 1
                        }
                    }
                }
            }
        };

        var index = new CorpusDiscoveryIndex(repository, NullLogger<CorpusDiscoveryIndex>.Instance);

        var stats = index.GetStatistics(sourceId);
        Assert.Equal(5, stats.DocumentLength);
        Assert.Equal(2, stats.UniqueTerms);
        Assert.Contains(sourceId, index.SearchCorpus("alpha", topK: 10));
    }

    [Fact]
    public async Task IndexAsync_does_not_fail_when_persistence_fails()
    {
        var index = new CorpusDiscoveryIndex(
            new FailingCorpusIndexRepository(),
            NullLogger<CorpusDiscoveryIndex>.Instance);

        var result = await index.IndexAsync("some content here", Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Constructor_does_not_fail_when_load_fails()
    {
        var index = new CorpusDiscoveryIndex(
            new FailingCorpusIndexRepository(),
            NullLogger<CorpusDiscoveryIndex>.Instance);

        // In-memory index starts empty but remains usable.
        Assert.Empty(index.SearchCorpus("anything", topK: 10));
    }

    private sealed class FakeCorpusIndexRepository : ICorpusIndexRepository
    {
        public List<(Guid SourceId, IReadOnlyDictionary<string, int> TermFrequency, int DocumentLength)> Upserted { get; } = new();

        public CorpusIndexSnapshot Snapshot { get; set; } = new();

        public Task<Result> UpsertDocumentAsync(
            Guid sourceId,
            IReadOnlyDictionary<string, int> termFrequency,
            int documentLength,
            CancellationToken cancellationToken = default)
        {
            Upserted.Add((sourceId, termFrequency, documentLength));
            return Task.FromResult(Result.Success());
        }

        public Task<Result<CorpusIndexSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CorpusIndexSnapshot>.Success(Snapshot));
        }
    }

    private sealed class FailingCorpusIndexRepository : ICorpusIndexRepository
    {
        public Task<Result> UpsertDocumentAsync(
            Guid sourceId,
            IReadOnlyDictionary<string, int> termFrequency,
            int documentLength,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure("database unavailable"));
        }

        public Task<Result<CorpusIndexSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CorpusIndexSnapshot>.Failure("database unavailable"));
        }
    }
}
