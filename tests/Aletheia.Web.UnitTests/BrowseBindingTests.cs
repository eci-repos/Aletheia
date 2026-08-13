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
        Assert.Contains("text-bg-success", source);
        Assert.Contains(">Ingested</span>", source);
        Assert.Contains("text-bg-warning", source);
        Assert.Contains(">Not ingested</span>", source);
    }

    [Fact]
    public void Browse_ingestion_badge_explains_missing_embeddings()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("ingestion job may have failed", source);
        Assert.Contains("chunk(s) embedded", source);
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
