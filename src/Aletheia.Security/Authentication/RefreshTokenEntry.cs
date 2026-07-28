namespace Aletheia.Security.Authentication;

public sealed class RefreshTokenEntry
{
    public string Token { get; }
    public string UserId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsRevoked { get; set; }

    public RefreshTokenEntry(string token, string userId, DateTimeOffset expiresAt, bool isRevoked = false, DateTimeOffset? createdAt = null)
    {
        Token = token;
        UserId = userId;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        IsRevoked = isRevoked;
    }

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsValid => !IsRevoked && !IsExpired;
}
