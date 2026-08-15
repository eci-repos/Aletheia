using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.GraphRAG;
using Moq;

namespace RAGS.UnitTests.GraphRAG;

public sealed class SummariesStatusServiceTests
{
    private static GraphNode Node(string id, string type, params (string Key, object Value)[] properties)
    {
        var dict = new Dictionary<string, object>();
        foreach (var (key, value) in properties)
        {
            dict[key] = value;
        }

        return new GraphNode(id, id, type, dict);
    }

    private static Mock<IGraphProvider> ProviderWith(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge>? edges = null)
    {
        var provider = new Mock<IGraphProvider>();
        provider
            .Setup(p => p.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<GraphNode>>.Success(nodes));
        provider
            .Setup(p => p.GetEdgesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<GraphEdge>>.Success(edges ?? Array.Empty<GraphEdge>()));
        return provider;
    }

    [Fact]
    public async Task GetAsync_reports_empty_graph_when_no_nodes()
    {
        var service = new SummariesStatusService(ProviderWith(Array.Empty<GraphNode>()).Object);

        var result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.GraphExists);
        Assert.Equal(0, result.Value.NodeCount);
        Assert.Equal(0, result.Value.CommunityCount);
        Assert.Empty(result.Value.Sources);
    }

    [Fact]
    public async Task GetAsync_counts_communities_and_summarized_communities()
    {
        var nodes = new List<GraphNode>
        {
            Node("src-1", "Source", ("sourceName", "RFP 2026")),
            Node("ent-1", "Entity", ("sourceId", "src-1")),
            Node("comm-1", "Community", ("summary", "A summary exists.")),
            Node("comm-2", "Community"),
            Node("chunk-1", "Chunk")
        };
        var service = new SummariesStatusService(ProviderWith(nodes).Object);

        var result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value!;
        Assert.True(snapshot.GraphExists);
        Assert.Equal(5, snapshot.NodeCount);
        Assert.Equal(1, snapshot.EntityCount); // chunk nodes are not entities
        Assert.Equal(2, snapshot.CommunityCount);
        Assert.Equal(1, snapshot.SummarizedCommunityCount);
        Assert.Equal(1, snapshot.SourceCount);
    }

    [Fact]
    public async Task GetAsync_aggregates_per_source_via_has_member_edges()
    {
        var nodes = new List<GraphNode>
        {
            Node("src-1", "Source", ("sourceName", "RFP 2026")),
            Node("src-2", "Source", ("sourceName", "Contract")),
            Node("ent-1", "Entity", ("sourceId", "src-1")),
            Node("ent-2", "Entity", ("sourceId", "src-2")),
            Node("comm-1", "Community", ("summary", "summarized")),
            Node("comm-2", "Community")
        };
        var edges = new List<GraphEdge>
        {
            new("e1", "comm-1", "ent-1", "has_member"),
            new("e2", "comm-2", "ent-2", "has_member")
        };
        var service = new SummariesStatusService(ProviderWith(nodes, edges).Object);

        var result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        var snapshot = result.Value!;
        Assert.Equal(2, snapshot.Sources.Count);

        var source1 = snapshot.Sources.Single(s => s.SourceName == "RFP 2026");
        Assert.Equal(1, source1.EntityCount);
        Assert.Equal(1, source1.CommunityCount);
        Assert.Equal(1, source1.SummarizedCommunityCount);

        var source2 = snapshot.Sources.Single(s => s.SourceName == "Contract");
        Assert.Equal(1, source2.EntityCount);
        Assert.Equal(1, source2.CommunityCount);
        Assert.Equal(0, source2.SummarizedCommunityCount);
    }

    [Fact]
    public async Task GetAsync_counts_a_community_once_per_source_even_with_many_members()
    {
        var nodes = new List<GraphNode>
        {
            Node("src-1", "Source", ("sourceName", "RFP 2026")),
            Node("ent-1", "Entity", ("sourceId", "src-1")),
            Node("ent-2", "Entity", ("sourceId", "src-1")),
            Node("comm-1", "Community", ("summary", "summarized"))
        };
        var edges = new List<GraphEdge>
        {
            new("e1", "comm-1", "ent-1", "has_member"),
            new("e2", "comm-1", "ent-2", "has_member")
        };
        var service = new SummariesStatusService(ProviderWith(nodes, edges).Object);

        var result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        var source = result.Value!.Sources.Single();
        Assert.Equal(2, source.EntityCount);
        Assert.Equal(1, source.CommunityCount);
        Assert.Equal(1, source.SummarizedCommunityCount);
    }
}
