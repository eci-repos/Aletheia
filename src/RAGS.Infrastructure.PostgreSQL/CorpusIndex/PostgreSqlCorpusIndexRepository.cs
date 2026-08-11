using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.CorpusIndex;

/// <summary>
/// PostgreSQL persistence for the LazyGraphRAG corpus index. Two tables:
/// <c>lazygraphrag_corpus_documents</c> (per-source length) and <c>lazygraphrag_corpus_terms</c>
/// (per-source term frequency). Document count and average document length are derived from the
/// documents table, so no separate statistics row is needed.
/// </summary>
public sealed class PostgreSqlCorpusIndexRepository : ICorpusIndexRepository
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlCorpusIndexRepository(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result> UpsertDocumentAsync(
        Guid sourceId,
        IReadOnlyDictionary<string, int> termFrequency,
        int documentLength,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            const string upsertDocumentSql = @"
INSERT INTO lazygraphrag_corpus_documents (source_id, document_length, updated_at)
VALUES (@SourceId, @DocumentLength, NOW())
ON CONFLICT (source_id) DO UPDATE SET
    document_length = EXCLUDED.document_length,
    updated_at = NOW();";
            await connection.ExecuteAsync(
                upsertDocumentSql,
                new { SourceId = sourceId, DocumentLength = documentLength },
                transaction).ConfigureAwait(false);

            const string deleteTermsSql = "DELETE FROM lazygraphrag_corpus_terms WHERE source_id = @SourceId;";
            await connection.ExecuteAsync(deleteTermsSql, new { SourceId = sourceId }, transaction).ConfigureAwait(false);

            if (termFrequency.Count > 0)
            {
                const string insertTermsSql = @"
INSERT INTO lazygraphrag_corpus_terms (source_id, term, frequency)
VALUES (@SourceId, @Term, @Frequency);";
                await connection.ExecuteAsync(
                    insertTermsSql,
                    termFrequency.Select(kv => new { SourceId = sourceId, Term = kv.Key, Frequency = kv.Value }),
                    transaction).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"LazyGraphRAG corpus index persistence failed. {ex.Message}");
        }
    }

    public async Task<Result<CorpusIndexSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
SELECT
    d.source_id AS SourceId,
    d.document_length AS DocumentLength,
    t.term AS Term,
    t.frequency AS Frequency
FROM lazygraphrag_corpus_documents d
LEFT JOIN lazygraphrag_corpus_terms t ON t.source_id = d.source_id
ORDER BY d.source_id;";
            var rows = await connection.QueryAsync<CorpusTermRow>(sql).ConfigureAwait(false);

            var documents = new Dictionary<Guid, CorpusDocumentIndex>();
            foreach (var row in rows)
            {
                if (!documents.TryGetValue(row.SourceId, out var document))
                {
                    var termFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    document = new CorpusDocumentIndex
                    {
                        SourceId = row.SourceId,
                        DocumentLength = row.DocumentLength,
                        TermFrequency = termFrequency
                    };
                    documents[row.SourceId] = document;
                }

                if (!string.IsNullOrEmpty(row.Term))
                {
                    ((Dictionary<string, int>)document.TermFrequency)[row.Term] = row.Frequency;
                }
            }

            return Result<CorpusIndexSnapshot>.Success(new CorpusIndexSnapshot
            {
                Documents = documents.Values.ToList()
            });
        }
        catch (Exception ex)
        {
            return Result<CorpusIndexSnapshot>.Failure($"LazyGraphRAG corpus index load failed. {ex.Message}");
        }
    }

    private sealed class CorpusTermRow
    {
        public Guid SourceId { get; init; }
        public int DocumentLength { get; init; }
        public string? Term { get; init; }
        public int Frequency { get; init; }
    }
}
