# Headless.Features.Storage.PostgreSql

PostgreSQL raw-DDL storage for feature management.

## Problem Solved

Provides feature repositories and startup schema initialization without requiring the consumer to use Entity Framework for feature persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — binds `PostgreSqlFeaturesOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlFeaturesOptions> configure)` — overload for full option control
- `setup.UsePostgreSql(Action<PostgreSqlFeaturesOptions, IServiceProvider> configure)` — overload with service-provider access for late-bound configuration
- Idempotent schema, table, and index creation at host startup via `PostgreSqlFeaturesStorageInitializer`
- Raw ADO.NET repositories for feature values, feature definitions, and feature group definitions
- `PostgreSqlFeaturesOptions` — connection string and command timeout (`CommandTimeout`, default 30 seconds)
- Shares `FeaturesStorageOptions` with `Headless.Features.Storage.EntityFramework` (schema, table names, `InitializeOnStartup`)

## Installation

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessFeatures` registers the management core automatically.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UsePostgreSql(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

`PostgreSqlFeaturesOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | PostgreSQL connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `FeaturesStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

## Dependencies

- `Headless.Features.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlFeatureValueRecordRepository` as `IFeatureValueRecordRepository` (singleton)
- Registers `PostgreSqlFeatureDefinitionRecordRepository` as `IFeatureDefinitionRecordRepository` (singleton)
