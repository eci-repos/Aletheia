using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;

namespace Aletheia.KnowledgeGraph.Abstractions.Interfaces;

public interface IGraphService
{
    Task<Result> ClearAsync(CancellationToken cancellationToken = default);

    Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default);

    Task<Result> CreateEdgeAsync(GraphEdge edge, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphPath>>> FindShortestPathAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default);
}
