# Headless.MultiTenancy.Storage.EntityFramework

Entity Framework Core storage implementation for the Headless tenant catalog (`ITenantStore` / `ITenantDirectory`).

## Problem Solved

Provides an EF Core-backed `ITenantStore` using the consumer's own `DbContext`, with schema managed through EF migrations — R17's shipped, convenience-default schema. Apps with richer requirements can implement `ITenantStore` directly over their own aggregate instead.

## Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessTenancyCatalogSetupBuilder`
- `modelBuilder.AddHeadlessTenancyCatalog(DbContext context)` — applies the `TenantRecord` entity configuration, reading the active EF Core provider so the unique identifier index can be pinned to a deterministic collation
- `TenantRecord` — the single-table entity: `Id`, `Identifier`, `NormalizedIdentifier`, `Name`, `IsEnabled`, `ExtraProperties`
- Unique index on `NormalizedIdentifier`, pinned to a case- and accent-sensitive collation (`Latin1_General_100_BIN2` on SQL Server, `C` on PostgreSQL) so a lookup never matches a row differing only by case — SQL Server's default collation is case-insensitive and would otherwise break the catalog service's ordinal lookup contract

## Design Notes

`TenantRecord` derives `NormalizedIdentifier` from `Identifier` itself through `SetIdentifier(...)` — there is no public setter for `NormalizedIdentifier`, so app-seeded rows and identifier rebrands can never carry a stale or hand-written normalized value. The entity deliberately does not implement `IMultiTenant`: the catalog sits outside the EF tenant query filter by construction.

This package ships no framework write path — read-only `FindByIdentifierAsync`/`FindByIdAsync`/`GetAllAsync` only, matching `ITenantStore`/`ITenantDirectory`. Apps insert, update, and migrate `TenantRecord` directly against their own `DbContext`.

Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`, matching `Headless.Settings.Storage.EntityFramework`.

## Installation

```bash
dotnet add package Headless.MultiTenancy.Storage.EntityFramework
```

## Quick Start

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessTenancyCatalog(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy.Catalog(catalog => catalog.UseEntityFramework<AppDbContext>());
});
```

## Configuration

None. This package binds no options of its own — `UseEntityFramework<TContext>()` takes only the `DbContext` type argument. Cache and identifier-shape behavior is controlled by `TenantCatalogOptions` on `Headless.MultiTenancy`'s `Catalog(...)` builder, not by this package.

## Dependencies

- `Headless.MultiTenancy`
- `Headless.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `EfTenantStore<TContext>` as a singleton, exposed as both `ITenantStore` and `ITenantDirectory`
