---
domain: Permissions
packages: Permissions.Abstractions, Permissions.Core, Permissions.Storage.EntityFramework, Permissions.Storage.PostgreSql, Permissions.Storage.SqlServer, Permissions.Testing
---

# Permissions

> Dynamic permission management with hierarchical grant resolution (User > Role), explicit-deny semantics, caching, and database persistence via EF Core, PostgreSQL, or SQL Server.

## Orientation

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

## Agent Rules

- Inject `IPermissionManager` to read or write permission grants. Never roll custom permission checks.
- Check a permission with `GetAsync(name, currentUser)` and inspect `result.IsGranted`. Use `IsGrantedAsync(currentUser, name)` for a boolean shorthand.
- Define permissions by implementing `IPermissionDefinitionProvider` and calling `context.AddGroup("Name").AddChild("Name.Action")`. The method is `AddChild`, not `AddPermission` — the latter does not exist.
- Register definition providers with `services.AddPermissionDefinitionProvider<T>()`.
- Grant a permission with `GrantToUserAsync` / `GrantToRoleAsync`. Prohibit explicitly with `RevokeFromUserAsync` / `RevokeFromRoleAsync`. Delete all records for a principal with `DeleteAsync(providerName, providerKey)`.
- Resolution order (highest to lowest priority): **User** then **Role**. An explicit `Prohibited` from any provider denies access regardless of other grants. The default when no record exists is deny.
- Do NOT call `IDynamicPermissionDefinitionStore.SaveAsync` directly — `PermissionsInitializationBackgroundService` handles it on startup.
- Both `IPermissionManager` and direct `IPermissionGrantRepository` writes invalidate the affected cache entry (the repository removes it after `SaveChangesAsync`). Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale.
- Protect an endpoint with the permission name itself: `[Authorize("Orders.Edit")]` or `.RequireAuthorization("Orders.Edit")` works for any defined permission with no `AddPolicy` call. There is **no** `[HasPermission]` attribute. Build a `PermissionsRequirement` policy only for an any-of (OR) or all-of set under one name, and use `IPermissionManager` for in-code checks.
- Policy names passed to `[Authorize]` must come from code, never from user input: an undefined name throws "policy not found", and permission names match case-sensitively.
- `SetAsync` throws `ConflictException` when the permission is not defined, is disabled, restricts its providers and excludes the given `providerName`, or when no grant provider with that name is registered. Catch this for user-facing validation.
- Batch writes via `SetAsync(IReadOnlyCollection<string>, ...)` are all-or-nothing — a single invalid name rejects the entire batch.
- A batch grant converts existing `Prohibited` records to grants and inserts records for names that have none, exactly like the single-name path; names that are already granted are left untouched.
- `PermissionDefinition.Providers` restricts which grant providers can read/write that permission. An empty list allows all providers.
- For integration tests, reference `Headless.Permissions.Testing` and call `services.AddAlwaysAllowAuthorization()` to replace both `IPermissionManager` and `IAuthorizationService` with always-allow stubs. This lives in a separate test-only package so the production `Headless.Permissions.Core` surface never ships an authorization bypass.
- Grant caching is tenant-scoped: the cache key includes the current tenant id. A permission check for tenant A does not serve a cached result for tenant B.

## Core Concepts

### Permission Definitions and Groups

A *permission definition* declares a permission's identity and metadata: its unique `Name`, optional `DisplayName`, whether it is `IsEnabled` (disabled permissions always resolve to not-granted), which `Providers` may read/write it (empty = all), and an optional `ExtraProperties` bag (the model implements `IHasExtraProperties`) for custom metadata.

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

`PermissionGrantStore` caches resolved grant statuses to avoid repeated database reads per request. The cache is backed by a tenant-scoped `ICache<PermissionGrantCacheItem>` keyed on the current tenant id. When `IPermissionManager.SetAsync` writes a grant, `PermissionGrantStore` evicts the affected cache entries directly through `ICache`. Direct `IPermissionGrantRepository` writes also evict the affected cache entry (removed after `SaveChangesAsync`), so a repository-level write is reflected on the next read. Only writes that bypass the repository entirely (raw SQL, direct `DbContext`) leave the cache stale, and they stay stale for `PermissionManagementOptions.GrantCacheExpiration` (default 5 hours). A long-lived consumer that must observe such a write sooner, for example a streaming connection whose credential is checked once per event, calls `IPermissionGrantStore.RefreshAsync(providerName, providerKey)`, which reloads every grant for that target from the repository and replaces the cached statuses.

