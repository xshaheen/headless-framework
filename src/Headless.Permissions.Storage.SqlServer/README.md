# Headless.Permissions.Storage.SqlServer

SQL Server raw-DDL storage for permissions management.

## Problem Solved

Provides permissions repositories and startup schema initialization without requiring the consumer to use Entity Framework for permissions persistence.

## Key Features

- `AddHeadlessPermissions(setup => setup.UseSqlServer(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for permission grants, definitions, and groups
- Shares `PermissionsStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Permissions.Storage.SqlServer
```

## Quick Start

```csharp
builder.Services.AddPermissionsManagementCore(_ => { });
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UseSqlServer(connectionString);
});
```

## Configuration

Configure schema and table names through `PermissionsStorageOptions` on the shared permissions builder. Configure the connection string through `SqlServerPermissionsOptions`.

## Dependencies

- `Headless.Permissions.Storage.EntityFramework`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw SQL Server implementations of `IPermissionGrantRepository` and `IPermissionDefinitionRecordRepository`
