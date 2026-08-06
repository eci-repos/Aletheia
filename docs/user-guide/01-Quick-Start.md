# 0. Quick Start

Get productive in about five minutes.

## Prerequisites

- A browser and access to the Aletheia web application.
- A user account (ask your administrator). The default deployment seeds an administrator account with the password configured via `ALETHEIA_ADMIN_PASSWORD` (default `Admin123!` for local dev only — change it in production).

## Steps

1. **Log in** — open the application, sign in with your credentials.
2. **Upload a document** — open **Upload**, choose a file, and submit.
   - The file name should identify its canonical template (e.g., `CMP 2026 - 3. RFP Analysis.docx` matches the `3.0 - RFP Analysis` template). Documents that do not match a canonical template are not ingested.
   - Uploading the same content twice is detected as a duplicate and nothing is stored.
3. **Wait for ingestion** — open the **Activity** panel (bottom-right). The upload job should end in `Succeeded` / `Indexed`, then a `DocumentBriefs` job generates the Wiki brief. When the brief is ready, your document is searchable.
4. **Search** — open **Search**, type words from your document (e.g., `Scope of Work`), and review results with scores and citations.
5. **Read the Wiki** — open **Wiki** to read the automatically generated document brief for your document.
6. **Ask Copilot** — open **Copilot**:
   - On a fresh session the **Knowledge themes** picker appears; choose the themes that apply (e.g., **Analysis**) or leave **All themes** for the full repository.
   - Type your question. If a plan is proposed, review and click **Run**.
   - Answers include citations and telemetry (time, token estimates, context count, confidence).

## What happens after an upload (conceptually)

Document → canonical template match → text extraction → chunking → embeddings → semantic search → Wiki brief. Graph structures (GraphRAG/LazyGraphRAG) are built progressively; see Appendix A for details.

## Need help?

- See `10-Troubleshooting-and-FAQ.md`.
- Your administrator can check the Activity panel, `/api/jobs`, and the RAGS status chip in Search Center.