# Headless.Permissions.Storage.PostgreSql

PostgreSQL raw-DDL storage for permission management.

## Problem Solved

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — binds `PostgreSqlPermissionsOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlPermissionsOptions> configure)` — full option control
- `setup.UsePostgreSql(Action<PostgreSqlPermissionsOptions, IServiceProvider> configure)` — with resolved services
- Idempotent schema, table, and index creation at host startup via `PostgreSqlPermissionsStorageInitializer`
- `PostgreSqlPermissionsOptions` — `ConnectionString` and `CommandTimeout` (default 30 seconds)
- Shares `PermissionsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)
- Identifier names validated against PostgreSQL naming rules

## Installation

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

## Quick Start

Register required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessPermissions` registers the management core automatically.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UsePostgreSql(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

`PostgreSqlPermissionsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | Npgsql connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `PermissionsStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band. The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block. Defaults to `true`.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(connectionString);
});
```

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlPermissionGrantRepository` as `IPermissionGrantRepository` (singleton)
- Registers `PostgreSqlPermissionDefinitionRecordRepository` as `IPermissionDefinitionRecordRepository` (singleton)
