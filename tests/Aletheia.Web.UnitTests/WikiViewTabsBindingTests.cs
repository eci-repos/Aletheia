namespace Aletheia.Web.UnitTests;

public class WikiViewTabsBindingTests
{
    [Fact]
    public void Wiki_page_has_view_and_source_tabs()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Wiki.razor"));

        Assert.Contains(">View</button>", source);
        Assert.Contains(">Source</button>", source);
        Assert.Contains("MarkdownRenderer.ToHtml(_selectedPage.Summary)", source);
        Assert.Contains("wiki-source-view", source);
        Assert.Contains("SetView(WikiViewMode.", source);
    }

    [Fact]
    public void Wiki_tabs_default_to_rendered_view()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Wiki.razor"));

        Assert.Contains("_viewMode = WikiViewMode.View;", source);
        Assert.Contains("_viewMode == WikiViewMode.Source", source);
    }

    [Fact]
    public void Copilot_uses_shared_markdown_renderer()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("MarkdownRenderer.ToHtml(content)", source);
        Assert.DoesNotContain("AppendTable", source);
        Assert.DoesNotContain("AppendParagraph", source);
        Assert.DoesNotContain("TryAppendHeading", source);
    }

    [Fact]
    public void Copilot_json_special_case_is_preserved()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("copilot-json", source);
        Assert.Contains("HtmlEncoder.Default.Encode", source);
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
