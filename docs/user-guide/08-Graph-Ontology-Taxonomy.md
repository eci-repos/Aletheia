# 7. Graph Explorer / Ontology / Taxonomy

These are **operator/internal surfaces**, hidden from end users unless `FeatureFlags:ShowInternalSearch` is enabled (default false). When hidden, their API endpoints return HTTP 404 and the navigation hides the links.

## Graph Explorer

- Visualizes the knowledge graph stored in Neo4j: source nodes, entity nodes, typed relationships, and communities.
- Entity-to-source edges use the `found_in` relationship; source nodes are `Type == "Source"`.
- Useful for verifying ingestion, inspecting entities extracted from your documents, and understanding GraphRAG community structure.
- See Appendix A for how the graph is built.

## Ontology Explorer

- Inspects the ontology used for entity/term normalization and graph extraction.
- Helps operators see how domain concepts are represented before/after normalization.

## Taxonomy Explorer

- Browsable taxonomy of domain terms and stop-words configuration (Sprint 50).
- Affects term normalization used across retrieval and graph extraction.

## When you would use these

- Verifying that a newly uploaded document contributed graph nodes/edges.
- Auditing entity extraction quality.
- Troubleshooting graph-backed retrieval (broad Copilot questions).

For full details see `docs/AdministratorGuide.md` (GraphRAG smoke test) and Appendix A.