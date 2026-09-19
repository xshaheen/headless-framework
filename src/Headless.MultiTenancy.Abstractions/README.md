# Headless.MultiTenancy.Abstractions

Defines the contract surface for the Headless multi-tenancy family: the ambient tenant accessor, the tenant write guard, the tenancy exception types, and the opt-in tenant catalog's store SPI and models.

## Why use this package

Provides a storage- and host-independent contract surface for reading and scoping the ambient tenant identity, and for looking up tenant metadata by identifier or canonical id, so packages across the framework (EF Core, Jobs, Messaging, Api, Permissions, Settings, Features, ...) can depend on one shared set of tenant types without pulling in an implementation package. Splits into two halves: the tenant-context contracts (`ICurrentTenant` and friends — always relevant) and the tenant-catalog contracts (`ITenantStore` and friends — relevant only to hosts that opt in to catalog resolution; see the "Tenant Catalog" section of the multi-tenancy domain doc for setup and concepts).

## Install

```bash
dotnet add package Headless.MultiTenancy.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Multi-Tenancy guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/multi-tenancy.md#headlessmultitenancyabstractions)
