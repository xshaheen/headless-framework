# Headless.Coordination.PostgreSql

Stores coordination membership in PostgreSQL with server-clock liveness.

## Problem Solved

Provides an authoritative PostgreSQL membership provider for multi-instance apps that already depend on a PostgreSQL primary.

## Key Features

- Atomic incarnation allocation with `INSERT ... ON CONFLICT ... RETURNING`.
- Heartbeat guard rejects stale, impossible, dead, gracefully left, and pruned incarnations.
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
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
        options.ConfiguredNodeId = "orders-worker-0";
    });

    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
    });
});
```

## Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `PostgreSqlCoordinationOptions.ConnectionString`, optional `DataSource`, `CommandTimeout`, and `InitializeOnStartup` with `setup.UsePostgreSql(...)`.

## Dependencies

- `Headless.Coordination.Core.Database`
- `Headless.Hosting`
- `Npgsql`

## Side Effects

Registers the core membership services, PostgreSQL membership store, storage initializer, and initializer hosted service. Creates snake_case tables and columns. Requires PostgreSQL DDL permission when initialization runs on startup.
