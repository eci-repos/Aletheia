namespace Aletheia.Foundation.Security;

public static class RoleDefinitions
{
    public const string Administrator = "Administrator";
    public const string PowerUser = "PowerUser";
    public const string Contributor = "Contributor";
    public const string Reader = "Reader";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlyCollection<string> AllRoles = new[]
    {
        Administrator,
        PowerUser,
        Contributor,
        Reader,
        Auditor
    };
}
