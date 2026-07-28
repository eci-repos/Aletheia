# Authentication Architecture

## Overview

Aletheia supports production-grade authentication with JWT-based session management and pluggable identity providers.

## Supported Identity Providers

- **Local Identity** (`LocalIdentityProvider`): username/password validation against the in-memory user store.
- **Microsoft Entra ID (Azure AD)**: ready for extension via `IIdentityProvider` abstraction.

## Key Abstractions

| Interface | Responsibility |
|-----------|----------------|
| `IIdentityProvider` | Validates credentials and returns a `UserIdentity`. |
| `IAuthenticationService` | Authenticates users, issues/validates tokens, and manages refresh tokens. |
| `ICurrentUserService` | Exposes the currently authenticated user from `HttpContext`. |
| `IUserService` | Creates and manages user accounts. |

## Token Flow

1. Client calls `POST /api/auth/login` with username, password, and optional identity provider.
2. `AuthenticationService` delegates credentials to the configured `IIdentityProvider`.
3. On success, `JwtTokenService` issues a signed access token and a refresh token.
4. Client presents the access token in the `Authorization: Bearer <token>` header.
5. When the access token expires, client calls `POST /api/auth/refresh` with the refresh token.
6. `InMemoryRefreshTokenStore` validates the refresh token and issues a new pair.
7. Logout revokes the refresh token via `POST /api/auth/revoke`.

## Token Configuration

| Setting | Default | Source |
|---------|---------|--------|
| Secret | required | `Authentication:Jwt:Secret` or `ALETHEIA_JWT_SECRET` env var |
| Issuer | `Aletheia` | `Authentication:Jwt:Issuer` |
| Audience | `Aletheia.API` | `Authentication:Jwt:Audience` |
| Access Token Lifetime | 60 minutes | `Authentication:Jwt:AccessTokenLifetimeMinutes` |
| Refresh Token Lifetime | 7 days | `Authentication:Jwt:RefreshTokenLifetimeMinutes` |

## Startup Seeding

An `AdminSeederHostedService` creates the default `admin` user on first startup if it does not exist. The password is read from the `ALETHEIA_ADMIN_PASSWORD` environment variable. Change it immediately after first deployment.
