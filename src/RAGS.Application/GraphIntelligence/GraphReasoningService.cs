using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class GraphReasoningService : IGraphReasoningService
{
    private readonly Kernel _kernel;
    private readonly IGraphProvider _provider;
    private readonly IRagsService _ragsService;

    public GraphReasoningService(Kernel kernel, IGraphProvider provider, IRagsService ragsService)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
    }

    public async Task<Result<IReadOnlyList<GraphPath>>> DiscoverReasoningPathsAsync(string query, CancellationToken cancellationToken = default)
    {
        var selectedEntities = await SelectEntitiesCoreAsync(query, cancellationToken).ConfigureAwait(false);
        if (!selectedEntities.Any())
        {
            return Result<IReadOnlyList<GraphPath>>.Success(Array.Empty<GraphPath>());
        }

        var paths = new List<GraphPath>();
        var visited = new HashSet<string>();

        foreach (var startEntity in selectedEntities)
        {
            var frontier = new Queue<(GraphNode Node, List<GraphNode> Path, int Depth)>();
            frontier.Enqueue((startEntity, new List<GraphNode> { startEntity }, 0));

            while (frontier.Count > 0)
            {
                var (currentNode, pathSoFar, depth) = frontier.Dequeue();

                if (depth >= 3) continue;

                var neighbors = await _provider.GetNeighborsAsync(currentNode.Id, cancellationToken).ConfigureAwait(false);
                if (!neighbors.IsSuccess || neighbors.Value is null) continue;

                foreach (var neighbor in neighbors.Value)
                {
                    if (visited.Contains(neighbor.Id)) continue;
                    visited.Add(neighbor.Id);

                    var newPath = new List<GraphNode>(pathSoFar) { neighbor };

                    // Build edges for this path step
                    var edges = new List<GraphEdge>();
                    for (var i = 0; i < newPath.Count - 1; i++)
                    {
                        edges.Add(new GraphEdge($"edge_{i}", newPath[i].Id, newPath[i + 1].Id, "relates"));
                    }

                    paths.Add(new GraphPath(newPath, edges));

                    if (depth < 2)
                    {
                        frontier.Enqueue((neighbor, newPath, depth + 1));
                    }
                }
            }
        }

        return Result<IReadOnlyList<GraphPath>>.Success(paths);
    }

    public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveGraphAwareAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        // Step 1: Base semantic search
        var baseResults = await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK), cancellationToken).ConfigureAwait(false);
        if (baseResults.IsFailure || baseResults.Value is null)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure(baseResults.Error ?? "Retrieval failed.");
        }

        // Step 2: Select graph entities matching the query
        var selectedEntities = await SelectEntitiesCoreAsync(query, cancellationToken).ConfigureAwait(false);
        if (!selectedEntities.Any())
        {
            return Result<IReadOnlyList<SearchResult>>.Success(baseResults.Value);
        }

        var resultSet = new Dictionary<Guid, SearchResult>();
        foreach (var r in baseResults.Value)
        {
            resultSet[r.Chunk.Id] = r;
        }

        // Step 3: Boost results linked to selected entities
        foreach (var entity in selectedEntities)
        {
            // Search for chunks related to this entity.
            var entityResults = await _ragsService.RetrieveAsync(
                new RetrievalRequest(entity.Label, Math.Min(3, topK)), cancellationToken).ConfigureAwait(false);

            if (entityResults.IsSuccess && entityResults.Value is not null)
            {
                foreach (var result in entityResults.Value)
                {
                    if (resultSet.TryGetValue(result.Chunk.Id, out var existing))
                    {
                        resultSet[result.Chunk.Id] = new SearchResult(
                            existing.Chunk,
                            Math.Min(existing.Score + 0.15f, 1.0f));
                    }
                    else
                    {
                        resultSet[result.Chunk.Id] = new SearchResult(result.Chunk, result.Score * 0.8f);
                    }
                }
            }

            // Step 4: Traverse neighbors for additional context
            var neighbors = await _provider.GetNeighborsAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            if (!neighbors.IsSuccess || neighbors.Value is null) continue;

            foreach (var neighbor in neighbors.Value.Take(topK))
            {
                var neighborResults = await _ragsService.RetrieveAsync(
                    new RetrievalRequest(neighbor.Label, 2), cancellationToken).ConfigureAwait(false);

                if (neighborResults.IsSuccess && neighborResults.Value is not null)
                {
                    foreach (var nr in neighborResults.Value)
                    {
                        if (!resultSet.ContainsKey(nr.Chunk.Id))
                        {
                            resultSet[nr.Chunk.Id] = new SearchResult(nr.Chunk, nr.Score * 0.6f);
                        }
                    }
                }
            }
        }

        var finalResults = resultSet.Values
            .OrderByDescending(r => r.Score)
            .Take(topK * 2)
            .ToList();

        return Result<IReadOnlyList<SearchResult>>.Success(finalResults);
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> SelectEntitiesAsync(string query, CancellationToken cancellationToken = default)
    {
        var selected = await SelectEntitiesCoreAsync(query, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<GraphNode>>.Success(selected);
    }

    public Task<Result<IReadOnlyList<GraphCommunity>>> SelectCommunitiesAsync(string query, CancellationToken cancellationToken = default)
    {
        // Communities are not yet persisted in the graph.
        return Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>()));
    }

    /// <summary>
    /// Uses Semantic Kernel IChatCompletionService to identify entities in the query,
    /// falls back to graph search if LLM is unavailable.
    /// </summary>
    private async Task<IReadOnlyList<GraphNode>> SelectEntitiesCoreAsync(string query, CancellationToken cancellationToken)
    {
        var allNodes = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        if (allNodes.IsFailure || allNodes.Value is null || !allNodes.Value.Any())
        {
            return Array.Empty<GraphNode>();
        }

        var entityNodes = allNodes.Value.Where(n =>
            !n.Type.Equals("Source", StringComparison.OrdinalIgnoreCase) &&
            !n.Type.Equals("SourceDocument", StringComparison.OrdinalIgnoreCase) &&
            !n.Type.Equals("Chunk", StringComparison.OrdinalIgnoreCase) &&
            !n.Type.Equals("Community", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!entityNodes.Any())
        {
            // If no entity nodes exist yet (pre-migration data), fall back to Source nodes.
            entityNodes = allNodes.Value.ToList();
        }

        // Primary: Use LLM to extract entities from the query
        var queryEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are an entity extraction assistant. Extract all named entities from the user's query. " +
                "Return ONLY a comma-separated list of entity names, nothing else. For example: 'Microsoft, Bill Gates, OpenAI'.");
            history.AddUserMessage(query);

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            var content = response.Content ?? string.Empty;

            foreach (var part in content.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    queryEntities.Add(trimmed);
            }
        }
        catch
        {
            // LLM unavailable — fall back to label substring matching
            var lowerQuery = query.ToLowerInvariant();
            foreach (var node in entityNodes)
            {
                if (lowerQuery.Contains(node.Label.ToLowerInvariant()))
                    queryEntities.Add(node.Label);
            }
        }

        // Match extracted entities to graph nodes
        var matched = new List<GraphNode>();
        foreach (var qe in queryEntities)
        {
            var lowerQe = qe.ToLowerInvariant();
            foreach (var node in entityNodes)
            {
                if (node.Label.Equals(qe, StringComparison.OrdinalIgnoreCase) ||
                    node.Label.Contains(qe, StringComparison.OrdinalIgnoreCase) ||
                    lowerQe.Contains(node.Label.ToLowerInvariant()))
                {
                    if (!matched.Any(m => m.Id == node.Id))
                    {
                        matched.Add(node);
                    }
                }
            }
        }

        return matched;
    }
}
