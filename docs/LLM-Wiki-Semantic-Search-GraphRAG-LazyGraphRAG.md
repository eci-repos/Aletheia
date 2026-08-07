# LLM Wiki, Semantic Search, GraphRAG, and LazyGraphRAG: How They Work and Why They Matter

**Status:** Draft for discussion
**Scope:** Aletheia as built (Sprint 58). This is a technical discussion, not a user manual — the end-user guides live in `docs/user-guide/`.

---

## 1. Orientation: Four Retrieval Surfaces, One Knowledge Estate

Aletheia stores one corpus — the registered repository documents — and exposes it through four retrieval surfaces that differ in *when* intelligence is spent and *what* they are good at. Semantic Search spends its effort at query time on vector similarity over pre-embedded chunks. The LLM Wiki spends its effort at ingestion time to produce durable, human-readable pages. GraphRAG spends its effort at ingestion time to build a typed knowledge graph with precomputed summaries. LazyGraphRAG spends almost nothing at ingestion time and does its graph exploration on demand, within a strict budget. The four are not competitors; they are a ladder of cost and capability that the product composes — Search Center exposes them as modes, Copilot selects among them per question, and the Wiki sits on top as the durable, curated surface.

Underneath all four sits a guarantee that makes them coherent: **every document in the estate is in a canonical form**. A document is not a blob of text; it is an instance of a known kind with a known structure, a known theme, and a known provenance. Section 2 explains why that guarantee matters and what it buys. Sections 3–6 walk each surface in turn — what a user gains and how it is built. Section 7 covers theme-based scoping, which is the user-facing expression of canonical form. Section 8 shows how the modes cooperate, and Section 9 closes with a short look at where the stack is headed.

---

## 2. Canonical Form: The Guarantee Under Everything

### What it means

Every ingested document is mapped to a **canonical template** in `docs/doc-templates` when one matches. The template declares the document's kind, its ordered sections, and its themes (a first-line `Theme:` metadata, e.g. `3.0 - RFP Analysis` → `Analysis`, or `Theme: Analysis, As-Built` for multiple). `DocumentTemplateRegistry` is the single source of truth: it resolves a file name to a canonical name, exposes the template's ordered sections, and derives the theme set. The gate at ingestion was **softened in Sprint 59** — `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` no longer stops when no canonical template is found; it ingests the document anyway with `template_status = Uncategorized` and no document brief, so a new document kind arriving before its template is written is never lost. A new template (and promotion of existing rows via `POST /api/knowledge/reevaluate`) unlocks the full experience. The estate is therefore not a free-for-all of arbitrary files; it is a curated set of known document kinds, each with a predictable shape — and nothing is silently refused.

### Why it is guaranteed

The guarantee is structural, not aspirational. The ingestion pipeline is a sequence of stages, each of which assumes the previous one produced canonical output: upload → SHA-256 fingerprint (duplicate detection) → text extraction → canonical-template resolution → RAGS ingestion (chunks + embeddings) → knowledge indexing (taxonomy/ontology + graph seed) → document brief (only for `Canonical` documents). Since Sprint 59 the template step marks rather than blocks: a non-canonical document is ingested and searchable with `template_status = Uncategorized`, and template-dependent features (briefs, per-section retrieval, themes) stay gated until the row is `Canonical`. Because the template set and status are persisted with the document (`file_metadata.template_name` + `theme` text[] + `template_status`), the canonical identity survives restarts and is available to every downstream consumer — retrieval, briefs, themes, promotion, and the graph.

Two further mechanisms keep the estate canonical over time. **Duplicate detection** (Sprint 56) fingerprints every upload with SHA-256 before any storage write; an exact duplicate returns HTTP 409 and is neither stored nor ingested, so the estate never accumulates duplicate noise. **Document updates** replace rather than accumulate: a changed file keeps its identity, gets a new version, and re-runs ingestion and brief regeneration, so the estate reflects the current document rather than a growing pile of revisions.

### What it buys

