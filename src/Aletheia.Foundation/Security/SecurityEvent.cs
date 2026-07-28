namespace Aletheia.Foundation.Security;

public enum SecurityEventType
{
    AuthenticationSuccess,
    AuthenticationFailure,
    AuthorizationFailure,
    RoleAssigned,
    RoleRemoved,
    UserCreated,
    UserDisabled,
    UserEnabled,
    PasswordReset,
    UserDeleted,
    TokenRefreshed,
    TokenRevoked,
    SessionExpired,
    AdministrativeAction
}

public sealed class SecurityEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    public SecurityEventType EventType { get; }
    public string UserId { get; }
    public string? Username { get; }
    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public string? Resource { get; }
    public string? Action { get; }
    public string? Details { get; }
    public bool Success { get; }
    public string? Error { get; }

    public SecurityEvent(
        SecurityEventType eventType,
        string userId,
        string? username = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? resource = null,
        string? action = null,
        string? details = null,
        bool success = true,
        string? error = null)
    {
        EventType = eventType;
        UserId = userId ?? "anonymous";
        Username = username;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Resource = resource;
        Action = action;
        Details = details;
        Success = success;
        Error = error;
    }
}
