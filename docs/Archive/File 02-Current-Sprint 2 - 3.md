# Current Sprint

Sprint: Repository Foundation

Status: Active

Previous Sprint:

- Phase 0 Completed
- Phase 1 Completed

Those phases are accepted and closed.

Authorized Phases:

- Phase 2 - Repository Contracts
- Phase 3 - Repository Core

No other phases are authorized.

---

# Phase 2 Objectives

Projects:

- Repository.Abstractions

Implement:

- IRepositoryService
- IStorageProvider
- IMetadataRepository
- IVersioningService
- IAuditService
- ISearchProvider

Create DTOs:

- UploadRequest
- UploadResponse
- DownloadRequest
- DownloadResponse
- FileMetadata
- FileDescriptor
- SearchRequest
- SearchResponse

Create domain contracts required for repository operations.

---

# Phase 3 Objectives

Projects:

- Repository.Domain
- Repository.Application

Implement:

- Upload Use Case
- Download Use Case
- Search Use Case
- Metadata Use Case
- Versioning Use Case

Business logic must remain infrastructure independent.

Business logic must be fully testable.

---

# Testing Requirements

Create:

- Repository.UnitTests

Coverage Target:

- Minimum 80%

---

# Out of Scope

Do NOT begin:

- Repository Infrastructure
- PostgreSQL
- MinIO
- Repository.API
- RAGS
- HXP
- Neo4j
- pgvector

These belong to future sprints.

---

# Exit Criteria

- Contracts implemented
- Repository business layer implemented
- Tests passing
- Documentation updated
- Coverage >= 80%