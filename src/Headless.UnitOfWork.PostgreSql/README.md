# Headless.UnitOfWork.PostgreSql

## Problem Solved

Runs raw-ADO `NpgsqlConnection` work as a scoped unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

## Key Features

- `IUnitOfWorkManager.BeginAsync(connection, …)` — owned mode: begins the transaction on that line (opening the connection when it is closed); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back.
- `IUnitOfWorkManager.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback.
- `IUnitOfWorkManager.RunAsync(connection, operation, …)` — begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddPostgreSqlUnitOfWork()` — registers the scoped manager (idempotent; there are no provider options).

## Design Notes

Npgsql exposes no commit edge, so observed mode is explicit: nothing completes the unit for you. A unit enlisted with `Enlist` and disposed without `CompleteAsync` or `RollbackAsync` after its transaction completed is logged by the manager (`Headless.UnitOfWork.UnitOfWorkManager`, event 2) as a forgotten completion; the durable rows are relay-recovered, but the fast-path dispatch was lost. A dispose while the transaction is still open is the normal failure path and logs nothing.

`RunAsync` treats a drain fault after a durable commit as a logged error (event 11), not a caller failure: surfacing it would invite a retry that double-applies an already-committed transaction. A rollback fault after the operation threw is logged (event 10) and the operation's own exception is rethrown.

There is no execution-strategy retry for raw ADO; that is an EF Core concept (`Headless.UnitOfWork.EntityFramework`).

## Installation

```bash
dotnet add package Headless.UnitOfWork.PostgreSql
```

## Quick Start

```csharp
using Headless.UnitOfWork;
using Npgsql;

services.AddPostgreSqlUnitOfWork();

// unitOfWork is the scoped IUnitOfWorkManager; bus and jobs are the scoped facades.
await using var unit = await unitOfWork.BeginAsync(connection, cancellationToken: ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new NpgsqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (NpgsqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await bus.PublishAsync(new OrderPlaced(orderId), ct);
await unit.CompleteAsync(ct);
```

Observed mode, for a transaction you own:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var unit = unitOfWork.Enlist(connection, tx);
// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Npgsql`

## Side Effects

Registers the scoped `IUnitOfWorkManager` only.
