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

    Task<Result> DeleteConceptAsync(string key, CancellationToken cancellationToken = default);

    Task<Result> SaveFactsAsync(Guid sourceId, IReadOnlyList<DocumentFact> facts, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DocumentFact>>> GetFactsAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>All verified facts across sources — the end-user glossary surface.</summary>
    Task<Result<IReadOnlyList<DocumentFact>>> GetAllFactsAsync(CancellationToken cancellationToken = default);

    Task<Result> RecordUnmappedTermAsync(string term, Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Pending (unreviewed) unmapped terms for the admin governance surface.</summary>
    Task<Result<IReadOnlyList<UnmappedTerm>>> GetUnmappedTermsAsync(CancellationToken cancellationToken = default);

    /// <summary>Mark an unmapped term resolved (confirmed as an alias or dismissed) so it leaves the review queue.</summary>
    Task<Result> ResolveUnmappedTermAsync(string term, Guid sourceId, CancellationToken cancellationToken = default);
}
