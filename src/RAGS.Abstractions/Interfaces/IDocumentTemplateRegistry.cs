using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IDocumentTemplateRegistry
{
    /// <summary>Returns the ordered template sections for a document, if a template matches its file name.</summary>
    IReadOnlyList<DocumentTemplateSection>? TryGetSections(string fileName);

    /// <summary>Returns the canonical template name for a document, or null when no template matches.</summary>
    string? TryGetCanonicalName(string fileName);

    /// <summary>Returns the knowledge theme of the canonical template matching the document, or null when no template matches.</summary>
    string? TryGetTheme(string fileName);

    /// <summary>Returns the distinct knowledge themes declared by the canonical templates, in declaration order.</summary>
    IReadOnlyList<string> ListThemes();
}