# Headless.Settings.Storage.SqlServer

SQL Server raw-DDL storage for settings management.

## Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — overload that binds `SqlServerSettingsOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerSettingsOptions> configure)` — overload for full option control
- `setup.UseSqlServer(Action<SqlServerSettingsOptions, IServiceProvider> configure)` — overload for late-bound configuration
- Idempotent schema, table, and index creation at host startup via `SqlServerSettingsStorageInitializer`
- Raw ADO.NET repositories for setting values and definitions
- `SqlServerSettingsOptions` — connection string and command timeout
- Shares `SettingsStorageOptions` from `Headless.Settings.Core` (schema, table names, `InitializeOnStartup`)

## Installation

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

## Quick Start

`AddHeadlessSettings(...)` registers the settings management core automatically. Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService` (the management core throws on startup if encryption is missing).

```csharp
builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);

builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UseSqlServer(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessSettings(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

Configure schema and table names through `SettingsStorageOptions` on the shared settings builder. Configure the connection string and command timeout through `SqlServerSettingsOptions`.

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Set `SettingsStorageOptions.InitializeOnStartup = false` to skip the startup DDL when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block.

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UseSqlServer(connectionString);
});
```

## Dependencies

- `Headless.Settings.Core`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerSettingValueRecordRepository` as `ISettingValueRecordRepository` (singleton)
- Registers `SqlServerSettingDefinitionRecordRepository` as `ISettingDefinitionRecordRepository` (singleton)
