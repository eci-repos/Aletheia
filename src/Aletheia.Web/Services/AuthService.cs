using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace Aletheia.Web.Services;

public sealed class AuthService : AuthenticationStateProvider
{
    private const string StorageKey = "aletheia.auth";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IJSRuntime _jsRuntime;
    private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());
    private AuthSession? _session;
    private bool _loaded;

    public AuthService(IHttpClientFactory httpClientFactory, IJSRuntime jsRuntime)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    public bool IsAuthenticated => _currentUser.Identity?.IsAuthenticated ?? false;

    public string? UserName => _currentUser.Identity?.Name;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return new AuthenticationState(_currentUser);
    }

    public async Task<AuthLoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return AuthLoginResult.Failure("Username and password are required.");
        }

        var client = _httpClientFactory.CreateClient("RepositoryApiAnonymous");
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password
        }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return AuthLoginResult.Failure(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
        }

        var session = await response.Content.ReadFromJsonAsync<AuthSession>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (session is null || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return AuthLoginResult.Failure("Authentication response was invalid.");
        }

        await SetSessionAsync(session).ConfigureAwait(false);
        return AuthLoginResult.Success();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(_session?.RefreshToken) && !string.IsNullOrWhiteSpace(_session.AccessToken))
        {
            var client = _httpClientFactory.CreateClient("RepositoryApiAnonymous");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

            try
            {
                await client.PostAsJsonAsync("/api/auth/revoke", new
                {
                    refreshToken = _session.RefreshToken
                }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Local sign-out must still clear stale credentials if the API is unavailable.
            }
        }

        await ClearSessionAsync().ConfigureAwait(false);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);

        if (_session is null)
        {
            return null;
        }

        if (_session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _session.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(_session.RefreshToken))
        {
            return _session.AccessToken;
        }

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient("RepositoryApiAnonymous");
            response = await client.PostAsJsonAsync("/api/auth/refresh", new
            {
                refreshToken = _session.RefreshToken
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return _session.AccessToken;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return _session.AccessToken;
            }

            var refreshed = await response.Content.ReadFromJsonAsync<AuthSession>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
            {
                return _session.AccessToken;
            }

            await SetSessionAsync(refreshed with { User = _session.User }).ConfigureAwait(false);
            return _session?.AccessToken;
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return;
        }

        var session = JsonSerializer.Deserialize<AuthSession>(stored, JsonOptions);
        if (session is null || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _session = session;
            _currentUser = CreatePrincipal(session.User);
            return;
        }

        _session = session;
        _currentUser = CreatePrincipal(session.User);
    }

    private async Task SetSessionAsync(AuthSession session)
    {
        _session = session;
        _currentUser = CreatePrincipal(session.User);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(session, JsonOptions)).ConfigureAwait(false);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async Task ClearSessionAsync()
    {
        _session = null;
        _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey).ConfigureAwait(false);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal CreatePrincipal(AuthUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Username))
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("display_name", user.DisplayName),
            new("identity_provider", user.IdentityProvider)
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return payload?.Error ?? $"Authentication failed with status {(int)response.StatusCode}.";
        }
        catch
        {
            return $"Authentication failed with status {(int)response.StatusCode}.";
        }
    }
}

public sealed record AuthLoginResult(bool IsSuccess, string? Error)
{
    public static AuthLoginResult Success() => new(true, null);

    public static AuthLoginResult Failure(string error) => new(false, error);
}

internal sealed record AuthSession(AuthUser User, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

internal sealed record AuthUser(
    string UserId,
    string Username,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    string IdentityProvider);

internal sealed record ErrorPayload(string Error);
