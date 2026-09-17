# Headless.UnitOfWork.Abstractions

## Problem Solved

Defines the public unit-of-work contracts without provider dependencies: the scoped manager entry point, the unit handle, the resource seams, and the shared `TransactionEnlistment` knob.

## Key Features

- `IUnitOfWorkManager` (scoped): `Current`, the resource-less `BeginAsync(options?, ct)`, plus the provider primitives — the resource-factory `BeginAsync` (hidden), observed-mode `Enlist` (hidden), and swap-and-restore `Adopt` (hidden).
- `IUnitOfWork`: `State`, `Failure`, `Resource`, `OnCompleted(Func<ValueTask>)`, `OnFailed(Func<UnitOfWorkFailure, ValueTask>)`, `GetOrAdd<TState>` (both overloads), `PreventRetry()` / `IsRetryPrevented`, `CompleteAsync(ct)`, idempotent `RollbackAsync()`, dispose both ways.
- `IUnitOfWorkResource` (`IsOwned`, `IsTransactionCompleted`, `CommitAsync`, `RollbackAsync`) and `IRelationalUnitOfWorkResource` (`Connection`, `Transaction`, non-null while active).
- `UnitOfWorkState` (`Active = 0`, `Completed = 1`, `Failed = 2`); `UnitOfWorkFailure` with `UnitOfWorkFailureReason` (`RolledBack`, `Abandoned`, `Faulted`, `ScopeDisposed`, `ChildAbandoned`).
- `UnitOfWorkOptions`: intentionally empty today; propagation knobs land here additively.
- `TransactionEnlistment { WhenAvailable = 0, Required = 1, Never = 2 }`: the one shared knob unifying what used to be two separate per-domain atomicity flags (Messaging's delivery-mode-based enlistment requirement and Jobs' atomic-enlistment flag) into a single enum with one guarantee matrix for both.

## Design Notes

The manager is scoped by decision: `Current` is a plain field on it, one slot per DI scope, with no `AsyncLocal` anywhere. The developer opens the unit of work explicitly on the line they choose; no middleware or filter opens one for them. Dispose without `CompleteAsync` is an implicit rollback; `RollbackAsync` is how the owner of an observed-mode transaction reports its own rollback and suppresses the forgotten-completion warning. `OnCompleted` callbacks are process-local fast-path dispatchers, never the durability mechanism — durable delivery comes from rows committed in the transaction plus the consumer's recovery sweep. An `OnCompleted` fault after a successful commit leaves the unit `Completed` (the data is durable); a commit fault transitions to `Failed` before the exception propagates, so a retry cannot double-apply.

Nesting is join-by-default: a begin on the same resource returns a child handle whose completions transfer to the root; a resource-bearing begin under a resource-less root opens an independent nested unit; a different resource under a resource-bearing unit throws.

## Installation

```bash
dotnet add package Headless.UnitOfWork.Abstractions
```

## Quick Start

```csharp
using Headless.UnitOfWork;

public sealed class PlaceOrderHandler(IUnitOfWorkManager unitOfWork, AppDbContext db, IBus bus)
{
    public async Task<Result<OrderId>> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken: ct); // or BeginAsync(db, ct) from the EF provider
        await bus.PublishAsync(new OrderPlaced(orderId), ct);  // joins the active unit
        await uow.CompleteAsync(ct);                           // commit, then dispatch
        return Result.Ok(orderId);
    }
}
```

## Configuration

None.

## Dependencies

None.

## Side Effects

None.
