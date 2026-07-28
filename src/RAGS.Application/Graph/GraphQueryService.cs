using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.Graph;

public sealed class GraphQueryService : IGraphQueryService
{
    private readonly IGraphProvider _provider;

    public GraphQueryService(IGraphProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string query, CancellationToken cancellationToken = default)
        => _provider.SearchNodesAsync(query, cancellationToken);

    public Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string query, CancellationToken cancellationToken = default)
        => _provider.SearchRelationshipsAsync(query, cancellationToken);

    public async Task<Result<IReadOnlyList<GraphNode>>> TraverseAsync(string startNodeId, int depth, CancellationToken cancellationToken = default)
    {
        var result = await _provider.GetSubgraphAsync(startNodeId, depth, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string fromNodeId, string toNodeId, CancellationToken cancellationToken = default)
        => _provider.FindPathsAsync(fromNodeId, toNodeId, cancellationToken);

    public Task<Result<IReadOnlyList<GraphNode>>> GetConnectedEntitiesAsync(string nodeId, CancellationToken cancellationToken = default)
        => _provider.GetNeighborsAsync(nodeId, cancellationToken);

    public Task<Result<IReadOnlyList<GraphNode>>> GetNeighborhoodAsync(string nodeId, int depth, CancellationToken cancellationToken = default)
        => _provider.GetSubgraphAsync(nodeId, depth, cancellationToken);

    public async Task<Result<IReadOnlyList<GraphNode>>> GetEntityGraphAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var startNode = await _provider.GetNodeAsync(entityId, cancellationToken).ConfigureAwait(false);
        if (startNode.IsFailure || startNode.Value is null)
            return Result<IReadOnlyList<GraphNode>>.Failure(startNode.Error ?? "Entity not found.");

        var neighbors = await _provider.GetNeighborsAsync(entityId, cancellationToken).ConfigureAwait(false);
        if (neighbors.IsFailure)
            return neighbors;

        var all = new List<GraphNode> { startNode.Value };
        if (neighbors.Value is not null)
            all.AddRange(neighbors.Value);

        return Result<IReadOnlyList<GraphNode>>.Success(all);
    }
}
