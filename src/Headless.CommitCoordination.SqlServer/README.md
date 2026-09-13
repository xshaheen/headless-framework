# Headless.CommitCoordination.SqlServer

## Problem Solved

Enlists raw-ADO `SqlConnection` transactions in commit coordination so work buffered inside the transaction — outbox dispatch, durable jobs — drains after commit and is discarded on rollback.

## Key Features

- `SqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO: opens the connection if closed, begins the transaction, enlists, runs the operation, commits, and signals the outcome for you (no execution-strategy retry).
- `SqlConnection.EnlistCommitCoordination(transaction, services)` — the advanced seam for a transaction you already own; returns the `ICommitScope` you must signal.
- DI extension `AddSqlServerCommitCoordination()` (parameterless; there are no provider options).

## Design Notes

SQL Server signaling is **explicit**: this package observes no commit edge, so nothing signals for you. `ExecuteCoordinatedTransactionAsync` signals `Committed` after its own `CommitAsync` and `RolledBack` when the operation or commit throws. A caller that uses `EnlistCommitCoordination` directly owns that signal — call `scope.SignalAsync(CommitOutcome.Committed)` (or `RolledBack`) immediately after the transaction completes, then dispose the scope.

An un-signalled dispose is a rollback: the enlisted work is discarded. When the transaction had already completed by the time the scope is disposed without a signal, the package logs a warning (`SqlServerCommitScope`, event 1) because the signal was almost certainly forgotten — durable outbox rows are still relay-recovered, but the fast-path dispatch was lost. An un-signalled dispose while the transaction is still open (the operation threw before commit) is the normal failure path and logs nothing.

The same contract applies to `Headless.CommitCoordination.PostgreSql`. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signals for the caller.

## Installation

```bash
dotnet add package Headless.CommitCoordination.SqlServer
```

## Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit + signal into one call so nothing can be forgotten:

```csharp
services.AddSqlServerCommitCoordination();

await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) => {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider
);
```

### Advanced: raw enlistment

```csharp
await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
await using var scope = connection.EnlistCommitCoordination(tx, requestServiceProvider);

// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await scope.SignalAsync(CommitOutcome.Committed); // REQUIRED — nothing signals for you
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers the core commit coordination services only. It starts no hosted service and subscribes to no diagnostics.
