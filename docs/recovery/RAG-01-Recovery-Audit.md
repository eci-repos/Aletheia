# Recovery Audit: Phases 15 (GraphRAG) and 16 (LazyGraphRAG)

**Audit Date:** 2026-07-19
**Auditor:** OpenHands Recovery Agent
**Authority Chain:** Charter > Current Sprint > Work Plan
**Scope:** Only Phase 15 and Phase 16 as defined in the Work Plan and authorized by `Sprint-08-GraphRAG - Recovery.md`.
**Directive:** No new functionality may be added until the audit is complete.

---

## 1. Document Review & Authority Interpretation

### Verified Documents
| Document | Revision | Key Constraints |
|---|---|---|
| `docs/File 00-Aletheia-Charter.md` | v2026-07-18 | Definition of Done: compiles, tests pass, docs updated, Docker verified, CI passes, acceptance criteria satisfied. |
| `docs/File 01-Aletheia-WorkPlan.md` | v2026-07-18 | Phase 15: Implement GraphRAG (`expansionHops`, `PgVectorStore`). Phase 16: Implement LazyGraphRAG (`OntologyProvider`, `IRagsService`, `maxExpanded`). |
| `docs/File 02-Current-Sprint.md` | v2026-07-19 | Active Sprint: `Sprint-08-GraphRAG - Recovery.md` — authorizes Phase 15 and Phase 16 review and remediation only. |
| `docs/File 03-openhands.md` | v2026-07-18 | Every completed feature requires README update, Architecture diagrams, and API documentation. Abstractions-first is mandatory. |

---

## 2. Audit Methodology

1. **Structural Scan:** Located all files matching `*GraphRag*`, `*LazyGraphRag*`.
2. **Build Verification:** Ran `dotnet build Aletheia.slnx`.
3. **Test Verification:** Ran `dotnet test <all .csproj>`.
4. **Code Review:** Opened every Phase 15/16 `.cs` file. Examined implementation against Work Plan deliverables.
5. **API Surface Review:** Confirmed Web ↔ API service wiring (`RepositoryApiClient.cs`, `SearchCenter.razor`, `NavMenu.razor`).
6. **Abstraction/DI Audit:** Checked interface segregation (SOLID / Charter requirements).

---

## 3. Current Implementation Evidence

### 3.1 Services

| File | Lines | Evidence |
|---|---|---|
| `src/RAGS.Application/GraphRAG/GraphRagService.cs` | ~165 | Full source located (Clean Architecture++). Uses constructor-injected `IRagsService`, `IGraphService`, `IChunkingPipeline`. Implements `RetrieveAsync(query, expansionHops)` and stub `ReplaceEmbeddingsAsync(...)`. |
| `src/RAGS.Application/LazyGraphRAG/LazyGraphRagService.cs` | ~225 | Full source located. Constructor-injects `IRagsService`, `IChunkingPipeline`, `IOntologyProvider`. Implements `IngestAsync(...)`, `RetrieveAsync(...)`, and stub `PostProcessAsync(...)`. Contains internal POJOs `LazyNode`, `LazyEdge`, `LazyChunk`. |

### 3.2 API Controllers

| File | Lines | Evidence |
|---|---|---|
| `src/Repository.API/Controllers/GraphRagController.cs` | ~50 | `GET /api/graphrag/retrieve` (takes `query`, `expansionHops`, `topK`). Injects concrete `GraphRagService`. |
| `src/Repository.API/Controllers/LazyGraphRagController.cs` | ~70 | `POST /api/lazygraphrag/ingest` and `GET /api/lazygraphrag/retrieve`. Injects concrete `LazyGraphRagService`. |

### 3.3 Web Layer

| File | Lines | Evidence |
|---|---|---|
| `src/Aletheia.Web/Pages/SearchCenter.razor` | ~269 | UI toggles between Semantic / GraphRAG / LazyGraphRAG. Calls `RepositoryApiClient.GraphRagRetrieveAsync(...)` (lines 110-114) and `LazyGraphRagRetrieveAsync(...)` (lines 116-119). **Requires** `new SearchResult(...)` constructor fix (see Section 5). |
| `src/Aletheia.Web/Services/RepositoryApiClient.cs` | ~125-180 | `GraphRagRetrieveAsync(...)` and `LazyGraphRagRetrieveAsync(...)` found at lines 222+ and 242+. **NO** `GraphRagIngestAsync(...)` found. `LazyGraphRagIngestAsync(...)` found at line 235. |
| `src/Aletheia.Web/Layout/NavMenu.razor` | — | Zero matches for `SearchCenter`, `graphrag`, `lazygraphrag` in content. **No navigation to SearchCenter.** |

