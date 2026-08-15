using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PgVector.VectorStore;

public sealed class PgVectorStore : ISourceFilteredVectorStore
{
    private const string StoreFailedMessage = "Vector store operation failed.";
    private const string SearchFailedMessage = "Vector search failed.";
    private const string DeleteFailedMessage = "Vector deletion failed.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly int _vectorDimension;
    private readonly int _commandTimeoutSeconds;

    public PgVectorStore(PostgreSqlConnectionFactory connectionFactory, int vectorDimension, int commandTimeoutSeconds = 30)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _vectorDimension = vectorDimension > 0 ? vectorDimension : throw new ArgumentOutOfRangeException(nameof(vectorDimension));
        _commandTimeoutSeconds = commandTimeoutSeconds > 0 ? commandTimeoutSeconds : throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
    }

    public int CommandTimeoutSeconds => _commandTimeoutSeconds;



    public async Task<Result> StoreAsync(Guid chunkId, ReadOnlyMemory<float> vector, Chunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk is null)
        {
            throw new ArgumentNullException(nameof(chunk));
        }

        const string sql = @"
            INSERT INTO embeddings (chunk_id, source_id, content, embedding, page_number)
            VALUES (@ChunkId, @SourceId, @Content, @Embedding::vector, @PageNumber)
            ON CONFLICT (chunk_id)
            DO UPDATE SET
                source_id = EXCLUDED.source_id,
                content = EXCLUDED.content,
                embedding = EXCLUDED.embedding,
                page_number = EXCLUDED.page_number";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var parameters = new
            {
                chunkId,
                chunk.SourceId,
                chunk.Content,
                chunk.PageNumber,
                Embedding = VectorToString(vector)
            };

            await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{StoreFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> StoreBatchAsync(IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items, CancellationToken cancellationToken = default)
    {
        var itemList = items?.ToList() ?? throw new ArgumentNullException(nameof(items));
        if (itemList.Count == 0)
        {
            return Result.Success();
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            const string sql = @"
                INSERT INTO embeddings (chunk_id, source_id, content, embedding, page_number)
                VALUES (@ChunkId, @SourceId, @Content, @Embedding::vector, @PageNumber)
                ON CONFLICT (chunk_id)
                DO UPDATE SET
                    source_id = EXCLUDED.source_id,
                    content = EXCLUDED.content,
                    embedding = EXCLUDED.embedding,
                    page_number = EXCLUDED.page_number";

            foreach (var (chunkId, vector, chunk) in itemList)
            {
                var parameters = new
                {
                    ChunkId = chunkId,
                    chunk.SourceId,
                    chunk.Content,
                    chunk.PageNumber,
                    Embedding = VectorToString(vector)
                };

                await connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, transaction: transaction, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure($"{StoreFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> SearchAsync(ReadOnlyMemory<float> vector, int topK, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                e.chunk_id as ""ChunkId"",
                e.source_id as ""SourceId"",
                e.content as ""Content"",
                m.file_name as ""SourceName"",
                e.chunk_index as ""ChunkIndex"",
                e.page_number as ""PageNumber"",
                1 - (e.embedding <=> @QueryEmbedding::vector) as ""Score""
            FROM embeddings e
            LEFT JOIN LATERAL (
                SELECT file_name
                FROM file_metadata
                WHERE file_id = e.source_id
                ORDER BY uploaded_at DESC
                LIMIT 1
            ) m ON true
            ORDER BY e.embedding <=> @QueryEmbedding::vector
            LIMIT @TopK";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<EmbeddingRow>(
                new CommandDefinition(sql, new
                {
                    QueryEmbedding = VectorToString(vector),
                    TopK = topK
                }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var results = rows.Select(row => MapToSearchResult(row)).ToList();

            return Result<IReadOnlyList<SearchResult>>.Success(results);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> SearchKeywordAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        return await SearchKeywordAsync(query, topK, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> SearchKeywordAsync(
        string query,
        int topK,
        IReadOnlyList<Guid>? sourceIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>());
        }

        if (sourceIds is not null && sourceIds.Count == 0)
        {
            return Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>());
        }

        var sourcePredicate = sourceIds is { Count: > 0 }
            ? "AND e.source_id = ANY(@SourceIds)"
            : string.Empty;

        var sql = $@"
            SELECT
                e.chunk_id as ""ChunkId"",
                e.source_id as ""SourceId"",
                e.content as ""Content"",
                m.file_name as ""SourceName"",
                e.chunk_index as ""ChunkIndex"",
                e.page_number as ""PageNumber"",
                (CASE WHEN m.file_name ILIKE '%' || @Query || '%' THEN 1.0 ELSE 0.9 END) as ""Score""
            FROM embeddings e
            LEFT JOIN LATERAL (
                SELECT file_name
                FROM file_metadata
                WHERE file_id = e.source_id
                ORDER BY uploaded_at DESC
                LIMIT 1
            ) m ON true
            WHERE (e.content ILIKE '%' || @Query || '%'
               OR m.file_name ILIKE '%' || @Query || '%')
              {sourcePredicate}
            ORDER BY e.created_at DESC
            LIMIT @TopK";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<EmbeddingRow>(
                new CommandDefinition(sql, new
                {
                    Query = query.Trim(),
                    TopK = topK,
                    SourceIds = sourceIds?.ToArray()
                }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var results = rows.Select(row => MapToSearchResult(row, "keyword")).ToList();

            return Result<IReadOnlyList<SearchResult>>.Success(results);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> GetSourceChunksAsync(
        Guid sourceId,
        int take,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                e.chunk_id as ""ChunkId"",
                e.source_id as ""SourceId"",
                e.content as ""Content"",
                m.file_name as ""SourceName"",
                e.chunk_index as ""ChunkIndex"",
                e.page_number as ""PageNumber"",
                0.0 as ""Score""
            FROM embeddings e
            LEFT JOIN LATERAL (
                SELECT file_name
                FROM file_metadata
                WHERE file_id = e.source_id
                ORDER BY uploaded_at DESC
                LIMIT 1
            ) m ON true
            WHERE e.source_id = @SourceId
            ORDER BY e.chunk_index ASC NULLS LAST, e.chunk_id
            LIMIT @Take";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<EmbeddingRow>(
                new CommandDefinition(sql, new { SourceId = sourceId, Take = take }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return Result<IReadOnlyList<SearchResult>>.Success(rows.Select(row => MapToSearchResult(row)).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyDictionary<Guid, int>>> GetChunkCountsAsync(
        IReadOnlyList<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        if (sourceIds is null || sourceIds.Count == 0)
        {
            return Result<IReadOnlyDictionary<Guid, int>>.Success(new Dictionary<Guid, int>());
        }

        const string sql = @"
            SELECT e.source_id as ""SourceId"", COUNT(*) as ""Count""
            FROM embeddings e
            WHERE e.source_id = ANY(@SourceIds)
            GROUP BY e.source_id";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<ChunkCountRow>(
                new CommandDefinition(sql, new { SourceIds = sourceIds }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return Result<IReadOnlyDictionary<Guid, int>>.Success(rows.ToDictionary(row => row.SourceId, row => row.Count));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyDictionary<Guid, int>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM embeddings WHERE source_id = @SourceId";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { SourceId = sourceId }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{DeleteFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> ReplaceSourceAsync(
        Guid sourceId,
        IEnumerable<(Guid ChunkId, ReadOnlyMemory<float> Vector, Chunk Chunk)> items,
        CancellationToken cancellationToken = default)
    {
        var itemList = items?.ToList() ?? throw new ArgumentNullException(nameof(items));
        if (itemList.Count == 0)
        {
            // Nothing to write — clear the source's rows so a re-ingest of an empty source leaves no stale embeddings.
            return await DeleteBySourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Delete old rows and insert the new batch in ONE transaction so an interruption
            // leaves either the old or the new embeddings, never zero (write-new-then-swap).
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM embeddings WHERE source_id = @SourceId",
                    new { SourceId = sourceId },
                    transaction: transaction,
                    commandTimeout: _commandTimeoutSeconds,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            const string sql = @"
                INSERT INTO embeddings (chunk_id, source_id, content, embedding, page_number)
                VALUES (@ChunkId, @SourceId, @Content, @Embedding::vector, @PageNumber)
                ON CONFLICT (chunk_id)
                DO UPDATE SET
                    source_id = EXCLUDED.source_id,
                    content = EXCLUDED.content,
                    embedding = EXCLUDED.embedding,
                    page_number = EXCLUDED.page_number";

            foreach (var (chunkId, vector, chunk) in itemList)
            {
                var parameters = new
                {
                    ChunkId = chunkId,
                    chunk.SourceId,
                    chunk.Content,
                    chunk.PageNumber,
                    Embedding = VectorToString(vector)
                };

                await connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, transaction: transaction, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure($"{StoreFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> SearchBySourceAsync(
        ReadOnlyMemory<float> vector,
        int topK,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                e.chunk_id as ""ChunkId"",
                e.source_id as ""SourceId"",
                e.content as ""Content"",
                m.file_name as ""SourceName"",
                e.chunk_index as ""ChunkIndex"",
                e.page_number as ""PageNumber"",
                1 - (e.embedding <=> @QueryEmbedding::vector) as ""Score""
            FROM embeddings e
            LEFT JOIN LATERAL (
                SELECT file_name
                FROM file_metadata
                WHERE file_id = e.source_id
                ORDER BY uploaded_at DESC
                LIMIT 1
            ) m ON true
            WHERE e.source_id = @SourceId
            ORDER BY e.embedding <=> @QueryEmbedding::vector
            LIMIT @TopK";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<EmbeddingRow>(
                new CommandDefinition(sql, new
                {
                    QueryEmbedding = VectorToString(vector),
                    TopK = topK,
                    SourceId = sourceId
                }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return Result<IReadOnlyList<SearchResult>>.Success(rows.Select(row => MapToSearchResult(row)).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> SearchBySourcesAsync(
        ReadOnlyMemory<float> vector,
        int topK,
        IReadOnlyList<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT
                e.chunk_id as ""ChunkId"",
                e.source_id as ""SourceId"",
                e.content as ""Content"",
                m.file_name as ""SourceName"",
                e.chunk_index as ""ChunkIndex"",
                e.page_number as ""PageNumber"",
                1 - (e.embedding <=> @QueryEmbedding::vector) as ""Score""
            FROM embeddings e
            LEFT JOIN LATERAL (
                SELECT file_name
                FROM file_metadata
                WHERE file_id = e.source_id
                ORDER BY uploaded_at DESC
                LIMIT 1
            ) m ON true
            WHERE e.source_id = ANY(@SourceIds)
            ORDER BY e.embedding <=> @QueryEmbedding::vector
            LIMIT @TopK";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<EmbeddingRow>(
                new CommandDefinition(sql, new
                {
                    QueryEmbedding = VectorToString(vector),
                    TopK = topK,
                    SourceIds = sourceIds.ToArray()
                }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return Result<IReadOnlyList<SearchResult>>.Success(rows.Select(row => MapToSearchResult(row)).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    private static string VectorToString(ReadOnlyMemory<float> vector)
    {
        return $"[{string.Join(",", vector.ToArray())}]";
    }

    private static SearchResult MapToSearchResult(EmbeddingRow row, string? retrievalStrategy = null)
    {
        var citations = string.IsNullOrWhiteSpace(row.SourceName)
            ? Array.Empty<string>()
            : new[] { row.SourceName };

        return new SearchResult(
            new Chunk(row.ChunkId, row.SourceId, row.Content, row.ChunkIndex, row.PageNumber),
            (float)row.Score,
            citations,
            retrievalStrategy: retrievalStrategy ?? "semantic");
    }

    private record EmbeddingRow
    {
        public Guid ChunkId { get; set; }
        public Guid SourceId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? SourceName { get; set; }
        public double Score { get; set; }
        public int ChunkIndex { get; set; }
        public int? PageNumber { get; set; }
    }

    private record ChunkCountRow
    {
        public Guid SourceId { get; set; }
        public int Count { get; set; }
    }
}


