---
domain: Permissions
packages: Permissions.Abstractions, Permissions.Core, Permissions.Storage.EntityFramework
---

# Permissions

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Permissions.Abstractions](#headlesspermissionsabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
    - [Defining Permissions](#defining-permissions)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Permissions.Core](#headlesspermissionscore)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
    - [Authorization Requirement](#authorization-requirement)
  - [Permission Resolution](#permission-resolution)
    - [Resolution Examples](#resolution-examples)
    - [Permission States](#permission-states)
  - [Configuration](#configuration-1)
    - [Options](#options)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Permissions.Storage.EntityFramework](#headlesspermissionsstorageentityframework)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)

> Dynamic permission management with hierarchical grant resolution, caching, and ASP.NET Core authorization integration.

## Quick Orientation
- Install `Headless.Permissions.Abstractions` to depend on interfaces only (e.g., in domain/application layers).
- Install `Headless.Permissions.Core` for the full runtime: grant resolution, caching, background init, and `[HasPermission]` authorization attribute.
- Install `Headless.Permissions.Storage.EntityFramework` for database-backed persistence of permission definitions and grants.
- Register with `AddPermissionsManagementCore(options => ...)`, then add a definition provider via `AddPermissionDefinitionProvider<T>()`, and wire storage via `AddHeadlessPermissions(setup => setup.UseEntityFramework<TContext>())`.
- Permissions follow AWS IAM-style resolution: explicit Deny overrides all Grants; default is Deny.

## Agent Instructions
- Use `IPermissionManager` from Abstractions to check permissions. Call `GetAsync("Permission.Name", currentUser)` and inspect `result.IsGranted`.
- Do NOT roll custom permission checks — always use the framework's `IPermissionManager` or the `[HasPermission("...")]` attribute.
- Define permissions by implementing `IPermissionDefinitionProvider` and calling `context.AddGroup("GroupName").AddChild("Group.Action")`.
- Permission resolution order: User > Role > Store. An explicit `Prohibited` from ANY provider denies access regardless of other grants.
- Three states: **Granted** (record with `IsGranted = true`), **Prohibited** (record with `IsGranted = false`), **Undefined** (no record, defaults to deny).
- Core requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered. Ensure these are wired before adding permissions.
- Storage.EntityFramework uses the consumer's `DbContext`. Register `AddDbContextFactory<TContext>()` and call `modelBuilder.AddHeadlessPermissions(options)` in `OnModelCreating`.
- `PermissionsInitializationBackgroundService` runs on startup — permission definitions are synced to the database automatically.
- For testing, use `AlwaysAllowPermissionManager` from Core to bypass all permission checks.

---
# Headless.Permissions.Abstractions

Defines the unified interface for permission management across different providers.

## Problem Solved

Provides a provider-agnostic permission management API, enabling dynamic permission checking with support for multiple grant providers (User, Role, Store) without changing application code.

## Key Features

- `IPermissionManager` - Core interface for permission operations
- `IPermissionDefinitionProvider` - Define permissions in code
- `IPermissionDefinitionManager` - Manage permission definitions
- Grant provider names (User, Role)
- `PermissionDefinition` and `PermissionGroupDefinition` models
- `MultiplePermissionGrantResult` for batch checks

## Installation

```bash
dotnet add package Headless.Permissions.Abstractions
```

## Usage

```csharp
public sealed class OrderService(IPermissionManager permissions, ICurrentUser currentUser)
{
    public async Task DeleteOrderAsync(Guid orderId, CancellationToken ct)
    {
        var result = await permissions.GetAsync("Orders.Delete", currentUser, cancellationToken: ct);

        if (!result.IsGranted)
            throw new ForbiddenException();

        // Delete order...
    }
}
```

### Defining Permissions

```csharp
public class OrderPermissionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("Orders");

        group.AddChild("Orders.View");
        group.AddChild("Orders.Create");
        group.AddChild("Orders.Edit");
        group.AddChild("Orders.Delete");
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

- `Headless.Core`

## Side Effects

None.
---
# Headless.Permissions.Core

Core implementation of permission management with caching, providers, and authorization.

## Problem Solved

Provides the full permission management implementation including hierarchical grant resolution (User > Role > Store), caching, background initialization, and ASP.NET Core authorization integration.

## Key Features

- `PermissionManager` - Full implementation of `IPermissionManager`
- Grant providers: User, Role, Store
- Static and dynamic permission definition stores
- Permission grant caching with invalidation
- Background service for permission initialization
- ASP.NET Core authorization requirements
- `AlwaysAllowPermissionManager` for testing

## Installation

```bash
dotnet add package Headless.Permissions.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Requires: TimeProvider, ICache, IDistributedLock, IGuidGenerator
builder.Services.AddPermissionsManagementCore(options =>
{
    options.CacheKeyPrefix = "permissions:";
});

// Register permission definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// Add storage (e.g., Entity Framework)
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

### Authorization Requirement

```csharp
[Authorize]
[HasPermission("Orders.Edit")]
public async Task<IActionResult> EditOrder(Guid id) { }
```

## Permission Resolution

Permission resolution follows AWS IAM-style rules:

1. **Explicit Deny Overrides All Grants** - If ANY provider returns `Prohibited`, permission is denied regardless of other grants
2. **Grant if No Denials** - Permission granted if at least one provider grants and no provider denies
3. **Default Deny** - If no provider grants permission, access is denied

### Resolution Examples

```csharp
// Scenario 1: Explicit denial overrides grant
await permissionManager.GrantToUserAsync("Orders.Delete", userId); // User granted
await permissionManager.RevokeFromRoleAsync("Orders.Delete", role); // Role denied

var result = await permissionManager.GetAsync("Orders.Delete", currentUser);
// result.IsGranted = false (explicit deny overrides)
```

```csharp
// Scenario 2: Multiple grants
await permissionManager.GrantToRoleAsync("Orders.View", role);
await permissionManager.GrantToUserAsync("Orders.View", userId);

var result = await permissionManager.GetAsync("Orders.View", currentUser);
// result.IsGranted = true
// result.Providers contains both Role and User providers
```

### Permission States

- **Granted** - Record exists with `IsGranted = true`
- **Prohibited** - Record exists with `IsGranted = false` (explicit denial)
- **Undefined** - No record exists (default deny)

## Configuration

### Options

```csharp
services.AddPermissionsManagementCore(options =>
{
    options.CacheKeyPrefix = "permissions:";
});
```

## Dependencies

- `Headless.Permissions.Abstractions`
- `Headless.Domain`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IPermissionManager` as transient
- Registers permission stores as singletons
- Starts `PermissionsInitializationBackgroundService` hosted service
- Registers cache invalidation handler
---
# Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage implementation for permission management.

## Problem Solved

Provides EF Core repository implementations for permission grants, permission definitions, and permission group definitions using the consumer's own `DbContext`.

## Key Features

- `AddHeadlessPermissions(setup => setup.UseEntityFramework<TContext>())` storage registration
- `modelBuilder.AddHeadlessPermissions(options)` entity mapping for shared contexts
- `EfPermissionGrantRepository` for permission grants
- `EfPermissionDefinitionRecordRepository` for permission definitions
- `PermissionsStorageOptions` for schema and table-name configuration

## Installation

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

## Quick Start

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

builder.Services.AddPermissionsManagementCore();
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
