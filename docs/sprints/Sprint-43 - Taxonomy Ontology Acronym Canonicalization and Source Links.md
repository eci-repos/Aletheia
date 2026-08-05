### Sprint 43 - Taxonomy/Ontology Acronym Canonicalization and Source Links
**Status:** Completed.

#### Objective
Fix clean-room ingestion so Taxonomy and Ontology use a single canonical `RFP` concept and show relationships from that concept to every matching source document.

---

#### Background
The user observed that `RFP` appeared as `Rpf` in Taxonomy/Ontology and that selecting the entity did not show links to the two available RFP documents. Since the term was present but not linked, the issue was not only retrieval; it was concept canonicalization and entity-to-source persistence.

---

#### Goals
* Canonicalize acronym labels at write/read time.
* Tolerate legacy `Rpf`/`Rfp` lookup aliases.
* Persist `found_in` links from lightweight topic ontology entities to source document entities.
* Include source filenames in topic extraction.
* Validate two-source RFP linkage with a regression test.

---

#### Implementation Notes
* Added `KnowledgeTermNormalizer` in RAGS abstractions.
* Updated `UploadedContentKnowledgeIndexer` to use canonical labels, include `sourceName` in topic extraction, and create topic `found_in` ontology links during lightweight ingestion.
* Updated `TaxonomyService` and `OntologyService` to normalize display labels.
* Updated `OntologyService.GetRelationshipsAsync(...)` to query legacy acronym aliases.
* Expanded indexer schema creation to cover fresh taxonomy/ontology tables.

---

#### Validation
* Focused normalizer and indexer tests passed with 9 tests.
* The indexer regression validates canonical `RFP` is linked to two distinct source documents in Taxonomy and Ontology when PostgreSQL is available.
* Full RAGS unit tests passed with 192 tests.
* Solution build passed with the existing AngleSharp NU1902 warning.
* API/Web containers were rebuilt and restarted.
* `/health/live`, `/health/ready`, and `http://localhost:8081/copilot` returned `200`.

---

#### Handoff Notes
After the user resets the container and uploads the two CMP RFP documents, Taxonomy/Ontology should display `RFP` and selecting it should show `found_in` links to both documents. If not, inspect `taxonomy_tags`, `taxonomy_tag_sources`, `ontology_entities`, and `ontology_relationships` for the canonical name `RFP`.
