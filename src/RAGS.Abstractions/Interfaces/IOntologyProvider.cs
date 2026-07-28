using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IOntologyProvider
{
    Task<Result<IReadOnlyCollection<string>>> GetEntitiesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<string, string>>> GetRelationshipsAsync(string entity, CancellationToken cancellationToken = default);
}
