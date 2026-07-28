
## Authority

This document defines the currently authorized work.

All previously authorized phases are considered completed and closed.

Any restrictions contained in earlier sprint documents are superseded by this document.

OpenHands shall execute only the phases identified below.

# Current Sprint

Sprint: Repository Contracts and Core

Status: Active

Authorized Phases:

- Phase 2
- Phase 3

## Objectives

Create repository contracts and repository business logic.

### Phase 2

Projects:

- Repository.Abstractions

Implement:

- IRepositoryService
- IStorageProvider
- IMetadataRepository
- IVersioningService
- IAuditService
- ISearchProvider

DTOs:

- UploadRequest
- UploadResponse
- DownloadRequest
- DownloadResponse
- FileMetadata
- FileDescriptor
- SearchRequest
- SearchResponse

### Phase 3

Projects:

- Repository.Domain
- Repository.Application

Implement:

- Upload Use Case
- Download Use Case
- Search Use Case
- Metadata Use Case
- Versioning Use Case

Business layer must be infrastructure independent.

## Out Of Scope

Do NOT implement:

- PostgreSQL
- MinIO
- API Layer
- RAGS
- HXP

## Exit Criteria

- Contracts implemented
- Business logic implemented
- Unit tests passing
- Coverage >= 80%

