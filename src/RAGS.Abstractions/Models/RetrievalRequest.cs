namespace Aletheia.RAGS.Abstractions.Models;

public sealed class RetrievalRequest
{
    public RetrievalRequest(string query, int topK = 5, Guid? sourceId = null, IReadOnlyList<Guid>? sourceIds = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "TopK must be greater than zero.");
        }

        Query = query;
        TopK = topK;
        SourceId = sourceId;
        SourceIds = sourceIds;
    }

    public string Query { get; }

    public int TopK { get; }

    /// <summary>Single-source scope (Sprint 51). Ignored when <see cref="SourceIds"/> is set.</summary>
    public Guid? SourceId { get; }

    /// <summary>Knowledge-theme source set (Sprint 58). When non-null, retrieval is restricted to these sources (an empty set means no sources match).</summary>
    public IReadOnlyList<Guid>? SourceIds { get; }
}