### Reacting to a change

`PermissionManager` publishes `PermissionGrantChangedMessage` over `IBus` after a successful `SetAsync` or `DeleteAsync`. The message tells an instance that a copied grant decision is stale, so the instance can re-read instead of polling. Consume it like any other message:

```csharp
public sealed class ReloadRolePermissions(MyPolicyCache cache) : IConsume<PermissionGrantChangedMessage>
{
    public async ValueTask ConsumeAsync(ConsumeContext<PermissionGrantChangedMessage> context, CancellationToken ct)
    {
        var message = context.Message;

        // Scope matters: another role's grant must not invalidate this role's policy.
        if (
            !string.Equals(message.ProviderName, PermissionGrantProviderNames.Role, StringComparison.Ordinal)
            || !string.Equals(message.ProviderKey, cache.RoleId, StringComparison.Ordinal)
        )
        {
            return;
        }

        if (message.PermissionNames.Any(cache.Tracks))
        {
            await cache.ReloadAsync(ct);
        }
    }
}
```

The message carries permission names and the scope that changed, never grant values. Values would make delivery order load-bearing: two racing grant and revoke writes could leave a receiver holding the older state. A receiver re-reads instead, which is idempotent and order-free. `OriginHostName` names the host that wrote the change (`IHostIdentityAccessor.HostName`) for logs and telemetry. Do not filter on it. The writing process holds copies too, since the manager knows nothing about the state a consumer copied a grant into. The origin must re-read like every other instance.

`IBus` is optional. A host that never calls `AddHeadlessMessaging` writes grants exactly as before and publishes nothing. A failed publish is logged and never fails the write that already succeeded. In both cases a peer keeps its copy until it re-reads for its own reasons, so a consumer that must converge without a bus still needs a periodic refresh.

The announcement follows the write but not the commit. The injected `IBus` never enlists in a transaction, so when `SetAsync` runs inside a caller's unit of work (`RunAsync(db, …)`) the message goes out before that unit commits. A peer that re-reads in that window loads the old grant with no further signal. Call `SetAsync` outside a surrounding unit, or keep the backstop refresh above, when that window matters.

This signal is separate from grant-cache coherence. A store-backed write evicts the affected cache entries directly. `PermissionGrantChangedMessage` exists for state the framework cannot see, such as a resolved grant decision that a consumer copied into a field of its own.

### Static vs. Dynamic Definition Store

The *static store* (`IStaticPermissionDefinitionStore`) builds the permission catalog once at startup by invoking all registered `IPermissionDefinitionProvider` instances. It is thread-safe and lazily initialized on first access.

The *dynamic store* (`IDynamicPermissionDefinitionStore`) reads definitions from the database, caches them in-process with a configurable expiry (`DynamicDefinitionsMemoryCacheExpiration`, default 30 seconds), and coordinates cross-instance refreshes via a distributed cache stamp and a distributed lock. The dynamic store is disabled by default (`IsDynamicPermissionStoreEnabled = false`); enable it only when permission definitions must be edited at runtime without redeployment.

`IPermissionDefinitionManager` merges both stores. Static definitions take precedence over dynamic definitions of the same name.

### Startup Initialization

`PermissionsInitializationBackgroundService` runs after the application starts. It:

1. Persists static permission definitions to the database (guarded by a distributed lock; up to 10 jittered exponential-back-off retries capped at 30 seconds), when `SaveStaticPermissionsToDatabase = true`.
2. Pre-caches dynamic definitions from the database into the in-process cache, when `IsDynamicPermissionStoreEnabled = true`.

