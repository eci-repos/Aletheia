# 9. Troubleshooting & FAQ

## Search returns nothing

1. **Is the corpus empty?** Search Center tells you: "No documents have been ingested yet... check the Activity panel." Upload a document and wait for the upload job to end `Succeeded`/`Indexed` and the brief job to finish.
2. **Did ingestion fail?** Check the Activity panel and `/api/jobs`; a canonical-template mismatch stops ingestion ("no canonical document template found"). Fix the file name or register a template.
3. **Does the query match?** Try words from the document (e.g., `Scope of Work`). With content ingested, the keyword fallback returns results when vector scores are empty/below floor.
4. **Operator check:** `GET /api/rags/status` shows embedded chunk count, ingested source count, template-gate skips, extraction failures, and recent upload jobs.

## Copilot has no relevant information

- Confirm the document finished ingestion (Activity panel).
- **Theme filter**: if the session has themes selected and the document's theme is not among them, the document is excluded. Click **Edit** next to the chips and choose **All themes** (or add the theme).
- Rephrase with more words from the document.

## Upload says "Duplicate - already exists"

- The exact same content was already uploaded (SHA-256 fingerprint). Nothing was stored. Use **Browse → ↻** to update the existing document instead if you intended to replace it.

## Updating a document doesn't seem to change anything

- Same content → "no change" (intended).
- Changed content → version snapshot + re-ingestion + brief regeneration run in the background; wait for the Activity panel.

## Wiki brief looks stale

- The source document changed → the page is marked **Stale**; regenerate the brief (operator) or wait for the ingestion-triggered regeneration.

## Where is the theme picker?

- On a fresh Copilot session it opens automatically; otherwise use the **Knowledge** button in the conversation header.

## "Internal modes are missing" (WRAGS / GraphRAG / LazyGraphRAG in Search Center)

- Those are internal operator modes gated by `FeatureFlags:ShowInternalSearch` (default false). An administrator must enable the flag.

## General

**Q: Can I delete a document?**
A: Administrators can remove metadata/duplicates manually (`GET /api/files/duplicates` for candidates); there is no automatic deletion. See the Administrator Guide.

**Q: Are sessions shared across browsers?**
A: No — Copilot session state is stored per browser (localStorage).

**Q: What does the alignment-confidence percentage mean?**
A: A heuristic estimate of how well the answer aligns with retrieved context — not a calibrated correctness score.

**Q: Where do themes come from?**
A: The canonical templates (`docs/doc-templates`, `Theme:` first line). Ask an administrator to add/change themes.