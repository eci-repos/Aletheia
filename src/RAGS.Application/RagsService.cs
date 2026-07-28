using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Pipelines;

namespace Aletheia.RAGS.Application;

public sealed class RagsService : IRagsService
{
    private const string IngestionFailedMessage = "Ingestion failed.";
    private const string RetrievalFailedMessage = "Retrieval failed.";

    private readonly ChunkingPipeline _chunkingPipeline;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IVectorStore _vectorStore;

    public RagsService(
        ChunkingPipeline chunkingPipeline,
        IEmbeddingProvider embeddingProvider,
        IVectorStore vectorStore)
    {
        _chunkingPipeline = chunkingPipeline ?? throw new ArgumentNullException(nameof(chunkingPipeline));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
    }

    public async Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            // Delete existing embeddings for this source
            var deleteResult = await _vectorStore.DeleteBySourceAsync(request.SourceId, cancellationToken).ConfigureAwait(false);
            // Continue even if delete fails (first ingestion)

            // Chunk content
            var chunks = _chunkingPipeline.Chunk(request.SourceId, request.Content);

            // Generate embeddings and store
            var items = new List<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)>();
            foreach (var chunk in chunks)
            {
                var embeddingResult = await _embeddingProvider.GenerateAsync(chunk.Content, cancellationToken).ConfigureAwait(false);
                if (embeddingResult.IsFailure || embeddingResult.Value.IsEmpty)
                {
                    return Result.Failure($"{IngestionFailedMessage} Failed to generate embedding for chunk {chunk.Index}.");
                }

                items.Add((chunk.Id, embeddingResult.Value, chunk));
            }

            var storeResult = await _vectorStore.StoreBatchAsync(items, cancellationToken).ConfigureAwait(false);
            if (storeResult.IsFailure)
            {
                return Result.Failure(storeResult.Error ?? IngestionFailedMessage);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{IngestionFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var embeddingResult = await _embeddingProvider.GenerateAsync(request.Query, cancellationToken).ConfigureAwait(false);
        if (embeddingResult.IsFailure || embeddingResult.Value.IsEmpty)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure(embeddingResult.Error ?? RetrievalFailedMessage);
        }

        var searchResult = request.SourceId.HasValue && _vectorStore is ISourceFilteredVectorStore sourceFilteredVectorStore
            ? await sourceFilteredVectorStore.SearchBySourceAsync(embeddingResult.Value, request.TopK, request.SourceId.Value, cancellationToken).ConfigureAwait(false)
            : await _vectorStore.SearchAsync(embeddingResult.Value, request.TopK, cancellationToken).ConfigureAwait(false);
        if (searchResult.IsFailure)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure(searchResult.Error ?? RetrievalFailedMessage);
        }

        if (searchResult.Value is null)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure(RetrievalFailedMessage);
        }

        var results = searchResult.Value;
        if (request.SourceId.HasValue && _vectorStore is not ISourceFilteredVectorStore)
        {
            results = results
                .Where(result => result.Chunk.SourceId == request.SourceId.Value)
                .Take(request.TopK)
                .ToList();
        }

        return Result<IReadOnlyList<SearchResult>>.Success(results);
    }
}