Dependents can await `WaitForInitializationAsync()` on the `IInitializer` interface to block until both tasks complete. Cancellation, `ArgumentException`, and `NotSupportedException` fail immediately without retry; other terminal failures surface to every waiter. When both options are `false`, initialization is a no-op and `IsInitialized` is set to `true` immediately.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Permissions.Storage.EntityFramework` | You already use EF Core and want schema managed via EF migrations | You need to avoid an EF dependency or want zero-overhead ADO.NET | Portable across any EF-supported DB; startup validates that all permissions entities are in the EF model before hosted services start |
| `Headless.Permissions.Storage.PostgreSql` | You use PostgreSQL and want no EF Core dependency | You run SQL Server or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against PostgreSQL naming rules |
| `Headless.Permissions.Storage.SqlServer` | You use SQL Server and want no EF Core dependency | You run PostgreSQL or need EF migrations for schema management | Creates schema idempotently at startup via raw DDL; identifier names are validated against SQL Server naming rules |

---

## Headless.Permissions.Abstractions

Defines the unified interface for permission management across different grant providers and storage backends.

### API and behavior

- `IPermissionManager` — resolves and mutates permission grants with AWS IAM-style semantics; `GetAsync` (single), `GetAllAsync` (all or by names), `SetAsync` (grant or prohibit, single or batch), `DeleteAsync` (remove all records for a principal)
- `PermissionManagerExtensions` — convenience helpers: `IsGrantedAsync` (boolean overloads), `GrantToUserAsync`, `RevokeFromUserAsync`, `SetToUserAsync`, `GrantToRoleAsync`, `RevokeFromRoleAsync`, `SetToRoleAsync`
- `IPermissionDefinitionProvider` — contributes permission groups and definitions at startup via `IPermissionDefinitionContext`
- `IPermissionDefinitionManager` — looks up and enumerates all defined permissions (`FindAsync`, `GetPermissionsAsync`, `GetGroupsAsync`)
- `IPermissionDefinitionContext` — mutable builder passed to `IPermissionDefinitionProvider.Define`; `AddGroup(name, displayName)`, `GetGroup`, `GetGroupOrDefault`, `RemoveGroup`, `GetPermissionOrDefault`. Groups are created only through `AddGroup(name, ...)` — there is no instance-taking `AddGroup(PermissionGroupDefinition)` overload and `PermissionGroupDefinition`'s constructor is internal
- `PermissionGroupDefinition` — named container for permissions; `AddChild`, `GetFlatPermissions`, `GetPermissionOrDefault`. Constructed via `IPermissionDefinitionContext.AddGroup`, not directly
- `PermissionDefinition` — single permission; `AddChild` for nesting, `RemoveChild(name)` to detach a child, `Providers` list for restricting which grant providers can manage it
- `ICanAddChildPermission` — shared interface on both group and definition, enabling uniform `AddChild` calls in tree-building code
- `GrantedPermissionResult` — result of `GetAsync`; `Name`, `IsGranted`, and `Providers` (`IReadOnlyList<GrantPermissionProvider>` — the contributing grant providers with their keys; the framework populates it)
- `GrantPermissionProvider` — identifies a contributing provider by `Name` and the `Keys` (user id or role names) that granted the permission
- `MultiplePermissionGrantResult` — exposes the name-to-granted map as a read-only `Grants` (`IReadOnlyDictionary<string, bool>`) property, a `this[permissionName]` lookup indexer, and `AllGranted`/`AllProhibited` shorthand properties; returned by batch `IsGrantedAsync`
- `PermissionGrantProviderNames` — constants `User` and `Role` for the built-in providers

### Install

```bash
dotnet add package Headless.Permissions.Abstractions
```

### Setup and use

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

### Runtime behavior

None.

---

## Headless.Permissions.Core

Core implementation of permission management with grant resolution, caching, background initialization, and ASP.NET Core authorization integration.

### API and behavior

- `PermissionManager` — full `IPermissionManager` implementation; walks the grant-provider chain, caches results, and coordinates writes with cache invalidation
- `IPermissionGrantProvider` / built-in providers: `UserPermissionGrantProvider` (`"User"`) and `RolePermissionGrantProvider` (`"Role"`)
- `IStaticPermissionDefinitionStore` — builds the permission catalog lazily and thread-safely from all registered `IPermissionDefinitionProvider` instances
- `IDynamicPermissionDefinitionStore` — database-backed definition store with in-process caching and distributed-stamp cross-instance coordination; disabled by default
- `PermissionsInitializationBackgroundService` — seeds static definitions with up to 10 jittered exponential-back-off retries capped at 30 seconds; pre-caches dynamic definitions when enabled; implements `IInitializer`
- `PermissionManagementOptions` — all tuning options for lock keys/timeouts, cache expiry, dynamic store toggle
- `PermissionsStorageOptions` — schema and table name configuration shared across all storage providers
- `HeadlessPermissionsSetupBuilder` — fluent builder returned inside `AddHeadlessPermissions`; exposes `ConfigureManagement`, `ConfigureStorage`, `DisableStartupInitialization`, `DisablePermissionNamePolicies`, `RegisterExtension`. `ConfigureStorage` also accepts the `Headless:Permissions:Storage` configuration section.
- `PermissionPolicyProvider` — `IAuthorizationPolicyProvider` that resolves a defined permission name as a policy holding one `PermissionRequirement`; registered by default in place of ASP.NET Core's default provider
- `HeadlessPermissionsBuilder` — returned by `AddHeadlessPermissions`; exposes `Services` for post-registration additions
- `services.AddPermissionDefinitionProvider<T>()` — registers a custom `IPermissionDefinitionProvider` as singleton
- `services.AddPermissionGrantProvider<T>()` — registers an additional grant provider (last-registered = highest priority)
- `IGrantPermissionsSeedHelper` / `GrantPermissionsSeedHelper` — seed-time helper for granting all allowed permissions to a role idempotently
- `PermissionRequirement` / `PermissionRequirementHandler` — ASP.NET Core `IAuthorizationRequirement` for a single permission
- `PermissionsRequirement` / `PermissionsRequirementHandler` — multi-permission requirement with AND (`RequiresAll = true`) or OR semantics

The always-allow test doubles (`AlwaysAllowPermissionManager` / `AlwaysAllowAuthorizationService`) and `services.AddAlwaysAllowAuthorization()` live in the separate `Headless.Permissions.Testing` package, not Core.

### Design constraints

- Grant providers are stored in registration order with last-registered = highest priority. The built-in registration is `Role` first, then `User`, making User the highest-priority built-in provider. Custom providers added via `AddPermissionGrantProvider<T>()` are appended after `User` and override both built-ins.
- `AddHeadlessPermissions` is guarded on `IPermissionGrantStore` so calling it more than once is safe — the management core registers once. However, registering a second storage provider extension throws at host startup.
- **Permission-name policies.** `PermissionPolicyProvider` asks ASP.NET Core's default provider first, so a policy registered with `AddPolicy` always wins. ASP.NET matches those names ignoring case, so a host policy `orders.edit` shadows the permission `Orders.Edit`. On a miss, the name is looked up as a permission (ordinal, case-sensitive): a defined permission — enabled or disabled — resolves to a policy whose only requirement is `PermissionRequirement`; anything else resolves to `null` and ASP.NET Core throws its usual "policy not found" `InvalidOperationException`. A disabled permission therefore still resolves, and its policy denies everyone.
- **Provider registration order.** `AddHeadlessPermissions` replaces ASP.NET Core's `DefaultAuthorizationPolicyProvider` whether `AddAuthorization`/`AddControllers` ran before or after it. A host that registered its own `IAuthorizationPolicyProvider` before `AddHeadlessPermissions` keeps it and gets no permission-name resolution; such a provider can create `PermissionPolicyProvider` with `ActivatorUtilities.CreateInstance<PermissionPolicyProvider>(serviceProvider)` and consult it on its own misses. A provider registered afterwards with `TryAdd*` is skipped, and one registered afterwards with `AddSingleton` replaces ours. ASP.NET Core uses exactly one policy provider.
- **Generated policies hold only the permission requirement**, the same as a hand-written `AddPolicy(name, p => p.Requirements.Add(new PermissionRequirement(name)))`. ASP.NET Core never merges `DefaultPolicy` into a named policy, so tenant enforcement added through `RequireTenant()` and any default authentication schemes do not apply to `[Authorize("Orders.Edit")]`.
- **Caching.** A resolved name keeps its policy for the process lifetime, and the provider lets the authorization middleware cache the combined policy per endpoint. Misses are not cached, so a permission added later through the dynamic store resolves without a restart (after `DynamicDefinitionsMemoryCacheExpiration`); a permission deleted after first use keeps its policy, and `PermissionRequirementHandler` denies it because undefined permissions are never granted.
- **Failures propagate.** A definition-store failure (cache, lock, or database) while resolving a policy surfaces as that exception, never as "policy not found", and is not cached. The provider contract has no cancellation token, so with the dynamic store enabled a first lookup during an outage can wait up to `CrossApplicationsCommonLockAcquireTimeout`.
- The grant cache is tenant-scoped (`ScopedCache<PermissionGrantCacheItem>` keyed on `ICurrentTenant.Id`). A permission check for tenant A never returns a cached result for tenant B.
- `PermissionsInitializationBackgroundService` implements `IInitializer`: anything awaiting `WaitForInitializationAsync()` blocks until both the save and pre-cache steps complete. Cancellation, `ArgumentException`, and `NotSupportedException` fail immediately without retry; other failures retain 10 retries, and the terminal exception is surfaced to every waiter. If the host stops before initialization finishes, the background task and waiters are cancelled.
- `PermissionGrantRecord` implements `ICreateAudit` / `IUpdateAudit` and carries `CreatedAt` (non-null) and `UpdatedAt` (nullable) audit timestamps. Grants are insert-only — a revoke deletes the row and inserts a replacement rather than updating — so `UpdatedAt` is normally null. The EF provider stamps `CreatedAt` through the audit save-processor; the raw-SQL providers stamp it from the injected `TimeProvider`. Hydrate from storage with the `PermissionGrantRecord.FromStorage(...)` factory, which sets the audit fields.
- **Tenancy divergence (intentional).** `PermissionGrantRecord` keeps a first-class `TenantId` column and implements `IMultiTenant`, unlike `SettingValueRecord` / `FeatureValueRecord`, which scope tenancy through `ProviderName`/`ProviderKey` and have no tenant column. Grants need tenant-scoped uniqueness expressed directly in the `(Name, ProviderName, ProviderKey, TenantId)` unique index so the same grant can coexist per tenant and for the host. Because `TenantId` is nullable and PostgreSQL treats NULLs as distinct, every storage provider (EF, PostgreSQL, SQL Server) declares that constraint as a pair of filtered unique indexes — one `WHERE "TenantId" IS NOT NULL`, one over `(Name, ProviderName, ProviderKey)` `WHERE "TenantId" IS NULL` — so host-level grants are covered too. This is a deliberate design decision, not drift.

### Install

```bash
dotnet add package Headless.Permissions.Core
```

### Setup and use

Register required services (`TimeProvider`, `ICache`, `IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessPermissions`:

> A caching provider is a hard prerequisite. This package references `Headless.Caching.Abstractions` only, so the root `ICache` that the tenant-scoped `ICache<PermissionGrantCacheItem>` wrapper delegates to comes from `AddHeadlessCaching(...)` with a provider (`UseInMemory` / `UseRedis` / `UseHybrid`). `AddHeadlessPermissions` declares it via `Headless.Hosting`'s `RequireRegisteredService<T>`, so a host without one is refused at startup with a `MissingRequiredServiceException` rather than failing on the first permission check. Registration order does not matter — the check runs at host start.

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// 2. Register management core + storage
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

#### ASP.NET Core Authorization Integration

Use a defined permission name directly as the policy name. `AddHeadlessPermissions` registers `PermissionPolicyProvider`, so no `AddPolicy` call is needed:

```csharp
// Controllers
[Authorize("Orders.Edit")]
public IActionResult Edit(int id) => Ok();

