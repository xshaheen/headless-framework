# Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework extensions.

## Problem Solved

Provides a pre-configured Identity DbContext base class with framework-specific extensions, enabling seamless integration of ASP.NET Core Identity with the framework's EF Core conventions and interceptors.

## Key Features

- `HeadlessIdentityDbContext<>` - Base DbContext with Identity support
- Framework EF extensions pre-configured
- Support for custom user, role, and claim types
- Flexible service lifetime configuration

## Installation

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

## Quick Start

```csharp
public class AppDbContext : HeadlessIdentityDbContext<
    AppUser, AppRole, Guid,
    AppUserClaim, AppUserRole,
    AppUserLogin, AppRoleClaim, AppUserToken
>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}

// Registration
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessDbContext<
    AppDbContext,
    AppUser, AppRole, Guid,
    AppUserClaim, AppUserRole, AppUserLogin,
    AppRoleClaim, AppUserToken
>(options => options.UseNpgsql(connectionString));
```

## Configuration

No additional configuration required beyond DbContext options.

## Dependencies

- `Headless.Orm.EntityFramework`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

## Side Effects

- Registers DbContext with specified lifetime (default: Scoped)
- Adds framework EF extensions to DbContext options
