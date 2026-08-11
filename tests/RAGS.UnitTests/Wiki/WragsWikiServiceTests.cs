using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Wiki;
using System.Text.Json;

namespace RAGS.UnitTests.Wiki;

public sealed class WragsWikiServiceTests
{
    [Fact]
    public async Task SearchAsync_returns_stored_pages_without_regenerating()
    {
        var repository = new FakeWikiPageRepository(
            new[]
            {
                CreatePage("Project Helios", "Stored Helios")
            });
        var graph = new CountingGraphRagService();
        var service = CreateService(repository, graph);

        var result = await service.SearchAsync(new WikiSearchRequest
        {
            Query = "Project Helios",
            Mode = "wrags",
            TopK = 3
        });

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Stored Helios", result.Value![0].Title);
        Assert.Equal(0, graph.RetrieveCalls);
    }

    [Fact]
    public async Task RegenerateAsync_retrieves_and_persists_wiki_pages()
    {
        var repository = new FakeWikiPageRepository(Array.Empty<WikiPage>());
        var graph = new CountingGraphRagService();
        var service = CreateService(repository, graph);

        var result = await service.RegenerateAsync(new WikiSearchRequest
        {
            Query = "Project Helios",
            Mode = "wrags",
            TopK = 2
        });

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(1, graph.RetrieveCalls);
        Assert.Single(repository.SavedPages);
        Assert.Equal("Project Helios", repository.SavedPages[0].Topic);
    }

    [Fact]
    public async Task UpdatePageAsync_saves_edit_and_exposes_history()
    {
        var page = CreatePage("Project Helios", "Stored Helios");
        var repository = new FakeWikiPageRepository(new[] { page });
        var service = CreateService(repository, new CountingGraphRagService());

        var updated = await service.UpdatePageAsync(
            page.Id,
            new WikiPageEditRequest
            {
                Title = "Edited Helios",
                Summary = "Edited body.",
                RelatedTopics = new[] { "Helios", "WRAGS" },
                Status = "NeedsReview",
                ChangeNote = "Editorial cleanup"
            });
        var history = await service.GetHistoryAsync(page.Id, 10);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Edited Helios", updated.Value!.Title);
        Assert.Equal(2, updated.Value.Version);
        Assert.True(history.IsSuccess);
        Assert.Single(history.Value!);
        Assert.Equal("Stored Helios", history.Value![0].Title);
    }

