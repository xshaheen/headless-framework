# Headless.CommitCoordination.PostgreSql

## Problem Solved

Provides PostgreSQL commit coordination registration points for inline framework-owned transaction flows.

## Key Features

- Internal `PostgreSqlCommitSignalSource` registered as `ICommitSignalSource`.
- DI extension `AddPostgreSqlCommitCoordination()`.
- `NpgsqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO (opens the connection if closed; no execution-strategy retry).

## Installation

```bash
dotnet add package Headless.CommitCoordination.PostgreSql
```

## Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit + signal into one call so nothing can be forgotten:

```csharp
services.AddPostgreSqlCommitCoordination();

// Open + enlist + commit in one call; the enlist cannot be forgotten.
await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) => {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider
);
```

### Advanced: raw enlistment

> **WARNING — PostgreSQL is an inline (caller-driven) signal provider.** Npgsql exposes no commit
> diagnostic, so nothing signals for you. If you hand-roll `EnlistCommitCoordination`, you MUST call
> `scope.SignalAsync(CommitOutcome.Committed)` immediately after `transaction.CommitAsync(...)`.
> An un-signalled scope dispose drains as **rollback** and silently discards every enlisted publish on
> a transaction that actually committed — durable outbox rows survive (the relay sweep recovers them),
> but accelerator-only work is lost. Prefer the helper above; it signals for you.

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var scope = connection.EnlistCommitCoordination(tx, requestServiceProvider);

// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await scope.SignalAsync(CommitOutcome.Committed); // REQUIRED — see warning above
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Npgsql`

## Side Effects

Registers core commit coordination services and the internal `PostgreSqlCommitSignalSource` (exposed as `ICommitSignalSource`).
