# Headless.Features.Storage.SqlServer

SQL Server raw-DDL storage for feature management.

## Problem Solved

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(Action<SqlServerFeaturesOptions> configure)` — overload for full option control
- Idempotent schema, table, and index creation at host startup via `SqlServerFeaturesStorageInitializer`
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- `SqlServerFeaturesOptions` — connection string and command timeout (`CommandTimeout`, default 30 seconds)
- Shares `FeaturesStorageOptions` with `Headless.Features.Storage.EntityFramework` (schema, table names, `InitializeOnStartup`)

## Installation

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` registers the management core automatically.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UseSqlServer(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

`SqlServerFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `FeaturesStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

## Dependencies

- `Headless.Features.Core`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerFeatureValueRecordRepository` as `IFeatureValueRecordRepository` (singleton)
- Registers `SqlServerFeatureDefinitionRecordRepository` as `IFeatureDefinitionRecordRepository` (singleton)
