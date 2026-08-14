using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// The semantic (LLM) recognition layer of grounded fact extraction. Proposes candidate facts
/// with the exact source span each was read from. Proposals are never stored directly — the
/// fidelity gate (<c>FactVerifier</c>) confirms the span exists and the value parses first.
/// </summary>
public interface IFactProposer
{
    Task<Result<IReadOnlyList<ProposedFact>>> ProposeAsync(
        string text,
        IReadOnlyList<LexiconConcept> concepts,
        CancellationToken cancellationToken = default);
}
