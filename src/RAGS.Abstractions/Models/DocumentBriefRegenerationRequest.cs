namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>Request to (re)generate document briefs. When SourceId is omitted, all registered documents are regenerated.</summary>
public sealed class DocumentBriefRegenerationRequest
{
    public Guid? SourceId { get; init; }

    public string? SourceName { get; init; }
}
