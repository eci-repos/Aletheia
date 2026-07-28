namespace Aletheia.RAGS.Abstractions.Models;

public sealed class Chunk
{
    public Chunk(Guid id, Guid sourceId, string content, int index)
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
    }

    public Guid Id { get; }

    public Guid SourceId { get; }

    public string Content { get; }

    public int Index { get; }
}
