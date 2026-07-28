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
}
