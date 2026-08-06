# Sprint 58 - Session Knowledge Theme Filtering

**Status:** Active

Full authority: `docs/sprints/Sprint-58 - Session Knowledge Theme Filtering.md` (created 2026-08-06). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 57 (Search Center Retrieval Quality and Troubleshooting) is **complete, committed, and pushed**: commits `9cdc131`, `bcb59f9`, and `7220987` (ingestion routing regression fix) are on `origin/master` (HEAD `7220987`). Remaining Sprint 57 verification: Docker smoke test (upload -> ingest -> search empty vs populated -> status endpoint; plus the Sprint 56 duplicate/update flow) - can run in parallel with Sprint 58 work.

## Objective

Let the end-user scope a Copilot session to a set of **knowledge themes** derived from the canonical document templates (e.g., Analysis, As-Built, As-Proposed, or a combination). Themes are chosen at session creation and shown as chips in the session header; the selection restricts which registered documents Copilot retrieves from for that session. No selection = current behavior (all documents).

## Authorized Work (summary - see sprint file for details)

1. **Theme model**: each canonical template in `docs/doc-templates` gains a `Theme:` metadata line (e.g., `3.0 - RFP Analysis` -> `Analysis`); `DocumentTemplateRegistry` exposes `TryGetTheme` / `ListThemes`.
2. **Persistence**: `file_metadata.template_name` + `file_metadata.theme` (idempotent migration + init.sql); ingestion persists them; read-time fallback derivation for pre-migration rows.
3. **Session filter (API + retrieval)**: `ChatSession.ThemeFilter` rides the existing chat path into `ChatRequestOptions`; `RetrievalRequest.SourceIds` + PgVectorStore vector/keyword predicates; engine resolves theme -> source set and enforces in all RAGS retrieval paths (intersects with Sprint 51 single-document scope, union for collections); `GET /api/knowledge/themes`.
4. **Web UI (UX option #1)**: theme picker on "New chat"; theme chips in the session header with mid-session edit; persisted with session state (localStorage v2 key) and sent on every chat call.
5. **Tests**: RAGS (theme resolution, engine enforcement, intersection, keyword fallback, backward compat), Repository (persistence, themes endpoint, ListSourceIdsByThemeAsync), Web CoreCompile; existing suites green.
6. **Docs**: Architecture (retrieval pipeline theme stage), AdministratorGuide (theme convention + endpoint), OperationsGuide (troubleshooting), Development-Guidelines (new templates must declare a theme), AGENTS, File 02/03, handoff.

## Acceptance Criteria

- Templates carry themes; new ingestions persist `template_name` + `theme`; pre-existing documents resolve via fallback.
- Theme picker at session creation; header chips; mid-session edits apply to subsequent turns; selection persists and is sent on every chat call.
- `[Analysis]` restricts retrieval to Analysis-themed sources; combinations take the union; a named document outside the filter yields no results from that document.
- Empty `ThemeFilter` behaves exactly as before the sprint.
- Repository / RAGS / Foundation suites green; Web C#/Razor compiles.

## Out of Scope

- GraphRAG/LazyGraphRAG internals and community summaries; a global knowledge-scope widget over Search Center / Wiki / Browse; rerankers; multi-tenant/ACL scoping; new session stores (sessions remain client-side).

---

## Progress (2026-08-06)

- Sprint 58 sprint file created; Sprint 57 closed out (routing fix commit `7220987` pushed). No implementation yet.
### Implementation Progress (2026-08-06)

- **D1 (Theme model)**: `Theme: Analysis` on the RFP Analysis template; registry `TryGetTheme`/`ListThemes` + `Uncategorized`; Development-Guidelines convention documented.
- **D2 (Persistence)**: `file_metadata.template_name`/`theme` migration + init.sql; `SetTemplateAsync`/`ListThemeRowsAsync`; ingestion persists template + theme after the gate.
- **D3 (Session filter + retrieval)**: `RetrievalRequest.SourceIds` with PgVectorStore set predicates (vector + keyword fallback); `KnowledgeThemeService` singleton + `GET /api/knowledge/themes`; theme filter rides session -> payload -> options/plan -> engine; engine enforces on all RAGS paths, intersects Sprint 51 single-document scope, and post-filters tool results; direct Copilot path intersects the named source.
- **D4 (Web UI)**: New-chat theme picker (themes + document counts), header theme chips with Edit, selection persisted (session storage v2) and sent on every plan/chat call.
- **D5**: RAGS 249 / Repository 113 / Foundation 55 green; Web CoreCompile 0 errors.
- **Remaining**: Docker smoke test (upload -> ingest -> themes endpoint -> theme-scoped vs all-themes Copilot session), commit.