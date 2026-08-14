using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Orchestrates grounded fact extraction at ingestion: propose (LLM) → verify (fidelity gate) →
/// normalize (lexicon) → persist. Returns the verified facts; failures are best-effort and never
/// block ingestion.
/// </summary>
public interface IFactExtractionService
{
    Task<Result<IReadOnlyList<DocumentFact>>> ExtractAsync(
        Guid sourceId,
        string text,
        IReadOnlyList<TextPage>? pages,
        CancellationToken cancellationToken = default);
}
