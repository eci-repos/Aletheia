namespace Aletheia.Foundation.Security;

public interface ICurrentUserService
{
    UserIdentity? CurrentUser { get; }

    bool IsAuthenticated { get; }

    bool HasRole(string role);

    bool HasAnyRole(params string[] roles);

    bool HasAllRoles(params string[] roles);
}
