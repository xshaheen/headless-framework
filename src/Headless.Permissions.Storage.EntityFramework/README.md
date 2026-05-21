# Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage implementation for permission management.

## Problem Solved

Provides persistent storage for permission definitions and grants using Entity Framework Core, enabling database-backed permission management with full CRUD support.

## Key Features

- `IPermissionsDbContext` - DbContext interface for permissions
- `PermissionsDbContext` - Ready-to-use DbContext
- `PermissionsStorageOptions` - Schema and table-name configuration
- EF repositories for definitions and grants
- Model builder extensions for custom DbContext integration
- Pooled DbContext factory support

## Installation

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

## Quick Start

### Using Built-in DbContext

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPermissionsManagementDbContextStorage(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Permissions"))
);
```

### Custom Schema / Table Names

```csharp
builder.Services.AddPermissionsManagementDbContextStorage(
    options => options.UseNpgsql(builder.Configuration.GetConnectionString("Permissions")),
    storage =>
    {
        storage.Schema = "app_permissions";
        storage.PermissionGrantsTableName = "PermissionGrants";
        storage.PermissionDefinitionsTableName = "PermissionDefinitions";
        storage.PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions";
    }
);
```

### Using Custom DbContext

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IPermissionsDbContext
{
    public DbSet<PermissionDefinitionRecord> PermissionDefinitions => Set<PermissionDefinitionRecord>();
    public DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions => Set<PermissionGroupDefinitionRecord>();
    public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddPermissionsConfiguration(this);
    }
}

builder.Services.AddPermissionsManagementDbContextStorage<AppDbContext>(storage =>
{
    storage.Schema = "app_permissions";
});
```

## Configuration

`PermissionsStorageOptions` defaults preserve the original physical layout:

- `Schema = "permissions"`
- `PermissionGrantsTableName = "PermissionGrants"`
- `PermissionDefinitionsTableName = "PermissionDefinitions"`
- `PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions"`

The storage registration validates these values on startup; schema and table names must be non-empty (whitespace-only values are rejected).

### Custom DbContext + custom schema: per-DbContext EF model cache

The dedicated `PermissionsDbContext` registration (`AddPermissionsManagementDbContextStorage(o => ...)`) wires a custom `IModelCacheKeyFactory` that mixes the storage options into the EF compiled-model cache key. The shared-context overload `AddPermissionsManagementDbContextStorage<TContext>` does not — your `DbContextOptionsBuilder` belongs to the host app. Apply the same replacement yourself so the EF model cache picks up your storage options:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.ReplaceService<IModelCacheKeyFactory, PermissionsStorageModelCacheKeyFactory>();
});
builder.Services.AddPermissionsManagementDbContextStorage<AppDbContext>(storage =>
{
    storage.Schema = "app_permissions";
});
```

`PermissionsStorageModelCacheKeyFactory` is exported as `public sealed` for this purpose. A single process that registers a single storage configuration on its shared `DbContext` can omit the replacement; add it whenever the same `TContext` type is configured with different storage options at different points in the process lifetime (for example, integration test fixtures that re-bind the options between classes).

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IPermissionDefinitionRecordRepository` as singleton
- Registers `IPermissionGrantRepository` as singleton
- Registers validated `PermissionsStorageOptions`
