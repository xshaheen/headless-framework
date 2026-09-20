---
title: Coordination Domains Boundary — Locks vs Membership vs Unit of Work
date: 2026-06-21
last_updated: 2026-09-20
category: architecture-patterns
module: headless-coordination
problem_type: architecture_pattern
component: domain_boundary
severity: medium
related_components:
  - distributed_locks
  - coordination
  - unit_of_work
tags:
  - distributed-locks
  - coordination
  - unit-of-work
  - domain-boundary
---

# Coordination Domains Boundary

## Problem

The framework ships **three** sibling domains that all look structurally alike —
`Headless.DistributedLocks.*`, `Headless.Coordination.*`, and `Headless.UnitOfWork.*` —
each with an `Abstractions → Core → providers` layering. A reader scanning the
package list reasonably asks: *are these redundant, and which do I use?*

## Decision

They are **three distinct concerns and must not be merged.** They share no code by design — each contract
solves a different problem. Pick by the question you are answering:

| Use | When you need | Key contracts |
|---|---|---|
| **DistributedLocks** | **Mutual exclusion** — at most one worker in a critical section across processes | `IDistributedLock`, `IDistributedSemaphore`, `IDistributedReadWriteLock`, `IDistributedLease` |
| **Coordination** | **Cluster membership / node liveness** — which nodes are alive, who owns what, reclaim a dead owner's work | `INodeMembership`, `INodeIdProvider`, `IDeadOwnerReclaimer`, `NodeLivenessState`, `MembershipLostBehavior` |
| **UnitOfWork** | **Transaction outcome orchestration** — enlist durable outbox/job writes in the caller's transaction and defer dispatch/notifications until it commits | `IUnitOfWorkManager`, `IUnitOfWork`, `IUnitOfWorkResource`, `IRelationalUnitOfWorkResource`, `IUnitOfWorkFeatureProvider`, and `TransactionEnlistment` (Jobs' knob; Messaging enlists by calling `unit.Outbox` instead) |

### Quick disambiguation

- "Only one of my workers should run this at a time" → **DistributedLocks**.
- "Is this node still alive / who is the current owner / clean up after a crashed owner" → **Coordination**
  (see also `coordination-register-establishes-durable-liveness.md`).
- "Run these side-effects only if the DB transaction actually commits" → **UnitOfWork**.

They compose rather than overlap: e.g. a job may take a **DistributedLock**, rely on **Coordination** to
reclaim its lease if the node dies, and use the active **UnitOfWork** to enlist an outbox row in the
application transaction and accelerate its dispatch after commit.

### Jobs transactional deadlines

`JobOptions.Enlistment = TransactionEnlistment.Required` (or `RecurringJobOptions.Enlistment`) asserts that
an ordinary or keyed one-shot deadline must share the caller's live relational transaction — the active
`IUnitOfWork`'s resource. Jobs validates its actual configured database and captures the exact caller
connection and transaction before scheduling middleware. The Jobs-owned writer enlists the durable row
immediately; only restart/notification acceleration waits for commit. Keyed results are provisional until
the caller outcome, and operation savepoints protect replacement retirement plus insertion together.
Missing or incompatible capabilities fail before middleware.

`TransactionEnlistment` is Jobs-only now. Messaging once resolved the same enum with the same
matrix; it makes the same choice structurally instead, by which publisher the call site uses
(`IBus`/`IQueue` are autonomous, `unit.Outbox` writes inside the caller's transaction). The Jobs
semantics above are untouched by that change.

A Messaging transport delay is a delivery setting, not this transaction capability. Distributed locks
and membership also cannot make an application update and a Jobs row atomic. See
[docs/llms/jobs.md](../../llms/jobs.md) and [docs/llms/unit-of-work.md](../../llms/unit-of-work.md)
for supported providers, connection ownership, isolation, and retry requirements.

## Package-shape difference is justified, not drift (N5 — resolved; shape changed under the unit-of-work rewrite)

The three domains' *package shapes* differ, and the difference is **intentional**, driven by whether the
relational work is provider-specific or provider-agnostic:

- **`DistributedLocks` and `Coordination` have `Core.Database` + provider-specific packages, and no generic
  `EntityFramework`.** Their core primitive *is* provider-specific SQL: DistributedLocks uses
  `pg_advisory_xact_lock`/`pg_advisory_lock` on PostgreSQL versus `sp_getapplock`/`sp_releaseapplock` on
  SqlServer; Coordination has provider-specific membership stores/initializers. A single provider-agnostic EF
  package is **impossible** here, so `Core.Database` holds the shared non-SQL plumbing and each provider package
  carries its own dialect.
- **`UnitOfWork` has no `Core.Database` either, but for a different reason: its three providers
  (`EntityFramework`, `PostgreSql`, `SqlServer`) are symmetric, not asymmetric.** Earlier this domain's
  design gave the EF provider the sole ability to observe a database commit/rollback edge, so the raw-ADO
  providers needed an explicit-signal contract the EF one didn't. The current scoped design removes that
  asymmetry: the transactional resource's owner (the scoped unit-of-work manager, in the provider-agnostic
  base package `Headless.UnitOfWork`) owns the commit verb itself, so every provider ships the same
  `BeginAsync(resource)` / `Enlist(resource, transaction)` / `RunAsync(resource, ...)` trio over its own
  resource type (`DbContext`, `NpgsqlConnection`, `SqlConnection`), and `IUnitOfWork.CompleteAsync` is the
  one commit verb regardless of provider. No provider observes a database-native commit edge anymore, so
  no provider needs a privilege the others lack; `Core.Database` would have nothing shared to hold that the
  base `Headless.UnitOfWork` package doesn't already hold.

So this is **not** a coherence defect to refactor — the shapes encode a real implementation difference,
though the *nature* of that difference changed when transaction-outcome orchestration moved from an
ambient design (a stack-scoped object pushed onto async-flow state) to the current scoped design (a plain
field on a scoped DI-resolved manager, begun and completed explicitly). The one thing that *was*
normalized across all three domains: every relational provider uses the `PostgreSql` spelling (the lone
`Postgres` outlier was renamed).
