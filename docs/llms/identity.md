---
domain: Identity
packages: Identity.Storage.EntityFramework
---

# Identity

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [What this package adds over stock Identity + EF](#what-this-package-adds-over-stock-identity-ef)
    - [Type parameter forms](#type-parameter-forms)
    - [Save pipeline and `HeadlessDbContextServices`](#save-pipeline-and-headlessdbcontextservices)
- [Headless.Identity.Storage.EntityFramework](#headlessidentitystorageentityframework)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)

> ASP.NET Core Identity wired to the framework's EF Core save pipeline, auditing, and multi-tenancy conventions via a single base class.

## Quick Orientation

Single package: `Headless.Identity.Storage.EntityFramework`. Provides `HeadlessIdentityDbContext<>` — a base DbContext that layers the framework's EF Core runtime (auditing, soft delete, domain events, multi-tenancy query filters, save-changes pipeline) on top of ASP.NET Core Identity's `IdentityDbContext<>`.

Register with `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, ...>()` from `SetupIdentityEntityFramework`. This is the exact same pattern as `Headless.EntityFramework`'s `AddHeadlessDbContext<TDbContext>()`, extended with Identity-specific type parameters. The underlying service surface wired is identical — `HeadlessDbContextServices`, save pipeline, audit persistence, tenant/user accessors, `IDbContextFactory<TDbContext>`.

## Agent Instructions

- The registration method is `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>()` (or the 10-type-parameter form that includes `TUserPasskey`). There is **no** `AddHeadlessIdentity(...)` or `UseEntityFramework<...>()` API — those do not exist.
- Do NOT register via `AddDbContext<TDbContext>()` directly. That bypasses `HeadlessDbContextServices` injection, the save-changes pipeline, DI-registered interceptors, and `IDbContextFactory<TDbContext>` — the context will instantiate without its required constructor argument and throw at resolution time.
- Inherit from the 9-type-parameter form (`HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>`) for .NET 10 passkey-aware stores. The 8-type-parameter form is a convenience that hard-wires `TUserPasskey = IdentityUserPasskey<TKey>`.
- Your `DbContext` subclass constructor must forward to `base(services, options)` — the `HeadlessDbContextServices` parameter is required and injected by the DI container. EF Core pooling (`AddDbContextPool`) is incompatible with this constructor shape and is explicitly documented as unsupported.
- `AddHeadlessDbContext` sets `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` once, guarded by a sentinel so multiple calls do not repeat it. If a host must target an older schema, add `services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version1)` **after** the `AddHeadlessDbContext` call (later `Configure` delegates win in the standard options pipeline).
- All ORM conventions from `Headless.EntityFramework` apply: audit columns, soft-delete query filters, domain-event dispatch on `SaveChanges`, multi-tenancy tenant-write guard, and the `DefaultSchema` hook. Override `DefaultSchema` to namespace all Identity tables under a custom schema (e.g., `"identity"`).
- Do NOT call `AddIdentityCore<TUser>().AddEntityFrameworkStores<TDbContext>()` separately — `AddHeadlessDbContext` does not register `UserManager`/`RoleManager`. Add those via the standard `services.AddIdentityCore<TUser>().AddRoles<TRole>().AddEntityFrameworkStores<TDbContext>()` chain after the `AddHeadlessDbContext` call. The context registration order does not matter; the stores attach to whichever `TDbContext` is registered with EF.
- For Identity-only projects that do not use the full framework save pipeline, this package is NOT appropriate — use `Microsoft.AspNetCore.Identity.EntityFrameworkCore` directly.

## Core Concepts

### What this package adds over stock Identity + EF

`Microsoft.AspNetCore.Identity.EntityFrameworkCore` provides `IdentityDbContext<>` — a plain EF Core `DbContext` with Identity's tables. It has no awareness of auditing, tenancy, domain events, or the Headless save pipeline.

`HeadlessIdentityDbContext<>` derives from `IdentityDbContext<>` (so all stock Identity model/store conventions are inherited) and also implements `IHeadlessDbContext`. The framework runtime (`HeadlessDbContextRuntime`) is embedded in the constructor and takes over `SaveChanges`/`SaveChangesAsync`/`Dispose`/`DisposeAsync` to run the save-entry processor chain — the same pipeline that runs for `HeadlessDbContext`. This means:

- **Audit columns** (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`) are set automatically on every save for entities implementing the audit interfaces.
- **Soft delete** query filters are applied automatically for entities implementing `IHasDeletedAt`.
- **Domain events** are dispatched within the save transaction for entities implementing `IDomainEventEmitter`.
- **Multi-tenancy** query filters and the optional tenant-write guard apply the same way as any other `HeadlessDbContext`.
- **`IDbContextFactory<TDbContext>`** is registered as singleton so background services can resolve factory-created, scope-owning contexts without a separate `AddDbContextFactory` call.

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

### Save pipeline and `HeadlessDbContextServices`

The `HeadlessDbContextServices` parameter in the constructor carries the scoped save pipeline (processors, audit persistence, event dispatcher). It is resolved from the DI container and must not be constructed manually. The `AddHeadlessDbContext` extension wires everything needed — calling `services.AddHeadlessDbContextServices()` separately is not required.

---

## Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework EF Core conventions.

### Problem Solved

`IdentityDbContext<>` from `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is a plain DbContext with no awareness of the framework's save pipeline, auditing, soft delete, domain events, or multi-tenancy. This package provides `HeadlessIdentityDbContext<>` — a base class that combines both, so applications can use ASP.NET Core Identity alongside the full framework feature set without duplicate context registrations.

### Key Features

- `HeadlessIdentityDbContext<TUser, TRole, TKey, ...>` — base DbContext that extends `IdentityDbContext<>` with the framework EF Core runtime
- 8-type-parameter form (passkey hard-wired to `IdentityUserPasskey<TKey>`) and 9-type-parameter form (explicit `TUserPasskey`) for .NET 10 passkey-aware stores
- `services.AddHeadlessDbContext<TDbContext, TUser, TRole, TKey, ...>()` registration extension — mirrors the plain `AddHeadlessDbContext` API with additional Identity type parameters
- Full framework save pipeline: audit, soft delete, domain events, multi-tenancy query filters
- `IDbContextFactory<TDbContext>` registered automatically (singleton) for factory-created, scope-owning contexts
- `DefaultSchema` abstract member lets each derived context namespace all Identity tables under a custom schema
- `IdentityOptions.Stores.SchemaVersion` defaulted to `IdentitySchemaVersions.Version3` (passkey table support) — guarded by sentinel so multiple `AddHeadlessDbContext` calls are idempotent

### Design Notes

**Identity schema version default.** `AddHeadlessDbContext` configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` exactly once, guarded by `HeadlessIdentityDefaultsSentinel`. Version 3 is the modern Identity model that includes the `AspNetUserPasskeys` table required for WebAuthn/passkey flows. Greenfield applications get this without extra configuration; existing applications that must target version 1 override via `services.Configure<IdentityOptions>(...)` after registration.

**Not poolable.** `HeadlessIdentityDbContext` takes `HeadlessDbContextServices` as a constructor parameter (a scoped DI service). EF Core pooling resolves contexts through a single-`DbContextOptions` constructor and does not support this shape. Do not register with `AddDbContextPool` or `AddPooledDbContextFactory`.

**`IDbContextFactory<TDbContext>` scope ownership.** The factory registered by `AddHeadlessDbContext` is `HeadlessDbContextFactory<TDbContext>` — it creates a fresh DI scope per call and transfers ownership to the returned context, which disposes the scope alongside itself. This is the same implementation used by `Headless.EntityFramework` so behavior is at parity.

### Installation

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

### Quick Start

#### Define the DbContext

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

Service lifetimes default to `ServiceLifetime.Scoped` for both the context and its options. Override via the `contextLifetime` / `optionsLifetime` parameters when needed.

### Dependencies

- `Headless.EntityFramework`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

### Side Effects

- Calls `services.AddHeadlessDbContextServices()` — registers `HeadlessDbContextServices` (scoped), `IHeadlessSaveChangesPipeline`, `IHeadlessAuditPersistence`, `IAmbientDbTransactionAccessor`, `IAuditChangeCapture`, `ITenantWriteGuardBypass`, `TimeProvider` (`TimeProvider.System`), `ICurrentTenantAccessor`, `ICurrentTenant`, `ICurrentUser`, `ICorrelationIdProvider`, and related singletons.
- Uses the core no-op transaction-coordination seam by default; install `Headless.EntityFramework.CommitCoordination` when Identity saves must enlist buffered work in commit coordination.
- Calls `services.AddDiRegisteredInterceptorsConfiguration<TDbContext>()` — registers `IDbContextOptionsConfiguration<TDbContext>` that attaches DI-registered interceptors to EF Core options.
- Registers `TDbContext` via `services.AddDbContext<TDbContext>(...)` with the specified lifetimes.
- Registers `IDbContextFactory<TDbContext>` as `HeadlessDbContextFactory<TDbContext>` (singleton, idempotent via `TryAddSingleton`).
- Configures `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` once (guarded by `HeadlessIdentityDefaultsSentinel`).
