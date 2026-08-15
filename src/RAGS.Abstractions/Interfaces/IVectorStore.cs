using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IVectorStore
{
    Task<Result> StoreAsync(Guid chunkId, ReadOnlyMemory<float> vector, Chunk chunk, CancellationToken cancellationToken = default);

    Task<Result> StoreBatchAsync(IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SearchResult>>> SearchAsync(ReadOnlyMemory<float> vector, int topK, CancellationToken cancellationToken = default);

    Task<Result> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces a source's embeddings with a freshly chunked/embedded batch
    /// (write-new-then-swap). Default implementation is delete-then-store for stores that cannot do it
    /// atomically; stores may override with a single transaction so an interrupted ingestion leaves
    /// either the old or the new embeddings, never zero. An empty batch clears the source's rows.</summary>
    async Task<Result> ReplaceSourceAsync(Guid sourceId, IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken cancellationToken = default)
    {
        var deleteResult = await DeleteBySourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        return await StoreBatchAsync(items, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lexical fallback search over chunk content and file names. Not supported by default; stores may override.</summary>
    Task<Result<IReadOnlyList<SearchResult>>> SearchKeywordAsync(string query, int topK, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Failure("Keyword search is not supported by this store."));

    /// <summary>Lexical fallback restricted to a source set (Sprint 58). Default implementation ignores the filter; stores may override.</summary>
    Task<Result<IReadOnlyList<SearchResult>>> SearchKeywordAsync(string query, int topK, IReadOnlyList<Guid>? sourceIds, CancellationToken cancellationToken = default)
        => SearchKeywordAsync(query, topK, cancellationToken);

    /// <summary>Returns the first <paramref name="take"/> chunks of a source in document order (by chunk index).</summary>
    Task<Result<IReadOnlyList<SearchResult>>> GetSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));

    /// <summary>Returns the number of stored embeddings per source id (Sprint 69 — ingestion status in the
    /// Repository Browser). Default returns an empty map; stores may override.</summary>
    Task<Result<IReadOnlyDictionary<Guid, int>>> GetChunkCountsAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IReadOnlyDictionary<Guid, int>>.Success(new Dictionary<Guid, int>()));
}