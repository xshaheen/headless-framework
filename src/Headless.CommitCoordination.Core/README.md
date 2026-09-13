# Headless.CommitCoordination.Core

## Problem Solved

Implements the in-process coordinator, the ambient `AsyncLocal` stack, the scope factory, and the relational handle.

## Key Features

- Thread-safe callback registration and scope-local state under one coordinator lock.
- Ambient current coordinator through `ICurrentCommitCoordinator` (backed by an internal `AsyncLocal` stack); the push and pop run synchronously in the opening and disposing frames.
- Every `Open` is an independent root; a scope disposed out of order throws, and a frame whose parent already popped is ignored.
- Terminal claim is synchronous and first-wins; the drain runs callbacks in registration order without a cancellation token, disposes scope-local state on both outcomes, and surfaces callback faults after the drain (one as-is, several as an `AggregateException`).
- Signals are idempotent per outcome: a repeated same-outcome signal is silent, a conflicting one is ignored and logged (`CommitCoordinator`, event 1, warning).

## Design Notes

`Dispose` claims rollback for an un-signalled scope and runs the scope-local disposal in the background so sync callers are not blocked; a background disposal fault is logged (`CommitCoordinator`, event 2, error). `DisposeAsync` pops the ambient frame synchronously and then awaits the same disposal, so `await using` does not strand `AsyncLocal` state. A dispose after a signal claims nothing and logs nothing.

**Savepoints are invisible to the coordinator.** Enlisted work binds to the OUTERMOST commit edge only: a `RollbackToSavepoint` discards the database writes made after the savepoint but does NOT discard commit work registered during that window; on final commit, all registered work drains, including work registered inside the rolled-back region. If an operation publishes or enqueues inside a partial-rollback region, that mismatch is the consumer's to manage: enlist work only after the last possible partial rollback, or dispose the `OnCommit` registration handle while the coordinator is still active. Nested savepoint tracking is deliberately out of scope; so is scope joining, since every scope is its own root.

## Installation

```bash
dotnet add package Headless.CommitCoordination.Core
```

## Quick Start

```csharp
using Headless.CommitCoordination;

services.AddCommitCoordination();
```

Provider packages call this internally; call it directly only for a host that opens non-relational scopes itself.

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers `ICurrentCommitCoordinator` and `ICommitScopeFactory`; the backing stack, factory, coordinator, and relational-handle types are internal. Repeated calls are idempotent. `ICurrentCommitCoordinator` is registered unconditionally (guarded by an internal sentinel) so the real coordinator wins over the null fallback that `Headless.Messaging.Core` `TryAdd`s, whichever setup call the host invokes first.