// Minimal APIs
app.MapPut("/orders/{id}", (int id) => Results.Ok()).RequireAuthorization("Orders.Edit");
```

Stacked attributes and several names in one `RequireAuthorization(...)` call are AND. For an any-of (OR) set, or an all-of set under one name, register a `PermissionsRequirement` policy:

```csharp
// Multi-permission policy (AND); requiresAll: false gives OR
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

To make the namespace explicit, set a prefix; only prefixed names then resolve, with the prefix stripped before the lookup (`[Authorize("permission:Orders.Edit")]`):

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options => options.PolicyNamePrefix = "permission:");
    setup.UseEntityFramework<AppDbContext>();
});
```

Hosts that resolve policies themselves opt out, which leaves the existing `IAuthorizationPolicyProvider` untouched:

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.DisablePermissionNamePolicies();
    setup.UseEntityFramework<AppDbContext>();
});
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

        // How long the SHA-256 hash of saved permissions is cached (default: 30 days)
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

        // Only policy names with this prefix resolve as permissions (ordinal match,
        // stripped before lookup). Null resolves any defined permission name;
        // empty or whitespace fails validation (default: null)
        options.PolicyNamePrefix = null;
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

### Runtime behavior

- Registers `IPermissionManager` (`PermissionManager`) as singleton
- Registers `IPermissionGrantStore` (`PermissionGrantStore`) as singleton
- Registers `IPermissionGrantProviderManager` as singleton
- Registers `IStaticPermissionDefinitionStore`, `IDynamicPermissionDefinitionStore`, `IPermissionDefinitionManager` as singletons
- Registers `RolePermissionGrantProvider`, `UserPermissionGrantProvider` as singletons
- Starts `PermissionsInitializationBackgroundService` as a hosted service (`IInitializer`) unless `setup.DisableStartupInitialization()` was called
- Registers `IGrantPermissionsSeedHelper` as transient
- Registers `PermissionRequirementHandler` and `PermissionsRequirementHandler` as `IAuthorizationHandler` singletons
- Registers `PermissionPolicyProvider` as the singleton `IAuthorizationPolicyProvider`, replacing ASP.NET Core's default provider and keeping a host-registered one, unless `setup.DisablePermissionNamePolicies()` was called
- Registers a tenant-scoped `ICache<PermissionGrantCacheItem>` as singleton

---

## Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage implementation for permission management.

### API and behavior

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessPermissionsSetupBuilder`
- `modelBuilder.AddHeadlessPermissions(DbContext context)` — applies entity configurations by resolving `PermissionsStorageOptions` from the context's service provider (no constructor injection required)
- `modelBuilder.AddHeadlessPermissions(PermissionsStorageOptions options)` — overload for when you already hold the options
- `EfPermissionGrantRepository<TContext>` — EF repository for `IPermissionGrantRepository`
- `EfPermissionDefinitionRecordRepository<TContext>` — EF repository for `IPermissionDefinitionRecordRepository`
- Startup gate that inspects the EF model before hosted services start and throws `InvalidOperationException` with an actionable message if any permissions entity is missing
- Grant uniqueness declared as a pair of filtered unique indexes — `(TenantId, Name, ProviderName, ProviderKey) WHERE "TenantId" IS NOT NULL` and `(Name, ProviderName, ProviderKey) WHERE "TenantId" IS NULL` — matching the raw-DDL providers, so host (NULL-tenant) grants stay unique on databases that treat NULLs as distinct (PostgreSQL, SQLite)

