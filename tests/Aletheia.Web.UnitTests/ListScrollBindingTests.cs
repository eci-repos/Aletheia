namespace Aletheia.Web.UnitTests;

/// <summary>
/// Sprint 74: lists that can outgrow the viewport scroll inside their own panel
/// (.list-scroll / .table-scroll utilities in app.css; page-specific Wiki sidebar),
/// so a long listing no longer forces the whole panel/page to scroll.
/// </summary>
public class ListScrollBindingTests
{
    [Fact]
    public void App_css_defines_scrollable_list_utilities()
    {
        var css = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/css/app.css"));

        Assert.Contains(".list-scroll", css);
        Assert.Contains(".table-scroll", css);
        Assert.Contains("max-height", css);
        Assert.Contains("overflow-y: auto", css);
    }

    [Fact]
    public void Glossary_lists_scroll_within_their_panel()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Glossary/Index.razor"));

        Assert.Contains("class=\"list-scroll\"", source);
        Assert.Contains("table-responsive table-scroll", source);
    }

    [Fact]
    public void Governance_lists_scroll_within_their_cards()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Governance/Index.razor"));

        Assert.Contains("class=\"list-scroll\"", source);
        Assert.Contains("table-responsive table-scroll", source);
    }

    [Fact]
    public void Taxonomy_categories_scroll_within_their_panel()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/TaxonomyExplorer.razor"));

        Assert.Contains("class=\"list-scroll\"", source);
    }

    [Fact]
    public void Ontology_lists_scroll_within_their_panel()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/OntologyExplorer.razor"));

        Assert.Contains("class=\"list-scroll\"", source);
        Assert.Contains("table-responsive table-scroll", source);
    }

    [Fact]
    public void Wiki_index_scrolls_independently()
    {
        var css = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Wiki.razor.css"));

        Assert.Contains(".wiki-index", css);
        Assert.Contains("max-height", css);
        Assert.Contains("overflow-y: auto", css);
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
