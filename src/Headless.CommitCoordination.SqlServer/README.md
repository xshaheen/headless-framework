# Headless.CommitCoordination.SqlServer

## Problem Solved

Correlates SQL Server commit or rollback signals to attached commit scopes.

## Key Features

- `SqlServerCommitSignalSource`.
- Provider-key registry for detected commit and rollback signals.
- DI extension `AddSqlServerCommitCoordination()`.
- `SqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO (opens the connection if closed; no execution-strategy retry).

## Design Notes

Detected signals remove the scope from the registry before signaling. The returned scope still owns the ambient pop; the signal source owns an async service scope for the out-of-band drain and releases it after the terminal signal completes.

The SqlClient diagnostic is a low-latency commit signal, not the durability mechanism. It depends on `Microsoft.Data.SqlClient` diagnostic event names and payload shapes, which can drift across driver upgrades, so a missed, delayed, or disabled diagnostic must not lose work: the consumer commits a durable row inside the transaction and recovers it by polling, and a faulted drain is left for that recovery path. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signal has no such fragility.

## Installation

```bash
dotnet add package Headless.CommitCoordination.SqlServer
```

## Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (commit detection is out-of-band here, so no manual signal is needed, unlike PostgreSQL).

```csharp
services.AddSqlServerCommitCoordination();

// Open + enlist + commit in one call; the enlist cannot be forgotten.
await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) =>
    {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider);
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers core commit coordination services, `SqlServerCommitSignalSource`, `ICommitSignalSource`, the SqlClient diagnostic observer/listener, and an `IHostedService` that owns the diagnostic subscription lifetime.