- **Comparability across documents.** When every RFP maps to the same template, "what does each RFP say about scope of work" is a well-defined question, because every RFP has a scope-of-work section. This is what makes cross-document synthesis — GraphRAG global search, collection summaries, comparative Copilot answers — meaningful rather than approximate.
- **Section-aware retrieval.** Because the sections are known, evidence can be collected per section. Document briefs retrieve per-section evidence and follow the template's ordered sections; Copilot can organize an answer by the document's actual structure instead of guessing.
- **Structured, grounded briefs.** The Wiki brief is only possible because the template tells the generator what to cover and in what order, and the opening chunks tell it what the document is for.
- **Theme derivation.** The theme is a property of the template, so filtering by theme is filtering by document kind — a governance mechanism, not a keyword heuristic.
- **Noise control and governance.** Since Sprint 59 the gate is softened: non-canonical documents are ingested but flagged `Uncategorized` (no brief, no theme) and promoted once a template exists — nothing is silently lost, while dedup stops duplicates and every document carries a known kind, template, and themes — the estate is auditable.

### The two knowledge representations

The same canonical chunks feed two complementary representations. A **PostgreSQL taxonomy/ontology** layer (`UploadedContentKnowledgeIndexer`) persists topics and entities as tags, and ontology entities with `found_in` and `co_occurs_with` relationships — a browsable, governance-oriented view. A **Neo4j graph** (`IGraphProvider`) persists the typed entity/relationship/community structure that GraphRAG and LazyGraphRAG traverse. Both derive from the same canonical source, so they stay consistent with each other and with the chunks.

---

## 3. Semantic Search

### What a user gains

Semantic Search is the workhorse. It answers precise, scoped questions about specific documents: "what does the RFP say about the scope of work", "which requirements mention acceptance testing". Because it matches on meaning rather than exact words, it finds content even when the user's phrasing differs from the document's — a search for "requirements" surfaces content about obligations and deliverables. It is fast, deterministic, and every result carries a citation back to the source file, so a user can verify what they are told. It is also the fallback that keeps the other modes honest: when graph retrieval finds nothing usable, the answer still comes back grounded in chunks.

### How it works

Ingestion is `RagsService.IngestAsync`: the document text is split into chunks by `ChunkingPipeline`, each chunk is embedded by `IEmbeddingProvider`, and the vectors are stored in PostgreSQL via pgvector (`IVectorStore.StoreBatchAsync`). Re-ingestion deletes the source's prior embeddings first, so updates replace rather than accumulate.

Retrieval is `RagsService.RetrieveAsync`: the query is embedded, and the vector store returns the nearest chunks. Two refinements matter. First, retrieval can be scoped — to a single document (`sourceId`) or to a set of documents (`sourceIds`, used by the theme filter) — so a Copilot session scoped to "Analysis" documents never sees content from outside that theme. Second, a score floor with keyword fallback (Sprint 57): if vector search returns nothing, or its best score is below the configured `RAGS:MinimumScore`, the service falls back to a lexical search over chunk content and file names. The result's retrieval strategy (`semantic` vs `keyword`) tells the user which path produced it, so silence is never mistaken for absence.

Semantic Search is the engine under everything else: Search Center's primary mode, Copilot's default retrieval, the evidence collection for document briefs, and the final fallback in both graph modes.

---

## 4. The LLM Wiki

### What a user gains

The Wiki is the durable, human-readable knowledge surface. Where search returns chunks, the Wiki returns pages. Every ingested document gets a **document brief** — a plain-language summary that opens with the document's stated nature and purpose, then walks the canonical template's sections in order, each grounded in retrieved evidence and cited. A user who wants to know "what is this document about" reads the brief instead of assembling it from search results. On top of the auto-generated briefs sit **editable wiki pages** with a lifecycle — Generated, Reviewed, Approved, NeedsReview, Stale — so a team can curate and govern the knowledge rather than merely retrieve it. When the underlying document is updated, the page is flagged Stale so nobody trusts an outdated brief.

### How it works

