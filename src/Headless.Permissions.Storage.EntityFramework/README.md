# Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage for permissions management.

## Problem Solved

Provides EF Core repository implementations for permission grants, permission definitions, and permission group definitions using the consumer's own `DbContext`.

## Key Features

- `AddHeadlessPermissions(setup => setup.UseEntityFramework<TContext>())` storage registration
- `modelBuilder.AddHeadlessPermissions(options)` entity mapping for shared contexts
- `EfPermissionGrantRepository` for permission grants
- `EfPermissionDefinitionRecordRepository` for permission definitions
- `PermissionsStorageOptions` for schema and table-name configuration

## Design Notes

The package no longer ships a dedicated permissions DbContext or permissions-specific DbContext interface. Consumers register `AddDbContextFactory<TContext>()`, map the Headless entities in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties.

Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`. Writes commit through a fresh context owned by the repository.

## Installation

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

## Quick Start

`AddHeadlessPermissions(...)` registers the permissions management core automatically.
Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and
`IGuidGenerator`.

```csharp
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IOptions<PermissionsStorageOptions> permissionsStorage
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessPermissions(permissionsStorage.Value);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_permissions");
    setup.UseEntityFramework<AppDbContext>();
});
```

## Configuration

`PermissionsStorageOptions` defaults:

- `Schema = "permissions"`
- `PermissionGrantsTableName = "PermissionGrants"`
- `PermissionDefinitionsTableName = "PermissionDefinitions"`
- `PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions"`

The registration validates these values on startup. The startup gate also inspects the EF model before hosted services start and fails with an actionable message if any permissions entity is missing.

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IPermissionDefinitionRecordRepository` as singleton
- Registers `IPermissionGrantRepository` as singleton
- Registers validated `PermissionsStorageOptions`
- Registers an `IHostedLifecycleService` startup gate for missing entity mappings
