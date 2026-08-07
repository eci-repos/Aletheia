using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.LazyGraphRAG;

namespace RAGS.UnitTests.LazyGraphRAG;

public sealed class LazyEntityDiscoveryServiceTests
{
    [Fact]
    public async Task PersistAsync_creates_nodes_for_new_entities()
    {
        var provider = new RecordingGraphProvider();
        var service = new LazyEntityDiscoveryService(new StubEntityExtractionService(), provider);

        var result = await service.PersistAsync(new[]
        {
            new ExtractedEntity
            {
                Id = "entity-alexia",
                Name = "Alexia",
                Type = "project",
                Description = "Released candidate",
                Confidence = 0.91
            }
        });

        Assert.True(result.IsSuccess);
        var node = Assert.Single(provider.Nodes.Values);
        Assert.Equal("entity-alexia", node.Id);
        Assert.Equal("Alexia", node.Label);
        Assert.Equal("project", node.Type);
        Assert.Equal(0.91, node.Properties["confidence"]);
        Assert.Equal("Released candidate", node.Properties["description"]);
        Assert.Equal("lazy-query-time", node.Properties["discoveryMode"]);
        Assert.True(node.Properties.ContainsKey("discoveredAt"));
    }

    [Fact]
    public async Task PersistAsync_updates_existing_nodes_and_keeps_existing_properties()
    {
        var provider = new RecordingGraphProvider();
        provider.Nodes["entity-alexia"] = new GraphNode(
            "entity-alexia",
            "Alexia",
            "project",
            new Dictionary<string, object> { ["sourceName"] = "RC2 log" });
        var service = new LazyEntityDiscoveryService(new StubEntityExtractionService(), provider);

        var result = await service.PersistAsync(new[]
        {
            new ExtractedEntity
            {
                Id = "entity-alexia",
                Name = "Alexia",
                Type = "project",
                Confidence = 0.72
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Empty(provider.CreatedNodes);
        var updated = Assert.Single(provider.UpdatedNodes);
        Assert.Equal("entity-alexia", updated.Id);
        Assert.Equal("RC2 log", updated.Properties["sourceName"]);
        Assert.Equal(0.72, updated.Properties["confidence"]);
        Assert.Equal("lazy-query-time", updated.Properties["discoveryMode"]);
    }

    [Fact]
    public async Task PersistAsync_skips_keyword_entities()
    {
        var provider = new RecordingGraphProvider();
        var service = new LazyEntityDiscoveryService(new StubEntityExtractionService(), provider);

        var result = await service.PersistAsync(new[]
        {
            new ExtractedEntity { Id = "kw:alpha", Name = "alpha", Type = "keyword", Confidence = 0.5 },
            new ExtractedEntity { Id = "entity-bravo", Name = "Bravo", Type = "project", Confidence = 0.9 }
        });

        Assert.True(result.IsSuccess);
        var node = Assert.Single(provider.Nodes.Values);
        Assert.Equal("entity-bravo", node.Id);
        Assert.DoesNotContain(provider.CreatedNodes, n => n.Id == "kw:alpha");
    }

    [Fact]
    public async Task PersistAsync_skips_statistical_candidate_entities()
    {
        var provider = new RecordingGraphProvider();
        var service = new LazyEntityDiscoveryService(new StubEntityExtractionService(), provider);

        var result = await service.PersistAsync(new[]
        {
            new ExtractedEntity { Id = "lazy:alpha", Name = "alpha", Type = "statistical-candidate", Confidence = 0.35 }
        });

        Assert.True(result.IsSuccess);
        Assert.Empty(provider.CreatedNodes);
        Assert.Empty(provider.UpdatedNodes);
    }

    [Fact]
    public async Task PersistAsync_does_not_update_noise_entities_that_already_exist()
    {
        var provider = new RecordingGraphProvider();
        provider.Nodes["lazy:alpha"] = new GraphNode(
            "lazy:alpha",
            "alpha",
            "statistical-candidate",
            new Dictionary<string, object> { ["score"] = 0.4 });
        var service = new LazyEntityDiscoveryService(new StubEntityExtractionService(), provider);

        var result = await service.PersistAsync(new[]
        {
            new ExtractedEntity { Id = "lazy:alpha", Name = "alpha", Type = "statistical-candidate", Confidence = 0.35 }
        });

        Assert.True(result.IsSuccess);
        Assert.Empty(provider.UpdatedNodes);
        Assert.Equal(0.4, provider.Nodes["lazy:alpha"].Properties["score"]);
    }

    private sealed class StubEntityExtractionService : IEntityExtractionService
    {
        public Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAsync(string text, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(Array.Empty<ExtractedEntity>()));

        public Task<Result<IReadOnlyList<ExtractedEntity>>> ClassifyAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));

        public Task<Result<IReadOnlyList<ExtractedEntity>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
    }

    private sealed class RecordingGraphProvider : IGraphProvider
    {
        public Dictionary<string, GraphNode> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<GraphNode> CreatedNodes { get; } = new();
        public List<GraphNode> UpdatedNodes { get; } = new();

        public Task<Result<GraphNode?>> GetNodeAsync(string id, CancellationToken cancellationToken = default)
        {
            Nodes.TryGetValue(id, out var node);
            return Task.FromResult(Result<GraphNode?>.Success(node));
        }

        public Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            Nodes[node.Id] = node;
            CreatedNodes.Add(node);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            Nodes[node.Id] = node;
            UpdatedNodes.Add(node);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteNodeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result<GraphEdge?>> GetRelationshipAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<GraphEdge?>.Success(null));

        public Task<Result> CreateRelationshipAsync(GraphEdge edge, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result> DeleteRelationshipAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Nodes.Values.ToList()));

        public Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(Array.Empty<GraphEdge>()));

        public Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));

        public Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string label, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));

        public Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string type, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(Array.Empty<GraphEdge>()));

        public Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphPath>>.Success(Array.Empty<GraphPath>()));

        public Task<Result<IReadOnlyList<GraphNode>>> GetSubgraphAsync(string nodeId, int depth, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));

        public Task<Result<bool>> GraphExistsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
