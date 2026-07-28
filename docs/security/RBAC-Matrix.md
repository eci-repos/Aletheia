# RBAC Matrix

## Role Permissions

| Capability | Reader | Contributor | Power User | Administrator | Auditor |
|------------|--------|-------------|------------|---------------|---------|
| Read Repository APIs | ✓ | ✓ | ✓ | ✓ | ✓ |
| Read Graph APIs | ✓ | ✓ | ✓ | ✓ | ✓ |
| Read GraphRAG APIs | ✓ | ✓ | ✓ | ✓ | ✓ |
| Read LazyGraphRAG APIs | ✓ | ✓ | ✓ | ✓ | ✓ |
| Modify Content |   | ✓ | ✓ | ✓ |   |
| Create Users |   |   | ✓ | ✓ |   |
| Disable Users |   |   | ✓ | ✓ |   |
| Reset Passwords |   |   | ✓ | ✓ |   |
| Assign Contributor / Reader |   |   | ✓ | ✓ |   |
| Assign Any Role |   |   |   | ✓ |   |
| Delete Users |   |   |   | ✓ |   |
| Admin APIs |   |   | ✓ | ✓ |   |
| View Audit Logs |   |   |   | ✓ | ✓ |

## Role Hierarchy

```
Administrator
    └── Power User
            └── Contributor
                    └── Reader
```

`Auditor` is independent and grants read-only access plus audit visibility.
