using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IVectorStore
{
    Task<Result> StoreAsync(Guid chunkId, ReadOnlyMemory<float> vector, Chunk chunk, CancellationToken cancellationToken = default);

    Task<Result> StoreBatchAsync(IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SearchResult>>> SearchAsync(ReadOnlyMemory<float> vector, int topK, CancellationToken cancellationToken = default);

    Task<Result> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the first <paramref name="take"/> chunks of a source in document order (by chunk index).</summary>
    Task<Result<IReadOnlyList<SearchResult>>> GetSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
}
