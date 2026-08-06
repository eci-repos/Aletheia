using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ISourceFilteredVectorStore : IVectorStore
{
    Task<Result<IReadOnlyList<SearchResult>>> SearchBySourceAsync(
        ReadOnlyMemory<float> vector,
        int topK,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Vector search restricted to a set of sources (Sprint 58 knowledge-theme filter).</summary>
    Task<Result<IReadOnlyList<SearchResult>>> SearchBySourcesAsync(
        ReadOnlyMemory<float> vector,
        int topK,
        IReadOnlyList<Guid> sourceIds,
        CancellationToken cancellationToken = default);
}