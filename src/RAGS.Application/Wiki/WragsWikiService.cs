using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Wiki;

public sealed class WragsWikiService : IWragsWikiService
{
    private const int MaxSummaryLength = 2_400;

    private readonly IWikiPageRepository _repository;
    private readonly IRagsService _ragsService;
    private readonly IGraphRagService _graphRagService;
    private readonly ILazyGraphRagService _lazyGraphRagService;

    public WragsWikiService(
        IWikiPageRepository repository,
        IRagsService ragsService,
        IGraphRagService graphRagService,
        ILazyGraphRagService lazyGraphRagService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
    }

    public async Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        if (string.IsNullOrWhiteSpace(normalized.Query))
        {
            return Result<IReadOnlyList<WikiPage>>.Success(Array.Empty<WikiPage>());
        }

        if (normalized.Regenerate)
        {
            return await RegenerateAsync(normalized, cancellationToken).ConfigureAwait(false);
        }

        var stored = await _repository.SearchAsync(normalized.Query, normalized.TopK, cancellationToken).ConfigureAwait(false);
        if (stored.IsFailure)
        {
            return Result<IReadOnlyList<WikiPage>>.Failure(stored.Error ?? "WRAGS wiki search failed.");
        }

        if (stored.Value is { Count: > 0 })
        {
            return stored;
        }

        return await RegenerateAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<WikiPage>>> RegenerateAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        if (string.IsNullOrWhiteSpace(normalized.Query))
        {
            return Result<IReadOnlyList<WikiPage>>.Success(Array.Empty<WikiPage>());
        }

        var retrieval = await RetrieveKnowledgeAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (retrieval.IsFailure)
        {
            return Result<IReadOnlyList<WikiPage>>.Failure(retrieval.Error ?? "WRAGS wiki regeneration failed.");
        }

        var pages = retrieval.Value!
            .Select((result, index) => CreatePage(normalized.Query, normalized.Mode, result, index + 1))
            .ToList();

        if (pages.Count == 0)
        {
            return Result<IReadOnlyList<WikiPage>>.Success(pages);
        }

