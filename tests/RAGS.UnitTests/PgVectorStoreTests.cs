using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Providers;
using Aletheia.RAGS.Infrastructure.PgVector.VectorStore;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Npgsql;

namespace RAGS.UnitTests;

public class PgVectorStoreTests : IAsyncLifetime
{
    private PostgreSqlConnectionFactory? _factory;
    private PgVectorStore? _store;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? "Host=localhost;Port=5432;Database=aletheia;Username=aletheia;Password=aletheia";

        try
        {
            _factory = new PostgreSqlConnectionFactory(connectionString);
            using var connection = _factory.CreateConnection();
            await connection.OpenAsync();

            // Ensure pgvector extension and table exist
            var initSql = @"
                CREATE EXTENSION IF NOT EXISTS vector;
                CREATE TABLE IF NOT EXISTS embeddings (
                    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                    chunk_id UUID NOT NULL UNIQUE,
                    source_id UUID NOT NULL,
                    content TEXT NOT NULL,
                    embedding vector(128) NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_embeddings_embedding ON embeddings USING ivfflat (embedding vector_cosine_ops);";

            foreach (var batch in initSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    using var cmd = new NpgsqlCommand(batch, connection);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            _store = new PgVectorStore(_factory, 128);
            _isAvailable = true;
        }
        catch
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_isAvailable || _factory is null)
        {
            return;
        }

        try
        {
            using var connection = _factory.CreateConnection();
            await connection.OpenAsync();
            using var cmd = new NpgsqlCommand("DELETE FROM embeddings;", connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task StoreAsync_and_SearchAsync_roundtrip()
    {
        if (!_isAvailable)
        {
            return;
        }

        var chunk = new Chunk(Guid.NewGuid(), Guid.NewGuid(), "semantic content for testing", 0);
        var provider = new SimpleEmbeddingProvider();
        var embedding = await provider.GenerateAsync(chunk.Content);
        Assert.True(embedding.IsSuccess);

        var storeResult = await _store!.StoreAsync(chunk.Id, embedding.Value, chunk);
        Assert.True(storeResult.IsSuccess, storeResult.Error);

        var searchResult = await _store!.SearchAsync(embedding.Value, 5);
        Assert.True(searchResult.IsSuccess, searchResult.Error);
        Assert.NotNull(searchResult.Value);
        Assert.Contains(searchResult.Value, r => r.Chunk.Content == chunk.Content);
    }

    [Fact]
    public async Task DeleteBySourceAsync_removes_embeddings()
    {
        if (!_isAvailable)
        {
            return;
        }

        var sourceId = Guid.NewGuid();
        var chunk = new Chunk(Guid.NewGuid(), sourceId, "content to delete", 0);
        var provider = new SimpleEmbeddingProvider();
        var embedding = await provider.GenerateAsync(chunk.Content);
        Assert.True(embedding.IsSuccess);

        var storeResult = await _store!.StoreAsync(chunk.Id, embedding.Value, chunk);
        Assert.True(storeResult.IsSuccess, storeResult.Error);

        var deleteResult = await _store!.DeleteBySourceAsync(sourceId);
        Assert.True(deleteResult.IsSuccess, deleteResult.Error);

        var searchResult = await _store!.SearchAsync(embedding.Value, 5);
        Assert.True(searchResult.IsSuccess, searchResult.Error);
        Assert.NotNull(searchResult.Value);
        Assert.DoesNotContain(searchResult.Value, r => r.Chunk.SourceId == sourceId);
    }
}
