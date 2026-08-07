# 6. Copilot

Copilot (`/copilot`) answers questions grounded in the repository, with citations, telemetry, and an optional execution plan you can review.

## Session scope (Knowledge themes)

- On a fresh session, the **Knowledge themes** picker appears (see `04-Knowledge-Themes.md`).
- Pick the themes that apply, or leave **All themes** for the full repository.
- The selection is shown as chips in the header and can be edited mid-session; it is persisted with the session and sent with every request.
- Since Sprint 59 a template can declare multiple themes (e.g. `Analysis, As-Built`), and a session selecting any of them includes the document. Copilot's filter stays session-scoped — it is independent of the Search Center theme scope (see `05-Search-Center.md`).

## Asking a question

1. Type your question and press Enter or click **Send**.
2. A plan may be proposed in the execution panel:
   - **Review** the plan steps, then click **Run** (or **Revise** / **Cancel**).
   - Plans that need approval require your confirmation; others can be configured to run automatically.
3. The **Progress panel** shows stages: Planning, retrieval, synthesis, citations.

## Understanding answers

Each assistant message includes telemetry:

- Elapsed time and estimated token throughput.
- Retrieved context count and citation count.
- Retrieval-based **alignment confidence** (a heuristic, not a calibrated correctness score).

## Behavior notes

- **Grounded-only**: Copilot is instructed to ground answers in retrieved documents. When the repository has no relevant information, it says so (`RefuseWhenNoContext` configurable) instead of inventing.
- **Source scoping**: questions that name a specific document retrieve only from that document; collection questions retrieve each matching document independently (Sprint 51).
- **Theme scoping**: a named document outside the session themes returns no results from that document.
- **Broad/corpus questions** may use GraphRAG/LazyGraphRAG internally with semantic fallback (Appendix A).

## Session management

- **New chat** starts a fresh session (opens the theme picker).
- **Chats panel** lists recent sessions stored in your browser; opening one restores it.
- Sessions are client-side state — they persist per browser, not server-side.

## Troubleshooting

- "I could not find relevant information..." — try more words from the document, check the session themes include the document's theme, or confirm the document finished ingestion (Activity panel).
- Plan hangs — check the Progress panel; operators can inspect `/api/jobs` and chat job telemetry.