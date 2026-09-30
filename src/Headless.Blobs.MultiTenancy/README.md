# Headless.Blobs.MultiTenancy

Tenant scoping for Headless blob storage: every location is rewritten with the ambient tenant before it reaches the provider.

## Why use this package

Blob paths carry no tenant column, so two tenants that both store `avatars/1.png` overwrite each other. This package adds `tenancy.Blobs(b => b.ScopeByTenant())`, which wraps every store registered through `AddHeadlessBlobs` so each tenant gets its own path prefix or container, and refuses any blob operation that runs without a tenant.

## Install

```bash
dotnet add package Headless.Blobs.MultiTenancy
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobsmultitenancy)
