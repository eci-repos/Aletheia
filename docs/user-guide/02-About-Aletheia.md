# 1. About Aletheia

Aletheia is an AI-native knowledge and document management platform. It stores your documents, makes them searchable by meaning, generates plain-language briefs, and answers questions grounded in your documents through the Copilot assistant.

## The knowledge estate model

| Concept | Meaning |
|---|---|
| **Document / Source** | An uploaded file that passed the canonical template gate. It becomes a knowledge "source". |
| **Canonical template** | The document kind definition in `docs/doc-templates` (e.g., `3.0 - RFP Analysis`). Every ingested document must match one; the template defines the sections a brief follows. |
| **Theme** | A category label on a template (e.g., Analysis, As-Built, As-Proposed). Themes scope Copilot sessions. |
| **Chunk** | A piece of a document used for retrieval. |
| **Embedding** | The numeric meaning-vector of a chunk used for semantic search. |
| **Document brief** | The plain-language Wiki page generated for each document, following the template's ordered sections, with citations. |
| **Community summary** | An internal graph-level summary used by GraphRAG (not shown to end users). |

## Roles

- **Administrator** — full access: user management, duplicates cleanup, re-embedding, operator modes, configuration.
- **User** — upload, browse, search, wiki, Copilot.
- Ask your administrator which role you have.

## Main surfaces

| Surface | Purpose |
|---|---|
| **Dashboard** | Entry point; document counts and recent briefs. |
| **Browse** | List and inspect uploaded documents, download, update (↻). |
| **Upload** | Add documents (duplicate detection and update mode included). |
| **Search** | Semantic search across the repository (Search Center). |
| **Wiki** | Document briefs and wiki pages with lifecycle status. |
| **Copilot** | Theme-scoped, cited Q&A over the repository. |
| **Metadata / Governance / Graph / Ontology / Taxonomy** | Operator and internal surfaces (see section 8). |

## How answers stay grounded

All retrieval paths keep evidence: results carry scores, retrieval strategy (`semantic`, `keyword`, `summary-entity`, `summary-community`), and citations back to source documents. Copilot is instructed to say when the repository has no relevant information rather than inventing an answer.