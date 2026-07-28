# Authorization Architecture

## Overview

Aletheia implements Role-Based Access Control (RBAC) using ASP.NET Core policy-based authorization.

## Roles

| Role | Description |
|------|-------------|
| **Administrator** | Full system access. Can create, disable, and delete users; assign any role; reset passwords. |
| **Power User** | Can create users, assign Contributor and Reader roles, disable users, and reset passwords. |
| **Contributor** | Can create and modify content. Cannot manage users or roles. |
| **Reader** | Read-only access to all standard APIs. |
| **Auditor** | Read-only plus access to audit logs and user details. |

## Authorization Policies

Policies are defined in `AuthorizationPolicies` and registered during `AddAletheiaSecurity`:

- `AuthenticatedUserPolicy` – any authenticated user.
- `ReaderPolicy` – requires `Reader`, `Contributor`, `PowerUser`, or `Administrator`.
- `ContributorPolicy` – requires `Contributor`, `PowerUser`, or `Administrator`.
- `PowerUserPolicy` – requires `PowerUser` or `Administrator`.
- `AdminPolicy` – requires `Administrator`.
- `AuditorPolicy` – requires `Auditor`, `PowerUser`, or `Administrator`.

## Endpoint Protection

All API controllers require authentication by default via `[Authorize]`. Anonymous access is explicitly granted only on:

- `POST /api/auth/login`
- `POST /api/auth/refresh`

Admin endpoints (`GraphAdminController`) additionally require `Administrator` or `PowerUser` roles.
