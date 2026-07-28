namespace Aletheia.Foundation.Security;

public sealed class AuthenticationResult
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public UserIdentity? User { get; }
    public string? AccessToken { get; }
    public string? RefreshToken { get; }
    public DateTimeOffset? ExpiresAt { get; }

    private AuthenticationResult(bool isSuccess, string? error, UserIdentity? user, string? accessToken, string? refreshToken, DateTimeOffset? expiresAt)
    {
        IsSuccess = isSuccess;
        Error = error;
        User = user;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
    }

    public static AuthenticationResult Success(UserIdentity user, string accessToken, string refreshToken, DateTimeOffset expiresAt)
        => new(true, null, user, accessToken, refreshToken, expiresAt);

    public static AuthenticationResult Failure(string error)
        => new(false, error, null, null, null, null);
}
