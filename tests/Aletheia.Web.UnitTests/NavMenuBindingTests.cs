namespace Aletheia.Web.UnitTests;

public class NavMenuBindingTests
{
    [Fact]
    public void Nav_menu_no_longer_has_metadata_entry()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/NavMenu.razor"));

        Assert.DoesNotContain("href=\"metadata\"", source);
        Assert.DoesNotContain(">Metadata</span>", source);
    }

    [Fact]
    public void Browse_still_deep_links_to_metadata_editor()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Browse.razor"));

        Assert.Contains("metadata?fileId=", source);
        Assert.Contains("title=\"Edit\"", source);
    }

    [Fact]
    public void Metadata_page_route_is_untouched()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/MetadataEditor.razor"));

        Assert.Contains("@page \"/metadata\"", source);
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
