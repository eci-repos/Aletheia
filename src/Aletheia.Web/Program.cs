using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Aletheia.Web;
using Aletheia.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<CopilotStateService>();
builder.Services.AddScoped<RecentGraphContextService>();
builder.Services.AddScoped<SearchScopeStateService>();
builder.Services.AddScoped<AuthenticationStateProvider, AuthService>();
builder.Services.AddScoped<AuthService>(sp => (AuthService)sp.GetRequiredService<AuthenticationStateProvider>());
builder.Services.AddTransient<BearerTokenHandler>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
void ConfigureRepositoryApi(IServiceProvider services, HttpClient client)
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
    var navigation = services.GetRequiredService<NavigationManager>();
    client.BaseAddress = ResolveApiBaseAddress(apiBaseUrl, GetBrowserBaseUri(services) ?? navigation.BaseUri);
    client.Timeout = TimeSpan.FromMinutes(30);
}

builder.Services.AddHttpClient("RepositoryApiAnonymous", ConfigureRepositoryApi);
builder.Services.AddHttpClient("RepositoryApi", ConfigureRepositoryApi)
    .AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddScoped<RepositoryApiClient>();

await builder.Build().RunAsync();

static Uri ResolveApiBaseAddress(string? apiBaseUrl, string appBaseUri)
{
    var hostBaseAddress = new Uri(appBaseUri);
    if (string.IsNullOrWhiteSpace(apiBaseUrl))
    {
        return hostBaseAddress;
    }

    return Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var absolute)
        ? absolute
        : new Uri(hostBaseAddress, apiBaseUrl);
}

static string? GetBrowserBaseUri(IServiceProvider services)
{
    if (services.GetService<IJSRuntime>() is not IJSInProcessRuntime jsRuntime)
    {
        return null;
    }

    try
    {
        return jsRuntime.Invoke<string>("aletheia.getBaseUri");
    }
    catch
    {
        return null;
    }
}
