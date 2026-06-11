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
