using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Prunes low-relevance nodes and relationships from a subgraph 
/// before context generation to minimize token consumption.
/// </summary>
public interface ISubgraphPruningService
{
    Task<Result<IReadOnlyList<GraphNode>>> PruneNodesAsync(
        IReadOnlyList<GraphNode> nodes,
        string query,
        float relevanceThreshold = 0.3f,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphEdge>>> PruneRelationshipsAsync(
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<GraphNode> retainedNodes,
        CancellationToken cancellationToken = default);
}
