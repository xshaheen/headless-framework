---
title: "Atomic Time-Job Tree Deletion with Conflict Retry"
category: concurrency
date: 2026-09-03
tags: [jobs, entity-framework, postgresql, sql-server, foreign-key, retry, deadlock, transaction]
problem_type: concurrency_issue
components:
  - JobsEfCorePersistenceProvider
  - JobsManager
symptoms:
  - A child appended after descendant discovery can strand a row when deletion is not atomic
  - A partially deleted tree can be exposed when discovery and deepest-first deletes use separate transactions
  - Retrying an in-doubt commit can report zero rows after the first commit actually succeeded
severity: p2
research:
  agents: [main-orchestrator]
  documented_at: 2026-09-03T00:00:00Z
  conversation_context: "Issue #793 time-job tree deletion race across the generic EF, PostgreSQL, and SQL Server paths"
---

# Atomic Time-Job Tree Deletion with Conflict Retry

Time-job chains use a non-cascading parent/child foreign key and must be removed deepest-first. The previous discover-then-delete shape left a race window: another writer could append a child after discovery, allowing a delete attempt either to fail after earlier statements had removed rows or to miss the new descendant. The generic EF path, PostgreSQL package, and SQL Server package now share one provider-neutral mechanism rather than maintaining separate deletion strategies.

## Root Cause

Descendant discovery alone is not a concurrency fence. Correctness depends on keeping discovery and every deepest-first delete statement in one transaction, then treating the non-cascading foreign key as the signal that the discovered graph changed.

The race has two valid outcomes:

- **Append first:** a child committed after discovery makes the later parent delete fail its foreign-key check. The delete transaction rolls back, including every descendant already deleted, and a retry discovers the child.
- **Delete first:** once the delete has taken the parent row lock, a concurrent child insert waits on its foreign-key check. After the delete commits, the insert fails and creates no row.

In either arm, the database prevents a committed stranded descendant. This relies on the `DeleteBehavior.NoAction` relationship; a cascade would remove the conflict signal used by the append-first arm.

## Working Solution

### One read-committed transaction per attempt

`JobsEfCorePersistenceProvider.RemoveTimeJobsAsync` creates a fresh `DbContext`, transaction, descendant-level list, visited set, and deleted-row count for every attempt. Discovery and deepest-first deletion run in the same read-committed transaction. The context and transaction are disposed before any retry delay, releasing locks and discarding provider state made unusable by an error.

The mechanism lives in the generic EF provider and is therefore identical for generic EF, `Headless.Jobs.EntityFramework.PostgreSql`, and `Headless.Jobs.EntityFramework.SqlServer`. The provider packages continue to specialize claiming only; they do not carry parallel tree-deletion implementations.

### Retry only classified pre-commit conflicts

The provider retries the complete scope, with fresh discovery, at most three times using jittered exponential backoff. Retryable failures are driver-reported transient database errors plus these provider codes:

- PostgreSQL: foreign-key violation `23503`, serialization failure `40001`, and deadlock `40P01`.
- SQL Server: foreign-key violation `547`, deadlock victim `1205`, and snapshot isolation conflict `3960`.

`40001` and `3960` remain classified so the delete still behaves correctly when a consumer raises the default isolation level. Cancellation and non-database failures are never retried. When all retries are exhausted, the last conflict reaches `JobsManager`, which returns a failed `JobResult`; every failed attempt has rolled back, so the tree remains intact.

Commit is the boundary of certainty. It uses `CancellationToken.None`, and any commit exception propagates without retry because the server may have committed even if the acknowledgement was lost. A caller can safely repeat the delete; if the first commit succeeded, the repeated operation returns zero rows.

## Rejected Alternatives

### Serializable isolation

Serializable isolation does not remove the need for conflict handling. PostgreSQL SSI constrains only transactions that also use serializable isolation, so it cannot coordinate this delete with a read-committed appender. On SQL Server, the coordination store's serializable path exchanged foreign-key signals for deadlocks that still required retry. Read committed plus the existing foreign key is the smaller cross-provider contract.

### Provider-native locked recursion

A native lock walk would require separate PostgreSQL and SQL Server implementations. PostgreSQL does not allow `FOR UPDATE` in a recursive CTE, so its implementation would need a frontier walk. SQL Server's statement-end constraint-checking behavior was only third-party verified during this decision, which was not enough evidence for a new correctness-critical strategy. Deletion is a cold path, so the extra SQL, strategy selection, and conformance surface were not justified while bounded conflict retry remains sufficient.

## Remaining Boundaries

The PostgreSQL claim strategy still has no deadlock retry. A delete racing a claim can therefore fail one claim tick; the next scheduler tick recovers, and this pre-existing claim-side gap does not weaken delete atomicity.

Introduce provider-native locked frontier walks only if tree-delete retry exhaustion is observed in production. That evidence would show contention high enough to justify the extra provider-specific mechanisms and verification burden.

## Verification

The shared EF conformance harness covers both race arms against PostgreSQL and SQL Server: append-before-delete forces rollback and fresh discovery, while delete-before-append rejects the public update. The same harness covers a two-tree batch whose conflict retry returns the committed attempt's exact total. Unit coverage exercises every classified provider code, retry-then-success, non-retried cancellation, and retry exhaustion with the original tree intact.

## Prevention

- Keep discovery and mutation in one transaction whenever a relational invariant spans multiple statements.
- Classify conflicts by provider identity and error code; transient metadata alone is not a complete SQL Server signal.
- Recreate transaction-scoped state for each retry and release it before backoff.
- Mark the commit boundary explicitly so an uncertain outcome is never replayed as though it were a rolled-back attempt.
