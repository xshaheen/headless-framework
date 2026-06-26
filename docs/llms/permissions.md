---
domain: Permissions
packages: Permissions.Abstractions, Permissions.Core, Permissions.Storage.EntityFramework, Permissions.Storage.PostgreSql, Permissions.Storage.SqlServer
---

# Permissions

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Permission Definitions and Groups](#permission-definitions-and-groups)
    - [Grant Providers and Resolution Order](#grant-providers-and-resolution-order)
    - [Grant States](#grant-states)
    - [Grant Store and Caching](#grant-store-and-caching)
    - [Static vs. Dynamic Definition Store](#static-vs-dynamic-definition-store)
    - [Startup Initialization](#startup-initialization)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Permissions.Abstractions](#headlesspermissionsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Permissions.Core](#headlesspermissionscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Permissions.Storage.EntityFramework](#headlesspermissionsstorageentityframework)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Permissions.Storage.PostgreSql](#headlesspermissionsstoragepostgresql)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Permissions.Storage.SqlServer](#headlesspermissionsstoragesqlserver)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Dynamic permission management with hierarchical grant resolution (User > Role), explicit-deny semantics, caching, and database persistence via EF Core, PostgreSQL, or SQL Server.

## Quick Orientation

Install `Headless.Permissions.Abstractions` to depend on interfaces only (domain/application layers). Install `Headless.Permissions.Core` plus exactly one storage provider for the full runtime.

Typical setup:

```csharp
// 1. Register permission definitions
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// 2. Register management core + storage in one call
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

`AddHeadlessPermissions` requires `ICache`, `IDistributedLock`, `IGuidGenerator`, and `TimeProvider` to be registered first.

Provider packages:
- `Headless.Permissions.Storage.EntityFramework` — EF Core, schema via migrations
- `Headless.Permissions.Storage.PostgreSql` — raw ADO.NET, schema created at startup
- `Headless.Permissions.Storage.SqlServer` — raw ADO.NET, schema created at startup

## Agent Instructions

- Inject `IPermissionManager` to read or write permission grants. Never roll custom permission checks.
- Check a permission with `GetAsync(name, currentUser)` and inspect `result.IsGranted`. Use `IsGrantedAsync(currentUser, name)` for a boolean shorthand.
- Define permissions by implementing `IPermissionDefinitionProvider` and calling `context.AddGroup("Name").AddChild("Name.Action")`. The method is `AddChild`, not `AddPermission` — the latter does not exist.
- Register definition providers with `services.AddPermissionDefinitionProvider<T>()`.
- Grant a permission with `GrantToUserAsync` / `GrantToRoleAsync`. Prohibit explicitly with `RevokeFromUserAsync` / `RevokeFromRoleAsync`. Delete all records for a principal with `DeleteAsync(providerName, providerKey)`.
- Resolution order (highest to lowest priority): **User** then **Role**. An explicit `Prohibited` from any provider denies access regardless of other grants. The default when no record exists is deny.
- Do NOT call `IDynamicPermissionDefinitionStore.SaveAsync` directly — `PermissionsInitializationBackgroundService` handles it on startup.
- Do NOT bypass `IPermissionManager` to write directly to the repository — cache invalidation will not fire.
- There is **no** `[HasPermission]` attribute in this framework. Use `PermissionRequirement` / `PermissionsRequirement` and wire them into ASP.NET Core authorization policies, or use `IPermissionManager` in-code.
- `SetAsync` throws `ConflictException` when the permission is not defined, is disabled, restricts its providers and excludes the given `providerName`, or when no grant provider with that name is registered. Catch this for user-facing validation.
- Batch writes via `SetAsync(IReadOnlyCollection<string>, ...)` are all-or-nothing — a single invalid name rejects the entire batch.
- `PermissionDefinition.Providers` restricts which grant providers can read/write that permission. An empty list allows all providers.
- For integration tests, call `services.AddAlwaysAllowAuthorization()` to replace both `IPermissionManager` and `IAuthorizationService` with always-allow stubs.
- Grant caching is tenant-scoped: the cache key includes the current tenant id. A permission check for tenant A does not serve a cached result for tenant B.

## Core Concepts

### Permission Definitions and Groups

A *permission definition* declares a permission's identity and metadata: its unique `Name`, optional `DisplayName`, whether it is `IsEnabled` (disabled permissions always resolve to not-granted), which `Providers` may read/write it (empty = all), and optional `Properties` for custom metadata.

Permissions form a tree via `AddChild`. A group (`PermissionGroupDefinition`) is the top-level container; calling `group.AddChild("Orders.View")` adds a root permission to the group. Calling `permission.AddChild("Orders.View.Detail")` nests a child under that permission. Both `PermissionGroupDefinition` and `PermissionDefinition` implement `ICanAddChildPermission` so the same `AddChild` fluent call works on both.

```csharp
public sealed class OrderPermissionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("Orders", displayName: "Order Management");

        var view = group.AddChild("Orders.View");
        view.AddChild("Orders.View.Detail"); // nested child

        group.AddChild("Orders.Create");
        group.AddChild("Orders.Edit");
        group.AddChild("Orders.Delete");
    }
}
```

### Grant Providers and Resolution Order

`PermissionManager` delegates resolution to a chain of `IPermissionGrantProvider` implementations. Built-in providers (registration order, lowest to highest priority):

1. `RolePermissionGrantProvider` (`"Role"`) — checks grants attached to each role the current user holds. If the user has no roles, all permissions are `Undefined`.
2. `UserPermissionGrantProvider` (`"User"`) — checks grants attached to the user's id. Returns `Undefined` for anonymous principals (null user id).

Last-registered provider has the highest priority, so **User wins over Role**. Custom providers added with `services.AddPermissionGrantProvider<T>()` are appended after the built-in providers and therefore have higher priority than both.

Use `PermissionGrantProviderNames.User` and `PermissionGrantProviderNames.Role` as the `providerName` argument rather than string literals.

### Grant States

Each provider returns one of three states per permission:

- **Granted** — a record exists with `IsGranted = true`.
- **Prohibited** — a record exists with `IsGranted = false`. An explicit prohibition from **any** provider overrides grants from all other providers.
- **Undefined** — no record exists; the provider has no opinion. When all providers are `Undefined`, the permission is not granted (default deny).

`GrantedPermissionResult.IsGranted` reflects the final merged decision. `GrantedPermissionResult.Providers` lists the providers that contributed a grant (an explicit denial suppresses this list rather than appearing in it).

### Grant Store and Caching

`PermissionGrantStore` caches resolved grant statuses to avoid repeated database reads per request. The cache is backed by a tenant-scoped `ICache<PermissionGrantCacheItem>` keyed on the current tenant id. When `IPermissionManager.SetAsync` writes a grant, the framework publishes an event causing `PermissionGrantCacheItemInvalidator` to evict affected cache entries across the process. Writing directly to `IPermissionGrantRepository` bypasses this path and leaves stale cache entries.

### Static vs. Dynamic Definition Store

The *static store* (`IStaticPermissionDefinitionStore`) builds the permission catalog once at startup by invoking all registered `IPermissionDefinitionProvider` instances. It is thread-safe and lazily initialized on first access.

The *dynamic store* (`IDynamicPermissionDefinitionStore`) reads definitions from the database, caches them in-process with a configurable expiry (`DynamicDefinitionsMemoryCacheExpiration`, default 30 seconds), and coordinates cross-instance refreshes via a distributed cache stamp and a distributed lock. The dynamic store is disabled by default (`IsDynamicPermissionStoreEnabled = false`); enable it only when permission definitions must be edited at runtime without redeployment.

`IPermissionDefinitionManager` merges both stores. Static definitions take precedence over dynamic definitions of the same name.

### Startup Initialization

`PermissionsInitializationBackgroundService` runs after the application starts. It:

1. Persists static permission definitions to the database (guarded by a distributed lock; retries up to 10 times with exponential back-off), when `SaveStaticPermissionsToDatabase = true`.
2. Pre-caches dynamic definitions from the database into the in-process cache, when `IsDynamicPermissionStoreEnabled = true`.

Dependents can await `WaitForInitializationAsync()` on the `IInitializer` interface to block until both tasks complete. When both options are `false`, initialization is a no-op and `IsInitialized` is set to `true` immediately.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Permissions.Storage.EntityFramework` | You already use EF Core and want schema managed via EF migrations | You need to avoid an EF dependency or want zero-overhead ADO.NET | Portable across any EF-supported DB; startup validates that all permissions entities are in the EF model before hosted services start |
| `Headless.Permissions.Storage.PostgreSql` | You use PostgreSQL and want no EF Core dependency | You run SQL Server or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against PostgreSQL naming rules |
| `Headless.Permissions.Storage.SqlServer` | You use SQL Server and want no EF Core dependency | You run PostgreSQL or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against SQL Server naming rules |

---

## Headless.Permissions.Abstractions

Defines the unified interface for permission management across different grant providers and storage backends.

### Problem Solved

Provides a provider-agnostic permission management API, enabling dynamic permission checking with support for multiple grant providers (User, Role, and custom) without tying application code to a specific storage or grant strategy.

### Key Features

- `IPermissionManager` — resolves and mutates permission grants with AWS IAM-style semantics; `GetAsync` (single), `GetAllAsync` (all or by names), `SetAsync` (grant or prohibit, single or batch), `DeleteAsync` (remove all records for a principal)
- `PermissionManagerExtensions` — convenience helpers: `IsGrantedAsync` (boolean overloads), `GrantToUserAsync`, `RevokeFromUserAsync`, `SetToUserAsync`, `GrantToRoleAsync`, `RevokeFromRoleAsync`, `SetToRoleAsync`
- `IPermissionDefinitionProvider` — contributes permission groups and definitions at startup via `IPermissionDefinitionContext`
- `IPermissionDefinitionManager` — looks up and enumerates all defined permissions (`FindAsync`, `GetPermissionsAsync`, `GetGroupsAsync`)
- `IPermissionDefinitionContext` — mutable builder passed to `IPermissionDefinitionProvider.Define`; `AddGroup`, `GetGroup`, `GetGroupOrNull`, `RemoveGroup`, `GetPermissionOrDefault`
- `PermissionGroupDefinition` — named container for permissions; `AddChild`, `GetFlatPermissions`, `GetPermissionOrDefault`
- `PermissionDefinition` — single permission; `AddChild` for nesting, `Providers` list for restricting which grant providers can manage it
- `ICanAddChildPermission` — shared interface on both group and definition, enabling uniform `AddChild` calls in tree-building code
- `GrantedPermissionResult` — result of `GetAsync`; `Name`, `IsGranted`, `Providers` (the contributing grant providers with their keys)
- `GrantPermissionProvider` — identifies a contributing provider by `Name` and the `Keys` (user id or role names) that granted the permission
- `MultiplePermissionGrantResult` — `Dictionary<string, bool>` with `AllGranted` and `AllProhibited` shorthand properties; returned by batch `IsGrantedAsync`
- `PermissionGrantProviderNames` — constants `User` and `Role` for the built-in providers

### Installation

```bash
dotnet add package Headless.Permissions.Abstractions
```

### Quick Start

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

// Boolean shorthand
var canDelete = await permissions.IsGrantedAsync(currentUser, "Orders.Delete", ct);

// Batch check
var grantMap = await permissions.IsGrantedAsync(
    currentUser,
    ["Orders.View", "Orders.Edit", "Orders.Delete"],
    ct
);
if (grantMap.AllGranted) { /* all allowed */ }
```

#### Defining Permissions

```csharp
public sealed class OrderPermissionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("Orders");

        // Use AddChild (not AddPermission — that method does not exist)
        group.AddChild("Orders.View");
        group.AddChild("Orders.Create");
        group.AddChild("Orders.Edit");
        group.AddChild("Orders.Delete");

        // Nested children
        var billing = group.AddChild("Orders.Billing");
        billing.AddChild("Orders.Billing.Refund");
    }
}
```

### Configuration

None. This is an abstractions-only package.

### Dependencies

- `Headless.Core`

### Side Effects

None.

---

## Headless.Permissions.Core

Core implementation of permission management with grant resolution, caching, background initialization, and ASP.NET Core authorization integration.

### Problem Solved

Provides the full permission management runtime: AWS IAM-style grant resolution (User > Role), grant caching with cross-process invalidation, background startup sync of static definitions, and `PermissionRequirement` / `PermissionsRequirement` for wiring into ASP.NET Core authorization policies.

### Key Features

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

### Design Notes

- Grant providers are stored in registration order with last-registered = highest priority. The built-in registration is `Role` first, then `User`, making User the highest-priority built-in provider. Custom providers added via `AddPermissionGrantProvider<T>()` are appended after `User` and override both built-ins.
- `AddHeadlessPermissions` is guarded on `IPermissionGrantStore` so calling it more than once is safe — the management core registers once. However, registering a second storage provider extension throws at host startup.
- The grant cache is tenant-scoped (`ScopedCache<PermissionGrantCacheItem>` keyed on `ICurrentTenant.Id`). A permission check for tenant A never returns a cached result for tenant B.
- `PermissionsInitializationBackgroundService` implements `IInitializer`: anything awaiting `WaitForInitializationAsync()` blocks until both the save and pre-cache steps complete. If the host stops before initialization finishes, the `TaskCompletionSource` is cancelled.

### Installation

```bash
dotnet add package Headless.Permissions.Core
```

### Quick Start

Register required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessPermissions`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// 2. Register management core + storage
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

#### ASP.NET Core Authorization Integration

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

Or check inline:

```csharp
var isGranted = await permissionManager.IsGrantedAsync(currentUser, "Orders.Edit");
```

#### Seeding Permissions at Startup

```csharp
// In a data seeder or IHostedService:
await seedHelper.GrantAllPermissionsToRoleAsync("admin", tenantId: null, ct);
```

`IGrantPermissionsSeedHelper.GrantAllPermissionsToRoleAsync` skips permissions that already have a grant record (idempotent) and only grants permissions that allow the `Role` provider.

### Configuration

#### PermissionManagementOptions

Configure via `setup.ConfigureManagement(...)` or `services.Configure<PermissionManagementOptions>(...)`:

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options =>
    {
        // Distributed-lock key coordinating cross-instance definition saves
        // (default: "permissions:common_update_lock")
        options.CrossApplicationsCommonLockKey = "permissions:common_update_lock";

        // How long the cross-app lock is held (default: 10 minutes)
        options.CrossApplicationsCommonLockExpiration = TimeSpan.FromMinutes(10);

        // Max wait to acquire the cross-app lock (default: 5 minutes)
        options.CrossApplicationsCommonLockAcquireTimeout = TimeSpan.FromMinutes(5);

        // How long the per-application save lock is held (default: 10 minutes)
        options.ApplicationSaveLockExpiration = TimeSpan.FromMinutes(10);

        // Max wait to acquire the per-app save lock (default: 5 minutes)
        options.ApplicationSaveLockAcquireTimeout = TimeSpan.FromMinutes(5);

        // How long the MD5 hash of saved permissions is cached (default: 30 days)
        options.PermissionsHashCacheExpiration = TimeSpan.FromDays(30);

        // How long the cross-app update stamp lives in the distributed cache (default: 30 days)
        options.CommonPermissionsUpdatedStampCacheExpiration = TimeSpan.FromDays(30);

        // Distributed-cache key for the cross-app update stamp
        // (default: "permissions:updated_local_stamp")
        options.CommonPermissionsUpdatedStampCacheKey = "permissions:updated_local_stamp";

        // Persist static definitions to the DB on startup (default: true)
        options.SaveStaticPermissionsToDatabase = true;

        // Enable the dynamic definition store (default: false)
        options.IsDynamicPermissionStoreEnabled = false;

        // How long dynamic definitions stay in the in-process cache before
        // the distributed stamp is re-checked (default: 30 seconds)
        options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.FromSeconds(30);
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

An `(options, IServiceProvider)` overload is available for late-bound configuration.

#### PermissionsStorageOptions

Configure schema and table names via `setup.ConfigureStorage(...)`:

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "permissions"; // default
        o.PermissionGrantsTableName = "PermissionGrants"; // default
        o.PermissionDefinitionsTableName = "PermissionDefinitions"; // default
        o.PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions"; // default
        o.InitializeOnStartup = true; // default
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

`InitializeOnStartup = false` makes the raw-DDL startup initializer a no-op (useful when schema is provisioned out-of-band). It still reports `IsInitialized = true` so dependents do not block. Ignored by the EF provider (EF uses migrations).

### Dependencies

- `Headless.Permissions.Abstractions`
- `Headless.Domain`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`

### Side Effects

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

---

## Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage implementation for permission management.

### Problem Solved

Provides EF Core repository implementations for permission grants, permission definitions, and permission group definitions using the consumer's own `DbContext`, with schema managed through EF migrations.

### Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessPermissionsSetupBuilder`
- `modelBuilder.AddHeadlessPermissions(DbContext context)` — applies entity configurations by resolving `PermissionsStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessPermissions(PermissionsStorageOptions options)` — overload for when you already hold the options
- `EfPermissionGrantRepository<TContext>` — EF repository for `IPermissionGrantRepository`
- `EfPermissionDefinitionRecordRepository<TContext>` — EF repository for `IPermissionDefinitionRecordRepository`
- Startup gate that inspects the EF model before hosted services start and throws `InvalidOperationException` with an actionable message if any permissions entity is missing

### Design Notes

The package does not ship a dedicated permissions `DbContext` or a permissions-specific `DbContext` interface. Consumers register `AddDbContextFactory<TContext>()`, map entities with `modelBuilder.AddHeadlessPermissions(this)` in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties. Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`; writes commit through a fresh context owned by the repository.

### Installation

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

### Quick Start

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

### Configuration

`PermissionsStorageOptions` defaults:

- `Schema = "permissions"`
- `PermissionGrantsTableName = "PermissionGrants"`
- `PermissionDefinitionsTableName = "PermissionDefinitions"`
- `PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions"`
- `InitializeOnStartup = true`

Identifier names are validated using cross-provider rules (SQL Server superset) so the same options class works regardless of the underlying DB engine; the DB enforces type- and length-specific constraints at migration time. The startup gate inspects the EF model before hosted services start and fails with an actionable message if any permissions entity is missing.

`InitializeOnStartup` is ignored by the EF provider — EF uses migrations, not startup DDL.

### Dependencies

- `Headless.Permissions.Core`
- `Headless.Orm.EntityFramework`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Registers `IPermissionGrantRepository` (`EfPermissionGrantRepository<TContext>`) as singleton
- Registers `IPermissionDefinitionRecordRepository` (`EfPermissionDefinitionRecordRepository<TContext>`) as singleton
- Registers validated `PermissionsStorageOptions`
- Registers `PermissionsEntityValidationStartupGate<TContext>` as `IHostedService`

---

## Headless.Permissions.Storage.PostgreSql

PostgreSQL raw-DDL storage for permission management.

### Problem Solved

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework. All schema is created idempotently at host startup via raw ADO.NET.

### Key Features

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — binds `PostgreSqlPermissionsOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlPermissionsOptions> configure)` — full option control
- `setup.UsePostgreSql(Action<PostgreSqlPermissionsOptions, IServiceProvider> configure)` — with resolved services
- Idempotent schema, table, and index creation at host startup via `PostgreSqlPermissionsStorageInitializer`
- `PostgreSqlPermissionsOptions` — `ConnectionString` and `CommandTimeout` (default 30 seconds)
- Shares `PermissionsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)
- Identifier names validated against PostgreSQL naming rules

### Installation

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

### Quick Start

Register required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessPermissions` registers the management core automatically.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UsePostgreSql(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

### Configuration

#### Options

`PostgreSqlPermissionsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | Npgsql connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `PermissionsStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

### Dependencies

- `Headless.Permissions.Core`
- `Headless.Serializer.Json`
- `Npgsql`

### Side Effects

- Registers `PostgreSqlPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlPermissionGrantRepository` as `IPermissionGrantRepository` (singleton)
- Registers `PostgreSqlPermissionDefinitionRecordRepository` as `IPermissionDefinitionRecordRepository` (singleton)

---

## Headless.Permissions.Storage.SqlServer

SQL Server raw-DDL storage for permission management.

### Problem Solved

Provides permission repositories and startup schema initialization without requiring the consumer to use Entity Framework. All schema is created idempotently at host startup via raw ADO.NET.

### Key Features

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — binds `SqlServerPermissionsOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerPermissionsOptions> configure)` — full option control
- `setup.UseSqlServer(Action<SqlServerPermissionsOptions, IServiceProvider> configure)` — with resolved services
- Idempotent schema, table, and index creation at host startup via `SqlServerPermissionsStorageInitializer`
- `SqlServerPermissionsOptions` — `ConnectionString` and `CommandTimeout` (default 30 seconds)
- Shares `PermissionsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)
- Identifier names validated against SQL Server naming rules

### Installation

```bash
dotnet add package Headless.Permissions.Storage.SqlServer
```

### Quick Start

Register required services first — `TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`. `AddHeadlessPermissions` registers the management core automatically.

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "permissions");
    setup.UseSqlServer(connectionString);
});

// Or with full option control:
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
        options.CommandTimeout = TimeSpan.FromSeconds(60);
    });
});
```

### Configuration

#### Options

`SqlServerPermissionsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string (required). |
| `CommandTimeout` | 30 seconds | Timeout for DDL/DML commands. |

Configure schema and table names through `PermissionsStorageOptions` via `setup.ConfigureStorage(...)`. Set `InitializeOnStartup = false` when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true` so dependents awaiting `WaitForInitializationAsync` do not block.

### Dependencies

- `Headless.Permissions.Core`
- `Headless.Serializer.Json`
- `Microsoft.Data.SqlClient`

### Side Effects

- Registers `SqlServerPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerPermissionGrantRepository` as `IPermissionGrantRepository` (singleton)
- Registers `SqlServerPermissionDefinitionRecordRepository` as `IPermissionDefinitionRecordRepository` (singleton)
