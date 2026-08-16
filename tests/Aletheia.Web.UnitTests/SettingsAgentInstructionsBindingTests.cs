namespace Aletheia.Web.UnitTests;

/// <summary>
/// Sprint 77: the admin Settings panel gains an "AI Agent Instructions" card — per-role system
/// prompts with a Customized / Config default badge, Save, and Reset-to-config-default. The
/// RepositoryApiClient exposes the three agent-instruction endpoints.
/// </summary>
public class SettingsAgentInstructionsBindingTests
{
    [Fact]
    public void Settings_page_renders_agent_instructions_card()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Settings/Index.razor"));

        Assert.Contains("AI Agent Instructions (Administrator)", page);
        Assert.Contains("Per-role system prompts that shape how each AI agent behaves", page);
    }

    [Fact]
    public void Settings_page_renders_source_badge_for_override_and_config()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Settings/Index.razor"));

        Assert.Contains("item.Source == \"override\" ? \"bg-warning text-dark\" : \"bg-light text-dark border\"", page);
        Assert.Contains("item.Source == \"override\" ? \"Customized\" : \"Config default\"", page);
    }

    [Fact]
    public void Settings_page_renders_save_and_reset_buttons()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Settings/Index.razor"));

        Assert.Contains("SaveAgentInstructionAsync(item)", page);
        Assert.Contains("ResetAgentInstructionAsync(item)", page);
        Assert.Contains("disabled=\"@(item.Source != \"override\")\"", page);
    }

    [Fact]
    public void Settings_page_loads_agent_instructions_for_admins()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Settings/Index.razor"));

        Assert.Contains("GetAgentInstructionsAsync()", page);
        Assert.Contains("_agentInstructions = (await ApiClient.GetAgentInstructionsAsync())?.ToList();", page);
    }

    [Fact]
    public void RepositoryApiClient_exposes_agent_instruction_endpoints()
    {
        var client = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));

        Assert.Contains("GetAgentInstructionsAsync", client);
        Assert.Contains("UpdateAgentInstructionAsync", client);
        Assert.Contains("ResetAgentInstructionAsync", client);
        Assert.Contains("\"/api/settings/agent-instructions\"", client);
        Assert.Contains("/api/settings/agent-instructions/", client);
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
