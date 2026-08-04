namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// Everything the brief generator needs: the document identity, its canonical template
/// sections (ordered), and the retrieved per-section evidence (opening chunks first).
/// </summary>
public sealed class DocumentBriefRequest
{
    public Guid SourceId { get; init; }

    public string SourceName { get; init; } = string.Empty;

    public string CanonicalName { get; init; } = string.Empty;

    public IReadOnlyList<DocumentTemplateSection> Sections { get; init; } = Array.Empty<DocumentTemplateSection>();

    public IReadOnlyList<SearchResult> Evidence { get; init; } = Array.Empty<SearchResult>();
}
