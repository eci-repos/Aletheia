using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ILazyEntityDiscoveryService
{
    Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAtQueryTimeAsync(string query, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ExtractedEntity>>> CreateIncrementalAsync(string text, CancellationToken cancellationToken = default);

    Task<Result> PersistAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default);
}
