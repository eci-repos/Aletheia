using System.Text.RegularExpressions;
using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Neo4j.Driver;

namespace Aletheia.RAGS.Infrastructure.Graph.Providers;

public sealed class Neo4jGraphProvider : IGraphProvider, IAsyncDisposable
{
    private static readonly Regex TokenPattern = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    private readonly IDriver _driver;

    public Neo4jGraphProvider(string uri, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("URI is required.", nameof(uri));
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
    }

    public async Task<Result<GraphNode?>> GetNodeAsync(string id, CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (n)
            WHERE n.id = $id AND (n:GraphNode OR n:Entity)
            RETURN n
            LIMIT 1";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { id }).ConfigureAwait(false);
            var record = (await result.ToListAsync().ConfigureAwait(false)).FirstOrDefault();

            return Result<GraphNode?>.Success(record is null ? null : MapNode(record["n"].As<INode>()));
        }
        catch (Exception ex)
        {
            return Result<GraphNode?>.Failure($"Failed to get node: {ex.Message}");
        }
    }

    public async Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        var props = BuildNodeProperties(node);
        var labels = BuildNodeLabels(node.Type);
        var query = $@"
            MERGE (n:GraphNode {{id: $id}})
            SET n += $props
            SET n{labels}";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new { id = node.Id, props }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to create node: {ex.Message}");
        }
    }

    public async Task<Result> UpdateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        var props = BuildNodeProperties(node);
        var labels = BuildNodeLabels(node.Type);
        var query = $@"
            MERGE (n:GraphNode {{id: $id}})
            SET n = $props
            SET n{labels}";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new { id = node.Id, props }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to update node: {ex.Message}");
        }
    }

    public async Task<Result> DeleteNodeAsync(string id, CancellationToken cancellationToken = default)
    {
        const string query = "MATCH (n {id: $id}) DETACH DELETE n";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new { id }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete node: {ex.Message}");
        }
    }

    public async Task<Result<GraphEdge?>> GetRelationshipAsync(string id, CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (a)-[r {id: $id}]->(b)
            WHERE a.id IS NOT NULL AND b.id IS NOT NULL
            RETURN a.id AS sourceId, b.id AS targetId, type(r) AS relType, r
            LIMIT 1";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { id }).ConfigureAwait(false);
            var record = (await result.ToListAsync().ConfigureAwait(false)).FirstOrDefault();

            return Result<GraphEdge?>.Success(record is null ? null : MapEdge(record));
        }
        catch (Exception ex)
        {
            return Result<GraphEdge?>.Failure($"Failed to get relationship: {ex.Message}");
        }
    }

    public async Task<Result> CreateRelationshipAsync(GraphEdge edge, CancellationToken cancellationToken = default)
    {
        if (edge is null) throw new ArgumentNullException(nameof(edge));

        var props = BuildRelationshipProperties(edge);
        var relationshipType = EscapeToken(NormalizeToken(edge.RelationshipType, "related_to"));
        var query = $@"
            MATCH (a {{id: $sourceId}})
            MATCH (b {{id: $targetId}})
            MERGE (a)-[r:`{relationshipType}` {{id: $id}}]->(b)
            SET r += $props";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new
            {
                id = edge.Id,
                sourceId = edge.SourceId,
                targetId = edge.TargetId,
                props
            }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to create relationship: {ex.Message}");
        }
    }

    public async Task<Result> DeleteRelationshipAsync(string id, CancellationToken cancellationToken = default)
    {
        const string query = "MATCH ()-[r {id: $id}]->() DELETE r";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new { id }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete relationship: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (n)
            WHERE n.id IS NOT NULL AND (n:GraphNode OR n:Entity)
            RETURN n";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            return Result<IReadOnlyList<GraphNode>>.Success(records.Select(r => MapNode(r["n"].As<INode>())).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphNode>>.Failure($"Failed to retrieve nodes: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (a)-[r]->(b)
            WHERE a.id IS NOT NULL AND b.id IS NOT NULL
              AND ((a:GraphNode OR a:Entity) AND (b:GraphNode OR b:Entity))
            RETURN r, a.id AS sourceId, b.id AS targetId, type(r) AS relType";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            return Result<IReadOnlyList<GraphEdge>>.Success(records.Select(MapEdge).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphEdge>>.Failure($"Failed to retrieve edges: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("Node ID is required.", nameof(nodeId));

        const string query = @"
            MATCH (n {id: $nodeId})--(m)
            WHERE m.id IS NOT NULL AND (m:GraphNode OR m:Entity)
            RETURN DISTINCT m";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { nodeId }).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            return Result<IReadOnlyList<GraphNode>>.Success(records.Select(r => MapNode(r["m"].As<INode>())).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphNode>>.Failure($"Failed to retrieve neighbors: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string label, CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (n)
            WHERE n.id IS NOT NULL AND (n:GraphNode OR n:Entity)
              AND toLower(coalesce(n.label, '')) CONTAINS toLower($label)
            RETURN n";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { label }).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            return Result<IReadOnlyList<GraphNode>>.Success(records.Select(r => MapNode(r["n"].As<INode>())).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphNode>>.Failure($"Failed to search nodes: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string type, CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (a)-[r]->(b)
            WHERE a.id IS NOT NULL AND b.id IS NOT NULL
              AND (type(r) = $type OR r.type = $type)
            RETURN r, a.id AS sourceId, b.id AS targetId, type(r) AS relType";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { type = NormalizeToken(type, "related_to") }).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            return Result<IReadOnlyList<GraphEdge>>.Success(records.Select(MapEdge).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphEdge>>.Failure($"Failed to search relationships: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(startNodeId))
            throw new ArgumentException("Start node ID is required.", nameof(startNodeId));
        if (string.IsNullOrWhiteSpace(endNodeId))
            throw new ArgumentException("End node ID is required.", nameof(endNodeId));

        const string query = @"
            MATCH path = shortestPath((start {id: $startId})-[*]-(end {id: $endId}))
            RETURN path";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { startId = startNodeId, endId = endNodeId }).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            var paths = new List<GraphPath>();
            foreach (var record in records)
            {
                var path = record["path"].As<IPath>();
                var nodes = path.Nodes.Select(MapNode).ToList();
                var edges = path.Relationships.Select((relationship, index) =>
                {
                    var props = ToPropertyDictionary(relationship.Properties, "id", "type");
                    return new GraphEdge(
                        relationship.Properties.TryGetValue("id", out var id) ? ValueAsString(id) : $"path-{index}",
                        ValueAsString(relationship.StartNodeElementId),
                        ValueAsString(relationship.EndNodeElementId),
                        relationship.Type,
                        props);
                }).ToList();

                paths.Add(new GraphPath(nodes, edges));
            }

            return Result<IReadOnlyList<GraphPath>>.Success(paths);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphPath>>.Failure($"Failed to find paths: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> GetSubgraphAsync(string nodeId, int depth, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("Node ID is required.", nameof(nodeId));

        var boundedDepth = Math.Clamp(depth, 1, 8);
        var query = $@"
            MATCH path = (n {{id: $nodeId}})-[*1..{boundedDepth}]-(m)
            WHERE m.id IS NOT NULL AND (m:GraphNode OR m:Entity)
            RETURN DISTINCT m";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { nodeId }).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            return Result<IReadOnlyList<GraphNode>>.Success(records.Select(r => MapNode(r["m"].As<INode>())).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphNode>>.Failure($"Failed to get subgraph: {ex.Message}");
        }
    }

    public async Task<Result<bool>> GraphExistsAsync(CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (n)
            WHERE n.id IS NOT NULL AND (n:GraphNode OR n:Entity)
            RETURN count(n) AS count
            LIMIT 1";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query).ConfigureAwait(false);
            var record = await result.SingleAsync().ConfigureAwait(false);
            return Result<bool>.Success(record["count"].As<int>() > 0);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Failed to check graph existence: {ex.Message}");
        }
    }

    public async Task<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        const string query = "MATCH (n) DETACH DELETE n";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to clear graph: {ex.Message}");
        }
    }

    public async Task<Result> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return Result.Failure("Source ID is required.");
        }

        const string query = @"
            MATCH (n)
            WHERE n.sourceId = $sourceId
            DETACH DELETE n";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new { sourceId }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete source graph data: {ex.Message}");
        }
    }

    private static Dictionary<string, object> BuildNodeProperties(GraphNode node)
    {
        var props = new Dictionary<string, object>(node.Properties)
        {
            ["id"] = node.Id,
            ["label"] = node.Label,
            ["type"] = node.Type
        };
        return props;
    }

    private static Dictionary<string, object> BuildRelationshipProperties(GraphEdge edge)
    {
        var props = new Dictionary<string, object>(edge.Properties)
        {
            ["id"] = edge.Id,
            ["type"] = NormalizeToken(edge.RelationshipType, "related_to")
        };
        return props;
    }

    private static GraphNode MapNode(INode node)
    {
        var props = ToPropertyDictionary(node.Properties, "id", "label", "type");
        return new GraphNode(
            node.Properties.TryGetValue("id", out var id) ? ValueAsString(id) : node.ElementId,
            node.Properties.TryGetValue("label", out var label) ? ValueAsString(label) : node.Labels.FirstOrDefault(l => l != "GraphNode") ?? "Entity",
            node.Properties.TryGetValue("type", out var type) ? ValueAsString(type) : InferTypeFromLabels(node.Labels),
            props);
    }

    private static GraphEdge MapEdge(IRecord record)
    {
        var rel = record["r"].As<IRelationship>();
        var props = ToPropertyDictionary(rel.Properties, "id", "type");
        return new GraphEdge(
            rel.Properties.TryGetValue("id", out var id) ? ValueAsString(id) : rel.ElementId,
            ValueAsString(record["sourceId"]),
            ValueAsString(record["targetId"]),
            ValueAsString(record["relType"]),
            props);
    }

    private static Dictionary<string, object> ToPropertyDictionary(IReadOnlyDictionary<string, object> properties, params string[] excludedKeys)
    {
        var excluded = new HashSet<string>(excludedKeys, StringComparer.OrdinalIgnoreCase);
        return properties
            .Where(p => !excluded.Contains(p.Key) && p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value);
    }

    private static string BuildNodeLabels(string type)
    {
        var labels = new[]
        {
            BaseLabelForType(type),
            NormalizeToken(type, "Entity")
        }
        .Where(label => !label.Equals("GraphNode", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(label => $":`{EscapeToken(label)}`");

        return string.Concat(labels);
    }

    private static string BaseLabelForType(string type)
    {
        if (type.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("SourceDocument", StringComparison.OrdinalIgnoreCase))
        {
            return "Source";
        }

        if (type.Equals("Community", StringComparison.OrdinalIgnoreCase))
        {
            return "Community";
        }

        return "Entity";
    }

    private static string InferTypeFromLabels(IEnumerable<string> labels)
    {
        return labels.FirstOrDefault(l => !l.Equals("GraphNode", StringComparison.OrdinalIgnoreCase)) ?? "Entity";
    }

    private static string NormalizeToken(string value, string fallback)
    {
        var normalized = TokenPattern.Replace(value.Trim(), "_");
        normalized = Regex.Replace(normalized, "_+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = fallback;
        }

        if (char.IsDigit(normalized[0]))
        {
            normalized = $"_{normalized}";
        }

        return normalized;
    }

    private static string EscapeToken(string value)
    {
        return value.Replace("`", "``", StringComparison.Ordinal);
    }

    private static string ValueAsString(object value)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            _ => value.ToString() ?? string.Empty
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync().ConfigureAwait(false);
    }
}

