using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IRagsService
{
    Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the first chunks of a source in document order (the opening/Project Summary section).</summary>
    Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
}
