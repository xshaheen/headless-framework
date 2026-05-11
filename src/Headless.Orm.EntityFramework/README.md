# Headless.Orm.EntityFramework

Entity Framework Core integration with framework conventions, global filters, and DDD support.

## Problem Solved

Provides a feature-rich DbContext base class with automatic auditing, soft delete handling, domain event dispatching, multi-tenancy support, and framework type value converters.

## Key Features

- `HeadlessDbContext` - Base DbContext with framework integration
- Automatic `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` auditing
- Soft delete with `IsDeleted` global filter
- Multi-tenancy with `TenantId` filtering
- Domain event dispatching (local and distributed)
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

Soft delete and multi-tenancy filters are automatically applied.

```csharp
// Disable one named filter for a query
var allProducts = await dbContext.Products
    .IgnoreNotDeletedFilter()
    .ToListAsync();
```

### Extending Context Processing

`AddHeadlessDbContextServices()` registers ordered, composable save-time services. Add focused entry processors through `HeadlessDbContextOptions`; replace `IHeadlessSaveChangesPipeline` only when you need full orchestration control. Keep module-specific model mapping explicit with `ModelBuilder` extensions, such as `modelBuilder.AddSettingsConfiguration()`.

- `IHeadlessSaveEntryProcessor` for per-entry mutations before `SaveChanges`
- `IHeadlessMessageDispatcher` for local/distributed message publishing
- `IHeadlessSaveChangesPipeline` for transaction, audit, and message orchestration

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
    options.AddSaveEntryProcessor<AppSaveEntryProcessor>(250);
});
```

Message publishing defaults to a fail-fast dispatcher. If entities emit local or distributed messages, register a dispatcher to publish captured emitters through your application messaging infrastructure.

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

- Registers `HeadlessDbContextServices`, save-entry processors, a save pipeline, and a fail-fast message dispatcher
- Registers default implementations for `IClock`, `IGuidGenerator`, `ICurrentTenant`, `ICurrentUser`
- Replaces `ICompiledQueryCacheKeyGenerator` for multi-tenancy support
