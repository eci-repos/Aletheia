namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class PgVectorOptions
{
    public const string SectionName = "PgVector";

    public int CommandTimeoutSeconds { get; set; } = 30;

    public string VectorIndexType { get; set; } = "hnsw";
}
