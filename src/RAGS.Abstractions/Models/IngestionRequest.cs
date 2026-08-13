namespace Aletheia.RAGS.Abstractions.Models;

public sealed class IngestionRequest
{
    public IngestionRequest(Guid sourceId, string content, string? sourceName = null, IReadOnlyList<TextPage>? pages = null)
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
        Pages = pages;
    }

    public Guid SourceId { get; }

    public string Content { get; }

    public string? SourceName { get; }

    /// <summary>
    /// Page boundaries in <see cref="Content"/> when the extractor reported them (PDF and other
    /// page-aware types). Null for plain text and pre-Sprint-67 rows.
    /// </summary>
    public IReadOnlyList<TextPage>? Pages { get; }
}
