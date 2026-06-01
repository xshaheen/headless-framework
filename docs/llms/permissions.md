---
domain: Permissions
packages: Permissions.Abstractions, Permissions.Core, Permissions.Storage.EntityFramework, Permissions.Storage.PostgreSql, Permissions.Storage.SqlServer
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
- [Headless.Permissions.Storage.PostgreSql](#headlesspermissionsstoragepostgresql)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-2)
  - [Configuration](#configuration-3)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)
- [Headless.Permissions.Storage.SqlServer](#headlesspermissionsstoragesqlserver)
  - [Problem Solved](#problem-solved-4)
  - [Key Features](#key-features-4)
  - [Installation](#installation-4)
  - [Quick Start](#quick-start-3)
  - [Configuration](#configuration-4)
  - [Dependencies](#dependencies-4)
  - [Side Effects](#side-effects-4)

> Dynamic permission management with hierarchical grant resolution, caching, and ASP.NET Core authorization integration.

## Quick Orientation
- Install `Headless.Permissions.Abstractions` to depend on interfaces only (e.g., in domain/application layers).
- Install `Headless.Permissions.Core` for the full runtime: grant resolution, caching, background init, and `[HasPermission]` authorization attribute.
- Install `Headless.Permissions.Storage.EntityFramework` for EF Core-backed persistence using the consumer's `DbContext`.
- Install `Headless.Permissions.Storage.PostgreSql` or `Headless.Permissions.Storage.SqlServer` for raw DDL storage that initializes permissions tables at host startup without requiring an app EF context.
- Add a definition provider via `AddPermissionDefinitionProvider<T>()`, then wire storage via `AddHeadlessPermissions(setup => setup.UseEntityFramework<TContext>())` — that single call registers the management core automatically.
- Permissions follow AWS IAM-style resolution: explicit Deny overrides all Grants; default is Deny.

## Agent Instructions
- Use `IPermissionManager` from Abstractions to check permissions. Call `GetAsync("Permission.Name", currentUser)` and inspect `result.IsGranted`.
- Do NOT roll custom permission checks — always use the framework's `IPermissionManager` or the `[HasPermission("...")]` attribute.
- Define permissions by implementing `IPermissionDefinitionProvider` and calling `context.AddGroup("GroupName").AddChild("Group.Action")`.
- Permission resolution order: User > Role > Store. An explicit `Prohibited` from ANY provider denies access regardless of other grants.
- Three states: **Granted** (record with `IsGranted = true`), **Prohibited** (record with `IsGranted = false`), **Undefined** (no record, defaults to deny).
- Core requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered. Ensure these are wired before adding permissions.
- `AddHeadlessPermissions(...)` is the single entry point — it registers the management core automatically alongside the storage provider. To tune management options, call `setup.ConfigureManagement(options => ...)` inside the setup block (an `(options, IServiceProvider)` overload also exists); `services.Configure<PermissionManagementOptions>(...)` works too and composes regardless of order.
- Storage.EntityFramework uses the consumer's `DbContext`. Register `AddDbContextFactory<TContext>()` and call `modelBuilder.AddHeadlessPermissions(options)` in `OnModelCreating`.
- Storage.PostgreSql and Storage.SqlServer provide Mode 2 raw providers. Use `AddHeadlessPermissions(setup => setup.UsePostgreSql(connectionString))` or `UseSqlServer(connectionString)`.
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

// Register permission definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// Add management core + storage in one call (e.g., Entity Framework).
// AddHeadlessPermissions registers the management core automatically.
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

Tune the management options through `setup.ConfigureManagement(...)` inside the `AddHeadlessPermissions` block, next to `ConfigureStorage`:

```csharp
services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options =>
    {
        options.CrossApplicationsCommonLockKey = "permissions:common_update_lock";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

A `(options, IServiceProvider)` overload is available when configuration needs resolved services. `services.Configure<PermissionManagementOptions>(...)` also works and composes with the auto-registration regardless of order.

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

The registration validates these values on startup. The startup gate also inspects the EF model before hosted services start and fails with an actionable message if any permissions entity is missing.

Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA), so the raw-DDL startup initializer is skipped (no-op). The initializer still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. This only affects raw-DDL self-initializing providers (PostgreSQL / SqlServer); EF-mode storage uses migrations and ignores the flag.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UsePostgreSql(...);
});
```

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Registers `IPermissionDefinitionRecordRepository` as singleton
- Registers `IPermissionGrantRepository` as singleton
- Registers validated `PermissionsStorageOptions`
- Registers an `IHostedLifecycleService` startup gate for missing entity mappings
---
# Headless.Permissions.Storage.PostgreSql

PostgreSQL raw-DDL storage for permission management.

## Problem Solved

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework for permissions persistence.

## Key Features

- `AddHeadlessPermissions(setup => setup.UsePostgreSql(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for permission grants, permission definitions, and permission group definitions
- Shares `PermissionsStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessPermissions` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UsePostgreSql(connectionString);
});
```

## Configuration

Configure schema and table names through `PermissionsStorageOptions` on the shared permissions builder. Configure the connection string through `PostgreSqlPermissionsOptions`.

## Dependencies

- `Headless.Permissions.Storage.EntityFramework`
- `Headless.Serializer.Json`
- `Npgsql`

## Side Effects

- Registers `PostgreSqlPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw PostgreSQL implementations of `IPermissionGrantRepository` and `IPermissionDefinitionRecordRepository`
---
# Headless.Permissions.Storage.SqlServer

SQL Server raw-DDL storage for permission management.

## Problem Solved

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework for permissions persistence.

## Key Features

- `AddHeadlessPermissions(setup => setup.UseSqlServer(connectionString))`
- Idempotent schema, table, and index creation at host startup
- Raw ADO.NET repositories for permission grants, permission definitions, and permission group definitions
- Shares `PermissionsStorageOptions` with the EF provider

## Installation

```bash
dotnet add package Headless.Permissions.Storage.SqlServer
```

## Quick Start

Register the required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessPermissions` then registers the management core automatically.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UseSqlServer(connectionString);
});
```

## Configuration

Configure schema and table names through `PermissionsStorageOptions` on the shared permissions builder. Configure the connection string through `SqlServerPermissionsOptions`.

## Dependencies

- `Headless.Permissions.Storage.EntityFramework`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `SqlServerPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers raw SQL Server implementations of `IPermissionGrantRepository` and `IPermissionDefinitionRecordRepository`
