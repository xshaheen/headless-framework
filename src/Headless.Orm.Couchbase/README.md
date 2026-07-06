# Headless.Orm.Couchbase

Couchbase integration with bucket context, document set operations, cluster management, and transaction support.

## Problem Solved

Provides a typed context model over Couchbase buckets with helper APIs for document operations (KV, LookupIn, MutateIn, scan, N1QL queries, transactions) and schema bootstrap (scope/collection/index lifecycle), following the same context-provider pattern as `Headless.Orm.EntityFramework` but for the document model.

## Key Features

- `CouchbaseBucketContext` base context over Linq2Couchbase `BucketContext` — exposes typed `Query<T>(scope, collection)` for N1QL and `ExecuteTransactionAsync(Func<AttemptContext, Task<bool>>)` for Couchbase Transactions
- `IBucketContextProvider` / `BucketContextProvider` — resolves typed contexts per cluster key + bucket name + default scope; wires cluster and transaction objects via `ICouchbaseClustersProvider`
- `ICouchbaseClustersProvider` / `CouchbaseClustersProvider` — manages cluster connections keyed by `clusterKey`, each lazily initialized and cached; returns a `(ICluster, Transactions)` tuple per `GetClusterAsync`
- `DocumentSetExtensions` — KV operations (`GetAsync`, `ExistsAsync`, `UpsertAsync`, `InsertAsync`, `ReplaceAsync`, `RemoveAsync`, `UnlockAsync`, `TouchAsync`, `GetAndLockAsync`, `GetAnyReplicaAsync`, `LookupInAsync`, `MutateInAsync`, `ScanAsync`) typed against `IEntity` models; keys are derived from `IEntity.GetKey()`
- `ICouchbaseManager` / `CouchbaseManager` — idempotent scope, collection, and index bootstrapping with Polly retry; `CreateScopeAsync`, `CreateCollectionsAsync`, `CreateSecondaryIndexAsync`, `BuildDeferredIndexesAsync`
- `ICouchbaseClusterOptionsProvider` — consumer-supplied cluster connection options per cluster key
- `ICouchbaseTransactionConfigProvider` — consumer-supplied transaction configuration per cluster key
- `CouchbaseEventingFunctionsSeeder` — seeds eventing functions from embedded resources
- `SetupCouchbase.AddCouchbase()` — registers the framework-owned providers (`ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, `ICouchbaseAssemblyCollectionsReader`) in one call

## Installation

```bash
dotnet add package Headless.Orm.Couchbase
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
services.AddCouchbase();

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
- Call `services.AddCouchbase()` to register the framework-owned providers.
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

- `AddCouchbase()` registers `ICouchbaseClustersProvider`, `IBucketContextProvider`, `ICouchbaseManager`, and `ICouchbaseAssemblyCollectionsReader` as singletons via `TryAdd` (a consumer's own registration wins). It does not register `ICouchbaseClusterOptionsProvider` or `ICouchbaseTransactionConfigProvider`.
- Cluster connections are lazily initialized and statically cached by `clusterKey` in `CouchbaseClustersProvider`. Each cluster waits up to 1 minute for readiness on first access; a readiness failure is logged but does not throw (operations fail at call time).
- `CouchbaseManager` caches scope/collection specs per `clusterKey + bucketName` in-memory to reduce repeated `GetAllScopesAsync` calls; cache is invalidated on scope creation.
- `CouchbaseBucketContext.ExecuteTransactionAsync` emits `Information` logs on success and `Error` logs on failure via structured logging.
