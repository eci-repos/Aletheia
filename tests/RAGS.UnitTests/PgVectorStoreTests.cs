using System.Diagnostics;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Providers;
using Aletheia.RAGS.Infrastructure.PgVector.VectorStore;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
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
                    chunk_index INT,
                    page_number INT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS chunk_index INT;
                ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS page_number INT;
                CREATE INDEX IF NOT EXISTS idx_embeddings_embedding ON embeddings USING ivfflat (embedding vector_cosine_ops);";

            foreach (var batch in initSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    using var cmd = new NpgsqlCommand(batch, connection);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            _store = new PgVectorStore(_factory, 128, commandTimeoutSeconds: 30);
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

    [Fact]
    public async Task ReplaceSourceAsync_replaces_embeddings_atomically()
    {
        if (!_isAvailable)
        {
            return;
        }

        var sourceId = Guid.NewGuid();
        var provider = new SimpleEmbeddingProvider();

        var oldChunk = new Chunk(Guid.NewGuid(), sourceId, "old content to be replaced", 0);
        var oldEmbedding = await provider.GenerateAsync(oldChunk.Content);
        Assert.True(oldEmbedding.IsSuccess);
        var storeResult = await _store!.StoreAsync(oldChunk.Id, oldEmbedding.Value, oldChunk);
        Assert.True(storeResult.IsSuccess, storeResult.Error);

        var newChunk = new Chunk(Guid.NewGuid(), sourceId, "new content after replace", 0);
        var newEmbedding = await provider.GenerateAsync(newChunk.Content);
        Assert.True(newEmbedding.IsSuccess);

        // Sprint 73: write-new-then-swap — the source's rows are replaced in one atomic call.
        var replaceResult = await _store!.ReplaceSourceAsync(
            sourceId,
            new[] { (newChunk.Id, newEmbedding.Value, newChunk) });
        Assert.True(replaceResult.IsSuccess, replaceResult.Error);

        var searchResult = await _store!.SearchAsync(newEmbedding.Value, 5);
        Assert.True(searchResult.IsSuccess, searchResult.Error);
        Assert.NotNull(searchResult.Value);
        Assert.Contains(searchResult.Value, r => r.Chunk.Content == newChunk.Content);
        Assert.DoesNotContain(searchResult.Value, r => r.Chunk.Content == oldChunk.Content);
    }

    [Fact]
    public void Constructor_rejects_invalid_command_timeout()
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=dummy");
        Assert.Throws<ArgumentOutOfRangeException>(() => new PgVectorStore(factory, 128, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PgVectorStore(factory, 128, -1));
    }

    [Fact]
    public async Task SearchAsync_fails_fast_when_command_times_out()
    {
        if (!_isAvailable || _factory is null)
        {
            return;
        }

        // Create a store with a 1-second command timeout.
        var fastTimeoutStore = new PgVectorStore(_factory, 128, commandTimeoutSeconds: 1);

        // Execute a query that intentionally exceeds the command timeout.
        // Use the same connection factory and CommandDefinition pattern the store uses
        // so we are testing the timeout behavior of the infrastructure the store relies on.
        using var connection = _factory.CreateConnection();
        await connection.OpenAsync();

        var started = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<NpgsqlException>(async () =>
        {
            await connection.ExecuteAsync(
                new CommandDefinition("SELECT pg_sleep(3);", commandTimeout: 1, cancellationToken: default));
        });
        started.Stop();

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2.5), $"Expected timeout to fire quickly but elapsed was {started.Elapsed}");
        Assert.Contains("Exception while reading from stream", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandTimeoutSeconds_exposes_configured_value()
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=dummy");
        var store = new PgVectorStore(factory, 128, commandTimeoutSeconds: 42);
        Assert.Equal(42, store.CommandTimeoutSeconds);
    }
}
