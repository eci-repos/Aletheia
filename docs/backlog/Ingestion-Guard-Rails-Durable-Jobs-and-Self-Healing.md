# Backlog: Ingestion Guard-Rails — Durable Jobs and Self-Healing

**Status:** Proposed (not yet promoted to a sprint)
**Created:** 2026-08-14
**Source:** Operational incident (2026-08-14) — the Repository Browser flipped all three documents from **Ingested** to **Not ingested** after an API rebuild. Diagnosis: the `embeddings` and `document_facts` tables were genuinely empty (metadata/wiki/taxonomy/ontology intact) — the signature of a re-ingestion that deleted the old data but never wrote new data. Root cause: the ingestion job queue is **in-memory** and the ingestion pipeline is **delete-then-insert**, so an API restart mid-job loses the queue and leaves the source with no embeddings/facts.

## Problem

- **The ingestion job queue is in-memory.** `IngestionJobService` keeps queued/running jobs in a `Channel` + in-memory dictionary. An API container rebuild or restart loses them mid-flight — the job simply never completes, and nothing re-queues it.
- **Ingestion is delete-then-insert.** `RagsService.IngestAsync` deletes the source's old embeddings before re-embedding; `SaveFactsAsync` deletes old facts before inserting. An interruption between the delete and the insert leaves the source with **no** embeddings/facts — the Repository Browser then correctly reports "Not ingested" even though the document was ingested moments earlier.
- **Users are not controllable.** Rebuilds, restarts, and container churn happen mid-ingestion — a user may rebuild the API while a job is running, or restart the stack, or re-upload. The system must complete its work or self-heal regardless of what the user does; it cannot rely on the user to re-trigger a repair.

## Decisions (proposed approach)

1. **Durable job queue.** Persist ingestion jobs (kind, source, stage, status, timestamps) to PostgreSQL. The in-memory channel stays the fast path; the DB is the recovery record. On startup, re-queue jobs that were queued or running when the host stopped.
2. **Write-new-then-swap ingestion.** Change the delete-then-insert for embeddings and facts to write the new rows first, then remove the old rows (or do both in a single transaction). An interruption then leaves the previous good state, never an empty one.
3. **Startup reconciliation.** On API startup, scan for documents whose metadata exists but whose embeddings are missing (or whose last-ingestion marker is older than the last successful job) and auto-queue a repair. This self-heals interrupted ingestions without any user action.
4. **Job stage tracking.** Each job records its last committed stage; a resumed job continues from there instead of restarting from scratch.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Durable ingestion job queue** — persist jobs to PostgreSQL (`ingestion_jobs` table + repository), re-queue incomplete jobs on startup. | The queue survives API restarts; a job interrupted by a rebuild is picked up again. | ~1.5–2 days | Proposed |
| 2 | **Write-new-then-swap ingestion** — embeddings + facts write new rows before deleting old ones (or one transaction). | An interruption never leaves a source with zero embeddings/facts; the previous good state survives. | ~1 day | Proposed |
| 3 | **Startup reconciliation sweep** — detect documents with metadata but no embeddings and auto-queue a repair. | Self-heals interrupted ingestions with zero user action; the "Not ingested" flip becomes self-correcting. | ~1 day | Proposed |
| 4 | **Job stage tracking + resume** — record the last committed stage per job; resume from it. | A resumed job doesn't redo completed work (re-embedding, fact extraction, graph indexing are expensive). | ~0.5–1 day | Proposed |
| 5 | **Tests + docs** — repository round-trips for the durable queue, reconciliation-sweep tests, restart-survival tests; AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The resilience guarantees must be locked down. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — the durable queue and the write-new-then-swap pipeline are the two halves of "interruptions don't lose work"; the queue makes the job survive, the swap makes the data survive.
- **Item 3** — the reconciliation sweep is the safety net that catches anything the first two miss; it can land after 1 + 2.
- **Item 4** — stage tracking is an optimization on top of the durable queue; land it with or after item 1.
- **Item 5** alongside each item, not a trailing batch.

**Total (agent):** ~4–5 working days including build/test verification. A single sprint.

## Out of Scope

- Changing the ingestion pipeline's fidelity guarantees (Sprint 70) — the guard-rails make ingestion *resilient*, not *different*.
- Making the job queue distributed or multi-host (single-host recovery is the goal).
- User-facing job history/retry UI (the queue is internal; the Repository Browser's Ingestion column already surfaces the outcome).
- Guaranteeing no work is ever lost across a *database* loss (that is a backup/DR concern, not a job-queue concern).
