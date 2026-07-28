using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Interfaces;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.Graph;

public sealed class GraphService : IGraphService
{
    private readonly IGraphProvider _provider;

    public GraphService(IGraphProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
        => _provider.ClearAsync(cancellationToken);

    public Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        => _provider.CreateNodeAsync(node, cancellationToken);

    public Task<Result> CreateEdgeAsync(GraphEdge edge, CancellationToken cancellationToken = default)
        => _provider.CreateRelationshipAsync(edge, cancellationToken);

    public Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default)
        => _provider.GetNodesAsync(cancellationToken);

    public Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default)
        => _provider.GetEdgesAsync(cancellationToken);

    public Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
        => _provider.GetNeighborsAsync(nodeId, cancellationToken);

    public Task<Result<IReadOnlyList<GraphPath>>> FindShortestPathAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default)
        => _provider.FindPathsAsync(startNodeId, endNodeId, cancellationToken);
}
