# Production Security Checklist

## Secrets Management

- [ ] Set `ALETHEIA_JWT_SECRET` to a cryptographically random string (minimum 32 bytes).
- [ ] Set `ALETHEIA_ADMIN_PASSWORD` to a strong, unique password.
- [ ] Remove any plain-text secrets from `appsettings.json` and `appsettings.Production.json`.
- [ ] Configure secrets via environment variables or Docker secrets.
- [ ] Rotate JWT secret before go-live.

## CORS Hardening

- [ ] Configure `Cors:AllowedOrigins` to the exact production origins only.
- [ ] Remove `http://localhost:5000` from allowed origins in production.
- [ ] Do not use `AllowAnyOrigin()` in production.

## Headers

- [ ] `X-Content-Type-Options: nosniff`
- [ ] `X-XSS-Protection: 1; mode=block`
- [ ] `X-Frame-Options: DENY`
- [ ] `Referrer-Policy: strict-origin-when-cross-origin`
- [ ] `Content-Security-Policy: default-src 'self'`
- [ ] `Permissions-Policy` restricts unused browser features
- [ ] HSTS enabled for non-development environments

## Authentication

- [ ] `UseAuthentication()` is placed before `UseAuthorization()` in the middleware pipeline.
- [ ] Default admin password changed after first login.
- [ ] Refresh token lifetime set appropriately (default 7 days).
- [ ] Access token lifetime not excessively long (default 60 minutes).

## Authorization

- [ ] `[Authorize]` applied to all controllers.
- [ ] `[AllowAnonymous]` used only on login and refresh endpoints.
- [ ] Admin endpoints protected with role-based `[Authorize(Roles = "Administrator")]`.

## Auditing

- [ ] `AuditLogMiddleware` is active and logging mutating operations.
- [ ] Authentication events are logged.
- [ ] Authorization failures are logged at warning level.

## Network

- [ ] HTTPS redirection is enabled.
- [ ] HTTP ports are disabled or redirect to HTTPS.
- [ ] Load balancer or reverse proxy terminates TLS with valid certificates.
