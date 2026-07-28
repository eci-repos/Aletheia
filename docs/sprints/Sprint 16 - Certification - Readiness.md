# Current Sprint

Sprint: Platform Certification & Readiness

Status: Active

## Objective

Validate, certify, and prepare Aletheia for Production Hardening.

This sprint introduces no new platform functionality.

The purpose of this sprint is to verify:

- Architecture Compliance
- Dependency Injection Compliance
- Repository Integration
- RAGS Integration
- GraphRAG Integration
- LazyGraphRAG Integration
- Semantic Kernel Integration
- Security Readiness
- Documentation Readiness
- Production Readiness

This is a validation and certification sprint.

---

# Authority

The repository state is the source of truth.

Do not re-implement completed work.

Do not introduce new features.

Do not begin Production Hardening.

Fix only:

- Build defects
- Test defects
- Integration defects
- Wiring defects
- Configuration defects

found during validation.

---

# Architecture Certification

Verify compliance with:

- Clean Architecture
- Hexagonal Architecture
- Domain Driven Design
- SOLID Principles
- Dependency Inversion

Validate:

```text
Domain → Infrastructure references = 0

Business Logic in Controllers = 0

Business Logic in UI = 0

Direct Infrastructure Dependencies = 0
```

Provide evidence for all findings.

---

# Dependency Injection Audit

Inspect all DI registrations.

Validate:

```text
IRepositoryService

IRagsService

IGraphRagService

ILazyGraphRagService

IAIService

IChatService

IEmbeddingService

ITaxonomyProvider

IOntologyProvider

IGraphProvider

IGraphService

IGraphQueryService

IGraphAdminService
```

Confirm:

```text
All services registered

No duplicate registrations

No missing registrations

No direct instantiations
```

---

# Semantic Kernel Certification

Verify:

```text
Semantic Kernel is the default AI orchestration framework
```

Validate:

```text
Copilot is no longer the default

Ollama is configured as default provider

kimi-k2.7-code:cloud is configured as default model

Multi-provider configuration operational
```

Verify:

```text
Configuration-driven provider selection
```

Provide evidence.

---

# Repository Certification

Validate complete workflow:

```text
Upload Artifact
        ↓
Persist Metadata
        ↓
Store Content
        ↓
Version Storage
        ↓
