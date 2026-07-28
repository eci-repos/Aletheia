using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Discovers relationships between entities at query time.
/// All intelligence work is deferred to retrieval.
/// </summary>
public interface ILazyRelationshipDiscoveryService
{
    Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAtQueryTimeAsync(
        string query,
        IReadOnlyList<ExtractedEntity> entities,
        CancellationToken cancellationToken = default);

    Task<Result> PersistAsync(
        IReadOnlyList<ExtractedRelationship> relationships,
        CancellationToken cancellationToken = default);
}
