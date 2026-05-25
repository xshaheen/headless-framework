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

```csharp
builder.Services.AddPermissionsManagementCore(_ => { });
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `PermissionsStorageOptions` on the shared permissions builder. Configure the connection string through `PostgreSqlPermissionsOptions`.

## Dependencies

- `Headless.Permissions.Core`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of `IPermissionGrantRepository` and `IPermissionDefinitionRecordRepository`
