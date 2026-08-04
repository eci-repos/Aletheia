using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Generates per-document "briefs": nature/purpose first, then the canonical template's
/// ordered sections, grounded and cited, in plain language for end users.
/// </summary>
public interface IDocumentBriefService
{
    /// <summary>Generates (or regenerates) the brief for a single registered document.</summary>
    Task<Result<WikiPage>> RegenerateAsync(
        Guid sourceId,
        string sourceName,
        CancellationToken cancellationToken = default);

    /// <summary>Generates briefs for every registered document that matches a canonical template.</summary>
    Task<Result<DocumentBriefRegenerationResult>> RegenerateAllAsync(
        Action<DocumentBriefProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
