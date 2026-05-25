# Headless.Features.Storage.SqlServer

SQL Server raw-DDL storage for features management.

## Problem Solved

Provides features repositories and startup schema initialization without requiring the consumer to use Entity Framework for features persistence.

## Key Features

- `AddHeadlessFeatures(setup => setup.UseSqlServer(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for feature values, definitions, and groups
- Shares `FeaturesStorageOptions` from `Headless.Features.Core`

## Installation

```bash
dotnet add package Headless.Features.Storage.SqlServer
```

## Quick Start

```csharp
builder.Services.AddFeaturesManagementCore(_ => { });
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UseSqlServer(connectionString);
});
```

## Configuration

Configure schema and table names through `FeaturesStorageOptions` on the shared features builder. Configure the connection string through `SqlServerFeaturesOptions`.

## Dependencies

- `Headless.Features.Core`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw SQL Server implementations of `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
