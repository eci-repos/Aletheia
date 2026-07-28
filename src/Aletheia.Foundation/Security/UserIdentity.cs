namespace Aletheia.Foundation.Security;

public sealed class UserIdentity
{
    public string UserId { get; }
    public string Username { get; }
    public string Email { get; }
    public string DisplayName { get; }
    public IReadOnlyDictionary<string, string> Claims { get; }
    public IReadOnlyCollection<string> Roles { get; }
    public string IdentityProvider { get; }

    public UserIdentity(
        string userId,
        string username,
        string email,
        string displayName,
        IEnumerable<string> roles,
        string identityProvider,
        IReadOnlyDictionary<string, string>? claims = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        UserId = userId;
        Username = username;
        Email = email ?? string.Empty;
        DisplayName = displayName ?? username;
        Roles = roles?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
        IdentityProvider = identityProvider ?? "Local";
        Claims = claims ?? new Dictionary<string, string>().AsReadOnly();
    }

    public bool HasRole(string role) => Roles.Contains(role);

    public bool IsAdministrator => HasRole(RoleDefinitions.Administrator);
}
