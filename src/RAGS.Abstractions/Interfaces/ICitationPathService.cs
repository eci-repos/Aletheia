using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ICitationPathService
{
    Task<Result<IReadOnlyList<string>>> GetDocumentSourcesAsync(string resultId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<string>>> GetEntitySourcesAsync(string entityId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<string>>> GetRelationshipSourcesAsync(string relationshipId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphPath>>> GetGraphPathsAsync(string fromId, string toId, CancellationToken cancellationToken = default);
}
