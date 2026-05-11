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
    IHeadlessEntityModelProcessor entityProcessor,
    DbContextOptions<AppDbContext> options
) : HeadlessDbContext(entityProcessor, options)
{
    public DbSet<Product> Products => Set<Product>();

    public override string DefaultSchema => "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override Task PublishMessagesAsync(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    protected override void PublishMessages(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
    }

    protected override Task PublishMessagesAsync(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    protected override void PublishMessages(List<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction)
    {
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

`HeadlessEntityModelProcessor` is the default implementation behind `IHeadlessEntityModelProcessor`. Replace it in DI for full control, or derive from it and override focused hooks:

- `ProcessEntityType(...)` for model-level conventions
- `ConfigureQueryFilters<TEntity>(...)` for named global filters
- `ProcessEntry(...)` for per-entry save changes behavior
- `CollectMessages(...)` for local/distributed message batching

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

- Registers `IHeadlessEntityModelProcessor` as singleton
- Registers default implementations for `IClock`, `IGuidGenerator`, `ICurrentTenant`, `ICurrentUser`
- Replaces `ICompiledQueryCacheKeyGenerator` for multi-tenancy support
