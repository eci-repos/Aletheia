using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphQueryService
{
    Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> TraverseAsync(string startNodeId, int depth, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string fromNodeId, string toNodeId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetConnectedEntitiesAsync(string nodeId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetNeighborhoodAsync(string nodeId, int depth, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetEntityGraphAsync(string entityId, CancellationToken cancellationToken = default);
}
