# Headless.MultiTenancy.Storage.EntityFramework

Entity Framework Core storage implementation for the Headless tenant catalog (`ITenantStore` / `ITenantDirectory`).

## Why use this package

Provides an EF Core-backed `ITenantStore` using the consumer's own `DbContext`, with schema managed through EF migrations — a shipped, convenience-default schema. Apps with richer requirements can implement `ITenantStore` directly over their own aggregate instead.

## Install

```bash
dotnet add package Headless.MultiTenancy.Storage.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Multi-Tenancy guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/multi-tenancy.md#headlessmultitenancystorageentityframework)
