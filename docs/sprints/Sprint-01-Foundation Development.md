
## Authority

This document defines the currently authorized work.

All previously authorized phases are considered completed and closed.

Any restrictions contained in earlier sprint documents are superseded by this document.

OpenHands shall execute only the phases identified below.

# Current Sprint

Sprint: Foundation Development

Status: Active

Only work on:

- Phase 0
- Phase 1

No work is authorized outside these phases.

---

## Objectives

### Phase 0

Create:

- Solution structure
- Source folders
- Test folders
- Docker folder
- Documentation folder

Create:

- README.md
- Architecture.md
- Roadmap.md
- Development-Guidelines.md

Create:

- CI/CD baseline
- Build pipeline
- Test pipeline

---

### Phase 1

Projects:

- Aletheia.Foundation
- Aletheia.Contracts

Implement:

#### Domain Core

- Entity
- AggregateRoot
- ValueObject
- DomainEvent

#### Shared Types

- Result<T>
- PagedResult<T>

#### Validation

- ValidationResult
- ValidationException

#### Context

- CorrelationContext
- SecurityContext
- TenantContext

#### Audit Models

- AuditInfo
- AuditActor

#### Exceptions

- DomainException
- ValidationException
- SecurityException

---

## Testing Requirements

Create:

- Foundation.UnitTests

Coverage Target:

80% minimum

---

## Out Of Scope

Do NOT begin:

- Repository Platform
- RAGS
- HXP
- Neo4j
- pgvector
- MinIO
- PostgreSQL

Only Foundation work is authorized.

---

## Sprint Exit Criteria

- Builds successfully
- Tests pass
- Documentation updated
- CI pipeline green
- Coverage >= 80%

