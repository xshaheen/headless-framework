# Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework EF Core conventions.

## Problem Solved

`IdentityDbContext<>` from `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is a plain DbContext with no awareness of the framework's save pipeline, auditing, soft delete, domain events, or multi-tenancy. This package provides `HeadlessIdentityDbContext<>` — a base class that combines both, so applications can use ASP.NET Core Identity alongside the full framework feature set without duplicate context registrations.

## Key Features

- `HeadlessIdentityDbContext<TUser, TRole, TKey, ...>` — base DbContext that extends `IdentityDbContext<>` with the framework EF Core runtime
- 8-type-parameter form (passkey hard-wired to `IdentityUserPasskey<TKey>`) and 9-type-parameter form (explicit `TUserPasskey`) for .NET 10 passkey-aware stores
- `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, ...>()` registration extension — mirrors the plain `AddHeadlessDbContext` API with additional Identity type parameters
- Full framework save pipeline: audit, soft delete, domain events, multi-tenancy query filters
- `IDbContextFactory<TDbContext>` registered automatically (singleton) for factory-created, scope-owning contexts
- `DefaultSchema` abstract member lets each derived context namespace all Identity tables under a custom schema
- `IdentityOptions.Stores.SchemaVersion` defaulted to `IdentitySchemaVersions.Version3` (passkey table support) — guarded by sentinel so multiple `AddHeadlessDbContext` calls are idempotent

## Design Notes

**Identity schema version default.** `AddHeadlessDbContext` configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` exactly once, guarded by `HeadlessIdentityDefaultsSentinel`. Version 3 is the modern Identity model that includes the `AspNetUserPasskeys` table required for WebAuthn/passkey flows. Greenfield applications get this without extra configuration; existing applications that must target version 1 override via `services.Configure<IdentityOptions>(...)` after registration.

**Not poolable.** `HeadlessIdentityDbContext` takes `HeadlessDbContextServices` as a constructor parameter (a scoped DI service). EF Core pooling resolves contexts through a single-`DbContextOptions` constructor and does not support this shape. Do not register with `AddDbContextPool` or `AddPooledDbContextFactory`.

**`IDbContextFactory<TDbContext>` scope ownership.** The factory registered by `AddHeadlessDbContext` is `HeadlessDbContextFactory<TDbContext>` — it creates a fresh DI scope per call and transfers ownership to the returned context, which disposes the scope alongside itself. This is the same implementation used by `Headless.Orm.EntityFramework` so behavior is at parity.

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

## Dependencies

- `Headless.Orm.EntityFramework`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

## Side Effects

- Calls `services.AddHeadlessDbContextServices()` — registers `HeadlessDbContextServices` (scoped), `IHeadlessSaveChangesPipeline`, `IHeadlessAuditPersistence`, `IAmbientDbTransactionAccessor`, `IAuditChangeCapture`, `ITenantWriteGuardBypass`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenantAccessor`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider`, and related singletons.
- Calls `services.AddEntityFrameworkCommitCoordination()` (commit-coordination interceptor registered once).
- Calls `services.AddDiRegisteredInterceptorsConfiguration<TDbContext>()` — registers `IDbContextOptionsConfiguration<TDbContext>` that attaches DI-registered interceptors to EF Core options.
- Registers `TDbContext` via `services.AddDbContext<TDbContext>(...)` with the specified lifetimes.
- Registers `IDbContextFactory<TDbContext>` as `HeadlessDbContextFactory<TDbContext>` (singleton, idempotent via `TryAddSingleton`).
- Configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` once (guarded by `HeadlessIdentityDefaultsSentinel`).
