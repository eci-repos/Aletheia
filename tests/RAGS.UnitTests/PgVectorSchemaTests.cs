using Aletheia.RAGS.Infrastructure.PgVector.Schema;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;

namespace RAGS.UnitTests;

public class PgVectorSchemaTests
{
    [Fact]
    public void BuildSqlScript_defaults_to_hnsw_index_sql()
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=dummy");
        var schema = new PgVectorSchema(factory, 128);

        var sql = schema.BuildSqlScript();

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS vector", sql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS embeddings", sql);
        Assert.Contains("chunk_id UUID PRIMARY KEY", sql);
        Assert.Contains("embedding vector(128)", sql);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_embeddings_source_id", sql);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_hnsw", sql);
        Assert.Contains("USING hnsw", sql);
        Assert.Contains("vector_l2_ops", sql);
        Assert.Contains("m = 16", sql);
        Assert.Contains("ef_construction = 64", sql);
    }

    [Theory]
    [InlineData("ivfflat")]
    [InlineData("IVFFLAT")]
    [InlineData("Ivfflat")]
    public void BuildSqlScript_uses_ivfflat_when_configured(string indexType)
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=dummy");
        var schema = new PgVectorSchema(factory, 256, indexType);

        var sql = schema.BuildSqlScript();

        Assert.Contains("embedding vector(256)", sql);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_embeddings_embedding_ivfflat", sql);
        Assert.Contains("USING ivfflat", sql);
        Assert.Contains("lists = 100", sql);
        Assert.DoesNotContain("idx_embeddings_embedding_hnsw", sql);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hnsw")]
    public void BuildSqlScript_treats_empty_or_hnsw_as_hnsw(string? indexType)
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=dummy");
        var schema = new PgVectorSchema(factory, 64, indexType ?? string.Empty);

        var sql = schema.BuildSqlScript();

        Assert.Contains("idx_embeddings_embedding_hnsw", sql);
        Assert.Contains("USING hnsw", sql);
    }

    [Fact]
    public void Constructor_rejects_non_positive_vector_dimension()
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=dummy");
        Assert.Throws<ArgumentOutOfRangeException>(() => new PgVectorSchema(factory, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PgVectorSchema(factory, -1));
    }

    [Fact]
    public void Constructor_rejects_null_connection_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new PgVectorSchema(null!, 128));
    }
}
