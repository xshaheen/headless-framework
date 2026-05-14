# Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework extensions.

## Problem Solved

Provides a pre-configured Identity DbContext base class with framework-specific extensions, enabling seamless integration of ASP.NET Core Identity with the framework's EF Core conventions and interceptors.

## Key Features

- `HeadlessIdentityDbContext<>` - Base DbContext with Identity support
- Framework EF extensions pre-configured
- Support for custom user, role, claim, and passkey types
- ASP.NET Core Identity schema version 3 by default, enabling passkey tables for greenfield apps
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

`AddHeadlessDbContext<...>()` configures `IdentityOptions.Stores.SchemaVersion` to `IdentitySchemaVersions.Version3` by default so new applications get the modern Identity model, including passkey storage. If a host must target an older Identity schema, override it after registration with `Configure<IdentityOptions>()`.

## Dependencies

- `Headless.Orm.EntityFramework`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

## Side Effects

- Registers DbContext with specified lifetime (default: Scoped)
- Adds framework EF extensions to DbContext options
- Configures Identity store schema version 3 by default
