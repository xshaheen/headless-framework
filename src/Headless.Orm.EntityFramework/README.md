# Headless.Orm.EntityFramework

Entity Framework Core integration with framework conventions, global filters, and DDD support.

## Problem Solved

Provides a feature-rich DbContext base class with automatic auditing, soft delete handling, domain event dispatching, multi-tenancy support, and framework type value converters.

## Key Features

- `HeadlessDbContext` - Base DbContext with framework integration
- Automatic `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` auditing
- Soft delete with `IsDeleted` global filter
- Multi-tenancy with `AccountId` filtering
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
public class AppDbContext(DbContextOptions<AppDbContext> options) : HeadlessDbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

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
// Disable filters for a query
var allProducts = await dbContext.Products
    .IgnoreQueryFilters()
    .ToListAsync();
```

## Dependencies

- `Headless.Domain`
- `Headless.BuildingBlocks`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IHeadlessEntityModelProcessor` as singleton
- Registers default implementations for `IClock`, `IGuidGenerator`, `ICurrentTenant`, `ICurrentUser`
- Replaces `ICompiledQueryCacheKeyGenerator` for multi-tenancy support
