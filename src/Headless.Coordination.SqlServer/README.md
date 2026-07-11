# Headless.Coordination.SqlServer

Stores coordination membership in SQL Server with guarded writes and server UTC time.

## Problem Solved

Provides an authoritative SQL Server membership provider for multi-instance apps that already depend on a SQL Server primary.

## Key Features

- Atomic incarnation allocation under `UPDLOCK, HOLDLOCK`.
- Heartbeat guard rejects stale, impossible, dead, gracefully left, and pruned incarnations.
- Liveness classification uses `SYSUTCDATETIME()`.
- Bounded jittered retry for SQL Server deadlock victim error `1205` during guarded membership writes.
- DDL initialization uses `sp_getapplock`.

## Design Notes

The provider intentionally avoids `MERGE`. Explicit locking keeps the generation guard and liveness row update readable and testable.

Guarded membership writes intentionally keep `SERIALIZABLE` transactions plus generation-first `UPDLOCK, HOLDLOCK` access. Under a large concurrent startup, SQL Server can still choose one writer as deadlock victim (`1205`); the provider retries that rolled-back transaction with a short bounded jittered Polly policy. The retry is SQL Server-specific and does not lower isolation or serialize the hot path with `sp_getapplock`.

## Installation

```bash
dotnet add package Headless.Coordination.SqlServer
```

## Quick Start

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
        options.ConfiguredNodeId = "orders-worker-0";
    });

    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
    });
});
```

## Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `ConnectionString`, `Schema` (`dbo` by default), `CommandTimeout`, and `InitializeOnStartup` with `setup.UseSqlServer(...)`.

## Dependencies

- `Headless.Coordination.Core.Database`
- `Headless.Hosting`
- `Microsoft.Data.SqlClient`
- `Polly.Core`

## Side Effects

Registers the core membership services, SQL Server membership store, storage initializer, and initializer hosted service. Creates PascalCase tables and columns. Requires SQL Server DDL permission when initialization runs on startup.
