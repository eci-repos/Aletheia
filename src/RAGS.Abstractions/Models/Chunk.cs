namespace Aletheia.RAGS.Abstractions.Models;

public sealed class Chunk
{
    public Chunk(Guid id, Guid sourceId, string content, int index, int? pageNumber = null, int? offsetInPage = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Chunk ID is required.", nameof(id));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be zero or greater.");
        }

        Id = id;
        SourceId = sourceId;
        Content = content;
        Index = index;
        PageNumber = pageNumber;
        OffsetInPage = offsetInPage;
    }

    public Guid Id { get; }

    public Guid SourceId { get; }

    public string Content { get; }

    public int Index { get; }

    /// <summary>
    /// 1-based page number the chunk starts on, when the extractor reported page boundaries
    /// (PDF and other page-aware types). Null for pre-Sprint-67 rows and non-page-aware text.
    /// </summary>
    public int? PageNumber { get; }

    /// <summary>
    /// Best-effort character offset of the chunk's start within its page. Null when unknown.
    /// </summary>
    public int? OffsetInPage { get; }
}
