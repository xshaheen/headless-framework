# Headless.Settings.Storage.SqlServer

SQL Server raw-DDL storage for settings management.

## Problem Solved

Provides settings repositories and startup schema initialization without requiring the consumer to use Entity Framework for settings persistence.

## Key Features

- `AddHeadlessSettings(setup => setup.UseSqlServer(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for setting values and definitions
- Shares `SettingsStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Settings.Storage.SqlServer
```

## Quick Start

```csharp
builder.Services.AddSettingsManagementCore(_ => { });
builder.Services.AddHeadlessSettings(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "settings");
    setup.UseSqlServer(connectionString);
});
```

## Configuration

Configure schema and table names through `SettingsStorageOptions` on the shared settings builder. Configure the connection string through `SqlServerSettingsOptions`.

## Dependencies

- `Headless.Settings.Storage.EntityFramework`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerSettingsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw SQL Server implementations of the settings repositories
