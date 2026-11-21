---
domain: Permissions
packages: Permissions.Abstractions, Permissions, Permissions.Storage.EntityFramework, Permissions.Storage.PostgreSql, Permissions.Storage.SqlServer, Permissions.Testing
---

# Permissions

> Dynamic permission grants for users and roles, with explicit deny, caching, ASP.NET Core policy integration, and storage on EF Core, PostgreSQL, or SQL Server.

## Orientation

- `Headless.Permissions.Abstractions`: the contracts for domain and application code. `IPermissionManager`, `IPermissionDefinitionProvider`, the definition model, and `unit.Permissions`.
- `Headless.Permissions`: the runtime. Grant resolution, the grant cache, the definitions seeder, and the ASP.NET Core authorization integration.
- One storage provider: `Headless.Permissions.Storage.EntityFramework` (schema through your EF migrations), `Headless.Permissions.Storage.PostgreSql`, or `Headless.Permissions.Storage.SqlServer` (schema through `resources provision`).
- `Headless.Permissions.Testing`: always-allow doubles for integration tests.

Register the prerequisites, the definitions, and the runtime with one storage provider:

```csharp
// Prerequisites: TimeProvider, IGuidGenerator, a caching provider, and a distributed lock.
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory()); // production: the hybrid cache, see Caching
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// Definitions, then the runtime and its storage in one call.
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

Read a grant with `IPermissionManager.GetAsync` or `IsGrantedAsync`. Protect an endpoint with the permission name as the policy name: `[Authorize("Orders.Edit")]`.

**Verify:** `dotnet app.dll check` runs the startup validators and exits 1 with `Host check failed: …` when a prerequisite is missing (`MissingRequiredServiceException`) or the EF model lacks the permission entities. After the deployment runs `seed`, `dotnet app.dll seed verify` prints `Current` for `PermissionDefinitions`.

## Agent Rules

- Inject `IPermissionManager` to read or write grants. Do not write your own permission checks.
- Check one permission with `GetAsync(name, currentUser)` and read `result.IsGranted`, or call `IsGrantedAsync(currentUser, name)` for a `bool`. An undefined or disabled permission returns not granted. It does not throw.
- Define permissions in an `IPermissionDefinitionProvider` with `context.AddGroup("Orders").AddChild("Orders.Edit")`. The method is `AddChild`. There is no `AddPermission`.
- Register each definition provider with `services.AddPermissionDefinitionProvider<T>()`.
- Grant with `GrantToUserAsync` or `GrantToRoleAsync`. Deny explicitly with `RevokeFromUserAsync` or `RevokeFromRoleAsync`. Remove every record of a user or role with `DeleteAsync(providerName, providerKey)`.
- Pass `PermissionGrantProviderNames.User` or `PermissionGrantProviderNames.Role` as `providerName`, not a string literal.
- To make a grant, revoke, or delete atomic with your other writes, call it on the unit you hold: `unit.Permissions.GrantToRoleAsync(...)`. An injected `IPermissionManager` write never joins a unit. It commits before it returns. See [Writing grants inside a unit of work](#writing-grants-inside-a-unit-of-work).
- An explicit prohibition from any provider denies, whatever the other providers grant. With no record anywhere, the answer is deny.
- Protect an endpoint with the permission name itself: `[Authorize("Orders.Edit")]` or `.RequireAuthorization("Orders.Edit")`. You need no `AddPolicy` call. There is no `[HasPermission]` attribute. Register a `PermissionsRequirement` policy only for an any-of (OR) set or a named all-of set.
- Pass `[Authorize]` only policy names from code, never from user input. An undefined name throws "policy not found", and permission names match case-sensitively.
- A permission says whether this caller may act. A feature flag says whether the capability is on for the tenant. Gate a feature with `[RequiresFeature]` or `.RequireAuthorization(policy => policy.RequireFeatures(...))` ([Gating HTTP endpoints](features.md#gating-http-endpoints)). Put both on an endpoint that needs both. They combine into one policy, and both fail with 403, or 401 for an anonymous caller.
- A reason passed to `Fail(new AuthorizationFailureReason(...))` reaches the caller in the 403 `detail`. Keep internal detail out of it.
- `SetAsync` throws `ConflictException` when the permission is undefined or disabled, when its `Providers` excludes `providerName`, or when no grant provider has that name. Catch it for user-facing validation.
- A batch `SetAsync(IReadOnlyCollection<string>, ...)` is all-or-nothing: one invalid name rejects the whole batch.
- For a role or user edit form that must not overwrite another admin's save, read with `GetVersionedAsync(names, providerName, providerKey)` and save with `SetVersionedAsync(grants, providerName, providerKey, version)`. A stale save throws `ConflictException` with `permissions:version-mismatch`. See [Editing grants with optimistic concurrency](#editing-grants-with-optimistic-concurrency).
- Do not call `IDynamicPermissionDefinitionStore.SaveAsync`. The `PermissionDefinitions` seeder saves definitions when the deployment runs `seed`. See [Definition Seeding](#definition-seeding).
- Write grants only through `IPermissionManager`, `unit.Permissions`, or the EF `IPermissionGrantRepository`. Raw SQL, a direct `DbContext` write, or a direct call to the PostgreSQL or SQL Server repository leaves the grant cache stale for up to `GrantCacheExpiration` (5 hours).
- Use the hybrid cache ([`UseHybrid`](caching/hybrid.md#headlesscachinghybrid)) in production. Every authorized request reads the grant cache. See [Grant Store and Caching](#grant-store-and-caching).
- In integration tests that do not test authorization, call `services.AddAlwaysAllowAuthorization()` from `Headless.Permissions.Testing`.

## Core Concepts

### Permission Definitions and Groups

A permission definition has a unique `Name`, an optional `DisplayName`, `IsEnabled` (a disabled permission is never granted), `Providers` (the grant providers that may read or write it; empty allows all), and an `ExtraProperties` bag for your own metadata.

Permissions form a tree. A group, created with `context.AddGroup(name, displayName)`, is the top-level container. `group.AddChild("Orders.View")` adds a root permission, and `permission.AddChild("Orders.View.Detail")` nests a child. You cannot construct a group or a definition directly.

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

Two built-in grant providers resolve a permission, from lowest to highest priority:

1. **Role** (`PermissionGrantProviderNames.Role`) checks the grants of every role the current user holds, in one store read. A name any role prohibits is prohibited. Otherwise a name any role grants is granted. Role order never decides a conflict. A user with no roles gets `Undefined`.
2. **User** (`PermissionGrantProviderNames.User`) checks the grants on the user's id. An anonymous principal gets `Undefined`.

The last-registered provider has the highest priority, so User wins over Role. A provider you add with `services.AddPermissionGrantProvider<T>()` goes after both built-ins and outranks them.

### Grant States

Each provider returns one state per permission:

- **Granted**: a record exists with `IsGranted = true`.
- **Prohibited**: a record exists with `IsGranted = false`. A prohibition from any provider overrides grants from every other provider.
- **Undefined**: no record exists. When every provider returns `Undefined`, the permission is not granted.

`GrantedPermissionResult.IsGranted` is the merged decision. `GrantedPermissionResult.Providers` lists the providers, with their keys, that granted it. An explicit denial empties the list.

### Grant Store and Caching

The grant store caches each grant status in the registered `ICache`, keyed by the current tenant id. A check for tenant A never returns a cached result for tenant B.

- **Writes update the cache.** `IPermissionManager.SetAsync` writes the new status into the cache, and `DeleteAsync` marks the deleted names undefined. A `unit.Permissions` write updates the cache only after its unit commits, under the tenant that made the write. A rolled-back unit leaves the cache as it was. A cache fault after a commit is logged, not thrown.
- **Races resolve to the last commit.** A check that read the rows before a concurrent write never overwrites the newer status. Two writes to one grant leave the last committed status cached. Each write costs one extra repository read to guarantee this.
- **Bypassing writes go stale.** A direct EF `IPermissionGrantRepository` write updates the cache too. Raw SQL, a direct `DbContext` write, or a direct PostgreSQL or SQL Server repository call does not, and the old status stays cached for `PermissionManagementOptions.GrantCacheExpiration` (default 5 hours).
- **Force a reload.** A long-lived consumer that must see such a write sooner, such as a streaming connection that checks a credential per event, calls `IPermissionGrantStore.RefreshAsync(providerName, providerKey)`. It reloads every grant of that user or role and replaces the cached statuses.
- **Batched reads.** `IPermissionGrantStore.IsGrantedAsync(names, providerName, providerKeys)` returns every key's statuses for every name in one cache round trip and at most one repository read, so a check across N roles costs one call.

**Use the hybrid cache in production.** With `UseHybrid`, an in-process tier serves a warm check with no network trip. With a distributed-only cache (`UseRedis`), every read is a round trip on the request path, and nothing memoizes a result within a request. One tenant-scoped request that resolves its tenant through the catalog, checks a permission and a feature, and reads one setting waits on about five sequential round trips before the handler's own I/O: two for the tenant catalog, one for the permission check (the user and role reads run together), one for the feature check, and one for the setting. A second check of the same name in the same request repeats its trip.

### Reacting to a change

`IPermissionManager` publishes `PermissionGrantChangedMessage` over `IBus` after each successful `SetAsync` or `DeleteAsync`. Consume it to refresh a grant decision you copied into state of your own, instead of polling. The grant cache does not need it: store writes already update the cache.

Consume it with an [every-instance consumer](messaging/consumers.md#every-instance-bus-delivery), so every process receives every announcement. Delivery is at most once, so also reload when the subscription is established. `AddHeadlessPermissions` declares the message as `headless.permissions.grant-changed`, contract version `1` (`PermissionGrantChangedMessage.MessageName`), so you declare no `Message<T>` for it. Naming conventions (`UseConventions`) do not rename it. Only `MessagingOptions.MessageNamePrefix` applies.

```csharp
[BusConsumer("app.reload-role-permissions", EveryInstance = true)]
public sealed class ReloadRolePermissions(MyPolicyCache cache) : IConsume<PermissionGrantChangedMessage>, IOnSubscriptionEstablished
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

    // Announcements published while this process was not subscribed never arrive. That includes the first
    // establishment: anything loaded at startup was read before the subscription went live.
    public async ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken ct)
    {
        await cache.ReloadAsync(ct);
    }
}
```

- **Names, not values.** The message carries `PermissionNames`, `ProviderName`, and `ProviderKey`, never grant values. Re-read the grant on receipt. A re-read is idempotent and does not depend on delivery order.
- **Do not filter on `OriginHostName`.** It names the writing host for logs. The writing process holds copies too, so it re-reads like every other instance.
- **After the commit.** An `IPermissionManager` write commits before the message goes out, even inside your own `RunAsync(db, …)`, so a re-read loads the new grant. A `unit.Permissions` write publishes after its unit commits. A rolled-back unit publishes nothing. A crash between the commit and the publish loses the announcement.
- **Messaging is optional.** A host without `AddHeadlessMessaging` publishes nothing. A failed publish is logged and never fails the write. A consumer that must converge without a bus still needs a periodic refresh.
- **No ordering with cache invalidation.** With a hybrid cache, a peer can receive `PermissionGrantChangedMessage` before its own `CacheInvalidationMessage` and re-read the old grant from its local tier. When the first re-read shows no change, re-read again a few seconds later, and keep the periodic refresh short. A copied grant decision is only as current as its last re-read.

**Verify:** in a test, grant a permission through `IPermissionManager` and assert that the consumer reloaded.

### Editing grants with optimistic concurrency

An edit form reads what a group of permissions holds for one role or user, lets an admin change it, and saves it back. `GetVersionedAsync` returns each permission's state for that target and the group's version. `SetVersionedAsync` saves only while the group still has that version. Otherwise it throws `ConflictException` with the error code `permissions:version-mismatch` and writes nothing. Over HTTP, the version is the `ETag`, and the save takes it in `If-Match`. The Headless API exception handler maps the `ConflictException` to 409.

Each state is a `bool?`: `true` grants, `false` explicitly prohibits, and `null` means the target holds no record. Saving `null` clears the target's record for that permission.

```csharp
string[] orders = ["Orders.View", "Orders.Edit", "Orders.Refund"];

