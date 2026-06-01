# Headless.Features.Storage.PostgreSql

PostgreSQL raw-DDL storage for features management.

## Problem Solved

Provides features repositories and startup schema initialization without requiring the consumer to use Entity Framework for features persistence.

## Key Features

- `AddHeadlessFeatures(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for feature values, definitions, and groups
- Shares `FeaturesStorageOptions` from `Headless.Features.Core`

## Installation

```bash
dotnet add package Headless.Features.Storage.PostgreSql
```

## Quick Start

`AddHeadlessFeatures(...)` registers the features management core automatically. Register
the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and
`IGuidGenerator`.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "features");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `FeaturesStorageOptions` on the shared features builder. Configure the connection string through `PostgreSqlFeaturesOptions`.

Set `FeaturesStorageOptions.InitializeOnStartup = false` to skip the startup DDL when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. Defaults to `true`.

```csharp
builder.Services.AddHeadlessFeatures(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(...);
});
```

## Dependencies

- `Headless.Features.Core`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlFeaturesStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of `IFeatureValueRecordRepository` and `IFeatureDefinitionRecordRepository`
