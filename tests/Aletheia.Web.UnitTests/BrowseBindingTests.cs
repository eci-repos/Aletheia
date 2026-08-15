namespace Aletheia.Web.UnitTests;

public class BrowseBindingTests
{
    [Fact]
    public void Browse_has_ingestion_status_column()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("<th>Ingestion</th>", source);
    }

    [Fact]
    public void Browse_renders_ingested_and_not_ingested_badges()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("file.Ingested", source);
        Assert.Contains("bg-success", source);
        Assert.Contains(">Ingested</span>", source);
        Assert.Contains("bg-warning text-dark", source);
        Assert.Contains(">Not ingested</span>", source);
    }

    [Fact]
    public void Browse_ingestion_badge_explains_missing_embeddings()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("ingestion job may have failed", source);
        Assert.Contains("chunk(s) embedded", source);
    }

    [Fact]
    public void Browse_renders_processing_badge_for_active_ingestion()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("file.IsProcessing", source);
        Assert.Contains("bg-info text-dark", source);
        Assert.Contains(">Processing</span>", source);
    }

    [Fact]
    public void Browse_ingestion_badge_states_embeddings_only_scope()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("reflects embeddings only", source);
    }

    [Fact]
    public void Browse_search_states_it_is_a_metadata_filter()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("Searches file metadata", source);
        Assert.Contains("not document content", source);
    }

    [Fact]
    public void Browse_search_has_info_icon()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("ToggleSearchInfo", source);
        Assert.Contains("How does Browse search work?", source);
    }

    [Fact]
    public void Browse_search_info_points_to_search_center_for_content_search()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("Search Center", source);
        Assert.Contains("matches by meaning across document chunks", source);
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
