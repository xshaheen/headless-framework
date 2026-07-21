# Headless.CommitCoordination.Abstractions

## Problem Solved

Defines the public commit coordination contracts without provider dependencies.

## Key Features

- `ICommitCoordinator`, `ICurrentCommitCoordinator`, `ICommitScope`, and `ICommitSignalSource`.
- Outcome callbacks for commit and rollback.
- `CommitOutcome` has explicit values (`Unspecified = 0`, `Committed = 1`, `RolledBack = 2`); `Unspecified` is the default sentinel and is rejected by `ICommitScope.SignalAsync`.
- Typed scope-local work buffers.
- Capability lookup through `ICommitCapability`.

## Design Notes

The root contract is not a transaction. Consumers can register work but cannot decide the terminal outcome.

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
