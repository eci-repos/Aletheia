using Aletheia.Foundation.Security;
using Microsoft.AspNetCore.Authorization;

namespace Aletheia.Security.Authorization;

public static class AuthorizationPolicies
{
    public const string Administrator = "Administrator";
    public const string PowerUser = "PowerUser";
    public const string Contributor = "Contributor";
    public const string Reader = "Reader";
    public const string Auditor = "Auditor";
    public const string AdminOrPowerUser = "AdminOrPowerUser";
    public const string WriteAccess = "WriteAccess";
    public const string AnyAuthenticated = "AnyAuthenticated";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Administrator, policy => policy.RequireRole(RoleDefinitions.Administrator));
        options.AddPolicy(PowerUser, policy => policy.RequireRole(RoleDefinitions.PowerUser, RoleDefinitions.Administrator));
        options.AddPolicy(Contributor, policy => policy.RequireRole(RoleDefinitions.Contributor, RoleDefinitions.PowerUser, RoleDefinitions.Administrator));
        options.AddPolicy(Reader, policy => policy.RequireRole(RoleDefinitions.Reader, RoleDefinitions.Contributor, RoleDefinitions.PowerUser, RoleDefinitions.Administrator, RoleDefinitions.Auditor));
        options.AddPolicy(Auditor, policy => policy.RequireRole(RoleDefinitions.Auditor, RoleDefinitions.Administrator));
        options.AddPolicy(AdminOrPowerUser, policy => policy.RequireRole(RoleDefinitions.Administrator, RoleDefinitions.PowerUser));
        options.AddPolicy(WriteAccess, policy => policy.RequireRole(RoleDefinitions.Administrator, RoleDefinitions.PowerUser, RoleDefinitions.Contributor));
        options.AddPolicy(AnyAuthenticated, policy => policy.RequireAuthenticatedUser());
    }
}
