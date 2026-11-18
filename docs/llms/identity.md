---
domain: Identity
packages: Identity.Storage.EntityFramework
---

# Identity

> ASP.NET Core Identity on the Headless EF Core save pipeline: auditing, soft delete, event tiers, and multi-tenancy through one base `DbContext`.

## Orientation

One package: `Headless.Identity.Storage.EntityFramework`. Derive your context from `HeadlessIdentityDbContext<…>`, which extends ASP.NET Core Identity's `IdentityDbContext<…>` with the same save pipeline as `HeadlessDbContext` in [ORM](orm.md).

Register it with `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, …>()` (one context per scope) or `services.AddHeadlessDbContextPool<TDbContext, TUser, TRole, TKey, …>()` (pooled), from `SetupIdentityEntityFramework`. They match `Headless.EntityFramework`'s `AddHeadlessDbContext<TDbContext>()` and `AddHeadlessDbContextPool<TDbContext>()`, with the Identity type parameters added, and wire the same services: the save pipeline, audit persistence, tenant and user accessors, `IDbContextFactory<TDbContext>`, and request-scope binding. Register `UserManager` and `RoleManager` yourself with `AddIdentityCore`.

Use this package only when the application uses the Headless save pipeline. An Identity-only project uses `Microsoft.AspNetCore.Identity.EntityFrameworkCore` directly.

## Agent Rules

