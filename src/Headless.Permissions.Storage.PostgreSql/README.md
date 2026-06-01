# Headless.Permissions.Storage.PostgreSql

PostgreSQL raw-DDL storage for permissions management.

## Problem Solved

Provides permissions repositories and startup schema initialization without requiring the consumer to use Entity Framework for permissions persistence.

## Key Features

- `AddHeadlessPermissions(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for permission grants, definitions, and groups
- Uses the shared `PermissionsStorageOptions` from `Headless.Permissions.Core`

## Installation

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

## Quick Start

`AddHeadlessPermissions(...)` registers the permissions management core automatically.
Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and
`IGuidGenerator`.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `PermissionsStorageOptions` on the shared permissions builder. Configure the connection string through `PostgreSqlPermissionsOptions`.

Set `PermissionsStorageOptions.InitializeOnStartup = false` to skip the startup DDL when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. Defaults to `true`.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(...);
});
```

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of `IPermissionGrantRepository` and `IPermissionDefinitionRecordRepository`
