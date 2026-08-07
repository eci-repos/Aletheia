namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// Per-query diagnostic trace for a GraphRAG / LazyGraphRAG retrieval.
/// Exposes which fallback strategy produced the answer and how much work was done,
/// so operators and the Web UI can see the fired path without breaking the
/// existing <see cref="SearchResult"/> contract.
/// </summary>
public sealed class RetrievalTrace
{
    /// <summary>
    /// The fallback strategy that produced the answer (e.g. "lazy-graph", "graph-aware", "semantic").
    /// </summary>
    public string Strategy { get; set; } = "semantic";

    /// <summary>
    /// Number of LLM calls made during the retrieval.
    /// </summary>
    public int LlmCalls { get; set; }

    /// <summary>
    /// Total tokens consumed by LLM calls during the retrieval (0 when the provider
    /// does not report usage or the call site does not yet record it).
    /// </summary>
    public int TokensConsumed { get; set; }

    /// <summary>
    /// Number of graph nodes visited during traversal.
    /// </summary>
    public int NodesVisited { get; set; }

    /// <summary>
    /// Number of graph relationships traversed during traversal.
    /// </summary>
    public int RelationshipsTraversed { get; set; }

    /// <summary>
    /// Fraction of nodes retained after pruning (0..1); null when pruning was not applied.
    /// </summary>
    public double? PruningRatio { get; set; }

    /// <summary>
    /// Wall-clock time spent in the retrieval, in milliseconds.
    /// </summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Ordered list of the retrieval steps that ran (e.g. "corpus-search", "traversal", "ranking").
    /// </summary>
    public IReadOnlyList<string> Steps { get; set; } = Array.Empty<string>();
}
