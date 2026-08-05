using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PgVector.Schema;

public sealed class PgVectorSchema
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly int _vectorDimension;
    private readonly string _indexType;

    public PgVectorSchema(PostgreSqlConnectionFactory connectionFactory, int vectorDimension, string indexType = "hnsw")
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _vectorDimension = vectorDimension > 0 ? vectorDimension : throw new ArgumentOutOfRangeException(nameof(vectorDimension));
        _indexType = string.IsNullOrWhiteSpace(indexType) ? "hnsw" : indexType.ToLowerInvariant();
    }

    public string BuildSqlScript()
    {
        var indexSql = _indexType switch
        {
            "ivfflat" =>
                "CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_ivfflat ON embeddings USING ivfflat (embedding vector_l2_ops) WITH (lists = 100);",
            _ =>
                "CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_hnsw ON embeddings USING hnsw (embedding vector_l2_ops) WITH (m = 16, ef_construction = 64);"
        };

        return $@"
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS embeddings (
    chunk_id UUID PRIMARY KEY,
    source_id UUID NOT NULL,
    content TEXT NOT NULL,
    embedding vector({_vectorDimension}),
    chunk_index INT
);

ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS chunk_index INT;

CREATE INDEX IF NOT EXISTS idx_embeddings_source_id ON embeddings(source_id);

{indexSql}";
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition("CREATE EXTENSION IF NOT EXISTS vector;", transaction: transaction, commandTimeout: 60))
                .ConfigureAwait(false);

            await connection.ExecuteAsync(
                new CommandDefinition($@"
                CREATE TABLE IF NOT EXISTS embeddings (
                    chunk_id UUID PRIMARY KEY,
                    source_id UUID NOT NULL,
                    content TEXT NOT NULL,
                    embedding vector({_vectorDimension}),
                    chunk_index INT
                );

                ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS chunk_index INT;",
                transaction: transaction,
                commandTimeout: 60))
                .ConfigureAwait(false);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "CREATE INDEX IF NOT EXISTS idx_embeddings_source_id ON embeddings(source_id);",
                    transaction: transaction,
                    commandTimeout: 60))
                .ConfigureAwait(false);

            var indexSql = _indexType switch
            {
                "ivfflat" =>
                    "CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_ivfflat ON embeddings USING ivfflat (embedding vector_l2_ops) WITH (lists = 100);",
                _ =>
                    "CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_hnsw ON embeddings USING hnsw (embedding vector_l2_ops) WITH (m = 16, ef_construction = 64);"
            };

            await connection.ExecuteAsync(
                new CommandDefinition(indexSql, transaction: transaction, commandTimeout: 120))
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsHnswNotSupported(ex))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // Fall back to IVFFlat if HNSW is not supported by the installed pgvector version.
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_ivfflat ON embeddings USING ivfflat (embedding vector_l2_ops) WITH (lists = 100);",
                    commandTimeout: 120))
                .ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsHnswNotSupported(PostgresException ex)
    {
        return ex.Message.Contains("hnsw", StringComparison.OrdinalIgnoreCase)
            || ex.SqlState == "42601";
    }
}