    [Fact]
    public void WikiPage_round_trips_through_web_json()
    {
        var pageId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var primarySourceId = Guid.NewGuid();
        var reviewedAt = DateTimeOffset.UtcNow;
        var page = new WikiPage(
            pageId,
            "Project Helios",
            "Stored Helios",
            "Stored summary.",
            new[] { sourceId },
            new[] { "citation" },
            "wrags",
            version: 3,
            status: "Reviewed",
            score: 0.9f,
            rank: 2,
            retrievalStrategy: "summary-community",
            primarySourceId: primarySourceId,
            chunkIndex: 4,
            relatedTopics: new[] { "WRAGS", "GraphRAG" },
            reviewedBy: "admin",
            reviewedAt: reviewedAt,
            isStale: true,
            staleReason: "Source changed.",
            createdAt: reviewedAt.AddDays(-1),
            updatedAt: reviewedAt);

        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTrip = JsonSerializer.Deserialize<WikiPage>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTrip);
        Assert.Equal(pageId, roundTrip.Id);
        Assert.Equal("Stored Helios", roundTrip.Title);
        Assert.Equal("summary-community", roundTrip.RetrievalStrategy);
        Assert.Equal(primarySourceId, roundTrip.PrimarySourceId);
        Assert.Equal(new[] { "WRAGS", "GraphRAG" }, roundTrip.RelatedTopics);
        Assert.True(roundTrip.IsStale);
        Assert.Equal("Source changed.", roundTrip.StaleReason);
    }

    private static WragsWikiService CreateService(
        IWikiPageRepository repository,
        IGraphRagService graphRagService)
    {
        return new WragsWikiService(
            repository,
            new EmptyRagsService(),
            graphRagService,
            new EmptyLazyGraphRagService());
    }

    private static WikiPage CreatePage(string topic, string title)
    {
        return new WikiPage(
            Guid.NewGuid(),
            topic,
            title,
            "Stored summary.",
            new[] { Guid.NewGuid() },
            new[] { "citation" },
            "wrags",
            score: 0.9f,
            rank: 1,
            retrievalStrategy: "summary-entity",
            primarySourceId: Guid.NewGuid(),
            chunkIndex: 0);
    }

    private sealed class FakeWikiPageRepository : IWikiPageRepository
    {
        private IReadOnlyList<WikiPage> _pages;

        public FakeWikiPageRepository(IReadOnlyList<WikiPage> pages)
        {
            _pages = pages;
        }

        public List<WikiPage> SavedPages { get; } = new();

        public Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(_pages.Take(topK).ToList()));
        }

        public Task<Result<IReadOnlyList<WikiPage>>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(_pages.Take(take).ToList()));
        }

        public Task<Result<WikiPage?>> GetAsync(Guid pageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(_pages.FirstOrDefault(page => page.Id == pageId)));
        }

        public Task<Result<IReadOnlyList<WikiPageLink>>> GetRelatedAsync(Guid pageId, int take, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WikiPageLink> links = _pages
                .Where(page => page.Id != pageId)
                .Take(take)
                .Select(page => new WikiPageLink(page.Id, page.Topic, page.Title, page.Status, page.Version, page.UpdatedAt))
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<WikiPageLink>>.Success(links));
        }

        public Task<Result<IReadOnlyList<WikiPageHistoryEntry>>> GetHistoryAsync(Guid pageId, int take, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WikiPageHistoryEntry> history = _history
                .Where(item => item.PageId == pageId)
                .Take(take)
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<WikiPageHistoryEntry>>.Success(history));
        }

        public Task<Result<IReadOnlyList<WikiPage>>> UpsertAsync(IReadOnlyList<WikiPage> pages, CancellationToken cancellationToken = default)
        {
            SavedPages.AddRange(pages);
            _pages = pages;
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(pages));
        }

        public Task<Result<WikiPage?>> UpdateStatusAsync(Guid pageId, string status, string? reviewedBy, CancellationToken cancellationToken = default)
        {
            var page = _pages.FirstOrDefault(item => item.Id == pageId);
            if (page is null)
            {
                return Task.FromResult(Result<WikiPage?>.Success(null));
            }

            AddHistory(page, "Status", $"Status changed to {status}.");
            var updated = new WikiPage(
                page.Id,
                page.Topic,
                page.Title,
                page.Summary,
                page.SourceIds,
                page.Citations,
                page.GeneratedFrom,
                page.Version,
                status,
                page.Score,
                page.Rank,
                page.RetrievalStrategy,
                page.PrimarySourceId,
                page.ChunkIndex,
                page.RelatedTopics,
                reviewedBy,
                status == "Reviewed" ? DateTimeOffset.UtcNow : null,
                status is "Stale" or "NeedsReview",
                status == "Stale" ? "Marked stale." : null,
                page.CreatedAt,
                DateTimeOffset.UtcNow);

            _pages = _pages.Select(item => item.Id == pageId ? updated : item).ToList();
            return Task.FromResult(Result<WikiPage?>.Success(updated));
        }

        public Task<Result<WikiPage?>> UpdatePageAsync(Guid pageId, WikiPageEditRequest request, CancellationToken cancellationToken = default)
        {
            var page = _pages.FirstOrDefault(item => item.Id == pageId);
            if (page is null)
            {
                return Task.FromResult(Result<WikiPage?>.Success(null));
            }

            AddHistory(page, "Edit", request.ChangeNote);
            var updated = new WikiPage(
                page.Id,
                page.Topic,
                request.Title ?? page.Title,
                request.Summary ?? page.Summary,
                page.SourceIds,
                page.Citations,
                page.GeneratedFrom,
                page.Version + 1,
                request.Status ?? page.Status,
                page.Score,
                page.Rank,
                page.RetrievalStrategy,
                page.PrimarySourceId,
                page.ChunkIndex,
                request.RelatedTopics ?? page.RelatedTopics,
                page.ReviewedBy,
                page.ReviewedAt,
                request.Status is "Stale" or "NeedsReview",
                request.Status == "Stale" ? "Marked stale." : null,
                page.CreatedAt,
                DateTimeOffset.UtcNow);

            _pages = _pages.Select(item => item.Id == pageId ? updated : item).ToList();
            return Task.FromResult(Result<WikiPage?>.Success(updated));
        }

        private readonly List<WikiPageHistoryEntry> _history = new();

        private void AddHistory(WikiPage page, string changeType, string? changeNote)
        {
            _history.Add(new WikiPageHistoryEntry(
                Guid.NewGuid(),
                page.Id,
                page.Version,
                page.Title,
                page.Summary,
                page.Status,
                page.RelatedTopics,
                changeType,
                "test",
                changeNote,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class CountingGraphRagService : IGraphRagService
    {
        public int RetrieveCalls { get; private set; }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
        {
            RetrieveCalls++;
            var sourceId = Guid.NewGuid();
            IReadOnlyList<SearchResult> results = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceId, "Project Helios is a durable WRAGS knowledge topic.", 0),
                    0.95f,
                    new[] { "Project Helios citation" },
                    retrievalStrategy: "summary-entity",
                    rank: 1)
            };

            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(results));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
        {
            return Task.FromResult(Result<GlobalSearchResult>.Failure("Not used."));
        }
    }

    private sealed class EmptyRagsService : IRagsService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }
    }

    private sealed class EmptyLazyGraphRagService : ILazyGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
        {
            return Task.FromResult(Result<GlobalSearchResult>.Failure("Not used."));
        }
    }
}
