namespace Aletheia.Web.UnitTests;

public class DashboardBindingTests
{
    [Fact]
    public void Dashboard_cards_have_pastel_tint_classes()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Dashboard.razor"));

        Assert.Contains("dashboard-action-upload", source);
        Assert.Contains("dashboard-action-browse", source);
        Assert.Contains("dashboard-action-search", source);
        Assert.Contains("dashboard-action-wiki", source);
        Assert.Contains("dashboard-action-copilot", source);
    }

    [Fact]
    public void Dashboard_tint_css_defines_light_backgrounds()
    {
        var css = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Dashboard.razor.css"));

        Assert.Contains(".dashboard-action-upload", css);
        Assert.Contains(".dashboard-action-browse", css);
        Assert.Contains(".dashboard-action-search", css);
        Assert.Contains(".dashboard-action-wiki", css);
        Assert.Contains(".dashboard-action-copilot", css);
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
