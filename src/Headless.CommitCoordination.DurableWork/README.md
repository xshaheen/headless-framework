# Headless.CommitCoordination.DurableWork

## Problem Solved

Provides a base for durable work buffers that must write rows inside the active relational transaction.

## Key Features

- `DurableWorkBuffer<TRow>` base class.
- `DurableWorkProviderMismatchPolicy.Throw` default.
- Explicit `Warn` fallback for consumers that already have recovery.

## Design Notes

Durable work fails closed by default because running a job before its triggering data commits is a correctness bug.

## Installation

```bash
dotnet add package Headless.CommitCoordination.DurableWork
```

## Quick Start

```csharp
public sealed record JobRow(string Name);

public sealed class JobWorkBuffer(ICommitCoordinator coordinator)
    : DurableWorkBuffer<JobRow>(coordinator)
{
    protected override ValueTask WriteRowAsync(JobRow row, IRelationalCommitContext context, CancellationToken ct)
    {
        return ValueTask.CompletedTask;
    }
}
```

## Configuration

Choose `DurableWorkProviderMismatchPolicy.Throw` or `Warn` per buffer.

## Dependencies

- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

None.