### Design constraints

The package does not ship a dedicated permissions `DbContext` or a permissions-specific `DbContext` interface. Consumers register `AddDbContextFactory<TContext>()`, map entities with `modelBuilder.AddHeadlessPermissions(this)` in `OnModelCreating`, and keep their public context API free of framework-specific `DbSet` properties. Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`; writes commit through a fresh context owned by the repository.

### Install

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

### Setup and use

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

### Runtime behavior

- Registers `IPermissionGrantRepository` (`EfPermissionGrantRepository<TContext>`) as singleton
- Registers `IPermissionDefinitionRecordRepository` (`EfPermissionDefinitionRecordRepository<TContext>`) as singleton
- Registers validated `PermissionsStorageOptions`
- Registers `PermissionsEntityStartupValidator<TContext>` as an `IHeadlessStartupValidator`

---

## Headless.Permissions.Storage.PostgreSql

PostgreSQL raw-DDL storage for permission management.

### API and behavior

- `setup.UsePostgreSql(string connectionString)` — registers the PostgreSQL storage provider from a connection string
- `setup.UsePostgreSql(IConfiguration configuration)` — binds `PostgreSqlPermissionsOptions` from a configuration section
- `setup.UsePostgreSql(Action<PostgreSqlPermissionsOptions> configure)` — full option control
- `setup.UsePostgreSql(Action<PostgreSqlPermissionsOptions, IServiceProvider> configure)` — with resolved services
- Idempotent schema, table, and index creation at host startup via `PostgreSqlPermissionsStorageInitializer`
- `PostgreSqlPermissionsOptions` — `ConnectionString` and `CommandTimeout` (default 30 seconds)
- Shares `PermissionsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)
- Identifier names validated against PostgreSQL naming rules

