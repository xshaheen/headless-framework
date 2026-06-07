# Headless.Coordination.SqlServer

Stores coordination membership in SQL Server with guarded writes and server UTC time.

## Problem Solved

Provides an authoritative SQL Server membership provider for multi-instance apps that already depend on a SQL Server primary.

## Key Features

- Atomic incarnation allocation under `UPDLOCK, HOLDLOCK`.
- Equality heartbeat guard rejects stale and impossible incarnations.
- Liveness classification uses `SYSUTCDATETIME()`.
- DDL initialization uses `sp_getapplock`.

## Design Notes

The provider intentionally avoids `MERGE`. Explicit locking keeps the generation guard and liveness row update readable and testable.

## Installation

```bash
dotnet add package Headless.Coordination.SqlServer
```

## Quick Start

```csharp
services.AddSqlServerCoordination(options =>
{
    options.ConnectionString = connectionString;
});
services.Configure<CoordinationOptions>(options =>
{
    options.ClusterName = "orders";
    options.ConfiguredNodeId = "orders-worker-0";
});
```

## Configuration

Configure `ConnectionString`, `Schema` (`dbo` by default), `CommandTimeout`, and `InitializeOnStartup`. Configure shared `CoordinationOptions` for cluster name, node id, thresholds, role, metadata, and membership-loss behavior.

## Dependencies

- `Headless.Coordination.Core.Database`
- `Headless.Hosting`
- `Microsoft.Data.SqlClient`

## Side Effects

Registers the core membership services, SQL Server membership store, `ProviderCapabilities`, storage initializer, and initializer hosted service. Requires SQL Server DDL permission when initialization runs on startup.
