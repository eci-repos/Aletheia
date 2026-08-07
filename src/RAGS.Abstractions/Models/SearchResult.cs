namespace Aletheia.RAGS.Abstractions.Models;

public sealed class SearchResult
{
    public SearchResult(
        Chunk chunk,
        float score,
        IReadOnlyList<string>? citations = null,
        IReadOnlyDictionary<string, float>? rankingSignals = null,
        string retrievalStrategy = "semantic",
        int rank = 0)
    {
        Chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
        Score = score;
        Citations = citations ?? Array.Empty<string>();
        RankingSignals = rankingSignals ?? new Dictionary<string, float>();
        RetrievalStrategy = retrievalStrategy;
        Rank = rank;
    }

    public Chunk Chunk { get; }

    public float Score { get; }

    public IReadOnlyList<string> Citations { get; }

    public IReadOnlyDictionary<string, float> RankingSignals { get; }

    public string RetrievalStrategy { get; }

    public int Rank { get; }

    /// <summary>
    /// Per-query diagnostic trace (GraphRAG / LazyGraphRAG). Null for plain semantic retrieval.
    /// </summary>
    public RetrievalTrace? Trace { get; set; }
}
