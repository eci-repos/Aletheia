# 4. Search Center

Search Center (`/search`) is the primary human-facing retrieval workbench. It searches across the chunks of every ingested document.

## Modes

| Mode | Meaning | Visibility |
|---|---|---|
| **Semantic** | Meaning-based vector search over document chunks. Primary mode. | Always visible |
| **WRAGS** | Saved wiki pages first, semantic fallback. | Internal (hidden unless `FeatureFlags:ShowInternalSearch=true`) |
| **GraphRAG** | Graph summaries + global search. | Internal (hidden by default) |
| **LazyGraphRAG** | Budgeted query-time graph exploration. | Internal (hidden by default) |

When internal modes are hidden, their API endpoints return HTTP 404 and the UI hides the controls.

## Reading results

Each result shows:

- **Rank** and **score** — how close the chunk matched your query.
- **Retrieval strategy** — `semantic` (vector), `keyword` (lexical fallback when vector scores were empty/below the floor), or graph strategies (`summary-entity`, `summary-community`).
- **Citations** — the source file name(s) the chunk belongs to.

## Empty results are explained

Instead of a generic "No results", Search Center tells you why:

- **Corpus empty**: "No documents have been ingested yet. Upload a document and wait for the Activity panel to show Ready, then retry." It also shows example queries (e.g., `Scope of Work`) for a registered RFP Analysis document.
- **Nothing matched**: the message suggests trying words from your document or asking Copilot.

Operators can open the **RAGS status** chip (visible when internal search is enabled) for counts and recent template-gate skips.

## Keyword fallback

When vector retrieval returns nothing (or the best score is below the configured `RAGS:MinimumScore`), Search Center falls back to a lexical search over chunk content and file names so relevant content is never silently missed. The result's retrieval strategy tells you which path was used.

## Tips

- Semantic search matches by meaning: `requirements` finds content about requirements even when the exact word differs.
- For word-sharing queries that return nothing, the keyword fallback usually brings results.
- Example queries for an RFP Analysis document: `Scope of Work`, `Project Summary`, `requirements`.