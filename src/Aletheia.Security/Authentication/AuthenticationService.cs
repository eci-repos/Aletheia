using System.Security.Claims;
using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Aletheia.Security.Services;
using Microsoft.Extensions.Logging;

namespace Aletheia.Security.Authentication;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IEnumerable<IIdentityProvider> _identityProviders;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IUserStore _userStore;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IEnumerable<IIdentityProvider> identityProviders,
        JwtTokenService jwtTokenService,
        IRefreshTokenStore refreshTokenStore,
        IUserStore userStore,
        ILogger<AuthenticationService> logger)
    {
        _identityProviders = identityProviders;
        _jwtTokenService = jwtTokenService;
        _refreshTokenStore = refreshTokenStore;
        _userStore = userStore;
        _logger = logger;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password, string? identityProvider = null, CancellationToken cancellationToken = default)
    {
        var provider = string.IsNullOrWhiteSpace(identityProvider)
            ? _identityProviders.FirstOrDefault(p => p.Name == "Local")
            : _identityProviders.FirstOrDefault(p => p.Name == identityProvider);

        if (provider == null)
        {
            _logger.LogWarning("Authentication failed for {Username}: identity provider '{Provider}' not found.", username, identityProvider ?? "Local");
            return AuthenticationResult.Failure("Identity provider not found.");
        }

        var isValid = await provider.ValidateCredentialsAsync(username, password, cancellationToken).ConfigureAwait(false);
        if (!isValid)
        {
            _logger.LogWarning("Authentication failed for {Username}: invalid credentials.", username);
            return AuthenticationResult.Failure("Invalid credentials.");
        }

        var user = await provider.ResolveIdentityAsync(username, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            _logger.LogWarning("Authentication failed for {Username}: user identity could not be resolved.", username);
            return AuthenticationResult.Failure("User identity could not be resolved.");
        }

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(_jwtTokenService.RefreshTokenLifetime);

        await _refreshTokenStore.AddAsync(new RefreshTokenEntry(refreshToken, user.UserId, expiresAt), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Authentication succeeded for {Username} via {Provider}.", username, provider.Name);
        return AuthenticationResult.Success(user, accessToken, refreshToken, expiresAt);
    }

    public async Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var entry = await _refreshTokenStore.GetAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        if (entry == null || !entry.IsValid)
        {
            _logger.LogWarning("Token refresh failed: invalid or expired refresh token.");
            return AuthenticationResult.Failure("Invalid or expired refresh token.");
        }

        var userRecord = await _userStore.GetByIdAsync(entry.UserId, cancellationToken).ConfigureAwait(false);
        if (userRecord == null || !userRecord.IsEnabled)
        {
            _logger.LogWarning("Token refresh failed: user not found or disabled.");
            return AuthenticationResult.Failure("User not found or disabled.");
        }

        var identity = new UserIdentity(
            userRecord.UserId,
            userRecord.Username,
            userRecord.Email,
            userRecord.DisplayName,
            userRecord.Roles,
            "Local");

        var newAccessToken = _jwtTokenService.GenerateAccessToken(identity);
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(_jwtTokenService.RefreshTokenLifetime);

        await _refreshTokenStore.RevokeAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        await _refreshTokenStore.AddAsync(new RefreshTokenEntry(newRefreshToken, identity.UserId, expiresAt), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Token refreshed for user {UserId}.", identity.UserId);
        return AuthenticationResult.Success(identity, newAccessToken, newRefreshToken, expiresAt);
    }

    public async Task<Result> RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        await _refreshTokenStore.RevokeAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Refresh token revoked.");
        return Result.Success();
    }

    public async Task<Result> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var principal = _jwtTokenService.ValidateToken(accessToken);
        if (principal == null)
        {
            return Result.Failure("Invalid or expired token.");
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure("Token does not contain user identifier.");
        }

        var userRecord = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (userRecord == null || !userRecord.IsEnabled)
        {
            return Result.Failure("User not found or disabled.");
        }

        return Result.Success();
    }
}
