using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Lightweight corpus index for LazyGraphRAG.
/// Stores text statistics, TF-IDF, BM25 metadata without LLM extraction during ingestion.
/// </summary>
public interface ICorpusDiscoveryIndex
{
    Task<Result> IndexAsync(string content, Guid sourceId, CancellationToken cancellationToken = default);

    IReadOnlyList<string> GetTerms(Guid sourceId);

    float GetTfIdf(string term, Guid sourceId);

    float GetBm25Score(string term, Guid sourceId);

    CorpusStatistics GetStatistics(Guid sourceId);

    IReadOnlyList<Guid> SearchCorpus(string query, int topK = 10);
}

public sealed class CorpusStatistics
{
    public int TotalTerms { get; set; }
    public int UniqueTerms { get; set; }
    public int DocumentLength { get; set; }
    public float AverageDocumentLength { get; set; }
}
