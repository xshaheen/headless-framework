# Headless.Settings.Storage.PostgreSql

PostgreSQL raw-DDL storage for settings management.

## Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence. All schema is created idempotently at host startup via raw ADO.NET.

## Key Features

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — overload that binds `PostgreSqlSettingsOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlSettingsOptions> configure)` — overload for full option control
- `setup.UsePostgreSql(Action<PostgreSqlSettingsOptions, IServiceProvider> configure)` — overload for late-bound configuration
- Idempotent schema, table, and index creation at host startup via `PostgreSqlSettingsStorageInitializer`
- Raw ADO.NET repositories for setting values and definitions
- `PostgreSqlSettingsOptions` — connection string and command timeout
- Shares `SettingsStorageOptions` from `Headless.Settings.Core` (schema, table names, `InitializeOnStartup`)

## Installation

```bash
dotnet add package Headless.Settings.Storage.PostgreSql
```

## Quick Start

`AddHeadlessSettings(...)` registers the settings management core automatically. Register the required services first — `TimeProvider`, caching, distributed lock, and `IStringEncryptionService` (the management core throws on startup if encryption is missing).

```csharp
builder.Services.AddCaching();
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
builder.Services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UsePostgreSql(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessSettings(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

## Configuration

Configure schema and table names through `SettingsStorageOptions` on the shared settings builder. Configure the connection string and command timeout through `PostgreSqlSettingsOptions`.

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | PostgreSQL connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Set `SettingsStorageOptions.InitializeOnStartup = false` to skip the startup DDL when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block.

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(connectionString);
});
```

## Dependencies

- `Headless.Settings.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlSettingValueRecordRepository` as `ISettingValueRecordRepository` (singleton)
- Registers `PostgreSqlSettingDefinitionRecordRepository` as `ISettingDefinitionRecordRepository` (singleton)
