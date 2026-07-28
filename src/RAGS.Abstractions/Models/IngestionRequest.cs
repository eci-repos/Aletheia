namespace Aletheia.RAGS.Abstractions.Models;

public sealed class IngestionRequest
{
    public IngestionRequest(Guid sourceId, string content, string? sourceName = null)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        SourceId = sourceId;
        Content = content;
        SourceName = sourceName;
    }

    public Guid SourceId { get; }

    public string Content { get; }

    public string? SourceName { get; }
}
