# Headless.CommitCoordination.Abstractions

## Problem Solved

Defines the public commit coordination contracts without provider dependencies.

## Key Features

- `ICommitCoordinator`, `ICurrentCommitCoordinator`, `ICommitScope`, and `ICommitSignalSource`.
- Outcome callbacks for commit and rollback.
- `CommitOutcome` has explicit values (`Unspecified = 0`, `Committed = 1`, `RolledBack = 2`); `Unspecified` is the default sentinel and is rejected by `ICommitScope.SignalAsync`.
- Typed scope-local work buffers.
- `CommitRetryGuard`: shared scope-local marker for participant writes that the unit-of-work owner cannot safely replay.
- Capability lookup through `ICommitCapability`.

## Design Notes

The root contract is not a transaction. Consumers can register work but cannot decide the terminal outcome.

Participants obtain `coordinator.GetOrAdd(static _ => new CommitRetryGuard())` and call `PreventRetry()` before a non-replayable write attempt. The owner retains that instance and checks `IsRetryPrevented` even after scope disposal. The marker never resets. The Headless EF adapter observes it; other unit-of-work owners must explicitly honor it.

## Installation

```bash
dotnet add package Headless.CommitCoordination.Abstractions
```

## Quick Start

```csharp
var coordinator = currentCommitCoordinator.Current;
coordinator?.OnCommit((context, ct) => ValueTask.CompletedTask);
```

## Configuration

None.

## Dependencies

None.

## Side Effects

None.
