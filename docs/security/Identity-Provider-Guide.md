# Identity Provider Guide

## Local Identity Provider (Default)

The `LocalIdentityProvider` is enabled by default and authenticates against the in-memory user store.

### Enabling Local Authentication

No additional configuration is required. The admin user is seeded automatically on first startup.

### Default Admin Account

| Field | Default Value | Override |
|-------|---------------|----------|
| Username | `admin` | N/A |
| Password | `Admin123!` | `ALETHEIA_ADMIN_PASSWORD` env var |
| Email | `admin@aletheia.local` | N/A |
| Roles | `Administrator` | N/A |

> **Security Warning:** Change the default admin password immediately after deployment using the `POST /api/auth/users/admin/reset-password` endpoint.

## Microsoft Entra ID (Azure AD)

To add Entra ID support, implement `IIdentityProvider`:

```csharp
public class EntraIdIdentityProvider : IIdentityProvider
{
    public string Name => "EntraID";

    public Task<Result<UserIdentity>> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken)
    {
        // Integrate with Microsoft.Identity.Web or MSAL
        throw new NotImplementedException();
    }
}
```

Register the provider in `ServiceCollectionExtensions` after the local provider.

## Token Validation

All access tokens are validated using ASP.NET Core JWT Bearer middleware with:

- Issuer signing key validation
- Issuer and audience validation
- Lifetime validation
- 5-minute clock skew tolerance