app.MapGet("/roles/{role}/permissions/orders", async (string role, IPermissionManager permissions, HttpResponse response, CancellationToken ct) =>
{
    var group = await permissions.GetVersionedAsync(orders, PermissionGrantProviderNames.Role, role, ct);
    response.Headers.ETag = $"\"{group.Version}\"";

    return Results.Ok(group.Grants);
});

app.MapPut("/roles/{role}/permissions/orders", async (string role, Dictionary<string, bool?> body, HttpRequest request, IPermissionManager permissions, CancellationToken ct) =>
{
    var version = request.Headers.IfMatch.ToString().Trim('"');

    if (version.Length == 0)
    {
        return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
    }

    // Every name of the group, so an omitted name clears its record instead of failing the version check.
    var grants = orders.ToDictionary(name => name, name => body.GetValueOrDefault(name), StringComparer.Ordinal);
    await permissions.SetVersionedAsync(grants, PermissionGrantProviderNames.Role, role, version, ct);

    return Results.NoContent();
});
```

**Verify:** in a test, call `GetVersionedAsync`, change one of the permissions with `GrantToRoleAsync` or `RevokeFromRoleAsync`, then call `SetVersionedAsync` with the version you read. It throws `ConflictException` whose `Errors[0].Code` is `permissions:version-mismatch`, and `GetVersionedAsync` returns the other write's state.

- **The group.** The version covers the names you pass, for one target (`providerName` and `providerKey`) in the current tenant. Two forms over different names do not block each other. A version read in one tenant never authorizes a save in another.
- **Same names on save.** Save exactly the names you read. Another name set has another version, so a save with fewer or more names throws `permissions:version-mismatch`.
- **What changes the version.** Any grant, prohibition, or clear of one of the group's permissions for that target, through any `IPermissionManager` member, `unit.Permissions`, or the EF repository. A write that leaves a permission in the state it already had stores nothing, so the version stays.
- **The target's own records.** The states are what the role or user itself holds, not a user's effective result across roles.
- **Read from storage.** `GetVersionedAsync` reads the records from the database, not the grant cache.
- **Atomicity.** The check and the write run in one serializable transaction. Of two saves from one version that change anything, exactly one succeeds, even when they change different permissions. A serialization failure that leaves the version unchanged is retried, up to three attempts.
- **No schema change.** A grant record is never updated in place: a grant that becomes a prohibition is deleted and inserted again. The version hashes the record ids, so it needs no extra column.
- **Not in a unit of work.** `unit.Permissions` has no versioned write.
- **Supported stores.** The EF Core, PostgreSQL, and SQL Server storage support it. A custom `IPermissionGrantProvider` must implement `GetVersionedAsync` and `SetVersionedAsync`, and a custom `IPermissionGrantRepository` must implement `SaveIfVersionAsync` and compute the version with `PermissionGrantVersion.Compute`. Their defaults throw `NotSupportedException`.

### Writing grants inside a unit of work

`unit.Permissions` (namespace `Headless.UnitOfWork`) writes grants inside the unit you hold. It offers `SetAsync` (single or batch), `DeleteAsync(providerName, providerKey)`, `GrantToUserAsync`, `RevokeFromUserAsync`, `GrantToRoleAsync`, and `RevokeFromRoleAsync`. Each call runs on the unit's connection and transaction, so the grant commits or rolls back with your other writes:

```csharp
await factory.RunAsync(db, async (unit, ct) =>
{
    db.Users.Add(user);
    await db.SaveChangesAsync(ct);
    await unit.Permissions.GrantToRoleAsync("Orders.Edit", "editors", ct);
}, ct);
```

- **Which unit joins.** A unit with a live relational transaction on the database the storage uses: an EF unit (`BeginAsync(db)`, `RunAsync(db, …)`) or a raw-ADO unit (`BeginAsync(connection)`), on any of the three stores. A unit on another database, a unit with no resource, or a completed unit throws `InvalidOperationException` before any write, and the unit stays usable. `unit.Permissions` also throws `InvalidOperationException` when `AddHeadlessPermissions` was not called.
- **After commit.** The cache entries and `PermissionGrantChangedMessage` follow the commit, under the tenant that made the write. A rolled-back unit does neither.
- **Validation is unchanged.** Definition, provider, and key checks throw `ConflictException` or `ArgumentException` before the unit is touched, as `IPermissionManager.SetAsync` does.
- **Replay.** Inside an owned `RunAsync` block the writes replay with the block. Inside a domain-event handler running in the `HeadlessDbContext` save pipeline, the first write calls `IUnitOfWork.PreventRetry()`, because that replay cannot restore rows written outside the change tracker.
- **EF with a retrying execution strategy.** A context with `EnableRetryOnFailure` joins only from inside `RunAsync(db, …)`. From a raw-ADO unit it throws `InvalidOperationException` naming `RunAsync(db, …)`.
- **Custom extensions.** A custom `IPermissionGrantProvider` or `IPermissionGrantRepository` joins a unit only by implementing its `*EnlistedAsync` members. The defaults throw `NotSupportedException`. Replacing `IPermissionManager` does not disable `unit.Permissions`, which is registered separately.

**Verify:** in a test, throw inside the `RunAsync` block after the grant and assert that `IsGrantedAsync` still returns `false`.

### Static vs. Dynamic Definition Store

The static store builds the catalog once, on first use, from every registered `IPermissionDefinitionProvider`.

The dynamic store reads definitions from the database. It is off by default (`IsDynamicPermissionStoreEnabled = false`). Turn it on only when definitions must change at runtime without a redeploy. It caches definitions in process for `DynamicDefinitionsMemoryCacheExpiration` (default 30 seconds), then checks a distributed stamp that other instances bump.

- With the dynamic store on, the `PermissionDefinitions` [warm-up](utilities.md#warm-up) loads it after the host starts. The `warmup` readiness check reports `Unhealthy` until that load succeeds, and a failed load retries in the background. With it off, the warm-up does nothing.
- A save that adds or updates definitions publishes `DynamicPermissionDefinitionsChanged` over `IBus`, listing the names. Its wire name is `headless.permissions.definitions-changed`, contract version `1` (`DynamicPermissionDefinitionsChanged.MessageName`). Without messaging, other instances still refresh through the stamp on their next read.
- `IPermissionDefinitionManager` merges both stores. A static definition wins over a dynamic one with the same name.

### Definition Seeding

`AddHeadlessPermissions` registers a seeder named `PermissionDefinitions`. When `SaveStaticPermissionsToDatabase` is `true` (the default), it saves the static definitions to the database. The save is idempotent and runs under a distributed lock.

- **Run it on every release.** The host start does not seed. Run `dotnet app.dll seed` (or `deploy`) in the deployment. In development, call `services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None)`. `HeadlessTestServer` seeds by default. See [Database Seeders](utilities.md#database-seeders).
- **Retries.** A failed save retries up to 10 times with jittered exponential back-off capped at 30 seconds. Cancellation, `ArgumentException`, and `NotSupportedException` fail at once. The back-off waits real time, even under a `FakeTimeProvider`. A failed seed fails the `seed` verb, or the host start under `AddDeployStepsOnStartup`.
- **Extra properties.** `ExtraProperties` are stored as JSON. Keys keep their case, and values read back as `string`, `bool`, `long`, `decimal`, or `JsonElement`. Read them with `GetProperty<T>(name)`. Re-seeding unchanged definitions writes nothing.
- **Deleting definitions.** List removed names in `PermissionManagementProvidersOptions.DeletedPermissions` or `DeletedPermissionGroups` (`services.Configure<PermissionManagementProvidersOptions>(...)`). The next seed removes them.

A host that never seeds still serves its static definitions from memory. But deleted definitions stay in the database, other applications sharing the database keep stale definitions, and `DynamicPermissionDefinitionsChanged` is not published.

**Verify:** `dotnet app.dll seed verify` prints `Current` for `PermissionDefinitions`, and exits 1 when the store is `Stale`.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.Permissions.Storage.EntityFramework` | You already use EF Core and want the schema in your EF migrations | You want no EF Core dependency | Works on any EF Core database. A start fails when the EF model lacks the permission entities |
| `Headless.Permissions.Storage.PostgreSql` | You use PostgreSQL without EF Core | You run SQL Server or want EF migrations to own the schema | `resources provision` creates the tables. Table names follow PostgreSQL identifier rules |
| `Headless.Permissions.Storage.SqlServer` | You use SQL Server without EF Core | You run PostgreSQL or want EF migrations to own the schema | `resources provision` creates the tables. Table names follow SQL Server identifier rules |

