# Headless.Permissions.Core

Core implementation of permission management with grant resolution, caching, background initialization, and ASP.NET Core authorization integration.

## Problem Solved

Provides the full permission management runtime: AWS IAM-style grant resolution (User > Role), grant caching with cross-process invalidation, background startup sync of static definitions, and `PermissionRequirement` / `PermissionsRequirement` for wiring into ASP.NET Core authorization policies.

## Key Features

- `PermissionManager` — full `IPermissionManager` implementation; walks the grant-provider chain, caches results, and coordinates writes with cache invalidation
- `IPermissionGrantProvider` / built-in providers: `UserPermissionGrantProvider` (`"User"`) and `RolePermissionGrantProvider` (`"Role"`)
- `IStaticPermissionDefinitionStore` — builds the permission catalog lazily and thread-safely from all registered `IPermissionDefinitionProvider` instances
- `IDynamicPermissionDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination; disabled by default
- `PermissionsInitializationBackgroundService` — seeds static definitions to the database at startup with exponential-back-off retry; pre-caches dynamic definitions when enabled; implements `IInitializer`
- `PermissionManagementOptions` — all tuning options for lock keys/timeouts, cache expiry, dynamic store toggle
- `PermissionsStorageOptions` — schema and table name configuration shared across all storage providers
- `HeadlessPermissionsSetupBuilder` — fluent builder returned inside `AddHeadlessPermissions`; exposes `ConfigureManagement`, `ConfigureStorage`, `RegisterExtension`
- `HeadlessPermissionsBuilder` — returned by `AddHeadlessPermissions`; exposes `Services` for post-registration additions
- `services.AddPermissionDefinitionProvider<T>()` — registers a custom `IPermissionDefinitionProvider` as singleton
- `services.AddPermissionGrantProvider<T>()` — registers an additional grant provider (last-registered = highest priority)
- `services.AddAlwaysAllowAuthorization()` — replaces `IPermissionManager` and `IAuthorizationService` with always-allow stubs for integration testing
- `IGrantPermissionsSeedHelper` / `GrantPermissionsSeedHelper` — seed-time helper for granting all allowed permissions to a role idempotently
- `PermissionRequirement` / `PermissionRequirementHandler` — ASP.NET Core `IAuthorizationRequirement` for a single permission
- `PermissionsRequirement` / `PermissionsRequirementHandler` — multi-permission requirement with AND (`RequiresAll = true`) or OR semantics
- `AlwaysAllowPermissionManager` / `AlwaysAllowAuthorizationService` — test doubles

## Design Notes

- Grant providers are stored in registration order with last-registered = highest priority. Built-in registration is `Role` first, then `User`, making User the highest-priority built-in provider. Custom providers added via `AddPermissionGrantProvider<T>()` are appended after `User` and override both built-ins.
- `AddHeadlessPermissions` is guarded on `IPermissionGrantStore` so calling it more than once is safe — the management core registers once. Registering a second storage provider extension throws at host startup.
- The grant cache is tenant-scoped (`ScopedCache<PermissionGrantCacheItem>` keyed on `ICurrentTenant.Id`). A permission check for tenant A never returns a cached result for tenant B.

## Installation

```bash
dotnet add package Headless.Permissions.Core
```

## Quick Start

Register required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessPermissions`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// 2. Register management core + storage
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

### ASP.NET Core Authorization Integration

There is no `[HasPermission]` attribute. Wire permissions into ASP.NET Core policies using `PermissionRequirement` or `PermissionsRequirement`:

```csharp
// Single-permission policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanEditOrders", policy => policy.Requirements.Add(new PermissionRequirement("Orders.Edit")));
});

// Multi-permission policy (AND)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "CanManageOrders",
        policy =>
            policy.Requirements.Add(new PermissionsRequirement(["Orders.Create", "Orders.Edit"], requiresAll: true))
    );
});
```

Or check inline with `IPermissionManager`:

```csharp
var isGranted = await permissionManager.IsGrantedAsync(currentUser, "Orders.Edit");
```

### Seeding Permissions at Startup

```csharp
// In a data seeder or IHostedService:
await seedHelper.GrantAllPermissionsToRoleAsync("admin", tenantId: null, ct);
```

## Configuration

Tune management options through `setup.ConfigureManagement(...)` inside the `AddHeadlessPermissions` block:

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options =>
    {
        options.CrossApplicationsCommonLockKey = "permissions:common_update_lock";
        options.SaveStaticPermissionsToDatabase = true;
        options.IsDynamicPermissionStoreEnabled = false;
        options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

An `(options, IServiceProvider)` overload is available for late-bound configuration. `services.Configure<PermissionManagementOptions>(...)` also works and composes regardless of call order.

Configure schema and table names via `setup.ConfigureStorage(...)`:

```csharp
setup.ConfigureStorage(o =>
{
    o.Schema = "permissions";
    o.PermissionGrantsTableName = "PermissionGrants";
    o.PermissionDefinitionsTableName = "PermissionDefinitions";
    o.PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions";
    o.InitializeOnStartup = true;
});
```

## Dependencies

- `Headless.Permissions.Abstractions`
- `Headless.Domain`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IPermissionManager` (`PermissionManager`) as singleton
- Registers `IPermissionGrantStore` (`PermissionGrantStore`) as singleton
- Registers `IPermissionGrantProviderManager` as singleton
- Registers `IStaticPermissionDefinitionStore`, `IDynamicPermissionDefinitionStore`, `IPermissionDefinitionManager` as singletons
- Registers `RolePermissionGrantProvider`, `UserPermissionGrantProvider` as singletons
- Starts `PermissionsInitializationBackgroundService` as a hosted service (`IInitializer`)
- Registers `IGrantPermissionsSeedHelper` as transient
- Registers `PermissionRequirementHandler` and `PermissionsRequirementHandler` as `IAuthorizationHandler` singletons
- Registers `PermissionGrantCacheItemInvalidator` as `IDomainEventHandler<EntityChangedEventData<PermissionGrantRecord>>`
- Registers a tenant-scoped `ICache<PermissionGrantCacheItem>` as singleton