### 3.4 DI Registration (`src/Repository.API/Program.cs`)

Line 91: `builder.Services.AddSingleton<GraphRagService>();` (concrete registration, no interface).
Line 92: `builder.Services.AddSingleton<LazyGraphRagService>();` (concrete registration, no interface).

---

## 4. Test Evidence

| Test Project | Count | Status | Phase 15/16 Tests? |
|---|---|---|---|
| `tests/Aletheia.Foundation.UnitTests` | 55 | ✅ Passed | None |
| `tests/Repository.UnitTests` | 79 | ✅ Passed | None |
| `tests/Repository.IntegrationTests` | 16 | ✅ Passed | None |
| `tests/RAGS.UnitTests` | 16 | ✅ Passed | None |

**Total Phase 15/16 specific tests identified:** `0`
**Evidence:** `Get-ChildItem tests/ -Recurse -Filter "*.cs" | Where-Object { $_.Name -match "GraphRag" }` returned `0` results in all repositories.

---

## 5. Build-Defects Resolved During This Recovery Session

Because the previous session terminated prematurely, uncommitted build breakers were discovered during this audit. They have been fixed so that the audit could continue:

1. **SearchCenter.razor GraphRAG branch** — was attempting to initialize `SearchResult` via `{ ... }` initializer syntax. `SearchResult` is a sealed class with a parameterized constructor only. **Fixed:** Changed to `new SearchResult(r.Chunk, r.Score)`.
2. **SearchCenter.razor LazyGraphRAG branch** — same `SearchResult` init error. **Fixed:** Changed to positional constructor.
3. **Missing project reference** — `src/Aletheia.Web/Aletheia.Web.csproj` did not reference `KnowledgeGraph.Abstractions`. **Fixed:** Added `<ProjectReference Include="..\KnowledgeGraph.Abstractions\KnowledgeGraph.Abstractions.csproj" />`.
4. **Missing Razor `@using`** — `src/Aletheia.Web/_Imports.razor` lacked `@using Aletheia.KnowledgeGraph.Abstractions.Models`. **Fixed:** Added missing directives.

> **Note:** The above items are now **Current State** (not Technical Debt), but should be captured so stakeholders know the auditor modified files during the recovery session.

---

## 6. Gap Analysis by Phase

### 6.1 Phase 15 — GraphRAG

| Work Plan Deliverable | Status | Evidence / Gap |
|---|---|---|
| Use `GraphRagService.cs` | ✅ Implemented | File exists and compiles. Registered in `Program.cs`. |
| Use `IChunkingPipeline` | ✅ Implemented | Injected into `GraphRagService`. |
| Use `IRagsService` | ✅ Implemented | Injected into `GraphRagService`. Retrieves chunk context. |
| Use `IGraphService` | ✅ Implemented | Injected into `GraphRagService`. Node/edge retrieval and neighbor expansion. |
| Use `expansionHops` | ✅ Implemented | Parameter flows from Controller → Service → `GetNeighborsAsync` loop. |
| Connect Impls in `SearchCenter.razor` | ✅ Implemented | `GraphRagRetrieveAsync(...)` wired. |
| Configure DI Registrations | ⚠️ Defect | Registered as **concrete** singleton. No interface abstraction exists. |
| Add `GraphRagIngestAsync` to `RepositoryApiClient` | ❌ **MISSING** | No ingest path for GraphRAG from Web layer. `LazyGraphRagIngestAsync` exists but GraphRAG counterpart does not. |
| Exit Criteria: GraphRAG operational | ⚠️ Partial | Build succeeds; search path works; **no tests**; no API ingest endpoint. |

### 6.2 Phase 16 — LazyGraphRAG

| Work Plan Deliverable | Status | Evidence / Gap |
|---|---|---|
| Use `LazyGraphRagService` | ✅ Implemented | File exists and compiles. |
| Use `OntologyProvider` | ✅ Implemented | Injected into `LazyGraphRagService`. |
| Use `IRagsService` | ✅ Implemented | Injected into `LazyGraphRagService`. Stores base index and is called via `_lazyGraphRagService`.
| Configure maxExpansion | ⚠️ Partial | `LazyGraphRagService.RetrieveAsync` uses `maxExpanded` internally but does not expose a parameter to control it. Controller takes a `topK` but not `maxExpanded`. |
| Connect in `SearchCenter.razor` | ✅ Implemented | `RepositoryApiClient.LazyGraphRagRetrieveAsync(...)` wired. |
| Exit Criteria: GraphRAG + LazyGraphRAG operational | ⚠️ Partial | Ingest and retrieve API endpoints present. Web retrieval works. **No tests**. No `maxExpanded` wiring. DI defect same as Phase 15. |

