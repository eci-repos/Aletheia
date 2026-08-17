namespace Aletheia.Web.UnitTests;

/// <summary>
/// Sprint 76: the Graph Explorer gains (1) a custom drag-group on source-document nodes —
/// dragging a Source node moves its exclusively-found_in children with it — and (2) an explicit
/// zoom control (range slider + numeric scaling factor + Fit) wired to cy.zoom(). All behavior
/// lives in the initGraph JS in wwwroot/index.html + GraphExplorer.razor markup/handlers.
/// Sprint 79: the .context-mode block also gains a "Show orphan nodes (technical)" toggle
/// (off by default) that filters degree-0 nodes client-side via ApplyOrphanFilter/FilterOrphans.
/// </summary>
public class GraphExplorerBindingTests
{
    [Fact]
    public void InitGraph_js_defines_drag_group_handlers()
    {
        var index = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/index.html"));

        Assert.Contains("cy.on('grab', 'node'", index);
        Assert.Contains("cy.on('drag', 'node'", index);
        Assert.Contains("cy.on('free', 'node'", index);
    }

    [Fact]
    public void InitGraph_js_checks_found_in_exclusivity()
    {
        var index = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/index.html"));

        // A child joins the drag group only when EVERY found_in edge it has points to the
        // dragged document — a child found in multiple documents stays put.
        Assert.Contains("relationshipType') === 'found_in'", index);
        Assert.Contains("exclusivelyInThisSource", index);
        Assert.Contains("every(edge =>", index);
    }

    [Fact]
    public void InitGraph_js_adds_relationshipType_to_edge_data()
    {
        var index = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/index.html"));

        // The drag-group exclusivity check reads edge.data('relationshipType'), so the edge
        // elements must carry it (label alone is not enough — it is the display label).
        Assert.Contains("relationshipType: e.relationshipType", index);
    }

    [Fact]
    public void Index_html_defines_setGraphZoom_and_getGraphZoom()
    {
        var index = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/index.html"));

        Assert.Contains("window.setGraphZoom", index);
        Assert.Contains("window.getGraphZoom", index);
        Assert.Contains("cy.zoom({ level: clamped })", index);
    }

    [Fact]
    public void GraphExplorer_renders_zoom_slider_and_factor_input()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/GraphExplorer.razor"));

        Assert.Contains("graph-zoom-slider", page);
        Assert.Contains("graph-zoom-factor", page);
        Assert.Contains("OnZoomSliderAsync", page);
        Assert.Contains("OnZoomFactorAsync", page);
        Assert.Contains("setGraphZoom", page);
    }

    [Fact]
    public void GraphExplorer_has_fit_button_and_zoom_sync()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/GraphExplorer.razor"));

        Assert.Contains("FitGraphAsync", page);
        Assert.Contains("fitGraph", page);
        Assert.Contains("SyncZoomFromGraphAsync", page);
        Assert.Contains("getGraphZoom", page);
    }

    [Fact]
    public void PathFinder_grid_lets_selects_shrink_so_find_path_button_is_never_clipped()
    {
        var css = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/GraphExplorer.razor.css"));

        // The path-finder grid used minmax(150px, 1fr) for both selects, so its content
        // (~498px) overflowed the toolbar's 420px column floor and the rightmost "Find Path"
        // button was clipped at the workspace edge. The selects must be able to shrink
        // (minmax(0, 1fr)) and the button must never wrap.
        Assert.Contains("grid-template-columns: auto minmax(0, 1fr) auto minmax(0, 1fr) auto", css);
        Assert.Contains(".path-finder .btn", css);
        Assert.Contains("white-space: nowrap", css);
    }

    [Fact]
    public void GraphExplorer_has_orphan_toggle_off_by_default()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/GraphExplorer.razor"));

        // Checkbox in the .context-mode block, wired to the handler.
        Assert.Contains("Show orphan nodes (technical)", page);
        Assert.Contains("_showOrphanNodes", page);
        Assert.Contains("ToggleOrphanNodesAsync", page);
        // Off by default: declared with no initializer (false).
        Assert.Contains("private bool _showOrphanNodes;", page);
        // The orphan filter runs from ApplyGraphScope right after the chunk filter.
        Assert.Contains("ApplyChunkFilter();", page);
        Assert.Contains("ApplyOrphanFilter();", page);
        Assert.True(page.IndexOf("ApplyOrphanFilter();", StringComparison.Ordinal) >
                    page.IndexOf("ApplyChunkFilter();", StringComparison.Ordinal));
    }

    [Fact]
    public void GraphExplorer_orphan_filter_clears_path_selects_for_removed_nodes()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/GraphExplorer.razor"));

        // The orphan filter must clear a path select that references a node the filter removed
        // (the same guard pattern as ApplyChunkFilter).
        var methodStart = page.IndexOf("private void ApplyOrphanFilter()", StringComparison.Ordinal);
        var methodEnd = page.IndexOf(
            "public static (List<GraphNode> Nodes, List<GraphEdge> Edges) FilterOrphans",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart, "ApplyOrphanFilter method body not found.");
        var body = page[methodStart..methodEnd];

        Assert.Contains("FilterOrphans(_nodes, _edges)", body);
        Assert.Contains("_pathFrom = string.Empty", body);
        Assert.Contains("_pathTo = string.Empty", body);
    }

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
