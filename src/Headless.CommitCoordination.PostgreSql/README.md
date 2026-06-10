# Headless.CommitCoordination.PostgreSql

## Problem Solved

Provides PostgreSQL commit coordination registration points for inline framework-owned transaction flows.

## Key Features

- `PostgreSqlCommitSignalSource`.
- DI extension `AddPostgreSqlCommitCoordination()`.
- `NpgsqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO (opens the connection if closed; no execution-strategy retry).

## Installation

```bash
dotnet add package Headless.CommitCoordination.PostgreSql
```

## Quick Start

```csharp
services.AddPostgreSqlCommitCoordination();

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
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Npgsql`

## Side Effects

Registers core commit coordination services, `PostgreSqlCommitSignalSource`, and `ICommitSignalSource`.
