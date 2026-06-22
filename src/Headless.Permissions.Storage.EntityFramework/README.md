# Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage implementation for permission management.

## Problem Solved

Provides EF Core repository implementations for permission grants, permission definitions, and permission group definitions using the consumer's own `DbContext`, with schema managed through EF migrations.

## Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessPermissionsSetupBuilder`
- `modelBuilder.AddHeadlessPermissions(DbContext context)` — applies entity configurations by resolving `PermissionsStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessPermissions(PermissionsStorageOptions options)` — overload for when you already hold the options
- `EfPermissionGrantRepository<TContext>` — EF repository for `IPermissionGrantRepository`
- `EfPermissionDefinitionRecordRepository<TContext>` — EF repository for `IPermissionDefinitionRecordRepository`
- Startup gate that inspects the EF model before hosted services start and throws `InvalidOperationException` with an actionable message if any permissions entity is missing

## Design Notes

The package does not ship a dedicated permissions `DbContext` or a permissions-specific `DbContext` interface. Consumers register `AddDbContextFactory<TContext>()`, map entities with `modelBuilder.AddHeadlessPermissions(this)` in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties. Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`; writes commit through a fresh context owned by the repository.

## Installation

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

## Quick Start

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Resolves PermissionsStorageOptions from the context's service provider —
        // no need to inject IOptions<PermissionsStorageOptions> into the constructor.
        modelBuilder.AddHeadlessPermissions(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// AddHeadlessPermissions registers the management core automatically.
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
- `InitializeOnStartup = true`

Identifier names are validated using cross-provider rules (SQL Server superset) so the same options class works regardless of the underlying DB engine; the DB enforces type- and length-specific constraints at migration time. The startup gate inspects the EF model before hosted services start and fails with an actionable message if any permissions entity is missing.

`InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL.

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IPermissionGrantRepository` (`EfPermissionGrantRepository<TContext>`) as singleton
- Registers `IPermissionDefinitionRecordRepository` (`EfPermissionDefinitionRecordRepository<TContext>`) as singleton
- Registers validated `PermissionsStorageOptions`
- Registers `PermissionsEntityValidationStartupGate<TContext>` as `IHostedService`
