---
title: "Unit-of-Work Propagation and Enlistment Contract"
date: 2026-09-24
module: Headless.UnitOfWork.Abstractions
tags: [unit-of-work, transaction, entity-framework, dapper, outbox, jobs, messaging, replay]
problem_type: architecture_pattern
component: database
related_components:
  - IUnitOfWorkFactory
  - IUnitOfWork
  - IUnitOfWorkFeature
  - RelationalDatabaseIdentity
symptoms:
  - A caller expects an ambient Current unit and finds none
  - EF work is refused inside a raw-ADO unit that owns the same connection
  - An injected IBus or IJobScheduler write escapes the surrounding transaction
  - Enlisted writes are replayed or skipped unexpectedly after a retry
severity: medium
---

# Unit-of-Work Propagation and Enlistment Contract

`IUnitOfWorkFactory` is a singleton with no `Current`. The `IUnitOfWork` handle is the unit's only
identity, and propagation is keyed on the **resource**, never on a DI scope. Everything below is the
contract that callers and provider packages must preserve; the consumer-facing version lives in
[docs/llms/unit-of-work.md](../../llms/unit-of-work.md).

## Propagation is keyed on the resource

- `RunAsync(db, …)` / `RunAsync(connection, …)` on a `DbContext` or `DbConnection` that already
  carries a live unit **joins** it: the caller gets the owner's handle and does not commit.
- `BeginAsync` / `Enlist` on a resource that already carries a live unit is refused.
- A joined block that ends the owner's unit is refused once it returns.
- Any EF entry point on a context whose connection a raw-ADO unit owns is refused. EF cannot join an
  ADO transaction; begin the EF unit first and let the ADO code join it.
- A sibling `DbContext` built over an EF unit's connection joins it on lookup by adopting its
  transaction. That adoption is derived from the connection binding on each lookup, never recorded
  against the sibling, and is released when the unit ends.
- A callee can also read the unit through `db.UnitOfWork()`, `connection.UnitOfWork()` (EF binds its
  context's connection too, so Dapper-style helpers join), or `ConsumeContext.UnitOfWork`.
- There are no child views.

## Enlistment is the receiver's decision

This holds for both the messaging and jobs domains:

- `unit.Outbox` and `unit.Jobs` / `unit.TimeJobs<T>()` / `unit.CronJobs<T>()` always write inside the
  unit's transaction, and refuse when it cannot host the write.
- The injected `IBus` / `IQueue` / `IJobScheduler` and the managers are autonomous singletons. They
  never enlist.
- No option or policy overrides the receiver. Do not add one.

## Replay interaction

Enlisted writes call `PreventRetry()` only into an observed-mode unit — the EF save pipeline's own
save, which replays without re-running domain-event handlers. It is skipped for the EF
integration-event dispatcher's own publishes. A write issued directly inside your own
`RunAsync(db, …)` block leaves that block replayable. A caller-owned `SaveChangesAsync` that
dispatched events marks the block itself, because clearing the dispatched events leaves a replay
nothing to re-dispatch.

## Dead transactions and feature lifetimes

- A binding evicts an **owned** unit whose transaction ended behind its back — a transaction disposed
  by hand, or a pooled context reset. The unit is abandoned and the next entry point begins fresh, so
  a `RunAsync` never runs writes on a dead transaction and then reports success.
- `IUnitOfWorkFeature` services must be singletons. `GetFeature` reads the registered lifetime off
  the collection that `AddUnitOfWork()` captured and refuses scoped or transient registrations
  regardless of `ValidateScopes`.

## "Same database" is one check

Messaging storages and the Jobs store decide "same database" through the single
`RelationalDatabaseIdentity` check. Widen its normalization only where a false match is impossible: a
false match strands rows in a database no relay reads.
