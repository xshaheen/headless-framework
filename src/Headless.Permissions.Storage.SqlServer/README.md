# Headless.Permissions.Storage.SqlServer

SQL Server raw-DDL storage for permission management.

## Problem Solved

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — binds `SqlServerPermissionsOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerPermissionsOptions> configure)` — full option control
- `setup.UseSqlServer(Action<SqlServerPermissionsOptions, IServiceProvider> configure)` — with resolved services
- Idempotent schema, table, and index creation at host startup via `SqlServerPermissionsStorageInitializer`
- `SqlServerPermissionsOptions` — `ConnectionString` and `CommandTimeout` (default 30 seconds)
- Shares `PermissionsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)
- Identifier names validated against SQL Server naming rules

## Installation

```bash
dotnet add package Headless.Permissions.Storage.SqlServer
```

## Quick Start

Register required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessPermissions` registers the management core automatically.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UseSqlServer(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

`SqlServerPermissionsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `PermissionsStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band. The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block. Defaults to `true`.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UseSqlServer(connectionString);
});
```

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerPermissionGrantRepository` as `IPermissionGrantRepository` (singleton)
- Registers `SqlServerPermissionDefinitionRecordRepository` as `IPermissionDefinitionRecordRepository` (singleton)
