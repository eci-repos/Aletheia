namespace Aletheia.Foundation.Security;

public interface IIdentityProvider
{
    string Name { get; }

    Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<UserIdentity?> ResolveIdentityAsync(string username, CancellationToken cancellationToken = default);
}
