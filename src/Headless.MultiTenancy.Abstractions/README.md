# Headless.MultiTenancy.Abstractions

Defines the tenant-context contracts shared across the Headless multi-tenancy family: the ambient tenant accessor, the tenant write guard, and the tenancy exception types.

## Problem Solved

Provides a storage- and host-independent contract surface for reading and scoping the ambient tenant identity, so packages across the framework (EF Core, Jobs, Messaging, Api, Permissions, Settings, Features, ...) can depend on one shared set of tenant-context types without pulling in `Headless.Core`'s full implementation surface.

## Key Features

- `ICurrentTenant` — reads the ambient tenant id/name for the current async execution scope and scopes a temporary override via `Change(id, name)`
- `ICurrentTenantAccessor` — low-level read/write slot for the ambient `TenantInformation`, intended for framework infrastructure (for example middleware that sets the tenant from a JWT claim)
- `ITenantWriteGuardBypass` — tracks an operation-local bypass for intentional host or admin tenant-owned writes
- `CrossTenantWriteException` — thrown when a tenant write guard detects a tenant-owned write that does not match the current tenant context
- `MissingTenantContextException` — thrown when an operation requires an ambient tenant context but none is available

## Installation

```bash
dotnet add package Headless.MultiTenancy.Abstractions
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

`Headless.Primitives` (for `TenantInformation`).

## Side Effects

None.