- Register with `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>()`, or the form that adds `TUserPasskey`, or the pooled `services.AddHeadlessDbContextPool<…>()` with the same type parameters. No `AddHeadlessIdentity(...)` or `UseEntityFramework<...>()` API exists.
- Choose pooled or per-scope as in [ORM registration modes](orm.md#registration-modes-and-scope-binding): pooled for high throughput, per-scope when the context constructor or the options callback needs scoped services.
- Never register the context with `AddDbContext<TDbContext>()`. It skips the Headless services, request-scope binding, DI-registered interceptors, and `IDbContextFactory<TDbContext>`.
- Register Identity managers and stores with `services.AddIdentityCore<TUser>().AddRoles<TRole>().AddEntityFrameworkStores<TDbContext>()`. `AddHeadlessDbContext` registers the context only.
- Derive from the form with explicit `TUserPasskey` for passkey-aware stores. The shorter form fixes `TUserPasskey = IdentityUserPasskey<TKey>`.
- Give your context one constructor that takes `DbContextOptions<TDbContext>` and passes it to `base(options)`. A pooled context declares exactly one public constructor (its options plus, optionally, singleton services) and keeps no per-request state in its own fields.
- Override the abstract `DefaultSchema`. Return `null` for the database default schema, or a name such as `"identity"` to put every Identity table in that schema.
- Chain `.AddDomainEvents()` or `.AddIntegrationEventOutbox()` on the returned builder when an entity emits events. Neither tier is registered by default. See [The two-tier event model](orm.md#the-two-tier-event-model).
- To target an older Identity schema, call `services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version1)` after `AddHeadlessDbContext`. The registration sets `Version3` once, and the later `Configure` call wins.
- To make Identity tenant-owned, call `ConfigureTenantOwnedIdentity(modelBuilder)` after `base.OnModelCreating(modelBuilder)`. Enable automatic tenant stamping before tracking with `builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()))`. Your entities implement no tenancy interface.
- Use a fresh DI scope for the context, stores, `UserManager`, and `RoleManager` when you change tenants. The tenant filter does not protect an entity already tracked: `FindAsync` can return it without querying.
- Ship the migration and an explicit tenant backfill before you enable tenant-owned Identity. See [Tenant key storage and rollout](#tenant-key-storage-and-rollout).

## Core Concepts

### What this package adds over stock Identity and EF Core

`HeadlessIdentityDbContext<…>` derives from `IdentityDbContext<…>`, so every stock Identity model and store convention applies. It runs `SaveChanges` and `SaveChangesAsync` through the same pipeline as `HeadlessDbContext`:

- **Audit columns** (`CreatedAt`, `UpdatedAt`, `CreatedById`, `UpdatedById`) are set on every save for entities that implement the audit interfaces.
- **Soft delete** query filters apply to entities that implement `IDeleteAudit`. The framework has no suspension filter. Model a suspended user or account as your own state.
- **Domain events** from `IDomainEventEmitter` entities dispatch inside the save transaction, once `.AddDomainEvents()` is chained.
- **Multi-tenancy** query filters and the optional tenant write guard apply as for any `HeadlessDbContext`.
- **Unit of work.** When a unit of work is active on the context, buffered work such as outbox rows and jobs enlists in it. See [Unit of Work](unit-of-work.md).
- **`IDbContextFactory<TDbContext>`** is registered as a singleton by both registrations, so background services create contexts with no `AddDbContextFactory` call. A factory-created context gets its scoped services from a private DI scope, as [ORM scope binding](orm.md#registration-modes-and-scope-binding) describes.

You do not call `services.AddHeadlessDbContextServices()` yourself. Both registrations call it.

### Type parameter forms

| Base class | Use when |
| --- | --- |
| `HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>` | You need your own passkey entity type. |
| `HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>` | `IdentityUserPasskey<TKey>` is enough. |

The registration methods take `TDbContext` first, then the same type parameters as the base class you chose.

---

## Headless.Identity.Storage.EntityFramework

ASP.NET Core Identity storage on the Headless EF Core conventions.

### Setup

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

Define the context:

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : HeadlessIdentityDbContext<
        AppUser,
        AppRole,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>,
        IdentityUserPasskey<Guid>
    >(options)
{
    // Return null to use the database default schema, or a name to put Identity tables in that schema.
    public override string? DefaultSchema => "identity";
}
```

Register the context, the Identity stores, and the migrations:

```csharp
builder.Services.AddHeadlessDbContext<
    AppDbContext,
    AppUser,
    AppRole,
    Guid,
    IdentityUserClaim<Guid>,
    IdentityUserRole<Guid>,
    IdentityUserLogin<Guid>,
    IdentityRoleClaim<Guid>,
    IdentityUserToken<Guid>,
    IdentityUserPasskey<Guid>
>(options => options.UseNpgsql(connectionString));

builder.Services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddDbContextMigrations<AppDbContext>();
```

The shorter form omits `IdentityUserPasskey<Guid>` from both the base class and the registration:

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : HeadlessIdentityDbContext<
        AppUser, AppRole, Guid,
        IdentityUserClaim<Guid>, IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>
    >(options)
{
    public override string? DefaultSchema => null;
}

builder.Services.AddHeadlessDbContext<
    AppDbContext,
    AppUser, AppRole, Guid,
    IdentityUserClaim<Guid>, IdentityUserRole<Guid>,
    IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>,
    IdentityUserToken<Guid>
>(options => options.UseNpgsql(connectionString));
```

**Verify:** after `dotnet MyApp.dll resources provision`, `dotnet MyApp.dll resources verify -k EntityFramework` reports `Ok` for `AppDbContext`, and a test that resolves `UserManager<AppUser>` and calls `CreateAsync` gets `IdentityResult.Succeeded`. Unlike `Headless.EntityFramework`'s `AddHeadlessDbContext<TDbContext>()`, the Identity registrations add no `dbcontext-…` readiness health check, so readiness does not report this context.

### Configuration

`AddHeadlessDbContext` takes an optional `Action<HeadlessDbContextOptions>` as its second argument, for example to add a save-entry processor:

```csharp
builder.Services.AddHeadlessDbContext<AppDbContext, /* ... */>(
    options => options.UseNpgsql(connectionString),
    headlessOptions => headlessOptions.AddSaveEntryProcessor<MyCustomProcessor>(ServiceLifetime.Scoped)
);
```

| Argument | Default | Notes |
| --- | --- | --- |
| `optionsAction` | none | `Action<DbContextOptionsBuilder>` or `Action<IServiceProvider, DbContextOptionsBuilder>`. |
| `configureHeadlessOptions` | none | Configures `HeadlessDbContextOptions`. |
| `contextLifetime` (`AddHeadlessDbContext`) | `Scoped` | Must stay `Scoped`. `Singleton` and `Transient` throw `ArgumentException`. |
| `optionsLifetime` (`AddHeadlessDbContext`) | `Scoped` | `Singleton` builds the options once from the root provider and shares them across scopes. Pass it only when the options action reads no per-request or per-tenant state, because every scope gets the first build's values. |
| `poolSize` (`AddHeadlessDbContextPool`) | 1024 | Replaces the lifetimes. The pooled options are built once from the root provider, so the options callback must not resolve scoped services. Use `AddHeadlessDbContext` when it must. |

`IdentityOptions.Stores.SchemaVersion` defaults to `IdentitySchemaVersions.Version3`, which includes the `AspNetUserPasskeys` table that passkey flows need. To target an older schema, configure it after the registration:

```csharp
builder.Services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version1);
```

#### Tenant-owned Identity

`ConfigureTenantOwnedIdentity(ModelBuilder)` is a protected helper on both base classes. Call it after the base model call:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
	base.OnModelCreating(modelBuilder);
	ConfigureTenantOwnedIdentity(modelBuilder);
}
```

Enable the write guard separately:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
```

The helper configures your user, role, user claim, role claim, login, membership, token, and passkey types when the model is finalized. Each of them needs a mapped or shadow tenant string. A model that does not call the helper stays unchanged. Schema version 2 has no passkey table, and the helper does not add one.

| Model element | Tenant-owned behavior |
|---|---|
| Normalized user name and role name | Unique indexes add the tenant column, so equal names can exist in different tenants. |
| Email | The existing non-unique email index stays. `IdentityOptions.User.RequireUniqueEmail` validation uses tenant-filtered manager and store queries. |
| User and role | `Id` primary keys stay globally unique. Alternate keys `(TenantId, Id)` carry the relationships. |
| Claims, logins, memberships, tokens, and passkeys | Composite foreign keys of tenant plus user or role ID replace the original foreign keys. |
| Login, membership, token, and passkey primary keys | Their shapes stay. The external-login `(LoginProvider, ProviderKey)` pair and the passkey credential ID stay globally unique. |

Keep user and role IDs, external-login pairs, and passkey credential IDs globally unique. Tenant-scoped names do not make those keys reusable across tenants.

Composite foreign keys reject cross-tenant links, even through direct SQL. Existing delete behavior and supported navigation customizations stay. An ambiguous or incompatible relationship mapping fails model validation.

With the guard enabled, a missing tenant is captured before an entry becomes `Added`. An add under tenant A followed by a save under tenant B fails. Without the guard, set every tenant value before tracking, shadow values included. The tenant of an existing user or role cannot change, because it is part of an alternate key. A write-guard bypass does not allow reassignment.

| Failure | Exception |
| --- | --- |
| No ambient tenant, or a required tenant missing on an `Added` entry | `MissingTenantContextException` |
| A cross-tenant write, or an existing row whose original and current tenant differ | `CrossTenantWriteException` |
| The database matches zero rows for an update or delete | `DbUpdateConcurrencyException` |

The guard exceptions are thrown before local event handlers run. Required key values can fail during tracking. Generated updates and deletes include the original tenant beside Identity's own concurrency tokens. A `DbUpdateConcurrencyException` can come after local handlers have run. A failed save writes no outbox rows, but it cannot undo the effects of local or external handlers.

The read-filter bypass and the write-guard bypass are independent. Neither removes SQL concurrency predicates or database constraints. Bulk query operations apply query filters but skip the save guard. Raw SQL commands need explicit tenant predicates and authorization, and `BeginBypass()` has no effect on them.

#### Tenant key storage and rollout

New tenant columns default to 41 characters (`DomainConstants.IdMaxLength`). Within this opt-in, an otherwise unbounded string Identity key defaults to 128 characters. Compatible explicit lengths are kept and carried to dependent foreign-key columns. Every Identity tenant column must use one length. Conflicting lengths and explicit unbounded store types such as `text` or `nvarchar(max)` are rejected.

For a string-key model where `AppUser` derives from `IdentityUser<string>` and `AppRole` from `IdentityRole<string>`, put compatible overrides after the helper call:

```csharp
modelBuilder.Entity<AppUser>().Property(x => x.Id).HasMaxLength(96);
modelBuilder.Entity<AppRole>().Property(x => x.Id).HasMaxLength(96);
modelBuilder.Entity<AppUser>().Property<string>("TenantId").HasMaxLength(64);
```

The package does not check provider key-size limits. Confirm your lengths with the migration and representative writes. The upstream passkey primary key stays `varbinary(1024)`, but SQL Server rejects a 1,024-byte credential ID, because it exceeds the 900-byte key limit. Shorter credentials work.

You own tenant column types, conversions, collations, and validation in your EF model and migrations. The package chooses no collation, adds no tenant-ID check constraint, and does not reject trailing spaces. It stores IDs as given, without trimming or normalizing. The write guard compares IDs ordinally in memory, while queries, concurrency predicates, unique indexes, and foreign keys use database equality. Make sure distinct IDs stay distinct under that equality and that storage conversions keep them distinct.

Migrate existing rows in this order:

1. Add the new tenant columns as nullable.
2. Backfill every Identity row, dependents included, from verified ownership. Never assign a silent default tenant.
3. Validate tenant IDs, equality, lengths, and your ID format. Resolve duplicate normalized names within a tenant and parent-child tenant mismatches.
4. Apply required columns, your storage rules, the tenant alternate keys, the replacement composite foreign keys, and the tenant-scoped unique indexes. Review and run the migration on the target provider before you enable the model.

Tenant columns that entities already own through a tenancy interface also become concurrency tokens, even without the Identity opt-in. Their types, collations, and check constraints stay unchanged. No migration or backfill runs automatically.

### Design and runtime behavior

- `AddHeadlessDbContext` registers `TDbContext` scoped and bound to the resolving scope, and `IDbContextFactory<TDbContext>` as a singleton. A factory call opens a fresh DI scope that the returned context disposes with itself.
- `AddHeadlessDbContextPool` registers EF Core's pooled `IDbContextFactory<TDbContext>` and a scoped `TDbContext` leased from the pool. Disposing the scope returns the context to the pool. A factory-created pooled context opens a private DI scope only when it first needs a scoped service, and disposes it when the context returns to the pool.
- Both registrations attach DI-registered EF Core interceptors to the context options.
- Both registrations add fallbacks for `ICurrentTenant`, `ICurrentUser` (`NullCurrentUser`), `ICorrelationIdProvider`, and `TimeProvider` (`TimeProvider.System`) when the application registers none. With the `NullCurrentUser` fallback, `CreatedById` and `UpdatedById` stay `null`.
- Calling the registration more than once sets the Identity schema version once.
