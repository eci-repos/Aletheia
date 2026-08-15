using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Lexicon;
using Aletheia.RAGS.Application.Pipelines;
using Microsoft.Extensions.Logging;

namespace Aletheia.RAGS.Application;

public sealed class RagsService : IRagsService
{
    private const string IngestionFailedMessage = "Ingestion failed.";
    private const string RetrievalFailedMessage = "Retrieval failed.";

    private readonly ChunkingPipeline _chunkingPipeline;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IVectorStore _vectorStore;
    private readonly RetrievalOptions _retrievalOptions;
    private readonly ILexiconProvider? _lexiconProvider;
    private readonly Microsoft.Extensions.Logging.ILogger<RagsService> _logger;

    public RagsService(
        ChunkingPipeline chunkingPipeline,
        IEmbeddingProvider embeddingProvider,
        IVectorStore vectorStore,
        Microsoft.Extensions.Options.IOptions<RetrievalOptions>? retrievalOptions = null,
        Microsoft.Extensions.Logging.ILogger<RagsService>? logger = null,
        ILexiconProvider? lexiconProvider = null)
    {
        _chunkingPipeline = chunkingPipeline ?? throw new ArgumentNullException(nameof(chunkingPipeline));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _retrievalOptions = retrievalOptions?.Value ?? new RetrievalOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RagsService>.Instance;
        _lexiconProvider = lexiconProvider;
    }

    public async Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            // Chunk content (page-aware when the extractor reported page boundaries)
            var chunks = request.Pages is { Count: > 0 }
                ? _chunkingPipeline.Chunk(request.SourceId, request.Content, request.Pages)
                : _chunkingPipeline.Chunk(request.SourceId, request.Content);

            // Generate embeddings first, then swap the source's rows in one atomic replace
            // (write-new-then-swap). An interrupted ingestion leaves the OLD embeddings intact —
            // never zero, so the Repository Browser's "Ingested" status stays truthful.
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

            var storeResult = await _vectorStore.ReplaceSourceAsync(request.SourceId, items, cancellationToken).ConfigureAwait(false);
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

        // Sprint 68: expand domain acronyms ("AI" → "AI Artificial Intelligence") before embedding so a
        // short acronym retrieves documents that spell it out. Sprint 70: then expand lexicon concepts
        // into their alias family ("submission due date" → + "bid due", "proposal due date", "deadline",
        // ...) so a varied phrasing retrieves documents that use any surface form. The keyword fallback
        // keeps the original query — it is a whole-string ILIKE match and would not match the expanded phrase.
        var expandedQuery = QueryExpander.Expand(request.Query);
        if (_lexiconProvider is not null)
        {
            var concepts = await _lexiconProvider.GetConceptsAsync(cancellationToken).ConfigureAwait(false);
            expandedQuery = LexiconExpander.Expand(expandedQuery, concepts);
        }

        _logger.LogInformation("RAGS retrieval started for query '{Query}' (expanded '{ExpandedQuery}', topK={TopK}, sourceId={SourceId}).", request.Query, expandedQuery, request.TopK, request.SourceId);

        var embeddingResult = await _embeddingProvider.GenerateAsync(expandedQuery, cancellationToken).ConfigureAwait(false);
        if (embeddingResult.IsFailure || embeddingResult.Value.IsEmpty)
        {
            _logger.LogWarning("RAGS embedding generation failed for query '{Query}': {Error}.", request.Query, embeddingResult.Error);
            return Result<IReadOnlyList<SearchResult>>.Failure(embeddingResult.Error ?? RetrievalFailedMessage);
        }

        _logger.LogInformation("RAGS embedding generated for query '{Query}'; querying vector store.", request.Query);

        var searchResult = request.SourceIds is not null && _vectorStore is ISourceFilteredVectorStore sourceSetVectorStore
            ? await sourceSetVectorStore.SearchBySourcesAsync(embeddingResult.Value, request.TopK, request.SourceIds, cancellationToken).ConfigureAwait(false)
            : request.SourceId.HasValue && _vectorStore is ISourceFilteredVectorStore sourceFilteredVectorStore
                ? await sourceFilteredVectorStore.SearchBySourceAsync(embeddingResult.Value, request.TopK, request.SourceId.Value, cancellationToken).ConfigureAwait(false)
                : await _vectorStore.SearchAsync(embeddingResult.Value, request.TopK, cancellationToken).ConfigureAwait(false);
        if (searchResult.IsFailure)
        {
            _logger.LogWarning("RAGS vector search failed for query '{Query}': {Error}.", request.Query, searchResult.Error);
            return Result<IReadOnlyList<SearchResult>>.Failure(searchResult.Error ?? RetrievalFailedMessage);
        }

        if (searchResult.Value is null)
        {
            _logger.LogWarning("RAGS vector search returned null results for query '{Query}'.", request.Query);
            return Result<IReadOnlyList<SearchResult>>.Failure(RetrievalFailedMessage);
        }

        var results = searchResult.Value;
        // Sprint 57: score floor + keyword fallback - when vector retrieval returns nothing or its
        // best score is below the configured floor, fall back to lexical search so users get results
        // instead of silence.
        var minimumScore = _retrievalOptions.MinimumScore;
        var bestScore = results.Count == 0 ? 0f : results.Max(result => result.Score);
        if (results.Count == 0 || bestScore < minimumScore)
        {
            var keywordResult = await _vectorStore
                .SearchKeywordAsync(request.Query, request.TopK, request.SourceIds, cancellationToken)
                .ConfigureAwait(false);
            if (keywordResult.IsSuccess && keywordResult.Value is { Count: > 0 })
            {
                results = keywordResult.Value;
                _logger.LogInformation(
                    "RAGS retrieval used keyword fallback for query '{Query}' (vector count {VectorCount}, best score {BestScore:P2}, minimum {MinimumScore:P2}).",
                    request.Query,
                    searchResult.Value.Count,
                    bestScore,
                    minimumScore);
            }
            else if (keywordResult.IsFailure)
            {
                _logger.LogWarning("RAGS keyword fallback failed for query '{Query}': {Error}.", request.Query, keywordResult.Error);
            }
        }
        if (request.SourceIds is not null && _vectorStore is not ISourceFilteredVectorStore)
        {
            results = results
                .Where(result => request.SourceIds.Contains(result.Chunk.SourceId))
                .Take(request.TopK)
                .ToList();
        }
        else if (request.SourceId.HasValue && _vectorStore is not ISourceFilteredVectorStore)
        {
            results = results
                .Where(result => result.Chunk.SourceId == request.SourceId.Value)
                .Take(request.TopK)
                .ToList();
        }

        _logger.LogInformation("RAGS retrieval completed for query '{Query}'; returned {Count} result(s).", request.Query, results.Count);
        return Result<IReadOnlyList<SearchResult>>.Success(results);
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(
        Guid sourceId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var result = await _vectorStore
            .GetSourceChunksAsync(sourceId, Math.Clamp(take, 1, 50), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null
            ? result
            : Result<IReadOnlyList<SearchResult>>.Failure(result.Error ?? "Source chunk retrieval failed.");
    }
}
