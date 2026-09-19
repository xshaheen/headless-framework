# Headless.MultiTenancy

Composition root for tenant resolution, context, validation, and the optional tenant catalog.

## Why use this package

Provides one composition surface for tenant posture across Headless packages while keeping each package in charge of its own behavior. It owns the root builder, shared manifest, and validator contracts, plus the opt-in tenant catalog: a family-owned service that normalizes tenant identifiers, caches read-through lookups, and canonicalizes identifier→id before ambient context is set. It does not itself resolve tenants over HTTP, enforce authorization, propagate messages, or guard EF writes — seam packages (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`) contribute their own fluent extensions on top of this builder, and `Headless.Api.Core` is what turns catalog resolution into an HTTP pipeline behavior.

## Install

```bash
dotnet add package Headless.MultiTenancy
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Multi-Tenancy guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/multi-tenancy.md#headlessmultitenancy)
