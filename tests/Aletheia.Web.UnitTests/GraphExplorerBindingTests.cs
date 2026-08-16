namespace Aletheia.Web.UnitTests;

/// <summary>
/// Sprint 76: the Graph Explorer gains (1) a custom drag-group on source-document nodes —
/// dragging a Source node moves its exclusively-found_in children with it — and (2) an explicit
/// zoom control (range slider + numeric scaling factor + Fit) wired to cy.zoom(). All behavior
/// lives in the initGraph JS in wwwroot/index.html + GraphExplorer.razor markup/handlers.
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
