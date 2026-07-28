using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Interfaces;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Neo4j.Driver;

namespace Aletheia.KnowledgeGraph.Infrastructure.Neo4j.GraphStore;

public sealed class Neo4jGraphStore : IGraphService, IAsyncDisposable
{
    private const string ClearFailedMessage = "Failed to clear graph.";
    private const string CreateNodeFailedMessage = "Failed to create node.";
    private const string CreateEdgeFailedMessage = "Failed to create edge.";
    private const string GetNodesFailedMessage = "Failed to retrieve nodes.";
    private const string GetEdgesFailedMessage = "Failed to retrieve edges.";
    private const string GetNeighborsFailedMessage = "Failed to retrieve neighbors.";
    private const string FindPathFailedMessage = "Failed to find path.";

    private readonly IDriver _driver;

    public Neo4jGraphStore(string uri, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI is required.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
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
            return Result.Failure($"{ClearFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        const string query = "CREATE (n:Entity {id: $id, label: $label, type: $type})";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new { id = node.Id, label = node.Label, type = node.Type }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{CreateNodeFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> CreateEdgeAsync(GraphEdge edge, CancellationToken cancellationToken = default)
    {
        if (edge is null)
        {
            throw new ArgumentNullException(nameof(edge));
        }

        const string query = @"
            MATCH (a:Entity {id: $sourceId})
            MATCH (b:Entity {id: $targetId})
            CREATE (a)-[r:RELATES {type: $relType}]->(b)";

        try
        {
            await using var session = _driver.AsyncSession();
            await session.RunAsync(query, new
            {
                sourceId = edge.SourceId,
                targetId = edge.TargetId,
                relType = edge.RelationshipType
            }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{CreateEdgeFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        const string query = "MATCH (n:Entity) RETURN n.id AS id, n.label AS label, n.type AS type";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            var nodes = records.Select(r => new GraphNode(
                r["id"].As<string>(),
                r["label"].As<string>(),
                r["type"].As<string>())).ToList();

            return Result<IReadOnlyList<GraphNode>>.Success(nodes);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphNode>>.Failure($"{GetNodesFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default)
    {
        const string query = @"
            MATCH (a:Entity)-[r:RELATES]->(b:Entity)
            RETURN a.id AS sourceId, b.id AS targetId, r.type AS relType";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            var edges = records.Select((r, i) => new GraphEdge(
                $"e{i}",
                r["sourceId"].As<string>(),
                r["targetId"].As<string>(),
                r["relType"].As<string>())).ToList();

            return Result<IReadOnlyList<GraphEdge>>.Success(edges);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphEdge>>.Failure($"{GetEdgesFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node ID is required.", nameof(nodeId));
        }

        const string query = @"
            MATCH (n:Entity {id: $nodeId})-[:RELATES]-(m:Entity)
            RETURN m.id AS id, m.label AS label, m.type AS type";

        try
        {
            await using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { nodeId }).ConfigureAwait(false);
            var records = await result.ToListAsync().ConfigureAwait(false);

            var nodes = records.Select(r => new GraphNode(
                r["id"].As<string>(),
                r["label"].As<string>(),
                r["type"].As<string>())).ToList();

            return Result<IReadOnlyList<GraphNode>>.Success(nodes);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphNode>>.Failure($"{GetNeighborsFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GraphPath>>> FindShortestPathAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(startNodeId))
        {
            throw new ArgumentException("Start node ID is required.", nameof(startNodeId));
        }

        if (string.IsNullOrWhiteSpace(endNodeId))
        {
            throw new ArgumentException("End node ID is required.", nameof(endNodeId));
        }

        const string query = @"
            MATCH path = shortestPath((start:Entity {id: $startId})-[*]-(end:Entity {id: $endId}))
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
                var nodes = path.Nodes.Select(n => new GraphNode(
                    n["id"].As<string>(),
                    n["label"].As<string>(),
                    n["type"].As<string>())).ToList();

                var edges = path.Relationships.Select((r, i) => new GraphEdge(
                    $"p{i}",
                    r.StartNodeElementId,
                    r.EndNodeElementId,
                    r.Type)).ToList();

                paths.Add(new GraphPath(nodes, edges));
            }

            return Result<IReadOnlyList<GraphPath>>.Success(paths);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<GraphPath>>.Failure($"{FindPathFailedMessage} {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync().ConfigureAwait(false);
    }
}
