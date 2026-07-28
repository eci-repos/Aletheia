namespace Aletheia.Foundation.Context;

public sealed class SecurityContext
{
    private readonly List<string> _roles;

    public SecurityContext(string userId, IEnumerable<string>? roles = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        UserId = userId;
        _roles = roles?.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct().ToList() ?? new List<string>();
    }

    public string UserId { get; }

    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    public bool HasRole(string role) => _roles.Contains(role);
}
