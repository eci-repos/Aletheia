# 10. Glossary

| Term | Definition |
|---|---|
| **Activity panel** | Web panel showing background job progress (uploads, ingestion, briefs, re-embedding). |
| **Canonical template** | A document-kind definition in `docs/doc-templates` (e.g., `3.0 - RFP Analysis`) with ordered sections; every ingested document must match one. |
| **Chunk** | A piece of a document used for retrieval (chunking splits documents into chunks). |
| **Citation** | The source file name(s) associated with a retrieved chunk. |
| **Community** | A cluster of related entities in the knowledge graph; community summaries power GraphRAG global search. |
| **Community summary** | An internal, bottom-up summary of a community. Not shown to end users. |
| **Content hash** | SHA-256 fingerprint of an upload used for duplicate detection. |
| **Document brief** | The user-facing Wiki page generated per document following its template sections, cited. |
| **Embedding** | A numeric meaning-vector of a chunk used for semantic search. |
| **found_in** | The relationship type linking an entity to its source document in the graph. |
| **GraphRAG** | Index-heavy retrieval pattern: builds a typed knowledge graph + community summaries, retrieves from graph structure (Appendix A). |
| **Ingestion** | The pipeline that makes a document searchable: template gate → extraction → chunking → embeddings (and graph seeds). |
| **Keyword fallback** | Lexical search used when vector scores are empty/below the configured floor. |
| **Knowledge theme** | A category on a canonical template (Analysis, As-Built, As-Proposed, ...) used to scope Copilot sessions. |
| **LazyGraphRAG** | Cost-shifting retrieval pattern: cheap indexing, budgeted query-time graph exploration (Appendix A). |
| **MinIO** | Object storage backing uploaded files. |
| **Neo4j** | The graph database backing the knowledge graph. |
| **pgvector** | PostgreSQL extension storing embeddings for vector search. |
| **Reembed** | Background job that regenerates embeddings for all sources. |
| **Retrieval strategy** | The path a result came from: `semantic`, `keyword`, `summary-entity`, `summary-community`. |
| **Source** | An ingested document (identified by `source_id`, equal to the file ID). |
| **Source scoping** | Deterministic restriction of retrieval to the document(s) named by a question (Sprint 51). |
| **Template gate** | The ingestion rule: documents must match a canonical template or ingestion stops. |
| **Theme filter** | Session-level restriction of Copilot retrieval to documents of selected themes (Sprint 58). |
| **Wiki** | The end-user knowledge surface (document briefs + editable pages). "WRAGS" is the internal name. |
| **WRAGS** | Internal name for Wiki Retrieval-Augmented Generation System. |