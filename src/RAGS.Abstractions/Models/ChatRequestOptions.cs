namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatRequestOptions
{
    public string? OutputFormat { get; set; }

    public IReadOnlyList<SearchResult>? RetrievalResults { get; set; }

    public bool UseProvidedRetrievalOnly { get; set; }

    public string? ScopeInstruction { get; set; }

    /// <summary>Ordered template sections (heading + description) the answer should follow.</summary>
    public IReadOnlyList<DocumentTemplateSection>? SectionOutline { get; set; }
}
