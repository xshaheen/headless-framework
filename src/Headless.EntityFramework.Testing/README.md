# Headless.EntityFramework.Testing

Tenant-isolation assertions for Headless EF Core contexts.

## Why use this package

A tenant-owned entity is isolated only when two things hold: the tenant query filter hides another tenant's rows, and the tenant write guard refuses a write to them. This package asserts both for any entity in your model: seed a row as tenant A, then prove tenant B reads nothing and its update and delete throw `CrossTenantWriteException`. It is kept out of `Headless.EntityFramework` so the production surface ships no test assertions.

## Install

```bash
dotnet add package Headless.EntityFramework.Testing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Testing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/testing.md#headlessentityframeworktesting)
