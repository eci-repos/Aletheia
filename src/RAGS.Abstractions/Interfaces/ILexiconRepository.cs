using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Persistence for the normalized lexicon: canonical concepts + aliases, verified document facts,
/// and the governance loop's unmapped terms. Implemented by <c>PostgreSqlLexiconRepository</c>.
/// </summary>
public interface ILexiconRepository
{
    Task<Result<IReadOnlyList<LexiconConcept>>> GetAllConceptsAsync(CancellationToken cancellationToken = default);

    Task<Result> UpsertConceptAsync(LexiconConcept concept, CancellationToken cancellationToken = default);

    Task<Result> SaveFactsAsync(Guid sourceId, IReadOnlyList<DocumentFact> facts, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DocumentFact>>> GetFactsAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<Result> RecordUnmappedTermAsync(string term, Guid sourceId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UnmappedTerm>>> GetUnmappedTermsAsync(CancellationToken cancellationToken = default);
}
