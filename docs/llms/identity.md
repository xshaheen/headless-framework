---
domain: Identity
packages: Identity.Storage.EntityFramework
---

# Identity

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Identity.Storage.EntityFramework](#headlessidentitystorageentityframework)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)

> ASP.NET Core Identity integration with framework EF Core conventions and interceptors.

## Quick Orientation

Single package: `Headless.Identity.Storage.EntityFramework`. Provides `HeadlessIdentityDbContext<>` — a base DbContext that combines ASP.NET Core Identity with the framework's EF Core extensions (auditing, soft delete, domain events, etc.).

Typical registration:

```csharp
builder.Services.AddHeadlessDbContext<
    AppDbContext,
    AppUser, AppRole, Guid,
    AppUserClaim, AppUserRole, AppUserLogin,
    AppRoleClaim, AppUserToken, AppUserPasskey
>(options => options.UseNpgsql(connectionString));
```

Requires `Headless.Orm.EntityFramework` to be available (transitive dependency).

## Agent Instructions

- Prefer inheriting from `HeadlessIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>` for .NET 10 passkey-aware stores. The 8-type-parameter form remains available and uses `IdentityUserPasskey<TKey>`.
- Register via `AddHeadlessDbContext<>()` — do NOT use `AddDbContext<>()` directly, as this bypasses framework interceptors and conventions.
- `AddHeadlessDbContext<>()` defaults `IdentityOptions.Stores.SchemaVersion` to `IdentitySchemaVersions.Version3`; use a later `Configure<IdentityOptions>()` only when a host intentionally targets an older schema.
- This package depends on `Headless.Orm.EntityFramework` — all ORM conventions (audit fields, soft delete, multi-tenancy filters) apply automatically.
- Default service lifetime is Scoped. Override via the options if needed.
- For Identity-only projects without the full framework, this package is NOT appropriate — use `Microsoft.AspNetCore.Identity.EntityFrameworkCore` directly instead.

---

# Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework extensions.

## Problem Solved

Provides a pre-configured Identity DbContext base class with framework-specific extensions, enabling seamless integration of ASP.NET Core Identity with the framework's EF Core conventions and interceptors.

## Key Features

- `HeadlessIdentityDbContext<>` - Base DbContext with Identity support
- Framework EF extensions pre-configured
- Support for custom user, role, claim, and passkey types
- Identity schema version 3 by default for passkey support
- Flexible service lifetime configuration

## Installation

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

## Quick Start

```csharp
public class AppDbContext(HeadlessDbContextServices services, DbContextOptions options)
    : HeadlessIdentityDbContext<
        AppUser, AppRole, Guid,
        AppUserClaim, AppUserRole,
        AppUserLogin, AppRoleClaim, AppUserToken,
        AppUserPasskey
    >(services, options)
{
    public override string? DefaultSchema => null;
}

// Registration
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessDbContext<
    AppDbContext,
    AppUser, AppRole, Guid,
    AppUserClaim, AppUserRole, AppUserLogin,
    AppRoleClaim, AppUserToken, AppUserPasskey
>(options => options.UseNpgsql(connectionString));
```

## Configuration

No additional configuration is required beyond DbContext options for greenfield applications. Registration configures `IdentityOptions.Stores.SchemaVersion` to `IdentitySchemaVersions.Version3` so passkey storage is enabled by default.

## Dependencies

- `Headless.Orm.EntityFramework`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

## Side Effects

- Registers DbContext with specified lifetime (default: Scoped)
- Adds framework EF extensions to DbContext options
- Configures Identity store schema version 3 by default