### Install

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

### Setup and use

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

### Runtime behavior

- Registers `PostgreSqlPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `PostgreSqlPermissionGrantRepository` as `IPermissionGrantRepository` (singleton)
- Registers `PostgreSqlPermissionDefinitionRecordRepository` as `IPermissionDefinitionRecordRepository` (singleton)

---

## Headless.Permissions.Storage.SqlServer

SQL Server raw-DDL storage for permission management.

### API and behavior

- `setup.UseSqlServer(string connectionString)` — registers the SQL Server storage provider from a connection string
- `setup.UseSqlServer(IConfiguration configuration)` — binds `SqlServerPermissionsOptions` from a configuration section
- `setup.UseSqlServer(Action<SqlServerPermissionsOptions> configure)` — full option control
- `setup.UseSqlServer(Action<SqlServerPermissionsOptions, IServiceProvider> configure)` — with resolved services
- Idempotent schema, table, and index creation at host startup via `SqlServerPermissionsStorageInitializer`
- `SqlServerPermissionsOptions` — `ConnectionString` and `CommandTimeout` (default 30 seconds)
- Shares `PermissionsStorageOptions` with the EF provider (schema, table names, `InitializeOnStartup`)
- Identifier names validated against SQL Server naming rules

### Install

```bash
dotnet add package Headless.Permissions.Storage.SqlServer
```

### Setup and use

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

### Runtime behavior

- Registers `SqlServerPermissionsStorageInitializer` as `IHostedService` and `IInitializer`
- Registers `SqlServerPermissionGrantRepository` as `IPermissionGrantRepository` (singleton)
- Registers `SqlServerPermissionDefinitionRecordRepository` as `IPermissionDefinitionRecordRepository` (singleton)

---

## Headless.Permissions.Testing

Test-only doubles that bypass all permission and authorization checks.

### API and behavior

- `services.AddAlwaysAllowAuthorization()` — replaces `IPermissionManager` with `AlwaysAllowPermissionManager` and `IAuthorizationService` with `AlwaysAllowAuthorizationService`, granting every permission and authorizing every request
- `AlwaysAllowPermissionManager` — `IPermissionManager` that reports every permission as granted; `SetAsync` / `DeleteAsync` are no-ops
- `AlwaysAllowAuthorizationService` — `IAuthorizationService` that returns `AuthorizationResult.Success()` for every call

### Install

```bash
dotnet add package Headless.Permissions.Testing
```

### Setup and use

```csharp
// In an integration-test host builder, after AddHeadlessPermissions:
builder.Services.AddAlwaysAllowAuthorization();
```

### Configuration

None.

### Runtime behavior

- Replaces the registered `IPermissionManager` with `AlwaysAllowPermissionManager` (singleton)
- Replaces the registered `IAuthorizationService` with `AlwaysAllowAuthorizationService` (singleton)
- Does not touch policy resolution. On the endpoint (middleware) path, `[Authorize("<name>")]` still resolves the policy before calling the service, so an undefined name still throws "policy not found". Direct `IAuthorizationService.AuthorizeAsync(user, name)` calls succeed for any name, typos included.
