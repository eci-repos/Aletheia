using Aletheia.RAGS.Infrastructure.PostgreSQL.CorpusIndex;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace RAGS.UnitTests.CorpusIndex;

public sealed class PostgreSqlCorpusIndexRepositoryTests
{
    [Fact]
    public async Task UpsertDocumentAsync_then_LoadAsync_round_trips_same_stats()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? "Host=localhost;Port=5432;Database=aletheia;Username=aletheia;Password=aletheia";

        PostgreSqlConnectionFactory factory;
        try
        {
            factory = new PostgreSqlConnectionFactory(connectionString);
            await using var probe = factory.CreateConnection();
            await probe.OpenAsync();
        }
        catch
        {
            return; // live PostgreSQL not available — skip
        }

        await EnsureSchemaAsync(factory);

        var repository = new PostgreSqlCorpusIndexRepository(factory);
        var sourceId = Guid.NewGuid();
        var termFrequency = new Dictionary<string, int>
        {
            ["alpha"] = 2,
            ["beta"] = 1
        };

        try
        {
            var upsert = await repository.UpsertDocumentAsync(sourceId, termFrequency, 5);
            Assert.True(upsert.IsSuccess, upsert.Error);

            var load = await repository.LoadAsync();
            Assert.True(load.IsSuccess, load.Error);

            var document = Assert.Single(load.Value!.Documents.Where(d => d.SourceId == sourceId));
            Assert.Equal(5, document.DocumentLength);
            Assert.Equal(2, document.TermFrequency["alpha"]);
            Assert.Equal(1, document.TermFrequency["beta"]);
        }
        finally
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "DELETE FROM lazygraphrag_corpus_terms WHERE source_id = @SourceId; DELETE FROM lazygraphrag_corpus_documents WHERE source_id = @SourceId;",
                new { SourceId = sourceId });
        }
    }

    private static async Task EnsureSchemaAsync(PostgreSqlConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS lazygraphrag_corpus_documents (
    source_id UUID PRIMARY KEY,
    document_length INTEGER NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS lazygraphrag_corpus_terms (
    source_id UUID NOT NULL REFERENCES lazygraphrag_corpus_documents(source_id) ON DELETE CASCADE,
    term TEXT NOT NULL,
    frequency INTEGER NOT NULL,
    PRIMARY KEY (source_id, term)
);
CREATE INDEX IF NOT EXISTS idx_lazygraphrag_corpus_terms_term ON lazygraphrag_corpus_terms(term);");
    }
}
