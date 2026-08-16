namespace Aletheia.Web.UnitTests;

/// <summary>
/// Sprint 75: the Activity and Chats panels live in an in-flow right rail beside
/// &lt;main&gt; (mirroring the left sidebar) instead of position:fixed overlays, so
/// opening a panel PUSHES content instead of covering it. Collapsed = a 24px
/// vertical icon strip with count badges; below the breakpoint the rail returns
/// to a full-height overlay.
/// </summary>
public class RightRailBindingTests
{
    [Fact]
    public void MainLayout_renders_panels_inside_a_right_rail()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/MainLayout.razor"));

        Assert.Contains("<div class=\"right-rail\">", source);
        Assert.Contains("<ActivityPanel />", source);
        Assert.Contains("<ChatsPanel />", source);
    }

    [Fact]
    public void Panels_are_collapsed_by_default()
    {
        var activity = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor"));
        var chats = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ChatsPanel.razor"));

        // isOpen defaults to false (no initializer) and the aside class flips on it.
        Assert.Contains("private bool isOpen;", activity);
        Assert.Contains("@(isOpen ? \"open\" : \"collapsed\")", activity);
        Assert.Contains("private bool isOpen;", chats);
        Assert.Contains("@(isOpen ? \"open\" : \"collapsed\")", chats);
    }

    [Fact]
    public void Collapsed_strips_show_icon_and_count_badges()
    {
        var activity = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor"));
        var chats = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ChatsPanel.razor"));

        Assert.Contains("icon-activity", activity);
        Assert.Contains("activity-count", activity);
        Assert.Contains("icon-chats", chats);
        Assert.Contains("chats-count", chats);
    }

    [Fact]
    public void Collapsed_strips_use_the_same_narrow_width()
    {
        var activity = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor.css"));
        var chats = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ChatsPanel.razor.css"));

        // Sprint 78: the collapsed strip is just a few pixels more than the button
        // content (20px icon + 2px border) — 24px, not 42px — so it stops eating
        // ~18px of main. Both panels must use the same value so they cannot drift.
        Assert.Contains("width: 24px", activity);
        Assert.Contains("flex: 0 0 24px", activity);
        Assert.Contains("width: 24px", chats);
        Assert.Contains("flex: 0 0 24px", chats);
    }

    [Fact]
    public void Right_rail_css_is_in_flow_not_fixed()
    {
        var layout = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/MainLayout.razor.css"));
        var activity = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor.css"));
        var chats = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ChatsPanel.razor.css"));

        Assert.Contains(".right-rail", layout);
        Assert.Contains("flex-direction: column", layout);

        // The panels are in-flow flex rows on desktop — no position:fixed overlay.
        Assert.DoesNotContain("position: fixed", activity);
        Assert.DoesNotContain("position: fixed", chats);
        Assert.Contains("align-self: flex-end", activity);
        Assert.Contains("align-self: flex-end", chats);
    }

    [Fact]
    public void Responsive_fallback_returns_to_overlay()
    {
        var layout = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/MainLayout.razor.css"));
        var activity = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor.css"));
        var chats = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ChatsPanel.razor.css"));

        // Below the breakpoint the rail becomes a full-height overlay.
        Assert.Contains("@media (max-width: 640.98px)", layout);
        Assert.Contains("position: fixed", layout);
        Assert.Contains("z-index: 30", layout);

        // An open panel widens to nearly full width on narrow screens.
        Assert.Contains("calc(100vw - 12px)", activity);
        Assert.Contains("calc(100vw - 12px)", chats);
    }

    [Fact]
    public void App_css_defines_right_rail_icons()
    {
        var css = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/css/app.css"));

        Assert.Contains(".icon-activity", css);
        Assert.Contains(".icon-chats", css);
        Assert.Contains("--icon", css);
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
