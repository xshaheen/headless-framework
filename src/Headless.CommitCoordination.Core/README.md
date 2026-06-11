# Headless.CommitCoordination.Core

## Problem Solved

Implements the in-process coordinator, ambient stack, scope factory, and relational capability implementation.

## Key Features

- Thread-safe callback registration and typed buffers.
- Ambient current coordinator through `CommitScopeStack`.
- Nested scopes join the root by default.
- Terminal drain runs callbacks with `CancellationToken.None` and aggregates failures.

## Design Notes

`Dispose` schedules an un-signalled rollback drain in the background so sync callers are not blocked on async callbacks. `DisposeAsync` restores the ambient parent synchronously before any rollback drain so `await using` does not strand `AsyncLocal` state.

**Savepoints are invisible to the coordinator.** Enlisted work binds to the OUTERMOST commit edge only: a `RollbackToSavepoint` discards the database writes made after the savepoint but does NOT discard commit work buffered during that window — on final commit, all buffered work drains, including work registered inside the rolled-back region. If an operation publishes or enqueues inside a partial-rollback region, that mismatch is the consumer's to manage: enlist work only after the last possible partial rollback, or register/dispose the callback manually. Nested *scopes* (child coordinators joining the root) are supported and conformance-tested; nested *savepoint tracking* is deliberately out of scope.

## Installation

```bash
dotnet add package Headless.CommitCoordination.Core
```

## Quick Start

```csharp
services.AddCommitCoordination();
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers `CommitScopeStack`, `ICurrentCommitCoordinator`, and `CommitScopeFactory`.
