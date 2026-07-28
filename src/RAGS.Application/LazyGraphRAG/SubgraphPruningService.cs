using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

/// <summary>
/// Prunes low-relevance nodes and edges from a subgraph
/// to minimize token consumption during context generation.
/// </summary>
public sealed class SubgraphPruningService : ISubgraphPruningService
{
    public Task<Result<IReadOnlyList<GraphNode>>> PruneNodesAsync(
        IReadOnlyList<GraphNode> nodes,
        string query,
        float relevanceThreshold = 0.3f,
        CancellationToken cancellationToken = default)
    {
        if (!nodes.Any())
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));
        }

        var lowerQuery = query.ToLowerInvariant();
        var queryTerms = lowerQuery.Split(new[] { ' ', '.', ',', ';', ':', '!', '?', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();

        var scoredNodes = new List<(GraphNode Node, float Score)>();

        foreach (var node in nodes)
        {
            var score = ComputeRelevanceScore(node, queryTerms, lowerQuery);
            if (score >= relevanceThreshold)
            {
                scoredNodes.Add((node, score));
            }
        }

        var pruned = scoredNodes
            .OrderByDescending(s => s.Score)
            .Select(s => s.Node)
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(pruned));
    }

    public Task<Result<IReadOnlyList<GraphEdge>>> PruneRelationshipsAsync(
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<GraphNode> retainedNodes,
        CancellationToken cancellationToken = default)
    {
        var retainedIds = new HashSet<string>(retainedNodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);

        var pruned = edges
            .Where(e => retainedIds.Contains(e.SourceId) && retainedIds.Contains(e.TargetId))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(pruned));
    }

    private static float ComputeRelevanceScore(GraphNode node, HashSet<string> queryTerms, string lowerQuery)
    {
        var score = 0f;
        var label = node.Label.ToLowerInvariant();
        var type = node.Type.ToLowerInvariant();

        // Label exact match or containment
        if (label.Equals(lowerQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += 1.0f;
        }
        else if (lowerQuery.Contains(label) || label.Contains(lowerQuery))
        {
            score += 0.7f;
        }

        // Query term overlap
        foreach (var term in queryTerms)
        {
            if (label.Contains(term))
                score += 0.15f;
            if (type.Contains(term))
                score += 0.05f;
        }

        // Property overlap
        foreach (var prop in node.Properties)
        {
            if (prop.Value is string strValue)
            {
                var lowerValue = strValue.ToLowerInvariant();
                foreach (var term in queryTerms)
                {
                    if (lowerValue.Contains(term))
                        score += 0.05f;
                }
            }
        }

        return Math.Min(score, 1.0f);
    }
}
