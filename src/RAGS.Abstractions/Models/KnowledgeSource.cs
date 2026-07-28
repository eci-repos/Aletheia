namespace Aletheia.RAGS.Abstractions.Models;

public sealed class KnowledgeSource
{
    public KnowledgeSource(Guid sourceId, string sourceName, DateTimeOffset uploadedAt)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        SourceId = sourceId;
        SourceName = sourceName;
        UploadedAt = uploadedAt;
    }

    public Guid SourceId { get; }

    public string SourceName { get; }

    public DateTimeOffset UploadedAt { get; }
}
