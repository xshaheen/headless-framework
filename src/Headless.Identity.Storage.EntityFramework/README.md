# Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework EF Core conventions.

## Problem Solved

`IdentityDbContext<>` from `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is a plain DbContext with no awareness of the framework's save pipeline, auditing, soft delete, domain events, or multi-tenancy. This package provides `HeadlessIdentityDbContext<>` — a base class that combines both, so applications can use ASP.NET Core Identity alongside the full framework feature set without duplicate context registrations.

## Key Features

- `HeadlessIdentityDbContext<TUser, TRole, TKey, ...>` — base DbContext that extends `IdentityDbContext<>` with the framework EF Core runtime
- 8-type-parameter form (passkey hard-wired to `IdentityUserPasskey<TKey>`) and 9-type-parameter form (explicit `TUserPasskey`) for .NET 10 passkey-aware stores
- `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, ...>()` registration extension — mirrors the plain `AddHeadlessDbContext` API with additional Identity type parameters
- Full framework save pipeline: audit, soft delete, domain events, multi-tenancy query filters
- Explicit `ConfigureTenantOwnedIdentity(ModelBuilder)` opt-in for tenant ownership without custom interfaces, tenant-scoped names, and database-enforced same-tenant relationships
- `IDbContextFactory<TDbContext>` registered automatically (singleton) for factory-created, scope-owning contexts
- `DefaultSchema` abstract member lets each derived context namespace all Identity tables under a custom schema
- `IdentityOptions.Stores.SchemaVersion` defaulted to `IdentitySchemaVersions.Version3` (passkey table support) — guarded by sentinel so multiple `AddHeadlessDbContext` calls are idempotent

## Design Notes

**Identity schema version default.** `AddHeadlessDbContext` configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` exactly once, guarded by `HeadlessIdentityDefaultsSentinel`. Version 3 is the modern Identity model that includes the `AspNetUserPasskeys` table required for WebAuthn/passkey flows. Greenfield applications get this without extra configuration; existing applications that must target version 1 override via `services.Configure<IdentityOptions>(...)` after registration.

**Not poolable.** `HeadlessIdentityDbContext` takes `HeadlessDbContextServices` as a constructor parameter (a scoped DI service). EF Core pooling resolves contexts through a single-`DbContextOptions` constructor and does not support this shape. Do not register with `AddDbContextPool` or `AddPooledDbContextFactory`.

**`IDbContextFactory<TDbContext>` scope ownership.** The factory registered by `AddHeadlessDbContext` is `HeadlessDbContextFactory<TDbContext>` — it creates a fresh DI scope per call and transfers ownership to the returned context, which disposes the scope alongside itself. This is the same implementation used by `Headless.EntityFramework` so behavior is at parity.

## Installation

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

## Quick Start

### Define the DbContext

```csharp
// 9-type-parameter form — recommended for .NET 10 passkey-aware stores
public class AppDbContext(HeadlessDbContextServices services, DbContextOptions<AppDbContext> options)
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
    >(services, options)
{
    // Return null to use the database default schema, or a string to namespace Identity tables.
    public override string? DefaultSchema => "identity";
}
```

### Register

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

### 8-type-parameter convenience form

```csharp
// Equivalent — TUserPasskey is implicitly IdentityUserPasskey<TKey>
public class AppDbContext(HeadlessDbContextServices services, DbContextOptions<AppDbContext> options)
    : HeadlessIdentityDbContext<
        AppUser, AppRole, Guid,
        IdentityUserClaim<Guid>, IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>
    >(services, options)
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

## Configuration

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

Service lifetimes default to `ServiceLifetime.Scoped` for both the context and its options. Override via the `contextLifetime` / `optionsLifetime` parameters when needed.

### Tenant-owned Identity

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
builder.Services.AddHeadlessTenantWriteGuard();

// Alternative when composing root tenancy:
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

### Tenant key storage and rollout

New tenant columns default to 41 characters (`DomainConstants.IdMaxLength`). Otherwise unbounded string Identity keys default to 128 characters within this opt-in. Compatible explicit lengths are preserved and propagated to dependent foreign-key columns. All Identity tenant columns must use one consistent length. Conflicting lengths and explicit unbounded store types such as `text` or `nvarchar(max)` are rejected.

For a string-key model where `AppUser` derives from `IdentityUser<string>` and `AppRole` from `IdentityRole<string>`, compatible overrides can follow the helper call:

```csharp
modelBuilder.Entity<AppUser>().Property(x => x.Id).HasMaxLength(96);
modelBuilder.Entity<AppRole>().Property(x => x.Id).HasMaxLength(96);
modelBuilder.Entity<AppUser>().Property<string>("TenantId").HasMaxLength(64);
```

SQL Server validates the 900-byte budget for primary, alternate, and foreign keys and rejects unsafe configured lengths instead of silently shrinking them. The upstream passkey primary-key declaration `varbinary(1024)` is retained. That declaration does not mean SQL Server accepts a 1,024-byte credential ID: such an insert exceeds its 900-byte key limit. Ordinary shorter credentials work.

Tenant columns use SQL Server `Latin1_General_100_BIN2` or PostgreSQL `C` collation. Only tenant collation changes; equality of other Identity key columns remains provider-defined. Trailing U+0020 in tenant IDs is rejected at EF read/write boundaries and by database checks. IDs are never trimmed or normalized.

Consumers own the schema migration and the tenant assignment for existing rows:

1. Add new tenant columns as nullable.
2. Backfill all Identity rows from verified ownership relationships, including dependents. Do not assign a silent default tenant.
3. Validate tenant identity, equality, lengths, and trailing spaces. Resolve duplicate normalized names within each tenant and parent-child tenant mismatches.
4. Apply required columns, canonical collations, checks, tenant alternate keys, replacement composite foreign keys, and tenant-scoped unique indexes. Review and execute the migration for the target provider before enabling the model.

Review existing interface-owned tenant columns too: they gain canonical collation, trailing-space checks, and tenant concurrency-token metadata even outside Identity opt-in. No migration or backfill runs automatically.

## Dependencies

- `Headless.EntityFramework`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

## Side Effects

- Calls `services.AddHeadlessDbContextServices()` — registers `HeadlessDbContextServices` (scoped), `IHeadlessSaveChangesPipeline`, `IHeadlessAuditPersistence`, `IAmbientDbTransactionAccessor`, `IAuditChangeCapture`, `ITenantWriteGuardBypass`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenantAccessor`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider`, and related singletons.
- Uses the core no-op transaction-coordination seam by default; install `Headless.EntityFramework.CommitCoordination` when Identity saves must enlist buffered work in commit coordination.
- Calls `services.AddDiRegisteredInterceptorsConfiguration<TDbContext>()` — registers `IDbContextOptionsConfiguration<TDbContext>` that attaches DI-registered interceptors to EF Core options.
- Registers `TDbContext` via `services.AddDbContext<TDbContext>(...)` with the specified lifetimes.
- Registers `IDbContextFactory<TDbContext>` as `HeadlessDbContextFactory<TDbContext>` (singleton, idempotent via `TryAddSingleton`).
- Configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` once (guarded by `HeadlessIdentityDefaultsSentinel`).
