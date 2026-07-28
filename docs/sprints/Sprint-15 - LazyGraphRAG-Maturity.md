# Current Sprint

Sprint: LazyGraphRAG Maturity Completion

Status: Active

## Objective

Complete the remaining Microsoft-style LazyGraphRAG capabilities.

The existing implementation supports:

✅ Query-time entity discovery

✅ Query-time relationship discovery

✅ Graph expansion

✅ Incremental enrichment

The remaining work focuses on:

- Near-zero indexing cost
- Query-time graph construction
- LLM-guided traversal
- Cost-budgeted graph exploration
- Subgraph pruning

---

# Background

Current implementation still performs intelligence work during ingestion.

Microsoft LazyGraphRAG performs nearly all intelligence work during retrieval.

This sprint aligns Aletheia with that model.

---

# Requirements

## Lightweight Indexing

Refactor:

LazyGraphRagService.IngestAsync

Remove:

- LLM Entity Extraction
- LLM Relationship Extraction

During ingestion.

Replace with:

- Text Statistics
- TF-IDF Metadata
- BM25 Metadata
- Corpus Metadata

Create:

ICorpusDiscoveryIndex

---

## Query-Time Entity Discovery

Entity discovery must occur during retrieval.

Use:

ILazyEntityDiscoveryService

via Semantic Kernel.

---

## Query-Time Relationship Discovery

Relationship discovery must occur during retrieval.

Use:

ILazyRelationshipDiscoveryService

via Semantic Kernel.

Allow discovered relationships to persist.

---

## Query-Time Graph Construction

Support:

Query
→ Entity Discovery
→ Relationship Discovery
→ Temporary Graph
→ Graph Reasoning
→ Response

Implement dynamic graph construction.

---

## Cost Budget

Create:

IGraphTraversalBudget

Support:

- MaxLLMCalls
- MaxDepth
- MaxNodes
- MaxRelationships
- MaxTokenBudget
- MaxExecutionTime

All traversal decisions must honor budget.

---

## LLM-Guided Traversal

Replace heuristic traversal.

Use:

IGraphReasoningService

for:

- Edge Selection
- Node Selection
- Expansion Decisions
- Stop Conditions

---

## Subgraph Pruning

Create:

ISubgraphPruningService

Remove:

- Low Relevance Nodes
- Low Relevance Relationships

before context generation.

---

## Context Optimization

Enhance:

IGraphContextBuilder

Use:

- Summaries
- Taxonomies
- Ontologies
- Reasoning Paths
- Communities

to minimize token consumption.

---

## Persistent Enrichment

Persist:

- Newly discovered entities
- Newly discovered relationships
- Newly discovered graph structures

The graph should improve over time.

---

# Validation

Execute:

dotnet restore

dotnet build

dotnet test

---

# Deliverables

Provide:

- LazyGraphRAG Architecture Report
- Traversal Budget Report
- Graph Pruning Report
- Context Optimization Report
- LazyGraphRAG Maturity Report

Also create:

GraphRAG-Implementation-vs-Microsoft-Research-v3.md

showing:

- Microsoft GraphRAG
- Microsoft LazyGraphRAG
- Aletheia Implementation
- Remaining Gap

---

# Exit Criteria

✓ No LLM entity extraction during indexing

✓ Corpus metadata indexing implemented

✓ Query-time discovery operational

✓ Query-time graph construction operational

✓ Cost-budgeted traversal operational

✓ LLM-guided traversal operational

✓ Subgraph pruning operational

✓ Persistent enrichment operational

✓ Build succeeds

✓ Tests succeed

✓ Remaining LazyGraphRAG gap reduced to minimal levels