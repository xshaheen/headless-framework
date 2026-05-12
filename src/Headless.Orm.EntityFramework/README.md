# Headless.Orm.EntityFramework

Entity Framework Core integration with framework conventions, global filters, and DDD support.

## Problem Solved

Provides a feature-rich DbContext base class with automatic auditing, soft delete handling, domain event dispatching, multi-tenancy support, and framework type value converters.

## Key Features

- `HeadlessDbContext` - Base DbContext with framework integration
- Automatic auditing for `ICreateAudit` / `IUpdateAudit` / `IDeleteAudit` / `ISuspendAudit` entities (`DateCreated`, `DateUpdated`, `DateDeleted`, `DateSuspended`, plus `CreatedById` / `UpdatedById` / `DeletedById` / `SuspendedById` for `UserId` and `AccountId` audits)
- Soft delete (`IDeleteAudit.IsDeleted`) and suspend (`ISuspendAudit.IsSuspended`) global filters
- Multi-tenancy filter for `IMultiTenant` entities driven by `ICurrentTenant.Id`
- Composable save pipeline with built-in entry processors (`HeadlessEntitySaveEntryProcessor`, `HeadlessAuditSaveEntryProcessor`, `HeadlessLocalEventSaveEntryProcessor`, `HeadlessMessageCollectorSaveEntryProcessor`)
- Domain event collection and dispatch (local + distributed) via `IHeadlessMessageDispatcher`
- Transaction-aware save with audit-log second-pass commit
- Value converters: Money, Month, AccountId, UserId, DateTime normalization
- DataGrid extensions for pagination and ordering
- EF migration pre-seeder

## Installation

```bash
dotnet add package Headless.Orm.EntityFramework
```

## Quick Start

```csharp
public class AppDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<AppDbContext> options
) : HeadlessDbContext(services, options)
{
    public DbSet<Product> Products => Set<Product>();

    public override string DefaultSchema => "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

// Registration
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
);
```

## Configuration

### Value Converters

```csharp
modelBuilder.Entity<Order>()
    .Property(o => o.Total)
    .HasConversion<MoneyValueConverter>();
```

### Global Filters

Three named filters are applied automatically when entities implement the corresponding interfaces:

| Interface | Filter name (`HeadlessQueryFilters.*`) | Bypass extension |
| --- | --- | --- |
| `IMultiTenant` | `MultiTenancyFilter` | `IgnoreMultiTenancyFilter()` |
| `IDeleteAudit` | `NotDeletedFilter` | `IgnoreNotDeletedFilter()` |
| `ISuspendAudit` | `NotSuspendedFilter` | `IgnoreNotSuspendedFilter()` |

Bypasses are scoped to a single `IQueryable<T>` chain and emit a `[SECURITY AUDIT]` trace through `Debug.WriteLine` with the caller member + file for auditability.

```csharp
// Disable one named filter for a query
var allProducts = await dbContext.Products
    .IgnoreNotDeletedFilter()
    .ToListAsync();

// Combine multiple bypasses
var everything = await dbContext.Products
    .IgnoreMultiTenancyFilter()
    .IgnoreNotDeletedFilter()
    .IgnoreNotSuspendedFilter()
    .ToListAsync();
```

### Extending Context Processing

`AddHeadlessDbContextServices()` registers ordered, composable save-time services. Add focused entry processors through `HeadlessDbContextOptions`; replace `IHeadlessSaveChangesPipeline` only when you need full orchestration control. Keep module-specific model mapping explicit with `ModelBuilder` extensions, such as `modelBuilder.AddSettingsConfiguration()`.

- `IHeadlessSaveEntryProcessor` for per-entry mutations before `SaveChanges`
- `IHeadlessMessageDispatcher` for local/distributed message publishing
- `IHeadlessSaveChangesPipeline` for transaction, audit, and message orchestration

The default processor chain runs in registration order against every tracked entity, then again for any processors you add:

1. `HeadlessEntitySaveEntryProcessor` — stamps `Guid` IDs, tenant IDs, concurrency stamps
2. `HeadlessAuditSaveEntryProcessor` — stamps create/update/delete/suspend audit fields
3. `HeadlessLocalEventSaveEntryProcessor` — publishes `EntityCreated/Updated/Deleted/Changed` lifecycle messages on `ILocalMessageEmitter` entities
4. `HeadlessMessageCollectorSaveEntryProcessor` — collects pending local + distributed messages onto the save context

```csharp
public sealed class AppSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        // Apply app-specific save behavior here.
    }
}

services.AddHeadlessDbContextServices(options =>
{
    // Lifetime controls DI registration and per-save resolution; processor runs after the
    // built-in chain because registrations append to the tail.
    options.AddSaveEntryProcessor<AppSaveEntryProcessor>(ServiceLifetime.Scoped);
});
```

Re-registering the same processor type removes the prior entry and re-appends it, so the latest call always wins on position. Use `options.RemoveSaveEntryProcessor<TProcessor>()` to opt out of one of the built-in processors entirely.

Message publishing defaults to a fail-fast dispatcher (`ThrowHeadlessMessageDispatcher`). If entities emit local or distributed messages, register a dispatcher to publish captured emitters through your application messaging infrastructure.

```csharp
services.AddHeadlessDbContextServices();
services.AddHeadlessMessageDispatcher<AppHeadlessMessageDispatcher>();

// Or use a factory when the dispatcher wraps existing application services.
services.AddHeadlessMessageDispatcher(provider =>
    new AppHeadlessMessageDispatcher(provider.GetRequiredService<AppMessageBus>())
);
```

### Resilient Transactions

Instance methods on `HeadlessDbContext` that wrap an operation in a transaction coordinated with the execution strategy (safe for retrying providers like SQL Server `EnableRetryOnFailure`). The caller has full control — call `SaveChangesAsync` explicitly within the operation.

```csharp
await dbContext.ExecuteTransactionAsync(async (ctx, ct) =>
{
    ctx.Products.Add(new Product { Name = "Widget" });
    await ctx.SaveChangesAsync(ct);
});

// With a return value
var result = await dbContext.ExecuteTransactionAsync<int>(async (ctx, ct) =>
{
    var product = new Product { Name = "Widget" };
    ctx.Products.Add(product);
    await ctx.SaveChangesAsync(ct);
    return product.Id;
});
```

## Dependencies

- `Headless.Domain`
- `Headless.Core`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `HeadlessDbContextServices`, the default save-entry processor chain, `IHeadlessSaveChangesPipeline`, and a fail-fast `IHeadlessMessageDispatcher` (`ThrowHeadlessMessageDispatcher`)
- Registers framework defaults via `TryAddSingleton`: `IClock` (`Clock`), `IGuidGenerator` (`SequentialAtEndGuidGenerator`), `ICurrentTenant` (`CurrentTenant`), `ICurrentTenantAccessor` (`AsyncLocalCurrentTenantAccessor`), `ICurrentUser` (`NullCurrentUser`), `ICorrelationIdProvider` (`ActivityCorrelationIdProvider`), and `TimeProvider.System`
- Replaces `ICompiledQueryCacheKeyGenerator` so tenant-scoped queries can share compiled plans safely
