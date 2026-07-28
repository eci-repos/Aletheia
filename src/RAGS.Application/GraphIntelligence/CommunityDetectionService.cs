using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class CommunityDetectionService : ICommunityDetectionService
{
    private const int MaxLevels = 4;
    private const int MaxLocalMoveIterations = 20;

    private readonly IGraphProvider _provider;
    private readonly Dictionary<string, GraphCommunity> _lastDiscovered = new(StringComparer.OrdinalIgnoreCase);

    public CommunityDetectionService(IGraphProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<Result<IReadOnlyList<GraphCommunity>>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var nodesResult = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        var edgesResult = await _provider.GetEdgesAsync(cancellationToken).ConfigureAwait(false);

        if (nodesResult.IsFailure || edgesResult.IsFailure || nodesResult.Value is null)
        {
            return Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>());
        }

        var nodes = nodesResult.Value
            .Where(n => !n.Type.Equals("Community", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var edges = edgesResult.Value ?? Array.Empty<GraphEdge>();

        if (nodes.Count == 0)
        {
            return Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>());
        }

        var communities = DetectHierarchicalCommunities(nodes, edges);

        lock (_lastDiscovered)
        {
            _lastDiscovered.Clear();
            foreach (var community in communities)
            {
                _lastDiscovered[community.Id] = community;
            }
        }

        foreach (var community in communities)
        {
            await PersistCommunityAsync(community, cancellationToken).ConfigureAwait(false);
        }

        return Result<IReadOnlyList<GraphCommunity>>.Success(communities);
    }

    public Task<Result<IReadOnlyList<GraphCommunity>>> DetectClustersAsync(CancellationToken cancellationToken = default)
    {
        return DiscoverAsync(cancellationToken);
    }

    public async Task<Result> AssignAsync(string nodeId, string communityId, CancellationToken cancellationToken = default)
    {
        var nodeResult = await _provider.GetNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure || nodeResult.Value is null)
        {
            return Result.Failure($"Node '{nodeId}' not found.");
        }

        var node = nodeResult.Value;
        var properties = new Dictionary<string, object>(node.Properties)
        {
            ["communityId"] = communityId
        };

        return await _provider
            .UpdateNodeAsync(new GraphNode(node.Id, node.Label, node.Type, properties), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<GraphCommunity?>> GetCommunityAsync(string communityId, CancellationToken cancellationToken = default)
    {
        lock (_lastDiscovered)
        {
            if (_lastDiscovered.TryGetValue(communityId, out var cached))
            {
                return Result<GraphCommunity?>.Success(cached);
            }
        }

        var discovered = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (discovered.IsSuccess && discovered.Value is not null)
        {
            return Result<GraphCommunity?>.Success(discovered.Value.FirstOrDefault(c =>
                c.Id.Equals(communityId, StringComparison.OrdinalIgnoreCase)));
        }

        return Result<GraphCommunity?>.Success(null);
    }

    public async Task<Result<IReadOnlyList<GraphCommunity>>> GetCommunitiesForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        GraphCommunity[] cached;
        lock (_lastDiscovered)
        {
            cached = _lastDiscovered.Values
                .Where(c => c.MemberIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
                .OrderBy(c => Convert.ToInt32(c.Metadata.TryGetValue("level", out var level) ? level : 0))
                .ToArray();
        }

        if (cached.Length > 0)
        {
            return Result<IReadOnlyList<GraphCommunity>>.Success(cached);
        }

        var discovered = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (discovered.IsFailure || discovered.Value is null)
        {
            return Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>());
        }

        return Result<IReadOnlyList<GraphCommunity>>.Success(discovered.Value
            .Where(c => c.MemberIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
            .ToList());
    }

    private async Task PersistCommunityAsync(GraphCommunity community, CancellationToken cancellationToken)
    {
        var level = community.Metadata.TryGetValue("level", out var levelValue) ? levelValue : 0;
        var node = new GraphNode(
            community.Id,
            community.Name,
            "Community",
            new Dictionary<string, object>
            {
                ["description"] = community.Description ?? string.Empty,
                ["algorithm"] = "leiden",
                ["level"] = level,
                ["memberCount"] = community.MemberIds.Count
            });

        await _provider.CreateNodeAsync(node, cancellationToken).ConfigureAwait(false);

        foreach (var memberId in community.MemberIds.Take(250))
        {
            await _provider.CreateRelationshipAsync(
                new GraphEdge(
                    $"{community.Id}-has_member-{memberId}",
                    community.Id,
                    memberId,
                    "has_member",
                    new Dictionary<string, object> { ["level"] = level }),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<GraphCommunity> DetectHierarchicalCommunities(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphEdge> edges)
    {
        var originalMembers = nodes.ToDictionary(
            n => n.Id,
            n => (IReadOnlyCollection<string>)new[] { n.Id },
            StringComparer.OrdinalIgnoreCase);

        var currentNodeIds = nodes.Select(n => n.Id).ToList();
        var currentEdges = edges
            .Where(e => originalMembers.ContainsKey(e.SourceId) && originalMembers.ContainsKey(e.TargetId))
            .ToList();
        var allCommunities = new List<GraphCommunity>();

        for (var level = 0; level < MaxLevels && currentNodeIds.Count > 0; level++)
        {
            var assignments = RunLocalMoving(currentNodeIds, currentEdges);
            var groups = assignments
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Select(kv => kv.Key).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList())
                .OrderBy(g => g.First(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                break;
            }

            var nextMembers = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var members = group
                    .SelectMany(id => originalMembers[id])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var communityId = BuildCommunityId(level, members);
                nextMembers[communityId] = members;

                allCommunities.Add(new GraphCommunity
                {
                    Id = communityId,
                    Name = $"Community L{level} {communityId[^8..]}",
                    Description = $"Leiden community level {level} with {members.Count} members.",
                    MemberIds = members,
                    Metadata = new Dictionary<string, object>
                    {
                        ["algorithm"] = "leiden",
                        ["level"] = level,
                        ["memberCount"] = members.Count
                    }
                });
            }

            if (groups.Count == currentNodeIds.Count || groups.Count == 1)
            {
                break;
            }

            currentEdges = AggregateEdges(currentEdges, assignments);
            currentNodeIds = nextMembers.Keys.ToList();
            originalMembers = nextMembers.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        return allCommunities;
    }

    private static Dictionary<string, string> RunLocalMoving(IReadOnlyList<string> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var adjacency = BuildAdjacency(nodes, edges);
        var assignments = nodes.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);

        for (var iteration = 0; iteration < MaxLocalMoveIterations; iteration++)
        {
            var changed = false;
            foreach (var nodeId in nodes.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (!adjacency.TryGetValue(nodeId, out var neighbors) || neighbors.Count == 0)
                {
                    continue;
                }

                var bestCommunity = neighbors
                    .GroupBy(neighborId => assignments[neighborId], StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        CommunityId = g.Key,
                        Score = g.Count() + CommunityCompactnessBonus(g.Key, assignments, adjacency)
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.CommunityId, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .CommunityId;

                if (!assignments[nodeId].Equals(bestCommunity, StringComparison.OrdinalIgnoreCase))
                {
                    assignments[nodeId] = bestCommunity;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return assignments;
    }

    private static double CommunityCompactnessBonus(
        string communityId,
        IReadOnlyDictionary<string, string> assignments,
        IReadOnlyDictionary<string, HashSet<string>> adjacency)
    {
        var members = assignments
            .Where(kv => kv.Value.Equals(communityId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        if (members.Count <= 1)
        {
            return 0;
        }

        var internalEdges = members.Sum(member => adjacency.TryGetValue(member, out var neighbors)
            ? neighbors.Count(n => assignments.TryGetValue(n, out var c) && c.Equals(communityId, StringComparison.OrdinalIgnoreCase))
            : 0);

        return internalEdges / (double)(members.Count * members.Count);
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(IReadOnlyList<string> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var adjacency = nodes.ToDictionary(
            id => id,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            if (adjacency.ContainsKey(edge.SourceId) && adjacency.ContainsKey(edge.TargetId))
            {
                adjacency[edge.SourceId].Add(edge.TargetId);
                adjacency[edge.TargetId].Add(edge.SourceId);
            }
        }

        return adjacency;
    }

    private static List<GraphEdge> AggregateEdges(
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyDictionary<string, string> assignments)
    {
        return edges
            .Where(e => assignments.ContainsKey(e.SourceId) && assignments.ContainsKey(e.TargetId))
            .Select(e => (Source: assignments[e.SourceId], Target: assignments[e.TargetId]))
            .Where(e => !e.Source.Equals(e.Target, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Select((e, i) => new GraphEdge($"aggregate-{i}", e.Source, e.Target, "aggregate_link"))
            .ToList();
    }

    private static string BuildCommunityId(int level, IReadOnlyList<string> members)
    {
        var key = string.Join("|", members);
        return $"community:l{level}:{Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(key)):x8}";
    }
}
