# 5. Wiki

The Wiki (`/wiki`) is the end-user knowledge surface: plain-language **document briefs** generated for every ingested document, plus editable wiki pages with a lifecycle.

## Document briefs

- Generated automatically after ingestion by a `DocumentBriefs` background job (see the Activity panel).
- Each brief opens with the document's nature/purpose (from its opening chunks), then follows the **canonical template's ordered sections**, grounded and cited.
- Briefs are per-document pages (`generated_from = 'document-brief'`).

## Browsing and searching

- Search topics with the search box; recent pages are shown.
- Document briefs are ordered first in search/recent. Community summaries (internal GraphRAG content) are excluded from the user-facing Wiki.

## Page lifecycle

| Status | Meaning |
|---|---|
| Generated | Created automatically from the document. |
| Reviewed | Reviewed by a user. |
| Approved | Approved for use. |
| NeedsReview | Flagged for attention. |
| Stale | Source document changed since generation — regenerate. |

- Users can edit page bodies; edits create history revisions.
- Each page shows related topics and related pages.
- Source-change detection marks pages **Stale** when the underlying document is updated (Sprint 56 flow).

## Regeneration

- Operators can regenerate briefs via the UI (internal-search mode) or `POST /api/wiki/briefs/regenerate` (one document with `{ sourceId, sourceName }`, or all documents with an empty body).

## Notes

- The user-facing surface is always **Wiki** (never "WRAGS"); WRAGS is the internal name.
- Wiki search/recent do not include GraphRAG community summaries — those stay internal.