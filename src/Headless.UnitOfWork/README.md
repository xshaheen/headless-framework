# Headless.UnitOfWork

## Problem Solved

Implements the scoped `UnitOfWorkManager` (one unit-of-work slot per service scope, no `AsyncLocal`), the in-process unit engine with the atomic terminal claim, and `AddUnitOfWork()`.

## Key Features

- `AddUnitOfWork()`: idempotent `TryAddScoped<IUnitOfWorkManager, UnitOfWorkManager>`; framework setups call it so exactly one registration exists.
- Synchronous slot claim: the provider-facing `BeginAsync` claims the slot before its first await, so a concurrent begin in the same scope fails deterministically; a faulted resource begin releases the slot and propagates as-is.
- Join-by-default nesting: same resource → child view (registrations transfer on child complete; abandon drops them and aborts the root with `ChildAbandoned`); resource-less root + resource-bearing begin → independent nested unit (`Current` returns the innermost); different resource → the catalogued throw. A root refuses `CompleteAsync` while a child is active and after a child was abandoned.
- Engine semantics ported from the coordinator: first-wins terminal claim via `Interlocked`, ordered completion drain with fault aggregation (one as-is, several as `AggregateException`), deregistration handles honored up to the claim, scope-local state disposed on both outcomes.
- `OnFailed` drain (log-and-continue) on rollback, abandon, scope dispose, commit fault, and child abandon; `RollbackAsync` idempotent; a commit fault transitions to `Failed` before the exception propagates; an `OnCompleted` fault after a successful drain leaves `Completed`.
- Leak detection: disposing the manager with an active unit rolls it back, runs `OnFailed` with `ScopeDisposed`, and logs the leak warning; observed mode disposed un-completed after a finished transaction logs the forgotten-completion warning.
- `Adopt(unit)`: swap-and-restore, re-entrant when the slot already holds that unit, throws the concurrent-begin message over a different active unit.

## Design Notes

`CompleteAsync` in owned mode commits the resource, then drains `OnCompleted`, then disposes scope-local state; the terminal claim settles synchronously on the completer's thread before the drain, so a racing dispose never rolls committed work back. Sync `Dispose` claims synchronously and offloads the rollback + failure drain to the thread pool (a captured `SynchronizationContext` can neither deadlock nor stall the disposing thread); `DisposeAsync` awaits the same path inline. Savepoint-blindness is harmless by construction: a row written inside a rolled-back savepoint vanishes and its stale callback no-ops — callbacks are process-local accelerators, durability is the committed row plus the consumer's recovery sweep.

## Installation

```bash
dotnet add package Headless.UnitOfWork
```

## Quick Start

```csharp
using Headless.UnitOfWork;

services.AddUnitOfWork();

await using var uow = await unitOfWorkManager.BeginAsync(ct);
uow.OnCompleted(() => cache.RemoveAsync(key));
await uow.CompleteAsync(ct);
```

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers the scoped `IUnitOfWorkManager`; the manager, engine, and handle types are internal. Repeated calls are idempotent. The manager is scoped — resolving it (or a scoped facade over it) from the root provider under scope validation throws, which is the correct captive-dependency signal.
