using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.Web.Pages;
using Aletheia.Web.Services;

namespace Aletheia.Web.UnitTests;

public class GraphExplorerTests
{
    [Fact]
    public void FilterGraph_scopes_document_context_to_matching_node_and_one_hop_neighbors()
    {
        var sourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var nodes = new[]
        {
            new GraphNode("source-1", "CMP 2026 RFP", "Source", new Dictionary<string, object>
            {
                ["sourceId"] = sourceId.ToString(),
                ["sourceName"] = "CMP 2026 RFP.pdf"
            }),
            new GraphNode("entity-ai", "AI automation", "Entity"),
            new GraphNode("other", "Unrelated policy", "Entity")
        };
        var edges = new[]
        {
            new GraphEdge("edge-1", "entity-ai", "source-1", "found_in"),
            new GraphEdge("edge-2", "other", "entity-ai", "mentions")
        };
        var context = new[]
        {
            RecentGraphContextItem.Document(sourceId, "CMP 2026 RFP.pdf", DateTimeOffset.UtcNow)
        };

        var (scopedNodes, scopedEdges) = GraphExplorer.FilterGraph(nodes, edges, context);

        Assert.Equal(new[] { "entity-ai", "source-1" }, scopedNodes.Select(node => node.Id).OrderBy(id => id));
        Assert.Single(scopedEdges);
        Assert.Equal("edge-1", scopedEdges[0].Id);
    }

    [Fact]
    public void FilterGraph_scopes_search_context_by_query_terms()
    {
        var nodes = new[]
        {
            new GraphNode("cmp-ai", "AI requirements", "Entity"),
            new GraphNode("source-cmp", "CMP 2026 RFP", "Source"),
            new GraphNode("finance", "Finance controls", "Entity")
        };
        var edges = new[]
        {
            new GraphEdge("edge-1", "cmp-ai", "source-cmp", "found_in")
        };
        var context = new[]
        {
            RecentGraphContextItem.Search("CMP AI features", "semantic", DateTimeOffset.UtcNow)
        };

        var (scopedNodes, scopedEdges) = GraphExplorer.FilterGraph(nodes, edges, context);

        Assert.Contains(scopedNodes, node => node.Id == "cmp-ai");
        Assert.Contains(scopedNodes, node => node.Id == "source-cmp");
        Assert.DoesNotContain(scopedNodes, node => node.Id == "finance");
        Assert.Single(scopedEdges);
    }

    [Fact]
    public void FilterOrphans_drops_degree_zero_nodes_and_keeps_edges_between_kept_nodes()
    {
        var nodes = new[]
        {
            new GraphNode("source-1", "CMP 2026 RFP", "Source"),
            new GraphNode("entity-ai", "AI automation", "Entity"),
            new GraphNode("orphan-1", "Isolated entity", "Entity")
        };
        var edges = new[]
        {
            new GraphEdge("edge-1", "entity-ai", "source-1", "found_in")
        };

        var (filteredNodes, filteredEdges) = GraphExplorer.FilterOrphans(nodes, edges);

        Assert.Contains(filteredNodes, node => node.Id == "source-1");
        Assert.Contains(filteredNodes, node => node.Id == "entity-ai");
        Assert.DoesNotContain(filteredNodes, node => node.Id == "orphan-1");
        Assert.Single(filteredEdges);
        Assert.Equal("edge-1", filteredEdges[0].Id);
    }

    [Fact]
    public void FilterOrphans_keeps_all_nodes_when_every_node_has_an_edge()
    {
        var nodes = new[]
        {
            new GraphNode("source-1", "CMP 2026 RFP", "Source"),
            new GraphNode("entity-ai", "AI automation", "Entity")
        };
        var edges = new[]
        {
            new GraphEdge("edge-1", "entity-ai", "source-1", "found_in")
        };

        var (filteredNodes, filteredEdges) = GraphExplorer.FilterOrphans(nodes, edges);

        Assert.Equal(2, filteredNodes.Count);
        Assert.Single(filteredEdges);
    }

    [Fact]
    public void FilterOrphans_drops_edges_that_reference_a_node_outside_the_node_set()
    {
        var nodes = new[]
        {
            new GraphNode("entity-ai", "AI automation", "Entity")
        };
        var edges = new[]
        {
            new GraphEdge("edge-1", "entity-ai", "source-1", "found_in")
        };

        var (filteredNodes, filteredEdges) = GraphExplorer.FilterOrphans(nodes, edges);

        Assert.Single(filteredNodes);
        Assert.Empty(filteredEdges);
    }
}
