using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.GraphRAG;

/// <summary>
/// User-facing "Summaries" retrieval. GraphRAG and LazyGraphRAG are the same product to the user —
/// only the production of the summaries differs (pre-built at ingest vs. built on demand at query
/// time). This service hides that: it prefers pre-built GraphRAG community summaries and falls back
/// to LazyGraphRAG's query-time traversal when the graph has no usable summaries.
/// </summary>
public sealed class SummariesRetrievalService : ISummariesRetrievalService
{
    private readonly IGraphRagService _graphRagService;
    private readonly ILazyGraphRagService _lazyGraphRagService;

    public SummariesRetrievalService(IGraphRagService graphRagService, ILazyGraphRagService lazyGraphRagService)
    {
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
        string query,
        int topK = 5,
        int maxExpanded = 10,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        // GraphRAG-first: pre-built community summaries are the preferred source. Its internal
        // fallback chain (lazy enrichment → graph-aware → semantic) already degrades gracefully,
        // so an empty result here means the graph genuinely has nothing usable for this query.
        var graphResult = await _graphRagService
            .RetrieveAsync(query, topK, maxExpanded, cancellationToken, sourceIds)
            .ConfigureAwait(false);

        if (graphResult.IsSuccess && graphResult.Value is { Count: > 0 })
        {
            return graphResult;
        }

        // LazyGraphRAG-fallback: build summaries on demand at query time.
        return await _lazyGraphRagService
            .RetrieveAsync(query, topK, maxExpanded, cancellationToken, sourceIds)
            .ConfigureAwait(false);
    }
}
