---
domain: ORM
packages: Orm.EntityFramework, Orm.Couchbase
---

# ORM

> ORM domain includes two packages only: `Headless.Orm.EntityFramework` and `Headless.Orm.Couchbase`.

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Package Validation Snapshot](#package-validation-snapshot)
- [Agent Instructions](#agent-instructions)
- [Headless.Orm.EntityFramework](#headlessormentityframework)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Transaction Pattern](#transaction-pattern)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
    - [Validation Checklist](#validation-checklist)
- [Headless.Orm.Couchbase](#headlessormcouchbase)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Transaction Pattern](#transaction-pattern-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
    - [Validation Checklist](#validation-checklist-1)

## Quick Orientation

Choose package by storage model:

- `Headless.Orm.EntityFramework`: relational databases via EF Core (`DbContext` model, global filters, auditing, domain events)
- `Headless.Orm.Couchbase`: document database via Couchbase (`BucketContext`, document sets, KV + query + transaction helpers)

Use these packages when you want ORM-level persistence primitives. For raw SQL connection factories and Dapper-style access, use SQL packages instead.

## Package Validation Snapshot

Validated against current source tree on `29-03-2026` (UTC).

| Package                        | Source package path                                                    | Status  |
| ------------------------------ | ---------------------------------------------------------------------- | ------- |
| `Headless.Orm.EntityFramework` | `src/Headless.Orm.EntityFramework/Headless.Orm.EntityFramework.csproj` | Present |
| `Headless.Orm.Couchbase`       | `src/Headless.Orm.Couchbase/Headless.Orm.Couchbase.csproj`             | Present |

Validation notes:

- Domain frontmatter includes only existing ORM packages.
- API guidance below maps to currently present symbols in source (for example: `HeadlessDbContext`, `AddHeadlessDbContext<TDbContext>`, `ExecuteTransactionAsync`, `CouchbaseBucketContext`, `DocumentSetExtensions`, `IBucketContextProvider`).

## Agent Instructions

- Treat this domain as exactly two packages. Do not reference non-existing ORM packages.
- For relational stores, inherit from `HeadlessDbContext` and register with `AddHeadlessDbContext<TDbContext>(...)`.
- Always call `base.OnModelCreating(modelBuilder)` in `HeadlessDbContext` subclasses.
- Use `ExecuteTransactionAsync(...)` for multi-step EF operations that must be atomic under retry execution strategies.
- Do not mix framework concurrency stamping with ASP.NET Identity `ConcurrencyStamp` ownership on identity entities.
- For Couchbase, use `CouchbaseBucketContext` + `IBucketContextProvider` and keep cluster/bucket names explicit.
- `DocumentSetExtensions` are constrained to `IEntity` models and provide high-level KV operations.
- Keep local event handlers idempotent in EF workflows because retries can re-run handlers.

---

# Headless.Orm.EntityFramework

Entity Framework Core integration with framework conventions and save pipeline orchestration.

## Problem Solved

Provides a framework-aware base `DbContext` with conventions for auditing, soft delete, tenant filters, domain events, and transaction-aware save behavior.

## Key Features

- `HeadlessDbContext` base context
- DI registration via `AddHeadlessDbContext<TDbContext>(...)`
- Automatic audit fields (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`)
- Soft-delete and tenant-aware query behavior
- Domain event collection and dispatch in save pipeline
- Resilient transaction helpers: `ExecuteTransactionAsync(...)`
- Extensibility hooks through model processing services

## Installation

```bash
dotnet add package Headless.Orm.EntityFramework
```

## Quick Start

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : HeadlessDbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

builder.Services.AddHeadlessDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);
```

## Transaction Pattern

```csharp
await dbContext.ExecuteTransactionAsync(async (ctx, ct) =>
{
    var order = await ctx.Set<Order>().FindAsync([orderId], ct);
    if (order is null)
    {
        return;
    }

    order.Cancel();
    await ctx.SaveChangesAsync(ct);
}, cancellationToken: ct);
```

Use this when multiple operations must commit or roll back as one unit.

## Configuration

- Registration API: `AddHeadlessDbContext<TDbContext>(...)`
- EF options can be provided with `Action<DbContextOptionsBuilder>` or `Action<IServiceProvider, DbContextOptionsBuilder>`
- Framework extension is added through `AddHeadlessExtension()` internally during registration

## Dependencies

- `Headless.Domain`
- `Headless.Core`
- `Headless.Hosting`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers framework persistence services (runtime services, ordered model conventions, save-entry processors, clock/guid/current user-current tenant defaults)
- Replaces compiled query cache key generator to include framework behavior
- Forwards `DbContext` to registered `TDbContext` via scoped registration

## Validation Checklist

- `src/Headless.Orm.EntityFramework/Headless.Orm.EntityFramework.csproj` exists
- Public setup API found: `AddHeadlessDbContext<TDbContext>(...)`
- Base context found: `HeadlessDbContext`
- Transaction extensions found: `ExecuteTransactionAsync(...)`

---

# Headless.Orm.Couchbase

Couchbase integration for bucket-context based document access, transactions, and collection management.

## Problem Solved

Provides a typed context model over Couchbase buckets with helper APIs for querying, KV operations, and transaction execution.

## Key Features

- `CouchbaseBucketContext` base context
- `IBucketContextProvider` for resolving typed contexts per cluster + bucket
- `ICouchbaseClustersProvider` for cluster and transaction object lifecycle
- `DocumentSetExtensions` for KV and document operations
- `ICouchbaseManager` utilities for scope/collection/index lifecycle operations

## Installation

```bash
dotnet add package Headless.Orm.Couchbase
```

## Quick Start

```csharp
public sealed class AppBucketContext(IBucket bucket, Transactions transactions, ILogger<CouchbaseBucketContext> logger)
    : CouchbaseBucketContext(bucket, transactions, logger)
{
    public DocumentSet<Product> Products => GetDocumentSet<Product>("products");
}

var context = await bucketContextProvider.GetAsync<AppBucketContext>(
    clusterKey: "default",
    bucketName: "app",
    defaultScopeName: "_default"
);
```

## Transaction Pattern

```csharp
await context.ExecuteTransactionAsync(async attempt =>
{
    // return true to commit, false to rollback
    // perform transactional operations through attempt
    return true;
});
```

## Configuration

- Provide cluster options through `ICouchbaseClusterOptionsProvider`
- Provide transaction config through `ICouchbaseTransactionConfigProvider`
- Resolve bucket contexts through `IBucketContextProvider`
- Use `ICouchbaseManager` when bootstrapping scopes, collections, and indexes

## Dependencies

- `Headless.Domain`
- `Headless.Hosting`
- `Couchbase.Extensions.DependencyInjection`
- `Couchbase.Transactions`
- `Linq2Couchbase`

## Side Effects

- Caches cluster connections by `clusterKey` in provider lifecycle
- Initializes bucket contexts via reflection-based context initializer
- Emits transaction completion/failure logs from `CouchbaseBucketContext`

## Validation Checklist

- `src/Headless.Orm.Couchbase/Headless.Orm.Couchbase.csproj` exists
- Base context found: `CouchbaseBucketContext`
- Context provider found: `IBucketContextProvider` / `BucketContextProvider`
- Cluster provider found: `ICouchbaseClustersProvider`
- Document helper APIs found: `DocumentSetExtensions`
- Manager APIs found: `ICouchbaseManager`
