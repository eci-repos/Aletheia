namespace Aletheia.Web.UnitTests;

public class SearchCenterBindingTests
{
    [Fact]
    public void SearchCenter_has_semantic_and_summaries_mode_buttons()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("SetSemanticMode", source);
        Assert.Contains(">Semantic</button>", source);
        Assert.Contains("SetSummariesMode", source);
        Assert.Contains(">Summaries</button>", source);
    }

    [Fact]
    public void SearchCenter_internal_modes_are_gated_behind_show_internal_search()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("SetWragsMode", source);
        Assert.Contains("SetGraphRagMode", source);
        Assert.Contains("SetLazyGraphRagMode", source);
        Assert.Contains("@if (ShowInternalSearch)", source);
    }

    [Fact]
    public void SearchCenter_has_mode_info_icon()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("ToggleModeInfo", source);
        Assert.Contains("ModeInfoTitle", source);
        Assert.Contains("ModeInfoDetail", source);
    }

    [Fact]
    public void SearchCenter_summaries_mode_explains_when_summaries_are_created()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("Summaries are generated from the connections between your documents", source);
        Assert.Contains("may take time to appear", source);
    }

    [Fact]
    public void SearchCenter_has_admin_graph_summaries_status_block()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("AuthorizeView Roles=\"Administrator\"", source);
        Assert.Contains("Graph summaries", source);
        Assert.Contains("Re-cluster communities", source);
        Assert.Contains("LoadSummariesStatusAsync", source);
    }

    [Fact]
    public void SearchCenter_summaries_mode_calls_summaries_retrieve()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("_mode == \"summaries\"", source);
        Assert.Contains("SummariesRetrieveAsync", source);
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
