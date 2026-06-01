# Headless.Settings.Storage.PostgreSql

PostgreSQL raw-DDL storage for settings management.

## Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence.

## Key Features

- `AddHeadlessSettings(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for setting values and definitions
- Shares `SettingsStorageOptions` from `Headless.Settings.Core`

## Installation

```bash
dotnet add package Headless.Settings.Storage.PostgreSql
```

## Quick Start

`AddHeadlessSettings(...)` registers the settings management core automatically. Register
the required services first — `TimeProvider`, caching, distributed lock, and
`IStringEncryptionService` (the management core throws on startup if encryption is missing).

```csharp
builder.Services.AddCaching();
builder.Services.AddDistributedLock();
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetRequiredSection("Headless:StringEncryption")
);

builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `SettingsStorageOptions` on the shared settings builder. Configure the connection string through `PostgreSqlSettingsOptions`.

Set `SettingsStorageOptions.InitializeOnStartup = false` to skip the startup DDL when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. Defaults to `true`.

```csharp
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(...);
});
```

## Dependencies

- `Headless.Settings.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of the settings repositories
