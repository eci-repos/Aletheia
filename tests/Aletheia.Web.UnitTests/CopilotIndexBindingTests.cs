namespace Aletheia.Web.UnitTests;

public class CopilotIndexBindingTests
{
    [Fact]
    public void Copilot_plan_preview_binds_status_message_as_expression()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("StatusMessage=\"@_planStatusMessage\"", source);
        Assert.DoesNotContain("StatusMessage=\"_planStatusMessage\"", source);
    }

    [Fact]
    public void Copilot_header_exposes_new_chat_reset()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("@onclick=\"ResetChatAsync\"", source);
        Assert.Contains("New chat", source);
    }

    [Fact]
    public void Copilot_hides_plan_preview_after_execution_starts()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("@if (IsPlanPreviewVisible)", source);
        Assert.Contains("private bool IsPlanPreviewVisible => _pendingPlan is not null && !_activeJobId.HasValue && _progress is null;", source);
        Assert.Contains("Execution queued. Progress will appear shortly.", source);
    }

    [Fact]
    public void Search_center_shows_all_rags_modes()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("SetSemanticMode", source);
        Assert.Contains("SetWragsMode", source);
        Assert.Contains(">GraphRAG</button>", source);
        Assert.Contains(">LazyGraphRAG</button>", source);
        Assert.Contains("SetGraphRagMode", source);
        Assert.Contains("SetLazyGraphRagMode", source);
        Assert.Contains("ApiClient.GraphRagRetrieveAsync", source);
        Assert.Contains("ApiClient.LazyGraphRagRetrieveAsync", source);
    }

    [Fact]
    public void Wiki_shows_all_rags_mode_buttons()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Wiki.razor"));

        Assert.Contains(">Wiki</button>", source);
        Assert.Contains(">Semantic</button>", source);
        Assert.Contains(">GraphRAG</button>", source);
        Assert.Contains(">LazyGraphRAG</button>", source);
    }

    [Fact]
    public void Copilot_mirrors_chat_progress_to_activity_log()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("ActivityLog.Begin(\"Copilot\", \"Chat request\"", source);
        Assert.Contains("MirrorProgressMessagesToActivity(progress)", source);
        Assert.Contains("Sending request to chat agent", File.ReadAllText(FindRepoFile("src/RAGS.Application/Planning/ChatExecutionEngine.cs")));
    }

    [Fact]
    public void Activity_panel_polls_chat_jobs()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor"));
        var client = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));
        var activity = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/ActivityLogService.cs"));

        Assert.Contains("GetChatJobsAsync", source);
        Assert.Contains("UpsertChatJob", source);
        Assert.Contains("GetChatJobsAsync", client);
        Assert.Contains("public void UpsertChatJob(ChatJobSnapshot job)", activity);
    }

    [Fact]
    public void Activity_panel_can_copy_trace_to_clipboard()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/ActivityPanel.razor"));
        var index = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/index.html"));

        Assert.Contains("Copy trace", source);
        Assert.Contains("Copy all", source);
        Assert.Contains("CopyJobTraceAsync(job)", source);
        Assert.Contains("BuildTraceText(job)", source);
        Assert.Contains("BuildTraceText(null)", source);
        Assert.Contains("aletheia.copyText", source);
        Assert.Contains("navigator.clipboard.writeText", index);
    }

    [Fact]
    public void Copilot_planning_errors_surface_api_details()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));
        var client = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));

        Assert.DoesNotContain("_chatError = \"Unable to create execution plan.\"", page);
        Assert.Contains("Planning failed: the API returned no execution plan.", page);
        Assert.Contains("Copilot plan creation", client);
        Assert.Contains("POST /api/copilot/plan", client);
        Assert.Contains("throw new HttpRequestException(await BuildApiFailureAsync", client);
    }

    [Fact]
    public void Jwt_secret_resolution_prefers_environment_override()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Security/ServiceCollectionExtensions.cs"));
        var environmentIndex = source.IndexOf("Environment.GetEnvironmentVariable(envKey)", StringComparison.Ordinal);
        var configurationIndex = source.IndexOf("configuration[configKey]", StringComparison.Ordinal);

        Assert.True(environmentIndex >= 0);
        Assert.True(configurationIndex > environmentIndex);
    }

    [Fact]
    public void Copilot_approval_modal_has_dont_ask_again_checkbox()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("copilot-dont-ask-again", page);
        Assert.Contains("Don't ask again", page);
        Assert.Contains("@bind=\"_dontAskAgain\"", page);
        Assert.Contains("private bool _dontAskAgain = false;", page);
    }

    [Fact]
    public void Copilot_auto_approves_when_plan_does_not_require_approval()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("if (!_pendingPlan.RequiresApproval)", page);
        Assert.Contains("await ApprovePlan();", page);
    }

    [Fact]
    public void Copilot_dont_ask_again_writes_approval_preference()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));
        var client = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));

        Assert.Contains("UpdateMySettingsAsync", page);
        Assert.Contains("ChatApprovalSettings.RequireApproval", page);
        Assert.Contains("public async Task<bool> UpdateMySettingsAsync", client);
        Assert.Contains("public async Task<IReadOnlyDictionary<string, string>?> GetMySettingsAsync", client);
    }

    [Fact]
    public void Settings_api_exposes_admin_and_me_endpoints()
    {
        var controller = File.ReadAllText(FindRepoFile("src/Repository.API/Controllers/SettingsController.cs"));

        Assert.Contains("[Route(\"api/[controller]\")]", controller);
        Assert.Contains("[Authorize(Roles = RoleDefinitions.Administrator)]", controller);
        Assert.Contains("[HttpGet(\"me\")]", controller);
        Assert.Contains("[HttpPut(\"me\")]", controller);
        Assert.Contains("GetAppSettings", controller);
        Assert.Contains("UpdateMySettings", controller);
    }

    [Fact]
    public void Settings_foundation_has_tables_in_migration_and_init()
    {
        var migration = File.ReadAllText(FindRepoFile("src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-10-app-user-settings.sql"));
        var init = File.ReadAllText(FindRepoFile("scripts/init.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS app_settings", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS user_settings", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS app_settings", init);
        Assert.Contains("CREATE TABLE IF NOT EXISTS user_settings", init);
    }

    [Fact]
    public void Settings_page_renders_admin_gated_global_and_own_preferences()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Settings/Index.razor"));
        var nav = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Layout/NavMenu.razor"));

        Assert.Contains("@page \"/settings\"", page);
        Assert.Contains("My Preferences", page);
        Assert.Contains("Global Settings", page);
        Assert.Contains("AuthorizeView Roles=\"Administrator\"", page);
        Assert.Contains("ChatApprovalSettings.RequireApproval", page);
        Assert.Contains("ChatApprovalSettings.ForceApproval", page);
        Assert.Contains("AuthorizeView Roles=\"Administrator\"", nav);
        Assert.Contains("href=\"settings\"", nav);
    }

    [Fact]
    public void Settings_page_loads_my_and_app_settings_on_init()
    {
        var page = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Settings/Index.razor"));
        var client = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));

        Assert.Contains("ApiClient.GetMySettingsAsync()", page);
        Assert.Contains("ApiClient.GetAppSettingsAsync()", page);
        Assert.Contains("ApiClient.UpdateMySettingsAsync", page);
        Assert.Contains("ApiClient.UpdateAppSettingsAsync", page);
        Assert.Contains("public async Task<IReadOnlyDictionary<string, string>?> GetMySettingsAsync", client);
        Assert.Contains("public async Task<IReadOnlyDictionary<string, string>?> GetAppSettingsAsync", client);
        Assert.Contains("public async Task<bool> UpdateMySettingsAsync", client);
        Assert.Contains("public async Task<bool> UpdateAppSettingsAsync", client);
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
