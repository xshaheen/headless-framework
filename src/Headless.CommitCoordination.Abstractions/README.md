# Headless.CommitCoordination.Abstractions

## Problem Solved

Defines the public commit coordination contracts without provider dependencies.

## Key Features

- `ICommitCoordinator`: `State`, `Relational`, `OnCommit(Func<ValueTask>)`, and `GetOrAdd<TState>` (plus an allocation-free `GetOrAdd<TState, TArg>` overload).
- `ICurrentCommitCoordinator.Current`: the innermost active coordinator for the current async flow, or `null`.
- `ICommitScope`: the owner handle with `Coordinator` and `SignalAsync(CommitOutcome)`; disposable both ways.
- `ICommitScopeFactory.Open(IRelationalCommitContext?)`: the single scope-opening primitive used by provider helpers (hidden from IntelliSense).
- `IRelationalCommitContext`: the live `DbConnection` / `DbTransaction` handle.
- `CommitCoordinatorState` (`Active`, `Committed`, `RolledBack`) and `CommitOutcome` (`Unspecified = 0`, `Committed = 1`, `RolledBack = 2`); `Unspecified` is the default sentinel and is rejected by `ICommitScope.SignalAsync`.
- `CommitProbeMode` (`Disabled` / `Warn` / `Strict`): the shared reaction posture for a provider's startup self-probe.
- `CommitRetryGuard`: shared scope-local marker for participant writes that the unit-of-work owner cannot safely replay.

## Design Notes

The root contract is not a transaction. Consumers can register work but cannot decide the terminal outcome, and nothing they register is durable: callbacks are process-local, run once per coordinator instance after the physical outcome is durable, receive no cancellation token, are not recovered after a crash, drain in registration order, and a fault in one does not stop the rest. Nothing runs on rollback.

Participants obtain `coordinator.GetOrAdd(static _ => new CommitRetryGuard())` and call `PreventRetry()` before a non-replayable write attempt. The owner retains that instance and checks `IsRetryPrevented` even after scope disposal. The marker never resets. The Headless EF adapter observes it; other unit-of-work owners must explicitly honor it.

## Installation

```bash
dotnet add package Headless.CommitCoordination.Abstractions
```

## Quick Start

```csharp
using Headless.CommitCoordination;

var coordinator = currentCommitCoordinator.Current;

if (coordinator is { State: CommitCoordinatorState.Active })
{
    // Durable row first, inside the caller's transaction, when one is present.
    var transaction = coordinator.Relational?.Transaction;

    // Fast path only: dispatch the row sooner once the commit is durable.
    coordinator.OnCommit(() => ValueTask.CompletedTask);
}
```

## Configuration

None.

## Dependencies

None.

## Side Effects

None.
