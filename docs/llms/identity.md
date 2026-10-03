---
domain: Identity
packages: Identity.Storage.EntityFramework
---

# Identity

> ASP.NET Core Identity wired to the framework's EF Core save pipeline, auditing, and multi-tenancy conventions via a single base class.

## Orientation

Single package: `Headless.Identity.Storage.EntityFramework`. Provides `HeadlessIdentityDbContext<>` — a base DbContext that layers the framework's EF Core runtime (auditing, soft delete, domain events, multi-tenancy query filters, save-changes pipeline) on top of ASP.NET Core Identity's `IdentityDbContext<>`.

Register with `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, ...>()` (one instance per scope) or `services.AddHeadlessDbContextPool<TDbContext, TUser, TRole, TKey, ...>()` (pooled) from `SetupIdentityEntityFramework`. These are the same patterns as `Headless.EntityFramework`'s `AddHeadlessDbContext<TDbContext>()` and `AddHeadlessDbContextPool<TDbContext>()`, extended with Identity-specific type parameters. The underlying service surface wired is identical — save pipeline, audit persistence, tenant/user accessors, `IDbContextFactory<TDbContext>`, request-scope binding.

## Agent Rules

- The registration methods are `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>()` (or the 10-type-parameter form that includes `TUserPasskey`) and the pooled `services.AddHeadlessDbContextPool<...>()` with the same type-parameter forms. Choose between them as in [ORM registration modes](orm.md#registration-modes-and-scope-binding): pooled for high throughput, per-scope when the context constructor or options callback needs scoped services. There is **no** `AddHeadlessIdentity(...)` or `UseEntityFramework<...>()` API — those do not exist.
- Do NOT register via `AddDbContext<TDbContext>()` directly. That bypasses the Headless service registration, request-scope binding, DI-registered interceptors, and `IDbContextFactory<TDbContext>`.
- Inherit from the 9-type-parameter form (`HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>`) for .NET 10 passkey-aware stores. The 8-type-parameter form is a convenience that hard-wires `TUserPasskey = IdentityUserPasskey<TKey>`.
- Your `DbContext` subclass takes its `DbContextOptions<TDbContext>` and forwards it to `base(options)`; the base has no other constructor parameter. A pooled context declares exactly one public constructor (its options plus, optionally, singleton services) and keeps no per-request state in its own fields.
- `AddHeadlessDbContext` sets `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` once, guarded by a sentinel so multiple calls do not repeat it. If a host must target an older schema, add `services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version1)` **after** the `AddHeadlessDbContext` call (later `Configure` delegates win in the standard options pipeline).
- All ORM conventions from `Headless.EntityFramework` apply: audit columns, soft-delete query filters, domain-event dispatch on `SaveChanges`, multi-tenancy tenant-write guard, and the `DefaultSchema` hook. Override `DefaultSchema` to namespace all Identity tables under a custom schema (e.g., `"identity"`).
- Register Identity managers and stores separately through `services.AddIdentityCore<TUser>().AddRoles<TRole>().AddEntityFrameworkStores<TDbContext>()`. `AddHeadlessDbContext` registers the context, not `UserManager` or `RoleManager`.
- Opt in to tenant-owned Identity explicitly with `ConfigureTenantOwnedIdentity(modelBuilder)` after `base.OnModelCreating(modelBuilder)`. Use `.EntityFramework(ef => ef.GuardTenantWrites())` through `AddHeadlessTenancy(...)` for automatic tenant stamping before tracking. No tenancy interfaces are required.
- Use fresh DI scopes for contexts, stores, `UserManager`, and `RoleManager` when changing tenants. Filters do not protect an already tracked `FindAsync` match. Existing Identity ownership is immutable through tenant alternate keys, even under write-guard bypass.
- Preserve Identity primary-key shapes and global user/role IDs, external-login pairs, and passkey credential IDs. Tenant-scoped names do not make those global keys reusable across tenants.
- Ship consumer migrations and explicit tenant backfills before enabling tenant-owned Identity. Validate tenant IDs under the configured database equality, key lengths, duplicate tenant names, and same-tenant relationships.
- For Identity-only projects that do not use the full framework save pipeline, this package is NOT appropriate — use `Microsoft.AspNetCore.Identity.EntityFrameworkCore` directly.

## Core Concepts

### What this package adds over stock Identity + EF

`Microsoft.AspNetCore.Identity.EntityFrameworkCore` provides `IdentityDbContext<>` — a plain EF Core `DbContext` with Identity's tables. It has no awareness of auditing, tenancy, domain events, or the Headless save pipeline.

`HeadlessIdentityDbContext<>` derives from `IdentityDbContext<>` (so all stock Identity model/store conventions are inherited) and also implements `IHeadlessDbContext`. The framework runtime (`HeadlessDbContextRuntime`) is embedded in the constructor and takes over `SaveChanges`/`SaveChangesAsync`/`Dispose`/`DisposeAsync` to run the save-entry processor chain — the same pipeline that runs for `HeadlessDbContext`. This means:

- **Audit columns** (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`) are set automatically on every save for entities implementing the audit interfaces.
- **Soft delete** query filters are applied automatically for entities implementing `IHasDeletedAt`.
- **Domain events** are dispatched within the save transaction for entities implementing `IDomainEventEmitter`.
- **Multi-tenancy** query filters and the optional tenant-write guard apply the same way as any other `HeadlessDbContext`.
- **`IDbContextFactory<TDbContext>`** is registered as singleton by both registrations, so background services can create contexts without a separate `AddDbContextFactory` call. A factory-created context gets its scoped services from a private DI scope, as described in [ORM scope binding](orm.md#registration-modes-and-scope-binding).

### Type parameter forms

The 8-type-parameter form is the convenience form:

```csharp
HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>
// Internally uses TUserPasskey = IdentityUserPasskey<TKey>
```

The 9-type-parameter form (recommended for .NET 10 passkey-aware stores):

```csharp
HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>
```

Both require `DefaultSchema` to be overridden (abstract member) — return `null` to use the database's default schema, or a string literal to namespace all tables.

### Save pipeline and scope binding

The scoped save pipeline (processors, audit persistence, event dispatcher) is not a constructor argument. The context resolves it lazily from the DI scope bound to the current use: the resolving scope for a context resolved from DI, or a private scope for a context created through a factory. `AddHeadlessDbContext` and `AddHeadlessDbContextPool` wire everything needed — calling `services.AddHeadlessDbContextServices()` separately is not required.

---

## Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework EF Core conventions.

### API and behavior

- `HeadlessIdentityDbContext<TUser, TRole, TKey, ...>` — base DbContext that extends `IdentityDbContext<>` with the framework EF Core runtime
- 8-type-parameter form (passkey hard-wired to `IdentityUserPasskey<TKey>`) and 9-type-parameter form (explicit `TUserPasskey`) for .NET 10 passkey-aware stores
- `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, ...>()` and `services.AddHeadlessDbContextPool<TDbContext, TUser, TRole, TKey, ...>()` registration extensions — mirror the plain `AddHeadlessDbContext` / `AddHeadlessDbContextPool` APIs with additional Identity type parameters; the pooled form takes `Action<DbContextOptionsBuilder>` or `Action<IServiceProvider, DbContextOptionsBuilder>`, `configureHeadlessOptions`, and `poolSize` (default 1024)
- Full framework save pipeline: audit, soft delete, domain events, multi-tenancy query filters
- Explicit `ConfigureTenantOwnedIdentity(ModelBuilder)` opt-in for tenant ownership without custom interfaces, tenant-scoped names, and database-enforced same-tenant relationships
- `IDbContextFactory<TDbContext>` registered automatically (singleton) for factory-created contexts
- `DefaultSchema` abstract member lets each derived context namespace all Identity tables under a custom schema
- `IdentityOptions.Stores.SchemaVersion` defaulted to `IdentitySchemaVersions.Version3` (passkey table support) — guarded by sentinel so multiple `AddHeadlessDbContext` calls are idempotent

### Design constraints

**Identity schema version default.** `AddHeadlessDbContext` configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` exactly once, guarded by `HeadlessIdentityDefaultsSentinel`. Version 3 is the modern Identity model that includes the `AspNetUserPasskeys` table required for WebAuthn/passkey flows. Greenfield applications get this without extra configuration; existing applications that must target version 1 override via `services.Configure<IdentityOptions>(...)` after registration.

**Poolable.** `HeadlessIdentityDbContext` takes only its `DbContextOptions` and resolves its scoped collaborators from the bound scope, so `AddHeadlessDbContextPool` can pool it. The pooled options are built once from the root provider, so the options callback must not resolve scoped services; use `AddHeadlessDbContext` when it must.

**`IDbContextFactory<TDbContext>` scope ownership.** The factory registered by `AddHeadlessDbContext` is `HeadlessDbContextFactory<TDbContext>` — it creates a fresh DI scope per call and transfers ownership to the returned context, which disposes the scope alongside itself. The factory registered by `AddHeadlessDbContextPool` is EF's `PooledDbContextFactory<TDbContext>`; a context it creates opens a private DI scope only when it first needs a scoped collaborator and disposes it when the context returns to the pool. Both are the implementations `Headless.EntityFramework` uses, so behavior is at parity.

### Install

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

### Setup and use

#### Define the DbContext

```csharp
// 9-type-parameter form — recommended for .NET 10 passkey-aware stores
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
    // Return null to use the database default schema, or a string to namespace Identity tables.
    public override string? DefaultSchema => "identity";
}
```

#### Register

```csharp
// Registration — all 9 type parameters are required for the explicit-passkey form
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

// Wire ASP.NET Core Identity stores separately — AddHeadlessDbContext does not register UserManager/RoleManager
builder.Services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();
```

#### 8-type-parameter convenience form

```csharp
// Equivalent — TUserPasskey is implicitly IdentityUserPasskey<TKey>
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

### Configuration

`AddHeadlessDbContext` accepts an optional `Action<HeadlessDbContextOptions>` as a second parameter to configure the save-entry processor chain:

```csharp
builder.Services.AddHeadlessDbContext<AppDbContext, /* ... */>(
    options => options.UseNpgsql(connectionString),
    headlessOptions => headlessOptions.AddSaveEntryProcessor<MyCustomProcessor>(ServiceLifetime.Scoped)
);
```

`IdentityOptions.Stores.SchemaVersion` defaults to `IdentitySchemaVersions.Version3`. To target an older schema:

```csharp
builder.Services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version1);
```

For `AddHeadlessDbContext`, service lifetimes default to `ServiceLifetime.Scoped` for both the context and its options. Override via the `contextLifetime` / `optionsLifetime` parameters when needed; `contextLifetime` may not be `Singleton`. `AddHeadlessDbContextPool` takes the same options and `configureHeadlessOptions` arguments plus `poolSize` (default 1024) instead of lifetimes.

#### Tenant-owned Identity

The protected `ConfigureTenantOwnedIdentity(ModelBuilder)` helper is defined on the nine-type-parameter base and inherited by the convenience form. Call it in your context after the base model call:

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

The helper configures the actual generic user, role, user claim, role claim, login, membership, token, and present passkey types at model finalization. Every included type requires a mapped or shadow tenant string. Ordinary Identity models remain unchanged unless they opt in. Schema version 2 excludes passkeys; the helper does not add them back.

| Model element | Tenant-owned behavior |
|---|---|
| Normalized username and role name | Unique indexes append the tenant column, so equal names can exist in different tenants |
| Email | Existing non-unique email index remains; configured `IdentityOptions.User.RequireUniqueEmail` validation uses tenant-filtered manager/store queries |
| User and role | Existing `Id` primary keys remain globally unique; alternate keys `(TenantId, Id)` support relationships |
| Claims, logins, memberships, tokens, and present passkeys | Composite foreign keys include tenant plus the user or role ID, replacing the corresponding original foreign keys |
| Login, membership, token, and passkey primary keys | Existing shapes remain; the external-login `(LoginProvider, ProviderKey)` pair and passkey credential ID remain globally unique |

Composite foreign keys reject cross-tenant links even through direct SQL. Existing delete behavior and supported navigation customizations are preserved; ambiguous or incompatible relationship mappings fail model validation.

With the guard enabled, missing tenants are captured before entries become Added. An add under A followed by a save under B fails. Without the guard, supply every tenant value before tracking, including shadow values. Existing user and role tenant ownership is immutable because it participates in alternate keys; write-guard bypass does not permit reassignment.

Use fresh DI scopes for the context, stores, `UserManager`, and `RoleManager` across tenant changes. `TenantId` reads the active ambient tenant dynamically, but tracked entities remain in the context. `FindAsync` can return a tracked match without executing the tenant filter.

Missing ambient context or a missing required tenant on an Added entry throws `MissingTenantContextException`. Cross-tenant writes and existing-row original/current mismatches throw `CrossTenantWriteException`. These guard failures precede local handler dispatch; required key values can fail during tracking. Generated updates and deletes include the original tenant alongside Identity's existing concurrency tokens. SQL zero-row results remain `DbUpdateConcurrencyException` at the context boundary and may occur after local handlers run. A failed save prevents durable outbox persistence, but cannot undo local or external handler effects.

Read-filter bypass and write-guard bypass are independent. Neither removes SQL concurrency predicates or database constraints. Bulk query operations consume query filters but skip the save guard. Raw SQL commands require explicit tenant predicates and authorization; `BeginBypass()` has no effect on them.

#### Tenant key storage and rollout

New tenant columns default to 41 characters (`DomainConstants.IdMaxLength`). Otherwise unbounded string Identity keys default to 128 characters within this opt-in. Compatible explicit lengths are preserved and propagated to dependent foreign-key columns. All Identity tenant columns must use one consistent length. Conflicting lengths and explicit unbounded store types such as `text` or `nvarchar(max)` are rejected.

For a string-key model where `AppUser` derives from `IdentityUser<string>` and `AppRole` from `IdentityRole<string>`, compatible overrides can follow the helper call:

```csharp
modelBuilder.Entity<AppUser>().Property(x => x.Id).HasMaxLength(96);
modelBuilder.Entity<AppRole>().Property(x => x.Id).HasMaxLength(96);
modelBuilder.Entity<AppUser>().Property<string>("TenantId").HasMaxLength(64);
```

Headless does not estimate provider-specific key-size budgets. The EF provider and database enforce physical storage and index limits; consumers must verify configured lengths through migrations and representative writes. The upstream passkey primary-key declaration `varbinary(1024)` is retained. That declaration does not mean SQL Server accepts a 1,024-byte credential ID: such an insert exceeds its 900-byte key limit. Ordinary shorter credentials work.

Consumers configure tenant column types, conversions, collations, and database validation in their own EF model and migrations. Headless does not choose a collation, add tenant-ID check constraints, or reject trailing spaces. It preserves supplied IDs without trimming or normalization. The write guard compares IDs ordinally in memory; queries, concurrency predicates, unique indexes, and foreign keys use database equality. Consumers must ensure that distinct canonical IDs remain distinct under that equality and that storage conversions preserve identity.

Consumers own the schema migration and the tenant assignment for existing rows:

1. Add new tenant columns as nullable.
2. Backfill all Identity rows from verified ownership relationships, including dependents. Do not assign a silent default tenant.
3. Validate tenant identity, equality, lengths, and the application's ID format. Resolve duplicate normalized names within each tenant and parent-child tenant mismatches.
4. Apply required columns, consumer-configured storage rules, tenant alternate keys, replacement composite foreign keys, and tenant-scoped unique indexes. Review and execute the migration for the target provider before enabling the model.

Existing interface-owned tenant columns gain tenant concurrency-token metadata even outside Identity opt-in, without forced changes to their types, collations, or check constraints. No migration or backfill runs automatically.

### Runtime behavior

- Calls `services.AddHeadlessDbContextServices()` — registers `IHeadlessSaveChangesPipeline`, `IHeadlessAuditPersistence`, `IAmbientDbTransactionAccessor`, `IAuditChangeCapture`, `ITenantWriteGuardBypass`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenantAccessor`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider`, and related singletons.
- Identity saves route through the same `Headless.EntityFramework` save pipeline as any other `HeadlessDbContext`; when a unit of work is active on the context (or the scope), buffered work (outbox rows, jobs) enlists automatically — see [Unit of Work](unit-of-work.md). No opt-in adapter is needed.
- Calls `services.AddDiRegisteredInterceptorsConfiguration<TDbContext>()` — registers `IDbContextOptionsConfiguration<TDbContext>` that attaches DI-registered interceptors to EF Core options.
- `AddHeadlessDbContext` registers `TDbContext` via `services.AddDbContext<TDbContext>(...)` with the specified lifetimes, bound to the resolving scope, and `IDbContextFactory<TDbContext>` as `HeadlessDbContextFactory<TDbContext>` (singleton, idempotent via `TryAddSingleton`).
- `AddHeadlessDbContextPool` registers EF's pooled `IDbContextFactory<TDbContext>` via `AddPooledDbContextFactory<TDbContext>(...)` and a scoped `TDbContext` leased from that pool and bound to the resolving scope; disposing the scope returns the context to the pool.
- Configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` once (guarded by `HeadlessIdentityDefaultsSentinel`).
