using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphRagService
{
    Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
        string query,
        int topK = 5,
        int maxExpanded = 10,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null);

    /// <summary>
    /// Executes a global (organization-wide) search using map-reduce over community summaries.
    /// </summary>
    Task<Result<GlobalSearchResult>> GlobalSearchAsync(
        string query,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null);
}