        var saved = await _repository.UpsertAsync(pages, cancellationToken).ConfigureAwait(false);
        return saved.IsFailure
            ? Result<IReadOnlyList<WikiPage>>.Failure(saved.Error ?? "WRAGS wiki persistence failed.")
            : saved;
    }

    public Task<Result<IReadOnlyList<WikiPage>>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetRecentAsync(Math.Clamp(take, 1, 50), cancellationToken);
    }

    public Task<Result<WikiPage?>> GetAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        return _repository.GetAsync(pageId, cancellationToken);
    }

    public Task<Result<IReadOnlyList<WikiPageLink>>> GetRelatedAsync(
        Guid pageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetRelatedAsync(pageId, Math.Clamp(take, 1, 20), cancellationToken);
    }

    public Task<Result<IReadOnlyList<WikiPageHistoryEntry>>> GetHistoryAsync(
        Guid pageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetHistoryAsync(pageId, Math.Clamp(take, 1, 50), cancellationToken);
    }

    public Task<Result<WikiPage?>> UpdateStatusAsync(
        Guid pageId,
        WikiPageStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        var status = NormalizeStatus(update.Status);
        return _repository.UpdateStatusAsync(pageId, status, update.ReviewedBy, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        if (string.IsNullOrWhiteSpace(normalized.Query))
        {
            return Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>());
        }

        var pages = await _repository.SearchAsync(
            normalized.Query,
            normalized.TopK,
            cancellationToken).ConfigureAwait(false);
        if (pages.IsFailure)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure(pages.Error ?? "WRAGS retrieval failed.");
        }

        var results = pages.Value!
            .Where(page => !string.IsNullOrWhiteSpace(page.Summary))
            .Select((page, index) => ToSearchResult(page, index + 1))
            .ToList();

        return Result<IReadOnlyList<SearchResult>>.Success(results);
    }

    public Task<Result<WikiPage?>> UpdatePageAsync(
        Guid pageId,
        WikiPageEditRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Status = NormalizeStatus(request.Status);
        request.RelatedTopics = NormalizeRelatedTopics(request.RelatedTopics);
        return _repository.UpdatePageAsync(pageId, request, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<SearchResult>>> RetrieveKnowledgeAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken)
    {
        return request.Mode switch
        {
            "semantic" => await _ragsService.RetrieveAsync(
                new RetrievalRequest(request.Query, request.TopK),
                cancellationToken).ConfigureAwait(false),
            "graphrag" => await _graphRagService.RetrieveAsync(
                request.Query,
                request.TopK,
                request.Expansion,
                cancellationToken).ConfigureAwait(false),
            "lazygraphrag" => await _lazyGraphRagService.RetrieveAsync(
                request.Query,
                request.TopK,
                request.Expansion,
                cancellationToken).ConfigureAwait(false),
            _ => await RetrieveWragsAsync(request, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<Result<IReadOnlyList<SearchResult>>> RetrieveWragsAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken)
    {
        var graph = await _graphRagService.RetrieveAsync(
            request.Query,
            request.TopK,
            request.Expansion,
            cancellationToken).ConfigureAwait(false);

        if (graph.IsSuccess && graph.Value is { Count: > 0 })
        {
            return graph;
        }

        var lazy = await _lazyGraphRagService.RetrieveAsync(
            request.Query,
            request.TopK,
            request.Expansion,
            cancellationToken).ConfigureAwait(false);

        if (lazy.IsSuccess && lazy.Value is { Count: > 0 })
        {
            return lazy;
        }

        return await _ragsService.RetrieveAsync(
            new RetrievalRequest(request.Query, request.TopK),
            cancellationToken).ConfigureAwait(false);
    }

    private static WikiPage CreatePage(string topic, string mode, SearchResult result, int fallbackRank)
    {
        var content = result.Chunk.Content.Trim();
        var rank = result.Rank > 0 ? result.Rank : fallbackRank;
        var summary = content.Length <= MaxSummaryLength ? content : $"{content[..MaxSummaryLength]}...";
        var primarySourceId = result.Chunk.SourceId;
        var sourceIds = new[] { primarySourceId };

        return new WikiPage(
            Guid.NewGuid(),
            topic,
            BuildTitle(content, topic),
            summary,
            sourceIds,
            result.Citations,
            NormalizeMode(mode),
            version: 1,
            status: "Generated",
            score: result.Score,
            rank: rank,
            retrievalStrategy: result.RetrievalStrategy,
            primarySourceId: primarySourceId,
            chunkIndex: result.Chunk.Index,
            relatedTopics: ExtractRelatedTopics(topic, content),
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow);
    }

    private static SearchResult ToSearchResult(WikiPage page, int fallbackRank)
    {
        var sourceId = page.PrimarySourceId
            ?? page.SourceIds.FirstOrDefault(id => id != Guid.Empty);
        if (sourceId == Guid.Empty)
        {
            sourceId = page.Id;
        }

        var content = $"WRAGS Wiki: {page.Title}\nStatus: {page.Status}\nVersion: {page.Version}\n\n{page.Summary}";
        var score = page.Status.Equals("Reviewed", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(1f, page.Score + 0.08f)
            : page.IsStale ? Math.Max(0.1f, page.Score * 0.65f) : page.Score;

        var signals = new Dictionary<string, float>
        {
            ["wiki"] = 1f,
            ["reviewed"] = page.Status.Equals("Reviewed", StringComparison.OrdinalIgnoreCase) ? 1f : 0f,
            ["stale"] = page.IsStale ? 1f : 0f
        };

        return new SearchResult(
            new Chunk(page.Id, sourceId, content, page.ChunkIndex ?? 0),
            score,
            page.Citations,
            signals,
            retrievalStrategy: $"wrags-{page.GeneratedFrom}",
            rank: page.Rank > 0 ? page.Rank : fallbackRank);
    }

    private static WikiSearchRequest NormalizeRequest(WikiSearchRequest? request)
    {
        var mode = NormalizeMode(request?.Mode);
        return new WikiSearchRequest
        {
            Query = request?.Query?.Trim() ?? string.Empty,
            Mode = mode,
            TopK = Math.Clamp(request?.TopK ?? 6, 1, 12),
            Expansion = Math.Clamp(request?.Expansion ?? 1, 0, 3),
            Regenerate = request?.Regenerate == true
        };
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        return normalized is "semantic" or "graphrag" or "lazygraphrag" ? normalized : "wrags";
    }

    private static string NormalizeStatus(string? status)
    {
        var normalized = status?.Trim().Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized?.ToLowerInvariant() switch
        {
            "reviewed" => "Reviewed",
            "needsreview" => "NeedsReview",
            "approved" => "Approved",
            "stale" => "Stale",
            "generated" => "Generated",
            _ => "NeedsReview"
        };
    }

    private static IReadOnlyList<string> NormalizeRelatedTopics(IReadOnlyList<string>? relatedTopics)
    {
        if (relatedTopics is null)
        {
            return Array.Empty<string>();
        }

        return relatedTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Select(topic => topic.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
    }

    private static string BuildTitle(string content, string topic)
    {
        var firstLine = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return topic.Trim();
        }

        firstLine = firstLine.TrimStart('#', '*', '-', ' ');
        return firstLine.Length <= 96 ? firstLine : $"{firstLine[..96]}...";
    }

    private static IReadOnlyList<string> ExtractRelatedTopics(string topic, string content)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTopic(values, topic);

        foreach (var token in content.Split(new[] { ' ', '\r', '\n', '\t', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (values.Count >= 8)
            {
                break;
            }

            var cleaned = token.Trim('-', '#', '*', '/', '\\');
            if (cleaned.Length < 4 || cleaned.Length > 48)
            {
                continue;
            }

            if (char.IsUpper(cleaned[0]) || cleaned.Contains('-', StringComparison.Ordinal))
            {
                AddTopic(values, cleaned);
            }
        }

        return values.Take(8).ToList();
    }

    private static void AddTopic(HashSet<string> values, string topic)
    {
        var cleaned = topic.Trim();
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            values.Add(cleaned.Length <= 64 ? cleaned : cleaned[..64]);
        }
    }
}
