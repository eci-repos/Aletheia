namespace Aletheia.RAGS.Abstractions.Models;

public sealed class RetrievalRequest
{
    public RetrievalRequest(string query, int topK = 5, Guid? sourceId = null)
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
    }

    public string Query { get; }

    public int TopK { get; }

    public Guid? SourceId { get; }
}
