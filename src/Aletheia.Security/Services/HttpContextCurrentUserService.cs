using System.Security.Claims;
using Aletheia.Foundation.Security;
using Microsoft.AspNetCore.Http;

namespace Aletheia.Security.Services;

public sealed class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public UserIdentity? CurrentUser
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                return null;
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var username = user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
            var displayName = user.FindFirst("display_name")?.Value ?? username;
            var identityProvider = user.FindFirst("identity_provider")?.Value ?? "Local";
            var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            var claims = user.Claims
                .Where(c => c.Type != ClaimTypes.NameIdentifier && c.Type != ClaimTypes.Name && c.Type != ClaimTypes.Email && c.Type != ClaimTypes.Role)
                .ToDictionary(c => c.Type, c => c.Value);

            return new UserIdentity(userId, username, email, displayName, roles, identityProvider, claims.AsReadOnly());
        }
    }

    public bool IsAuthenticated => CurrentUser != null;

    public bool HasRole(string role)
    {
        return _httpContextAccessor.HttpContext?.User.IsInRole(role) ?? false;
    }

    public bool HasAnyRole(params string[] roles)
    {
        if (roles.Length == 0)
        {
            return true;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        return user != null && roles.Any(user.IsInRole);
    }

    public bool HasAllRoles(params string[] roles)
    {
        if (roles.Length == 0)
        {
            return true;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        return user != null && roles.All(user.IsInRole);
    }
}
