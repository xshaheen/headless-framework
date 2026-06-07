# Headless.Coordination.PostgreSql

Stores coordination membership in PostgreSQL with server-clock liveness.

## Problem Solved

Provides an authoritative PostgreSQL membership provider for multi-instance apps that already depend on a PostgreSQL primary.

## Key Features

- Atomic incarnation allocation with `INSERT ... ON CONFLICT ... RETURNING`.
- Equality heartbeat guard rejects stale and impossible incarnations.
- Liveness classification uses `clock_timestamp()`.
- DDL initialization uses PostgreSQL advisory locks.

## Design Notes

Operational reads join the generation table so superseded incarnations are not live candidates. Consumers must use the primary/write path for failover-driving reads.

## Installation

```bash
dotnet add package Headless.Coordination.PostgreSql
```

## Quick Start

```csharp
services.AddPostgresCoordination(options =>
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

Configure `PostgreSqlCoordinationOptions.ConnectionString`, optional `DataSource`, `CommandTimeout`, and `InitializeOnStartup`. Configure shared `CoordinationOptions` for cluster name, node id, thresholds, role, metadata, and membership-loss behavior.

## Dependencies

- `Headless.Coordination.Core.Database`
- `Headless.Hosting`
- `Npgsql`

## Side Effects

Registers the core membership services, PostgreSQL membership store, `ProviderCapabilities`, storage initializer, and initializer hosted service. Requires PostgreSQL DDL permission when initialization runs on startup.
