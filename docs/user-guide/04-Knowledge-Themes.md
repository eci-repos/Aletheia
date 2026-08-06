# 3. Knowledge Themes

Knowledge themes let you scope a Copilot session to the documents that matter for that conversation.

## What a theme is

A theme is a category assigned to a canonical template:

- `3.0 - RFP Analysis` → **Analysis**
- Future templates can declare their own themes, e.g. **As-Built**, **As-Proposed** (first line of the template file: `Theme: <Theme>`).

Every ingested document therefore has a theme (persisted at ingestion; documents ingested before themes existed resolve their theme from the file name automatically). Documents with no matching template are **Uncategorized**.

## Setting up a session (UX option #1)

1. Open **Copilot**.
2. On a fresh session the **Knowledge themes** picker appears automatically; you can also open it any time with the **Knowledge** button in the conversation header.
3. Check the themes that apply (e.g., **Analysis**). The picker shows the registered-document count per theme.
4. Click **Apply to session**.

The selection shows as chips in the session header, e.g. **Knowledge: Analysis**.

## Changing themes mid-session

- Click **Edit** next to the chips (or the **Knowledge** button) and apply a new selection.
- Changes apply to subsequent messages in the same session.

## What the filter does

- Copilot retrieval (vector search and keyword fallback) is restricted to documents whose theme is in the selection.
- A combination selects the **union** of the themes' documents.
- If a question names a specific document that is **outside** the selected themes, Copilot returns no results from that document (it is excluded).
- **All themes** (empty selection) = the full repository = the behavior before themes existed.

## Notes

- Themes do **not** filter Search Center or the Wiki — those stay full-corpus exploration surfaces.
- GraphRAG/LazyGraphRAG internal modes are not theme-filtered (out of scope; see Appendix A, section 8).
- The list of themes comes from the canonical templates; if you need a new theme, an administrator must add a template with that `Theme:` line and re-ingest.