---

## 7. Abstraction & Architecture Gaps (Cross-Phase)

Per the **Aletheia Charter** and **openhands.md**, service abstractions are mandatory.

| Defect | Evidence | Severity |
|---|---|---|
| **No `IGraphRagService` interface** | `GraphRagService` is a bare class. Controller injects concrete class. | 🔴 High |
| **No `ILazyGraphRagService` interface** | `LazyGraphRagService` is a bare class. Controller injects concrete class. | 🔴 High |
| **`RepositoryApiClient` returns raw Abstractions types** | Returns `IReadOnlyList<SearchResult>`. Fine as a DTO, but client now tightly coupled to `RAGS.Abstractions`. Acceptable per DDD client boundary. | 🟡 Low |
| **`SearchCenter` directly references `GraphRAG`/`LazyGraphRAG` modes** | Hard-coded string literals (`"graphrag"`, `"lazygraphrag"`). Not a functional bug, but poor discoverability. | 🟡 Low |
| **No Navigation to SearchCenter** | `NavMenu.razor` has no link to the page implementing the brand-new GraphRAG/LazyGraphRAG toggles. | 🔴 High (UX) |

---

## 8. Recommendations & Elaborated Gap Tasks

To satisfy the Sprint exit criteria, the following remediation tasks must be executed **in order**:

### Priority 1: Interface Abstraction (Charter / openhands.md Compliance)
*Elaborate from tracker Item #2*
1. Create `IGraphRagService` in `src/RAGS.Abstractions/Interfaces/`.
2. Refactor `GraphRagService` : `IGraphRagService`.
3. Create `ILazyGraphRagService` in `src/RAGS.Abstractions/Interfaces/`.
4. Refactor `LazyGraphRagService` : `ILazyGraphRagService`.
5. Update `Repository.API/Program.cs` to register via interface (`AddSingleton<IGraphRagService, GraphRagService>()` etc.).
6. Update Controllers to inject interfaces.

### Priority 2: Missing Ingestion API for GraphRAG
*Elaborate from tracker Item #1*
1. Add `GraphRagIngestAsync(Guid sourceId, string content)` to `RepositoryApiClient.cs`.
2. Verify `GraphRagController` has corresponding `POST` endpoint (currently none exists; may require adding one, or confirming no ingest path is needed for GraphRAG if it reuses `IRagsService` ingestion like LazyGraphRAG does).

### Priority 3: Web Navigation / UX
*Elaborate from tracker Item #6*
1. Add `NavMenu.razor` entry for the `SearchCenter` page so users can discover the new search modes.

### Priority 4: Retrieve-Time Parameter Exposure
*Elaborate from tracker Item #4 / #5*
1. Wire `maxExpanded`/`expansionHops` (or an equivalent tunable) through Controller → Service → Web layer so the user-tunable knobs on `SearchCenter` are used properly.

### Priority 5: Automated Testing
*Elaborate from tracker Item #5*
1. Add `tests/RAGS.UnitTests/GraphRAG/GraphRagServiceTests.cs` exercising `RetrieveAsync` with mocked `IGraphService`.
2. Add `tests/RAGS.UnitTests/LazyGraphRAG/LazyGraphRagServiceTests.cs` exercising `IngestAsync` and `RetrieveAsync`.
3. Verify test + DRY principle (reuse `Chunk` and `GraphNode` mocks from existing test utilities if any).

### Priority 6: Stub Implementation Replacement
*Elaborate from tracker Item #3 & #4*
1. `GraphRagService.ReplaceEmbeddingsAsync(...)` currently returns `new List<SearchResult>()` — implement actual PgVectorStore embed-replacement logic.
2. `LazyGraphRagService.PostProcessAsync(...)` currently returns `new List<SearchResult>()` — implement semantic/graph hybrid ranking strategy.

> **Blocked until audit approved:** No code changes beyond this audit are authorized until the recovery audit is accepted.

---

## 9. Sign-off Statement

This audit confirms that **structural implementation of Phase 15 (GraphRAG) and Phase 16 (LazyGraphRAG) is largely present** but contains **critical architectural gaps** (missing interfaces, missing tests, missing navigation, and unimplemented stubs) that prevent it from satisfying the Aletheia Charter’s Definition of Done.

**Build Status:** ✅ Clean  
**Test Status:** ❌ Zero targeted tests for audited phases  
**Architecture Compliance:** ❌ Missing abstraction interfaces  
**API Surface:** ⚠️ Phase 16 ingest complete; Phase 15 ingest missing from web/API layer  

**Next Step:** Obtain stakeholder approval of this audit, then proceed with Task Tracker items in priority order.
