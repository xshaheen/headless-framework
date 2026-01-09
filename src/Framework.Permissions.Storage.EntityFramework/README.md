# Framework.Permissions.Storage.EntityFramework

Entity Framework Core storage implementation for permission management.

## Problem Solved

Provides persistent storage for permission definitions and grants using Entity Framework Core, enabling database-backed permission management with full CRUD support.

## Key Features

- `IPermissionsDbContext` - DbContext interface for permissions
- `PermissionsDbContext` - Ready-to-use DbContext
- EF repositories for definitions and grants
- Model builder extensions for custom DbContext integration
- Pooled DbContext factory support

## Installation

```bash
dotnet add package Framework.Permissions.Storage.EntityFramework
```

## Quick Start

### Using Built-in DbContext

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPermissionsManagementDbContextStorage(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Permissions"))
);
```

### Using Custom DbContext

```csharp
public class AppDbContext : DbContext, IPermissionsDbContext
{
    public DbSet<PermissionDefinitionRecord> PermissionDefinitions => Set<PermissionDefinitionRecord>();
    public DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions => Set<PermissionGroupDefinitionRecord>();
    public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePermissionManagement();
    }
}

builder.Services.AddPermissionsManagementDbContextStorage<AppDbContext>();
```

## Configuration

No additional configuration required beyond DbContext setup.

## Dependencies

- `Framework.Permissions.Core`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IPermissionDefinitionRecordRepository` as singleton
- Registers `IPermissionGrantRepository` as singleton
