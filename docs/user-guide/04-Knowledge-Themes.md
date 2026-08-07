# 3. Knowledge Themes

Knowledge themes categorize your documents so you can scope a Copilot session — or the Search Center — to the documents that matter for a conversation or search.

## What a theme is

A theme is a category declared on a canonical template:

- `3.0 - RFP Analysis` → **Analysis**
- Templates can declare more than one theme, e.g. `Theme: Analysis, As-Built` (comma-separated). A document matching that template belongs to **both** themes.

Every ingested document therefore has one or more themes (persisted at ingestion; documents ingested before themes existed resolve their themes from the file name automatically). Documents with no matching template are **Uncategorized** — they are still searchable, but carry no theme until a template matches and an administrator promotes them.

## Setting up a session (UX option #1)

1. Open **Copilot**.
2. On a fresh session the **Knowledge themes** picker appears automatically; you can also open it any time with the **Knowledge** button in the conversation header.
3. Check the themes that apply (e.g., **Analysis**). The picker shows the registered-document count per theme; a document in multiple themes counts in each.
4. Click **Apply to session**.

The selection shows as chips in the session header, e.g. **Knowledge: Analysis**.

## Changing themes mid-session

- Click **Edit** next to the chips (or the **Knowledge** button) and apply a new selection.
- Changes apply to subsequent messages in the same session.

## Scoping Search Center

Since Sprint 59, Search Center has its own **theme scope** (separate from any Copilot session):

1. In **Search Center**, open the **Theme scope** filter above the search box.
2. Pick one or more themes (each shows its document count). Your selection is remembered between visits.
3. Semantic search results are restricted to documents in the selected themes; a **"Scoped to N themes"** indicator shows when a scope is active. Clear the scope to search everything again.

Graph retrieval modes and the Wiki ignore the theme scope (they stay full-corpus surfaces).

## What the filter does

- Copilot retrieval (vector search and keyword fallback) is restricted to documents whose theme is in the session selection.
- A combination selects the **union** of the themes' documents.
- If a question names a specific document that is **outside** the selected themes, Copilot returns no results from that document (it is excluded).
- **All themes** (empty selection) = the full repository = the behavior before themes existed.

## Notes

- The Wiki is not theme-filtered — it stays a full-corpus exploration surface.
- GraphRAG/LazyGraphRAG internal modes are not theme-filtered (out of scope; see Appendix A, section 8).
- The list of themes comes from the canonical templates; if you need a new theme, an administrator must add a template with that `Theme:` line and promote the affected documents (see the Administrator Guide).