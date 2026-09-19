# Headless.Identity.Storage.EntityFramework

Entity Framework Core integration for ASP.NET Core Identity with framework EF Core conventions.

## Why use this package

`IdentityDbContext<>` from `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is a plain DbContext with no awareness of the framework's save pipeline, auditing, soft delete, domain events, or multi-tenancy. This package provides `HeadlessIdentityDbContext<>` — a base class that combines both, so applications can use ASP.NET Core Identity alongside the full framework feature set without duplicate context registrations.

## Install

```bash
dotnet add package Headless.Identity.Storage.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Identity guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/identity.md#headlessidentitystorageentityframework)