Briefs are produced by `DocumentBriefService` through a `DocumentBriefs` background job that fires after ingestion. The service first applies the canonical gate: a document must have `template_status = Canonical` (i.e. match a template in `docs/doc-templates`) or no brief is produced — uncategorized documents are ingested and searchable but get their brief once promoted via `POST /api/knowledge/reevaluate`. It then collects evidence — the opening chunks (which carry the document's nature/purpose) plus a per-section semantic retrieval for each template section — and hands that evidence to `SemanticKernelDocumentBriefGenerator`, which writes the brief through the configured chat completion service under a strict system prompt: plain language, no chunk/community/graph jargon, every claim grounded and cited. If the LLM is unavailable, a deterministic fallback still produces a page from the retrieved evidence. Briefs are stored as `wiki_pages` rows with `generated_from = 'document-brief'` and can be regenerated on demand via `POST /api/wiki/briefs/regenerate`.

The page layer is `WragsWikiService` (WRAGS is the internal name; the user-facing surface is always "Wiki"). A search first looks up stored pages; on a miss it regenerates from the underlying retrieval. The service can also retrieve through the other modes — semantic, graphrag, lazygraphrag — and its default "wrags" mode cascades GraphRAG → LazyGraphRAG → semantic. Pages carry history revisions, related topics, and source-change stale detection. One deliberate boundary: GraphRAG's internal community summaries are excluded from the user-facing Wiki — they stay internal provenance, and the document briefs are what users see.

---

## 5. GraphRAG

### What a user gains

GraphRAG answers corpus-wide and cross-document questions that plain vector search cannot: "what are the main themes across the whole estate", "which entities appear across multiple RFPs", "how are these projects related". It does this by building a typed knowledge graph — entities, typed relationships between them, and hierarchical community summaries — and answering from the graph's structure and summaries rather than from raw chunks alone. The typed relationships are the differentiator: a vector search can tell you two documents both mention "Acme Corp", but the graph knows Acme *manages* Project X and *requires* deliverable Y, and it can surface that connection even when no single chunk states it. Because the graph is built from canonical chunks, its entities and communities inherit the estate's structure — an entity's community is a statement about the document kinds it appears in.

### How it works

Ingestion is index-heavy. `GraphRagService.IngestAsync` runs the standard RAG ingestion, then creates a source node, chunk nodes with `has_chunk` edges, and — per chunk — runs LLM entity extraction (`IEntityExtractionService`) and LLM relationship extraction (`IRelationshipExtractionService`). Entities become nodes connected to their source (`found_in`) and their chunk (`mentioned_in`); relationships become typed, directed edges. Entity summaries are persisted, a Leiden-inspired community detection (`ICommunityDetectionService`) groups related entities into hierarchical communities, and community summaries are precomputed and stored. The graph lives in Neo4j via `IGraphProvider`.

The upload path deliberately does not block on all of this. `UploadedContentKnowledgeIndexer.IndexLightweightAsync` seeds the graph — source node, chunk nodes, `has_chunk` edges, taxonomy topics — and marks chunks `lazyEnrichmentStatus = Pending`, so a freshly uploaded document is searchable immediately. The expensive entity/relationship/summary enrichment is deferred to query time for the chunks that actually matter to a question (`GraphRagService.EnsureQueryTimeEnrichmentAsync`), then the chunk is marked `lazyEnriched`.

Retrieval is a fallback cascade. `GraphRagService.RetrieveAsync` resolves the query's entities, locates their communities, and reads the stored entity and community summaries, assembling a structured context via `IGraphContextBuilder` and ranking the summary candidates with citations. When summaries are absent, it lazily enriches the top chunks and retries. If that still yields nothing, it falls back to graph-aware reasoning, then to semantic retrieval with multi-hop entity expansion (a bounded breadth-first walk over graph neighbors), and finally to plain semantic retrieval — so a GraphRAG query never returns empty when the corpus has relevant content. Global search (`GlobalGraphSearchService`) is a separate map-reduce: it selects the top-level community summaries, maps each against the query, and reduces the mapped answers into a corpus-level synthesis with citations.

---

## 6. LazyGraphRAG

### What a user gains

LazyGraphRAG is the cost-shifting variant. It is for large corpora where the full GraphRAG ingestion — LLM extraction on every chunk — is too slow or too expensive. Its indexing is nearly free: no LLM calls at all. The graph is constructed on demand at query time, within a strict budget, so latency and cost are predictable per question. It also improves over time: entities and relationships discovered during a query can be persisted, so later queries benefit from earlier ones (progressive enrichment). For a user, the benefit is a graph-capable answer on a corpus that could not afford full GraphRAG, with bounded cost.

### How it works

Ingestion is `LazyGraphRagService.IngestAsync`: standard RAG ingestion plus a lightweight corpus statistics index (`CorpusDiscoveryIndex`) that computes TF-IDF and BM25 scores per document — no entity extraction, no graph construction.

Retrieval is where the work happens. `LazyGraphRagService.RetrieveAsync` first searches the corpus statistics to find seed documents, then discovers candidate entities at query time from those statistics (terms scored by BM25 + TF-IDF + query overlap). A budgeted LLM call may guide relationship discovery. A temporary graph is built from the statistical candidates, and a priority-queue best-first traversal (`TraverseBestFirstAsync`) explores it — each node visit, relationship traversal, and LLM call is charged against `IGraphTraversalBudget`, which caps LLM calls, depth, nodes, relationships, tokens, and execution time (defaults: 5 calls, depth 3, 50 nodes, 100 relationships, 4000 tokens, 30 seconds). The traversed subgraph is pruned by `ISubgraphPruningService`, communities and summaries are resolved within the same budget, and the surviving context is combined with semantic retrieval and expansion (corpus terms, entity labels) before ranking and citation. Finally, the discovered entities and relationships are persisted so the next query starts smarter.

The budget is the point: every LazyGraphRAG query is bounded, so a single slow LLM call cannot blow the answer time or the token bill.

---

## 7. Theme Filtering: Scoping the Estate

### What a user gains

Theme filtering is the user-facing expression of canonical form. Because every document carries one or more themes derived from its canonical template, a user can scope a Copilot session or the Search Center to the document kinds that matter — "Analysis" documents, "As-Built" documents, or a combination — and retrieval will be restricted to those documents. In Copilot the selection is visible as chips in the session header, can be edited mid-session, and persists with the session. In Search Center it is a shared scope (remembered across visits) applied to semantic search, with a visible "Scoped to N themes" indicator. For a user working on a specific kind of work, this is a precision tool: it removes the noise of unrelated document kinds from every answer, and it makes the estate's breadth navigable.

### How it works

Themes are category labels on canonical templates (`Theme:` first line in `docs/doc-templates`, comma-separated for multiple). `DocumentTemplateRegistry` exposes `TryGetThemes` (a set per template) and `ListThemes` (flattened), with missing or mismatched templates falling back to `Uncategorized`. Ingestion persists `template_name` + `theme` (`text[]`) + `template_status` on `file_metadata`; pre-Sprint-58 rows derive their themes from the file name at read time (safety net only). A document with no matching template is still ingested — `template_status = Uncategorized` — and an administrator can promote it via `POST /api/knowledge/reevaluate` (which doubles as the backfill that persists derived fields for pre-migration rows). `GET /api/knowledge/themes` returns `[{ theme, documentCount }]` for the UI pickers; a document in multiple themes is matched by any of them and counted in each.

The filter resolves selected themes to source ids via `IKnowledgeThemeService.ResolveSourceIdsAsync` (match-any) and enforces them in every RAGS retrieval path via `RetrievalRequest.SourceIds` — pgvector applies a `source_id = ANY(...)` predicate on both vector and keyword search. In Copilot the selection rides the chat path end to end (`ChatSession.ThemeFilter` → `ChatPayload` → `ChatRequestOptions` → the plan → `ChatExecutionEngine`). In Search Center it rides `GET /api/rags/retrieve?themes=`, which the controller resolves to `SourceIds`. The semantics are precise: a named document outside the selected themes yields no results from that document (intersection with the Sprint 51 single-document scope), while collection paths take the union of theme-matched sources. The engine also post-filters any retrieval that bypassed its own paths (e.g. the repository-tool path), so no content from excluded documents reaches synthesis.

One documented boundary: theme scoping covers the **Copilot session** (Sprint 58) and **Search Center semantic search** (Sprint 59, Phase 1). GraphRAG/LazyGraphRAG internals, community summaries, and the Wiki are not theme-filtered — there is no global knowledge-scope widget over those surfaces. That is a deliberate scope decision, not an oversight, and it is a candidate for future work (theme-aware graph retrieval is backlog item 5).

---

## 8. How the Modes Cooperate

The surfaces are composed, not isolated. In Copilot (`ChatExecutionEngine`), a question that names a specific document is scoped to that document and answered from source-scoped semantic evidence — opening chunks first for template documents, then per-section retrieval. A broad, corpus-level question ("summarize the opportunities across the estate") routes to global search, which cascades GraphRAG global → LazyGraphRAG global → semantic fallback, so the strongest available graph evidence is used and semantic evidence is the safety net. Small-corpus requests take a fast path that hydrates and retrieves per source. The theme filter is enforced on every retrieval path, so a session scoped to certain themes never sees content from excluded documents. The Wiki's "wrags" mode cascades the same way. And every answer — from any mode — is grounded and cited, with telemetry (elapsed time, token estimates, context and citation counts, retrieval strategy, alignment confidence) so a user can see how the answer was produced.

A final boundary worth naming: the graph modes and raw Wiki/WRAGS are **internal operator modes**. They are hidden from end users unless `FeatureFlags:ShowInternalSearch` is enabled (default false); when hidden, their API endpoints return HTTP 404 and the UI hides the controls. The user-facing surfaces are Semantic Search, the Wiki, and Copilot — the graph machinery works underneath them, not beside them.

---

## 9. Future State (Short)

The stack is built and working; the near-term direction is hardening and observability rather than new retrieval paradigms. The most valuable candidates, drawn from the backlog and the GraphRAG maturity reports:

- **Per-request traversal budgets and a durable corpus index** — the LazyGraphRAG budget is currently a shared singleton (concurrent requests can corrupt each other's budget), and the corpus statistics index is in-memory (lost on restart, invisible to a second instance). Both should be per-request / persisted.
- **Batch GraphRAG ingestion** — today's per-chunk LLM and Neo4j round-trips make large-document ingestion slow and costly; `UNWIND`-based writes and bounded-concurrency extraction would change the economics.
- **Real token accounting and a hard deadline** — the token budget is currently estimated, not enforced; wiring provider usage data in would make cost guarantees real.
- **A per-query retrieval trace** — the graph modes are long fallback cascades, and which path fired is currently opaque; exposing the trace (entities → communities → summaries → pruning ratio) would make answers auditable.
- **A repeatable benchmark** — quality, recall, latency, and cost are not yet quantified against Microsoft-style baselines; a scoring harness would let the team tune rather than guess.
- **Theme-aware graph retrieval** — theme filtering currently applies to the RAGS paths; extending it to GraphRAG/LazyGraphRAG global and broad paths is a natural follow-up.

None of these change what a user sees; they make the existing four surfaces faster, cheaper, and more trustworthy.

---

*Grounded in the Aletheia codebase as of Sprint 59: `RagsService`, `DocumentBriefService`, `WragsWikiService`, `GraphRagService`, `GlobalGraphSearchService`, `LazyGraphRagService`, `CorpusDiscoveryIndex`, `GraphTraversalBudget`, `UploadedContentKnowledgeIndexer`, `RepositoryKnowledgeSourceIngestionService`, `DocumentTemplateRegistry`, `KnowledgeThemeService`, `TemplateReevaluationService`, `SearchScopeStateService`, `ChatExecutionEngine`. Companion reading: `docs/user-guide/Appendix-GraphRAG-and-LazyGraphRAG.md`, `docs/GraphRAG-Implementation-vs-Microsoft-Research.md`, `docs/graphrag/*` maturity reports, `docs/backlog/GraphRAG-LazyGraphRAG-Enhancements.md`, `docs/backlog/Canonical-Form-Themes-Filtering-Enhancements.md` (items 1-4 promoted to and implemented in Sprint 59; item 5, theme-aware graph retrieval, remains proposed).*
