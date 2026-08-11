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

    /// <summary>
    /// Batch node creation (Sprint 63). Providers that support it should send the nodes as a single
    /// UNWIND statement; the default implementation falls back to per-node calls so existing
    /// providers and test fakes keep working unchanged.
    /// </summary>
    Task<Result> CreateNodesAsync(IReadOnlyList<GraphNode> nodes, CancellationToken cancellationToken = default)
    {
        return CreateNodesAsyncCore(nodes, cancellationToken);
    }

    /// <summary>
    /// Batch relationship creation (Sprint 63). Providers that support it should send the edges as
    /// UNWIND statements; the default implementation falls back to per-edge calls.
    /// </summary>
    Task<Result> CreateRelationshipsAsync(IReadOnlyList<GraphEdge> edges, CancellationToken cancellationToken = default)
    {
        return CreateRelationshipsAsyncCore(edges, cancellationToken);
    }

    /// <summary>
    /// Batch node update (Sprint 63). Providers that support it should send the nodes as a single
    /// UNWIND statement; the default implementation falls back to per-node calls.
    /// </summary>
    Task<Result> UpdateNodesAsync(IReadOnlyList<GraphNode> nodes, CancellationToken cancellationToken = default)
    {
        return UpdateNodesAsyncCore(nodes, cancellationToken);
    }

    private async Task<Result> CreateNodesAsyncCore(IReadOnlyList<GraphNode> nodes, CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            var result = await CreateNodeAsync(node, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Success();
    }

    private async Task<Result> CreateRelationshipsAsyncCore(IReadOnlyList<GraphEdge> edges, CancellationToken cancellationToken)
    {
        foreach (var edge in edges)
        {
            var result = await CreateRelationshipAsync(edge, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Success();
    }

    private async Task<Result> UpdateNodesAsyncCore(IReadOnlyList<GraphNode> nodes, CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            var result = await UpdateNodeAsync(node, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Success();
    }
}
