# Headless.UnitOfWork.EntityFramework

## Problem Solved

Gives a plain EF Core `DbContext` the three unit-of-work entry points it needs: an owned begin (`BeginAsync(db)` — one line opens the transaction, one verb commits it and drains everything enlisted inside), an observed enlist (`Enlist(db, transaction)` — for code that owns its own commit edge), and an execution-strategy-safe block (`RunAsync(db, …)`). Misuse is loud: beginning over an existing transaction or under a retrying strategy throws a message that names the remedy. No interceptor, no options configuration, no startup gate — the unit of work owns its commit edge, so nothing needs to observe EF's transaction events.

## Key Features

- `IUnitOfWorkManager.BeginAsync(db, isolation = ReadCommitted, ct)` — owned mode: claims the manager's slot synchronously, rejects a context that already has a transaction (naming `Enlist`), rejects a retrying execution strategy (naming `RunAsync`), begins the transaction eagerly, and records the `DbContext → IUnitOfWork` binding. `CompleteAsync` commits, then drains.
- `IUnitOfWorkManager.Enlist(db, transaction)` — observed mode for a transaction the caller commits: the unit's verbs are no-ops on the transaction; `CompleteAsync` drains without committing; `RollbackAsync` reports the caller's rollback and suppresses the forgotten-completion warning.
- `IUnitOfWorkManager.RunAsync(db, operation, …)` (and the `TResult` overload) — begin → block → complete inside `db.Database.CreateExecutionStrategy()`. A retriable failure before commit replays with a fresh transaction and a fresh unit; once commit has started, or after `PreventRetry()`, the fault is captured and rethrown **outside** the strategy so EF cannot replay a possibly-committed block. A drain fault after a durable commit is logged and the block's result is returned (the same policy as the Npgsql/SqlClient `RunAsync`).
- `EfUnitOfWorkResource` (internal) — the relational resource: live `Connection`/`Transaction` for participants (outbox/job writers), owned commit/rollback that disposes the `IDbContextTransaction`, and `IsTransactionCompleted` detection for the forgotten-completion warning.
- `DbContextUnitOfWork.Find(db)` — public but hidden from IntelliSense; resolves the active unit bound to a context. The Headless save pipeline (in `Headless.EntityFramework`) consults it before the scope manager, so a factory-created context owning its own scope is still found.
- `AddEntityFrameworkUnitOfWork()` — idempotent; delegates to `AddUnitOfWork()` and registers nothing else.

## Design Notes

**Nesting is join-by-default.** A second `BeginAsync(db)` while a unit is active on the same context returns a child view over the same engine and the *same live resource* — no second transaction is begun (the factory returns the root's resource, the manager's identity comparison opens the child). Child registrations transfer to the root on child complete; an abandoned child drops its registrations and aborts the root. A resource-bearing begin under a resource-less root opens an independent nested unit.

**The retrying-strategy split is deliberate.** `BeginAsync(db)` throws EF's own `ExecutionStrategyExistingTransaction`-shaped message plus the `RunAsync` remedy because a user-initiated transaction cannot survive a strategy retry. `RunAsync` runs the begin *inside* the strategy, so retries replay the whole block with a fresh unit each attempt; the abandoned attempt's unit is unwound (rolled back) before the replay begins, or the replayed begin would meet a still-open transaction on the same context. The replay filter is: `CompleteAsync` started or `IsRetryPrevented` ⇒ rethrow outside the strategy. Reconcile an ambiguous post-commit fault by a client-generated key or another durable idempotency key before retrying the business operation.

**Observed mode is the advanced seam.** The save pipeline and the messaging inbox runners commit their own transactions and already know the outcome; they enlist, commit, then call `CompleteAsync` to drain. A dispose without either verb, after the transaction already finished, logs the forgotten-completion warning — durable rows are recovered by the relay, the process-local drain is not.

**The binding uses a `ConditionalWeakTable`**, so a pooled context never leaks a stale unit: `Find` returns only a unit that is still `Active`; a terminal unit is ignored and evicted. This package never references `Headless.EntityFramework` (the reference flows the other way), so the provider stays usable by any EF consumer.

## Installation

```bash
dotnet add package Headless.UnitOfWork.EntityFramework
```

## Quick Start

```csharp
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connectionString));
services.AddEntityFrameworkUnitOfWork();

// Owned mode: the transaction begins on this line; CompleteAsync commits and drains.
await using var unitOfWork = await unitOfWorkManager.BeginAsync(db, cancellationToken: ct);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
unitOfWork.OnCompleted(async () => await bus.PublishAsync(new OrderPlaced(order.Id), ct));
await unitOfWork.CompleteAsync(ct);

// Retrying strategy configured? Run the unit as a retriable block instead:
await unitOfWorkManager.RunAsync(
    db,
    async (unitOfWork, ct) =>
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        unitOfWork.OnCompleted(async () => await bus.PublishAsync(new OrderPlaced(order.Id), ct));
    },
    cancellationToken: ct
);
```

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

`AddEntityFrameworkUnitOfWork()` calls the idempotent `AddUnitOfWork()` (scoped `IUnitOfWorkManager`) and registers nothing else — no interceptor, no hosted service, no options. The manager is scoped: resolving it from the root provider under scope validation throws, which is the correct captive-dependency signal.
