# Headless.Features.Storage.PostgreSql

PostgreSQL raw-DDL storage for features management.

## Problem Solved

Provides features repositories and startup schema initialization without requiring the consumer to use Entity Framework for features persistence.

## Key Features

- `AddHeadlessFeatures(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for feature values, definitions, and groups
- Shares `FeaturesStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

## Quick Start

```csharp
builder.Services.AddFeaturesManagementCore(_ => { });
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `FeaturesStorageOptions` on the shared features builder. Configure the connection string through `PostgreSqlFeaturesOptions`.

## Dependencies

- `Headless.Features.Storage.EntityFramework`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
