# 2. Managing Documents

## Upload

1. Open **Upload**, choose a file, submit.
2. The file name must identify its canonical template (e.g., `CMP 2026 - 3. RFP Analysis.docx` → `3.0 - RFP Analysis`). If no template matches, ingestion stops with a message; register a template or correct the file name.
3. After upload, the **Activity** panel tracks the background jobs: upload ingestion (`Succeeded` / `Indexed`), then `DocumentBriefs` (Wiki brief generation).

## Duplicate detection

- Uploads are fingerprinted with SHA-256 **before** anything is stored.
- Uploading identical content again returns a **duplicate** result: the Web shows **"Duplicate - already exists"**, nothing is stored or ingested, and the Activity panel logs a warning.
- Administrators can list rows sharing a content hash via `GET /api/files/duplicates` for manual cleanup (no automatic deletion).

## Updating a document

- In **Browse**, use the **↻ (update)** action to replace a document.
- The same content → "no change" (nothing happens).
- Changed content → a new named version is snapshotted under the same file ID, the content is replaced, ingestion re-runs for the same source, and the Wiki brief regenerates.
- **Versioning is metadata-level**: `GET /api/versions` lists history, but all versions share the single stored blob per file ID (documented limitation).

## Browse, download, metadata

- **Browse** lists uploaded documents; you can download a file and open its metadata editor.
- **Metadata** shows descriptor, content type, size, upload time, tags, content hash, and (Sprint 58) the canonical `template_name` and `theme`.

## Tips

- Name files consistently so template matching is reliable (`CMP 2026 - 3. RFP Analysis.docx`, `CMP 2022 - 3. RFP Analysis.docx`).
- Wait for the Activity panel to show Ready before expecting search results.