---

## Headless.Permissions.Abstractions

The contracts that application and domain code reference.

### Install

```bash
dotnet add package Headless.Permissions.Abstractions
```

### API and behavior

- `IPermissionManager`: `GetAsync` (one name), `GetAllAsync` (all, or by names), `SetAsync` (grant or prohibit, one name or a batch), and `DeleteAsync` (every record of one user or role). `GetAsync` and `GetAllAsync` take an optional `providerName` that limits resolution to one provider.
- `PermissionManagerExtensions`: `IsGrantedAsync` (one name returns `bool`, several return `MultiplePermissionGrantResult`), `GrantToUserAsync`, `RevokeFromUserAsync`, `SetToUserAsync`, `GrantToRoleAsync`, `RevokeFromRoleAsync`, and `SetToRoleAsync`.
- `unit.Permissions`: the unit-bound writer. See [Writing grants inside a unit of work](#writing-grants-inside-a-unit-of-work).
- `IPermissionDefinitionProvider` and `IPermissionDefinitionContext`: `AddGroup(name, displayName)`, `GetGroup`, `GetGroupOrDefault`, `RemoveGroup`, and `GetPermissionOrDefault`. Create groups only through `AddGroup`.
- `PermissionGroupDefinition` and `PermissionDefinition`: both have `AddChild`. A definition also has `RemoveChild(name)` and `Providers`.
- `IPermissionDefinitionManager`: `FindAsync`, `GetPermissionsAsync`, and `GetGroupsAsync`.
- `GrantedPermissionResult`: `Name`, `IsGranted`, and `Providers` (each a `GrantPermissionProvider` with `Name` and the `Keys`, user id or role names, that granted it).
- `MultiplePermissionGrantResult`: `Grants` (name to granted), an indexer by name, `AllGranted`, and `AllProhibited`.
- `PermissionGrantChangedMessage`: the change announcement. See [Reacting to a change](#reacting-to-a-change).
- `PermissionGrantProviderNames`: the constants `User` and `Role`.

### Setup and use

```csharp
public sealed class OrderService(IPermissionManager permissions, ICurrentUser currentUser)
{
    public async Task<ApiResult> DeleteOrderAsync(Guid orderId, CancellationToken ct)
    {
        var result = await permissions.GetAsync("Orders.Delete", currentUser, cancellationToken: ct);

        if (!result.IsGranted)
            return ApiResult.Forbidden("Orders.Delete is not granted."); // maps to 403

        // Delete order...
        return ApiResult.Ok();
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

Define permissions as shown in [Permission Definitions and Groups](#permission-definitions-and-groups).

**Verify:** no startup signal of its own. The runtime registration in [Headless.Permissions](#headlesspermissions) carries the checks.

---

## Headless.Permissions

The runtime: grant resolution, the grant cache, the definitions seeder, and the ASP.NET Core authorization integration.

### Install

```bash
dotnet add package Headless.Permissions
```

### Setup and use

Register `TimeProvider`, `IGuidGenerator`, a caching provider (`AddHeadlessCaching` with `UseInMemory`, `UseRedis`, or `UseHybrid`), and an `IDistributedLock`. Then call `AddHeadlessPermissions` with one storage provider:

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// 2. Register management core + storage
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

- A host without a caching provider fails at start with `MissingRequiredServiceException`, not on the first check. Registration order does not matter.
- Calling `AddHeadlessPermissions` twice is safe: the runtime registers once. A second storage provider throws at host start.

**Verify:** `dotnet app.dll check` exits 0. A missing prerequisite makes it exit 1 with `Host check failed: …` naming the service.

#### ASP.NET Core Authorization Integration

Use a defined permission name as the policy name. `AddHeadlessPermissions` registers a policy provider that resolves it, so you add no policy:

```csharp
// Controllers
[Authorize("Orders.Edit")]
public IActionResult Edit(int id) => Ok();

// Minimal APIs
app.MapPut("/orders/{id}", (int id) => Results.Ok()).RequireAuthorization("Orders.Edit");
```

Stacked attributes, and several names in one `RequireAuthorization(...)` call, are AND. For an any-of (OR) set, or an all-of set under one name, register a `PermissionsRequirement` policy:

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

Or check in code:

```csharp
var isGranted = await permissionManager.IsGrantedAsync(currentUser, "Orders.Edit");
```

To make permission policies explicit, set a prefix. Only prefixed names then resolve, and the prefix is stripped before the lookup (`[Authorize("permission:Orders.Edit")]`):

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options => options.PolicyNamePrefix = "permission:");
    setup.UseEntityFramework<AppDbContext>();
});
```

To keep your own `IAuthorizationPolicyProvider` and turn off permission-name policies:

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.DisablePermissionNamePolicies();
    setup.UseEntityFramework<AppDbContext>();
});
```

How permission-name policies behave:

- **A registered policy wins.** A policy added with `AddPolicy` is looked up first, and ASP.NET Core matches it ignoring case, so a policy `orders.edit` shadows the permission `Orders.Edit`. Otherwise the name is looked up as a permission, ordinal and case-sensitive. A defined permission, enabled or disabled, resolves to a policy holding one `PermissionRequirement`. A disabled permission's policy denies everyone. Any other name throws ASP.NET Core's "policy not found" `InvalidOperationException`.
- **No default policy is merged.** A permission policy holds only the permission requirement. `RequireTenant()` and default authentication schemes on `DefaultPolicy` do not apply to `[Authorize("Orders.Edit")]`.
- **Failures.** A permission that is not granted stays unsatisfied, so ASP.NET Core returns 403, or 401 for an anonymous caller. Every permission requirement in one policy is read in one `GetAllAsync` call.
- **Your own policy provider.** `AddHeadlessPermissions` replaces ASP.NET Core's default provider whatever the call order. A provider you register before `AddHeadlessPermissions` stays, and permission names then do not resolve. To keep both, create `PermissionPolicyProvider` with `ActivatorUtilities.CreateInstance<PermissionPolicyProvider>(serviceProvider)` and call it on your provider's misses. A provider registered afterwards with `AddSingleton` replaces the permission provider. One registered with `TryAdd*` is ignored.
- **Caching.** A resolved name keeps its policy for the life of the process. A miss is not cached, so a permission added through the dynamic store resolves after `DynamicDefinitionsMemoryCacheExpiration` without a restart. A permission deleted after first use keeps its policy, which denies everyone.
- **Store outages.** A definition-store failure while resolving a policy surfaces as that exception, not as "policy not found". With the dynamic store on, a first lookup during an outage can wait up to `CrossApplicationsCommonLockAcquireTimeout`.
- **Workers.** `AddHeadlessPermissions` calls `AddAuthorizationCore()`, so `IAuthorizationService` resolves in a host that never called `AddAuthorization()`. Your own authorization registrations win.

`IAuthorizationPolicyCatalog` lists names:

- `GetRegisteredPolicyNamesAsync()`: the policies added to `AuthorizationOptions`. Policies a custom provider creates on demand are not listed. It reads a private ASP.NET Core field, so an ASP.NET Core release that renames it makes the call throw `MissingMethodException`.
- `GetPermissionNamesAsync()`: the defined permission names.
- `GetPolicyNamesAsync()`: every name that resolves to a policy, with any `PolicyNamePrefix` applied. Every name it returns is safe to pass to `IAuthorizationService`. Under `DisablePermissionNamePolicies()` or your own provider, it returns the registered policies only.

**Verify:** in an integration test, call the endpoint anonymously and assert 401, as a user without the grant and assert 403, then grant the permission and assert success.

#### Client Config for SPAs

A front end usually needs, at login and on refresh, the permissions and policies the user holds and the client-visible features and settings. Headless ships one reader per domain and no endpoint. Compose their results into your own response, such as a user-config endpoint or the login and refresh responses.

```csharp
public sealed class UserConfigFactory(
    IGrantedPoliciesReader grantedPolicies,
    IClientVisibleFeaturesReader features,
    IClientVisibleSettingsReader settings
)
{
    // The named policies the client is allowed to see; every defined permission is always included.
    private static readonly string[] _ExposedPolicies = ["Administrator", "Charity"];

    public async Task<UserConfig> BuildAsync(ClaimsPrincipal principal, string? tenantId, CancellationToken ct)
    {
        var context = new PrincipalContext(principal, tenantId);

        return new UserConfig
        {
            GrantedPolicies = await grantedPolicies.GetAsync(context, _ExposedPolicies, ct),
            Features = await features.GetAsync(context, ct),
            Settings = await settings.GetAsync(context, ct),
        };
    }
}
```

- **Pass the principal you return.** Each reader resolves for the `PrincipalContext` principal and tenant, not the ambient ones, and restores the ambient ones afterwards. At login the new principal is not ambient yet, so do not build the config from `ICurrentUser`. Pass `TenantId: null` for the host.
- **The set holds only granted names.** `IGrantedPoliciesReader.GetAsync` returns every granted permission name plus every listed policy the principal satisfies. A name that is not granted is absent. Project it to the shape the front end expects, such as `{ name: true }`.
- **Cost.** Every defined permission is read in one `GetAllAsync` call, and a listed name that resolves as a permission policy is decided from that read. Each other listed policy costs one `IAuthorizationService` evaluation. A registered policy that shadows a permission name is evaluated as a policy. If it fails, the name is absent even when the permission is granted.
- **Unknown names throw.** A listed name that is neither a registered policy nor a defined permission throws ASP.NET Core's "No policy found" `InvalidOperationException`. To expose every resolvable name, pass `await catalog.GetPolicyNamesAsync()` from `IAuthorizationPolicyCatalog`.

**Verify:** in a test, build the config for a principal holding one role grant and assert that the set contains that permission and omits an ungranted one.

#### Seeding Permissions at Startup

```csharp
// In a data seeder or IHostedService:
await seedHelper.GrantAllPermissionsToRoleAsync("admin", tenantId: null, ct);
```

`IGrantPermissionsSeedHelper.GrantAllPermissionsToRoleAsync` grants a role every permission that allows the `Role` provider. It skips permissions that already have a grant record, so it is idempotent.

**Verify:** after the seeder runs, `IsGrantedAsync` for a user in the role returns `true`.

### Configuration

#### PermissionManagementOptions

Configure with `setup.ConfigureManagement(...)` (also an `(options, IServiceProvider)` overload) or `services.Configure<PermissionManagementOptions>(...)`:

| Option | Default | Effect |
| --- | --- | --- |
| `GrantCacheExpiration` | 5 hours | How long a cached grant status lives. Bounds staleness after a write that bypasses the store |
| `IsDynamicPermissionStoreEnabled` | `false` | Turns on the database-backed definition store |
| `DynamicDefinitionsMemoryCacheExpiration` | 30 seconds | How long dynamic definitions stay in process before the distributed stamp is re-checked |
| `SaveStaticPermissionsToDatabase` | `true` | Whether the `PermissionDefinitions` seeder saves static definitions |
| `PolicyNamePrefix` | `null` | Only names with this prefix (ordinal) resolve as permissions. `null` resolves any defined name. Empty or white space fails validation |
| `CrossApplicationsCommonLockKey` | `permissions:common_update_lock` | Distributed-lock key for definition saves across applications |
| `CrossApplicationsCommonLockExpiration` | 10 minutes | How long that lock is held |
| `CrossApplicationsCommonLockAcquireTimeout` | 5 minutes | Longest wait for that lock |
| `ApplicationSaveLockExpiration` | 10 minutes | How long the per-application save lock is held |
| `ApplicationSaveLockAcquireTimeout` | 5 minutes | Longest wait for the per-application save lock |
| `PermissionsHashCacheExpiration` | 30 days | How long the hash of saved definitions is cached |
| `CommonPermissionsUpdatedStampCacheKey` | `permissions:updated_local_stamp` | Distributed-cache key of the cross-application update stamp |
| `CommonPermissionsUpdatedStampCacheExpiration` | 30 days | How long that stamp lives |

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options =>
    {
        options.IsDynamicPermissionStoreEnabled = true;
        options.PolicyNamePrefix = "permission:";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

#### PermissionsStorageOptions

Configure schema and table names with `setup.ConfigureStorage(...)`, or bind the `Headless:Permissions:Storage` section with its `IConfiguration` overload:

```csharp
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(o =>
    {
        o.Schema = "headless"; // default, shared by every Headless feature
        o.PermissionGrantsTableName = null; // default: permission_grants on PostgreSQL, PermissionGrants elsewhere
        o.PermissionDefinitionsTableName = null; // default: permission_definitions / PermissionDefinitions
        o.PermissionGroupDefinitionsTableName = null; // default: permission_group_definitions / PermissionGroupDefinitions
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

- **Naming.** On PostgreSQL, tables, columns, keys, and indexes are snake_case (`permission_grants`, `provider_key`, `pk_permission_grants`). On SQL Server and other databases they are PascalCase (`PermissionGrants`, `ProviderKey`, `PK_PermissionGrants`). The EF mapping and the raw providers produce the same names on the same database.
- **Custom table names.** A name you set is used as written, and its key and index names derive from it (`pk_MyGrants`). PostgreSQL truncates identifiers past 63 bytes, so every provider refuses a table name longer than 18 characters for grants, 49 for definitions, or 55 for groups.
- **Tenancy.** Each grant row carries a `TenantId` column (null for host grants). A grant is unique per tenant on `(TenantId, Name, ProviderName, ProviderKey)`, and host grants are unique on `(Name, ProviderName, ProviderKey)`, on every provider.

##### Column lengths

Every string column's maximum length is an option on `PermissionsStorageOptions`:

| Option | Column | Default |
| --- | --- | --- |
| `PermissionGrantsNameMaxLength` | grants `Name` | 128 |
| `PermissionGrantsProviderNameMaxLength` | grants `ProviderName` | 64 |
| `PermissionGrantsProviderKeyMaxLength` | grants `ProviderKey` | 64 |
| `PermissionGrantsTenantIdMaxLength` | grants `TenantId` | 41 |
| `PermissionDefinitionsNameMaxLength` | definitions `Name` and `ParentName` | 128 |
| `PermissionDefinitionsDisplayNameMaxLength` | definitions `DisplayName` | 256 |
| `PermissionDefinitionsProvidersMaxLength` | definitions `Providers` | 128 |
| `PermissionGroupDefinitionsNameMaxLength` | group definitions `Name` and definitions `GroupName` | 128 |
| `PermissionGroupDefinitionsDisplayNameMaxLength` | group definitions `DisplayName` | 256 |

```csharp
setup.ConfigureStorage(o => o.PermissionGrantsProviderKeyMaxLength = 128);
```

- Each value must be between 1 and 4000. The registration refuses any other value when the options are first read.
- One option sets the EF Core model, the raw providers' `CREATE TABLE`, and their query parameter sizes. Set it before the tables are created.
- A definition whose name or display name exceeds its length throws `ArgumentOutOfRangeException` when the seeder saves it.
- The grants unique indexes cover `TenantId`, `Name`, `ProviderName`, and `ProviderKey`; the definitions table indexes `Name` and `GroupName`, and the group definitions table `Name`. SQL Server limits a nonclustered index key to 1,700 bytes (850 `nvarchar` characters), so the four grants lengths together, and each indexed name length, must total at most 850. The registration refuses a wider configuration when the options are first read, on every provider, so one configuration stays valid on each database.

**Changing a length after the tables exist needs a migration in your application.** The option does not alter an existing column. Change the option and the column in the same release:

- **EF Core storage:** change the option, then add a migration (`dotnet ef migrations add WidenPermissionGrantProviderKeyColumn`). It contains the `AlterColumn`.
- **Raw PostgreSQL or SQL Server storage:** run the `ALTER` with the tool that owns your schema, then deploy the option. `resources provision` does not widen a column.

  ```sql
  -- PostgreSQL: widening a varchar changes metadata only and keeps its indexes
  ALTER TABLE headless.permission_grants ALTER COLUMN provider_key TYPE character varying(128);

  -- SQL Server: restate the nullability, or the column takes the session default
  ALTER TABLE [headless].[PermissionGrants] ALTER COLUMN [ProviderKey] nvarchar(128) NOT NULL;
  ```

Widen the column before, or together with, the code that writes longer values. Narrowing a column that holds longer values fails on both databases, so shorten the data first.

### Design and runtime behavior

- **Keys must survive every database unchanged.** Every grant-store entry point throws `ArgumentException` before touching storage when a permission name, provider name, or provider key starts or ends with white space, contains a NUL character, or contains an unpaired UTF-16 surrogate. Grant and revoke writes check the ambient `ICurrentTenant.Id` the same way. SQL Server ignores trailing spaces when it compares keys, so `"acme "` would overwrite the `"acme"` row there. Normalize keys at your own boundary. The store refuses rather than trims. `IPermissionManager.DeleteAsync` and `GrantPermissionsSeedHelper` apply the same rule.
- **Writes use the current tenant.** A grant or revoke writes, reads, and caches under `ICurrentTenant.Id`. To write another tenant's grant, wrap the call in `currentTenant.Change(tenantId)`.
- **A revoke replaces the row.** A grant-to-deny flip deletes the record and inserts a new one in one transaction, so a failure never leaves the permission undefined. `UpdatedAt` on a grant record is therefore normally null.
- **Tenancy fallback.** `AddHeadlessPermissions` registers an `AsyncLocal`-backed `ICurrentTenant` fallback. Any real tenancy registration replaces it.
- **Lifetimes.** `IPermissionManager`, the grant store, the definition stores, and the authorization handler are singletons. `IGrantPermissionsSeedHelper`, `IAuthorizationPolicyCatalog`, and `IGrantedPoliciesReader` are transient.
- **Startup does no writes.** The host start does not seed and does not create tables. Raw providers create tables only through `resources provision`. A start only verifies them.

---

## Headless.Permissions.Storage.EntityFramework

Entity Framework Core storage for grants and definitions.

### Install

```bash
dotnet add package Headless.Permissions.Storage.EntityFramework
```

### Setup and use

Register a singleton `IDbContextFactory<TContext>`, map the entities in `OnModelCreating`, then call `UseEntityFramework<TContext>()`:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Resolves PermissionsStorageOptions from the context's service provider —
        // no need to inject IOptions<PermissionsStorageOptions> into the constructor.
        modelBuilder.ConfigureHeadlessPermissions(this);
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

Create the tables with an EF migration. The provider writes no DDL.

**Verify:** `dotnet app.dll check` exits 0. A model without the entities exits 1 with `InvalidOperationException` text naming the missing entity and `modelBuilder.ConfigureHeadlessPermissions(this)`. A scoped or transient factory exits 1 with `InvalidServiceLifetimeException`.

### API and behavior

- `setup.UseEntityFramework<TContext>()` registers the EF storage provider.
- `modelBuilder.ConfigureHeadlessPermissions(DbContext context)` maps the entities. It reads `PermissionsStorageOptions` from the context's service provider and picks snake_case on Npgsql, PascalCase on every other provider.
- `modelBuilder.ConfigureHeadlessPermissions(PermissionsStorageOptions options, StorageNamingStyle style)` maps them from options you hold. Pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` (namespace `Headless.Hosting`) so the style matches the database.

### Design and runtime behavior

- **Factory.** Register the factory with `AddDbContextFactory<TContext>()` or `AddPooledDbContextFactory<TContext>()` for a plain `DbContext`, or `AddHeadlessDbContext<TContext>()` or `AddHeadlessDbContextPool<TContext>()` for a `HeadlessDbContext`. All four register a singleton factory. Startup refuses a scoped or transient one with `InvalidServiceLifetimeException`.
- **No permissions `DbContext`.** The package ships no context of its own and adds no `DbSet` to yours.
- **Own context per write.** Reads use `AsNoTracking()`. Writes commit through a fresh context, so they never join your outer transaction. Use `unit.Permissions` to join one.
- **Tenant isolation without a query filter.** Every read and delete filters on `ICurrentTenant.Id` (a null tenant matches only host rows), so a plain `DbContext` keeps one tenant's `admin` role apart from another's.
- **Tenant write guard.** A host grant saves under a `HeadlessDbContext` with `GuardTenantWrites()` when there is no ambient tenant and every record has a null `TenantId`. A write that names a tenant stays guarded.
- **Enlisted writes.** A `unit.Permissions` call borrows the unit's connection and transaction for that call only. A context whose options own their connection (`contextOwnsConnection: true`) is refused.
- **Identifiers.** Table names are checked against SQL Server identifier rules, which also satisfy the other databases, plus the PostgreSQL length limits in [PermissionsStorageOptions](#permissionsstorageoptions).

---

## Headless.Permissions.Storage.PostgreSql

PostgreSQL storage for grants and definitions, without EF Core.

### Install

```bash
dotnet add package Headless.Permissions.Storage.PostgreSql
```

### Setup and use

Register the prerequisites (`TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`). Then reuse the shared connection or give the feature its own:

```csharp
builder.Services.AddPostgreSqlSql(connectionString);
builder.Services.AddHeadlessPermissions(setup => setup.UsePostgreSql());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_permissions");
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

`UsePostgreSql` takes a connection string, an `IConfiguration` section, an `Action<PostgreSqlPermissionsOptions>`, an `Action<PostgreSqlPermissionsOptions, IServiceProvider>`, or nothing. With no argument it uses the connection from `AddPostgreSqlSql` ([shared connection and schema](sql.md#shared-connection-and-schema-for-storage-features)).

Run `dotnet app.dll resources provision` (or `deploy`) on every release. It applies the steps `Permissions/1` (tables) and `Permissions/2` (indexes) through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). A host start only verifies them and fails, naming `resources provision`, when a step is missing.

**Verify:** `dotnet app.dll resources verify` reports `Ok` for the schema resource ([Resource provisioning](utilities.md#resource-provisioning)).

### Configuration

`PostgreSqlPermissionsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | Npgsql connection string. Required. |
| `CommandTimeout` | 30 seconds | Timeout for each command. |

Configure schema and table names through `PermissionsStorageOptions` with `setup.ConfigureStorage(...)`. Table names are checked against PostgreSQL identifier rules.

---

## Headless.Permissions.Storage.SqlServer

SQL Server storage for grants and definitions, without EF Core.

### Install

```bash
dotnet add package Headless.Permissions.Storage.SqlServer
```

### Setup and use

Register the prerequisites (`TimeProvider`, `ICache`, `IDistributedLock`, and `IGuidGenerator`). Then reuse the shared connection or give the feature its own:

```csharp
builder.Services.AddSqlServerSql(connectionString);
builder.Services.AddHeadlessPermissions(setup => setup.UseSqlServer());

// Or give this feature its own connection and schema:
builder.Services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_permissions");
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

`UseSqlServer` takes a connection string, an `IConfiguration` section, an `Action<SqlServerPermissionsOptions>`, an `Action<SqlServerPermissionsOptions, IServiceProvider>`, or nothing. With no argument it uses the connection from `AddSqlServerSql` ([shared connection and schema](sql.md#shared-connection-and-schema-for-storage-features)).

Run `dotnet app.dll resources provision` (or `deploy`) on every release. It applies one step, `Permissions/1`, with the tables, indexes, and table types, through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). A host start only verifies it and fails, naming `resources provision`, when it is missing.

**Verify:** `dotnet app.dll resources verify` reports `Ok` for the schema resource ([Resource provisioning](utilities.md#resource-provisioning)).

### Configuration

`SqlServerPermissionsOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | `""` | SQL Server connection string. Required. |
| `CommandTimeout` | 30 seconds | Timeout for each command. |

Configure schema and table names through `PermissionsStorageOptions` with `setup.ConfigureStorage(...)`. Table names are checked against SQL Server identifier rules.

---

## Headless.Permissions.Testing

Always-allow doubles for integration tests that do not test authorization. It is a separate package so production code never references an authorization bypass.

### Install

```bash
dotnet add package Headless.Permissions.Testing
```

### Setup and use

```csharp
// In an integration-test host builder, after AddHeadlessPermissions:
builder.Services.AddAlwaysAllowAuthorization();
```

**Verify:** a request to an `[Authorize("Orders.Edit")]` endpoint by an authenticated test user with no grants succeeds.

### API and behavior

- `services.AddAlwaysAllowAuthorization()` replaces `IPermissionManager` with `AlwaysAllowPermissionManager` and `IAuthorizationService` with `AlwaysAllowAuthorizationService`, both singletons.
- `AlwaysAllowPermissionManager` reports every permission as granted. `GetVersionedAsync` reports every requested permission as granted under one fixed version. `SetAsync`, `SetVersionedAsync`, and `DeleteAsync` do nothing.
- `AlwaysAllowAuthorizationService` returns `AuthorizationResult.Success()` for every call.
- Policy resolution is unchanged. On the endpoint path, `[Authorize("<name>")]` still resolves the policy first, so an undefined name still throws "policy not found". A direct `IAuthorizationService.AuthorizeAsync(user, name)` call succeeds for any name, typos included.
