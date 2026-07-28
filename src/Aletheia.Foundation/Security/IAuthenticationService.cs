using Aletheia.Foundation.Shared;

namespace Aletheia.Foundation.Security;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password, string? identityProvider = null, CancellationToken cancellationToken = default);

    Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<Result> RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<Result> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken = default);
}
