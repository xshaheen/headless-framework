---
title: Coordination Domains Boundary — Locks vs Membership vs Commit
date: 2026-06-21
last_updated: 2026-09-05
category: architecture-patterns
module: headless-coordination
problem_type: architecture_pattern
component: domain_boundary
severity: medium
related_components:
  - distributed_locks
  - coordination
  - commit_coordination
tags:
  - distributed-locks
  - coordination
  - commit-coordination
  - domain-boundary
---

# Coordination Domains Boundary

## Problem

The framework ships **three** sibling domains that all look structurally alike —
`Headless.DistributedLocks.*`, `Headless.Coordination.*`, and `Headless.CommitCoordination.*` —
each with the same `Abstractions → Core → [Core.Database] → providers` layering. A reader scanning the
package list reasonably asks: *are these redundant, and which do I use?*

## Decision

They are **three distinct concerns and must not be merged.** They share no code by design — each contract
solves a different problem. Pick by the question you are answering:

| Use | When you need | Key contracts |
|---|---|---|
| **DistributedLocks** | **Mutual exclusion** — at most one worker in a critical section across processes | `IDistributedLock`, `IDistributedSemaphore`, `IDistributedReadWriteLock`, `IDistributedLease` |
| **Coordination** | **Cluster membership / node liveness** — which nodes are alive, who owns what, reclaim a dead owner's work | `INodeMembership`, `INodeIdProvider`, `IDeadOwnerReclaimer`, `NodeLivenessState`, `MembershipLostBehavior` |
| **CommitCoordination** | **Transaction outcome orchestration** — enlist durable outbox/job writes in the caller transaction and defer dispatch/notifications until it commits | `ICommitCoordinator`, `ICommitScope`, `IRelationalCommitContext`, `ICommitWorkBuffer`, `CommitOutcome` |

### Quick disambiguation

- "Only one of my workers should run this at a time" → **DistributedLocks**.
- "Is this node still alive / who is the current owner / clean up after a crashed owner" → **Coordination**
  (see also `coordination-register-establishes-durable-liveness.md`).
- "Run these side-effects only if the DB transaction actually commits" → **CommitCoordination**.

They compose rather than overlap: e.g. a job may take a **DistributedLock**, rely on **Coordination** to
reclaim its lease if the node dies, and use **CommitCoordination** to enlist an outbox row in the
application transaction and accelerate its dispatch after commit.

### Jobs transactional deadlines

`EnqueueOptions.RequireAtomicEnlistment` asserts that an ordinary or keyed one-shot deadline must
share the caller's live relational transaction. Jobs validates its actual configured database and
captures the exact caller connection and transaction before scheduling middleware. The Jobs-owned
writer enlists the durable row immediately; only restart/notification acceleration waits for commit.
Keyed results are provisional until the caller outcome, and operation savepoints protect replacement
retirement plus insertion together. Missing or incompatible capabilities fail before middleware.

A Messaging transport delay is a delivery setting, not this transaction capability. Distributed locks
and membership also cannot make an application update and a Jobs row atomic. See the
[Jobs atomic enqueue contract](../../../src/Headless.Jobs.Abstractions/README.md#commit-coordination-atomic-enqueue)
for supported providers, connection ownership, isolation, and retry requirements.

## Package-shape difference is justified, not drift (N5 — resolved)

The three domains' *package shapes* differ, and the difference is **intentional**, driven by whether the
relational work is provider-specific or provider-agnostic:

- **`DistributedLocks` and `Coordination` have `Core.Database` + provider-specific packages, and no generic
  `EntityFramework`.** Their core primitive *is* provider-specific SQL: DistributedLocks uses
  `pg_advisory_xact_lock`/`pg_advisory_lock` on PostgreSQL versus `sp_getapplock`/`sp_releaseapplock` on
  SqlServer; Coordination has provider-specific membership stores/initializers. A single provider-agnostic EF
  package is **impossible** here, so `Core.Database` holds the shared non-SQL plumbing and each provider package
  carries its own dialect.
- **`CommitCoordination` has a generic `EntityFramework` provider and no `Core.Database`.** Its commit-buffer
  logic is provider-agnostic EF (`DbContext` / `SaveChanges` transactions), so the generic `EntityFramework`
  package *is* its shared base; the `PostgreSql`/`SqlServer` packages add provider-specific commit-signal tuning
  on top.

So this is **not** a coherence defect to refactor — the shapes encode a real implementation difference. The one
thing that *was* normalized: all relational providers use the `PostgreSql` spelling (the lone `Postgres` outlier
was renamed).
