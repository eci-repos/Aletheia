using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Produces the brief text for a document from its retrieved evidence and template outline.
/// </summary>
public interface IDocumentBriefGenerator
{
    Task<Result<string>> GenerateAsync(
        DocumentBriefRequest request,
        CancellationToken cancellationToken = default);
}
