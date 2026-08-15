using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// User-facing "Summaries" retrieval. Prefers pre-built GraphRAG community summaries and
/// falls back to LazyGraphRAG's query-time traversal when the graph has no usable summaries.
/// The two engines are the same product to the user — only the production of the summaries differs.
/// </summary>
public interface ISummariesRetrievalService
{
    Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
        string query,
        int topK = 5,
        int maxExpanded = 10,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null);
}
