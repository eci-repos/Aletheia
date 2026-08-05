using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphProvider
{
    Task<Result<GraphNode?>> GetNodeAsync(string id, CancellationToken cancellationToken = default);

    Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default);

    Task<Result> UpdateNodeAsync(GraphNode node, CancellationToken cancellationToken = default);

    Task<Result> DeleteNodeAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<GraphEdge?>> GetRelationshipAsync(string id, CancellationToken cancellationToken = default);

    Task<Result> CreateRelationshipAsync(GraphEdge edge, CancellationToken cancellationToken = default);

    Task<Result> DeleteRelationshipAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string label, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string type, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> GetSubgraphAsync(string nodeId, int depth, CancellationToken cancellationToken = default);

    Task<Result<bool>> GraphExistsAsync(CancellationToken cancellationToken = default);

    Task<Result> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes all nodes and relationships attributed to the given source (nodes whose sourceId property matches), including source/chunk/entity nodes.</summary>
    Task<Result> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure("DeleteSourceAsync is not supported by this provider."));
    }
}
