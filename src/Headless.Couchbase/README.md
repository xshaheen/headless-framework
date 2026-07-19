# Headless.Couchbase

Couchbase integration with bucket context, document set operations, cluster management, and transaction support.

## Problem Solved

Provides a typed context model over Couchbase buckets with helper APIs for document operations (KV, LookupIn, MutateIn, scan, N1QL queries, transactions) and schema bootstrap (scope/collection/index lifecycle), following the same context-provider pattern as `Headless.EntityFramework` but for the document model.

## Key Features

- `CouchbaseBucketContext` base context over Linq2Couchbase `BucketContext` — exposes typed `Query<T>(scope, collection)` for N1QL and `ExecuteTransactionAsync(Func<AttemptContext, Task<bool>>)` for Couchbase Transactions
- `IBucketContextProvider` / `BucketContextProvider` — resolves typed contexts per cluster key + bucket name + default scope; wires cluster and transaction objects via `ICouchbaseClustersProvider`
- `ICouchbaseClustersProvider` / `CouchbaseClustersProvider` — manages cluster connections keyed by `clusterKey`, each lazily initialized and cached; returns a `CouchbaseClusterConnection` (cluster + transaction manager) per `GetClusterAsync`
- `DocumentSetExtensions` — KV operations (`GetAsync`, `ExistsAsync`, `UpsertAsync`, `InsertAsync`, `ReplaceAsync`, `RemoveAsync`, `UnlockAsync`, `TouchAsync`, `GetAndLockAsync`, `GetAnyReplicaAsync`, `GetAllReplicasAsync`, `LookupInAsync`, `MutateInAsync`, `ScanAsync`) typed against `IEntity` models; keys are derived from `IEntity.GetKey()`
- `ICouchbaseManager` / `CouchbaseManager` — idempotent scope, collection, and index bootstrapping with Polly retry (configurable via `CouchbaseManagerOptions`: `MaxRetries`, `RetryDelay`, `Timeout`); `CreateScopeAsync`, `CreateCollectionsAsync`, `CreateSecondaryIndexAsync`, `BuildDeferredIndexesAsync`
- `ICouchbaseClusterOptionsProvider` — consumer-supplied cluster connection options per cluster key
- `ICouchbaseTransactionConfigProvider` — consumer-supplied transaction configuration per cluster key
- `CouchbaseEventingFunctionsSeeder` — seeds eventing functions from embedded resources
- `SetupCouchbase.AddHeadlessCouchbase()` — registers the framework-owned providers (`ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, `ICouchbaseAssemblyCollectionsReader`) in one call; overloads accept `IConfiguration`, `Action<CouchbaseManagerOptions>`, or `Action<CouchbaseManagerOptions, IServiceProvider>` to tune the manager's resilience options

## Installation

```bash
dotnet add package Headless.Couchbase
```

## Quick Start

```csharp
// Define a typed bucket context
public sealed class AppBucketContext(
    IBucket bucket,
    Transactions transactions,
    ILogger<CouchbaseBucketContext> logger
) : CouchbaseBucketContext(bucket, transactions, logger)
{
    public DocumentSet<Product> Products => GetDocumentSet<Product>("products");
}

// Register the two application-specific providers (or use the shipped defaults):
services.AddSingleton<ICouchbaseClusterOptionsProvider, MyClusterOptionsProvider>();
services.AddSingleton<ICouchbaseTransactionConfigProvider, MyTransactionConfigProvider>();

// Register the framework-owned providers in one call:
services.AddHeadlessCouchbase();

// Resolve context
var context = await bucketContextProvider.GetAsync<AppBucketContext>(
    clusterKey: "default",
    bucketName: "app",
    defaultScopeName: "_default"
);

// KV operations via DocumentSetExtensions
var product = await context.Products.GetAsync<Product, string>("product-123");
await context.Products.UpsertAsync(product);

// N1QL query
var results = context.Query<Product>("_default", "products")
    .Where(p => p.IsActive)
    .ToList();

// Couchbase Transaction — return true to commit, false to rollback
await context.ExecuteTransactionAsync(async attempt =>
{
    // perform transactional KV operations through attempt
    return true;
});
```

## Configuration

- Implement and register `ICouchbaseClusterOptionsProvider` to supply cluster options (connection string, credentials) per cluster key.
- Implement and register `ICouchbaseTransactionConfigProvider` to supply transaction configuration per cluster key.
- Call `services.AddHeadlessCouchbase()` to register the framework-owned providers.
- Optionally tune the manager's Polly resilience via `CouchbaseManagerOptions` (`MaxRetries`, default 3; `RetryDelay`, default 500 ms; `Timeout`, default 10 s) using the `AddHeadlessCouchbase(IConfiguration)` / `AddHeadlessCouchbase(Action<CouchbaseManagerOptions>)` / `AddHeadlessCouchbase(Action<CouchbaseManagerOptions, IServiceProvider>)` overloads; options are validated (FluentValidation) on startup.
- Use `ICouchbaseManager` during application startup or in an `IInitializer` to bootstrap scopes, collections, and indexes idempotently.

## Dependencies

- `Headless.Domain`
- `Headless.Hosting`
- `Couchbase.Extensions.DependencyInjection`
- `Couchbase.Transactions`
- `Linq2Couchbase`
- `Polly`
- `Humanizer`

## Side Effects

- `AddHeadlessCouchbase()` registers `ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, and `ICouchbaseAssemblyCollectionsReader` as singletons via `TryAdd` (a consumer's own registration wins). It does not register `ICouchbaseClusterOptionsProvider` or `ICouchbaseTransactionConfigProvider`.
- Cluster connections are lazily initialized and cached per `CouchbaseClustersProvider` instance (a singleton within one container) by `clusterKey`; separate containers hold independent connections. Each cluster waits up to 1 minute for readiness on first access; a readiness failure is logged but does not throw (operations fail at call time).
- `CouchbaseManager` caches scope/collection specs per `clusterKey + bucketName` in-memory to reduce repeated `GetAllScopesAsync` calls; cache is invalidated on scope creation.
- `CouchbaseBucketContext.ExecuteTransactionAsync` emits `Information` logs on success and `Error` logs on failure via structured logging.

## Cancellation

- The async provider seams (`ICouchbaseClusterOptionsProvider.GetAsync`, `ICouchbaseTransactionConfigProvider.GetAsync`, `ICouchbaseClustersProvider.GetClusterAsync`, `IBucketContextProvider.GetAsync`) and `CouchbaseBucketContext.ExecuteTransactionAsync` accept an optional trailing `CancellationToken`.
- `DocumentSetExtensions.GetAllReplicasAsync` returns one task per replica and accepts `GetAllReplicasOptions`; pass the caller token through `GetAllReplicasOptions.CancellationToken(...)`.
- Because clusters are created once per provider and cached by `clusterKey`, the token passed to `GetClusterAsync` governs only the connection attempt that first materializes a cluster; callers that receive an already-cached cluster complete without observing the token.
- `ICluster.BucketAsync` exposes no cancellation overload, so `IBucketContextProvider.GetAsync` honors the token before opening the bucket, not during.
- The Couchbase transactions SDK (`Transactions.RunAsync`) exposes no `CancellationToken` hook, so `ExecuteTransactionAsync` observes the token only before the transaction begins; once the SDK transaction loop starts it runs to completion, SDK timeout, or failure. Bound in-transaction duration with `PerTransactionConfig` (timeout / durability) instead.
