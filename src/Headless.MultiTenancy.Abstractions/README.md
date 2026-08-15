# Headless.MultiTenancy.Abstractions

Defines the contract surface for the Headless multi-tenancy family: the ambient tenant accessor, the tenant write guard, the tenancy exception types, and the opt-in tenant catalog's store SPI and models.

## Problem Solved

Provides a storage- and host-independent contract surface for reading and scoping the ambient tenant identity, and for looking up tenant metadata by identifier or canonical id, so packages across the framework (EF Core, Jobs, Messaging, Api, Permissions, Settings, Features, ...) can depend on one shared set of tenant types without pulling in an implementation package. Splits into two halves: the tenant-context contracts (`ICurrentTenant` and friends — always relevant) and the tenant-catalog contracts (`ITenantStore` and friends — relevant only to hosts that opt in to catalog resolution; see the "Tenant Catalog" section of the multi-tenancy domain doc for setup and concepts).

## Key Features

- **Tenant context**:
    - `ICurrentTenant` — reads the ambient tenant id/name for the current async execution scope and scopes a temporary override via `Change(id, name)`
    - `ICurrentTenantAccessor` — low-level read/write slot for the ambient `TenantInformation`, intended for framework infrastructure (for example middleware that sets the tenant from a JWT claim)
    - `ITenantWriteGuardBypass` — tracks an operation-local bypass for intentional host or admin tenant-owned writes
    - `CrossTenantWriteException` — thrown when a tenant write guard detects a tenant-owned write that does not match the current tenant context
    - `MissingTenantContextException` — thrown when an operation requires an ambient tenant context but none is available
- **Tenant catalog** (opt-in; see `Headless.MultiTenancy`'s `Catalog(...)` for wiring):
    - `TenantInfo` — non-generic, non-sealed canonical tenant metadata: `Id` (canonical id), `Identifier` (public-facing, pre-normalized), `Name`, `IsEnabled`, `ExtraProperties` (read-along payload, never queried). Apps that need typed columns beyond `ExtraProperties` subclass it from their own `ITenantStore` implementation.
    - `ITenantStore` — read-only store SPI: `FindByIdentifierAsync(normalizedIdentifier)`, `FindByIdAsync(id)`. Deliberately minimal — normalization, shape validation, and caching are owned by the catalog service, never by the store. Implementing this over an app-owned tenant aggregate is a first-class path, not a fallback.
    - `ITenantDirectory` — optional enumeration capability (`GetAllAsync()`) a store implements alongside `ITenantStore`. All v1 stores implement it; the catalog service itself never calls it — it exists for app-owned fan-out (for example a cron job iterating every tenant).
    - `ICurrentTenantInfo` — reads catalog `TenantInfo` for the ambient tenant (`GetAsync()`). Resolves by the ambient id observed at each call — no per-scope memoization — and never throws for an absent tenant: returns `null` when no tenant context is ambient, no store is configured, or the id has no catalog row. Returns data even for a disabled tenant; rejecting a disabled tenant is a resolution-time concern only.
    - `TenantResolutionOutcome` / `TenantResolutionKind` — the closed outcome set produced by identifier-based resolution: `Resolved` (carries the `TenantInfo`), `Unknown`, `Disabled`, `Ignored`, `Invalid`.

## Installation

```bash
dotnet add package Headless.MultiTenancy.Abstractions
```

Most applications receive this package transitively through `Headless.Core` (which implements the tenant-context contracts) or through a seam package (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`, `Headless.MultiTenancy`). Add it directly only when authoring a package that needs these contracts without pulling in an implementation — for example a custom `ITenantStore` over an app-owned tenant aggregate.

## Quick Start

```csharp
public sealed class OrderService(ICurrentTenant currentTenant)
{
    public Order CreateOrder(CreateOrderRequest request)
    {
        return new Order { Id = Guid.NewGuid(), TenantId = currentTenant.Id, ... };
    }

    // Scope a temporary tenant override — for example inside a background job or an admin tool.
    public async Task RunAsAsync(string tenantId, Func<Task> work)
    {
        using (currentTenant.Change(tenantId))
        {
            await work();
        }
    }
}

// Implementing ITenantStore over an app-owned aggregate instead of a shipped store.
public sealed class AppTenantStore(AppDbContext db) : ITenantStore, ITenantDirectory
{
    public async Task<TenantInfo?> FindByIdentifierAsync(string normalizedIdentifier, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.NormalizedSlug == normalizedIdentifier, ct);

        return tenant is null ? null : new TenantInfo(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive);
    }

    public async Task<TenantInfo?> FindByIdAsync(string id, CancellationToken ct = default) => /* ... */;

    public async Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default) => /* ... */;
}
```

`ICurrentTenant` and `ICurrentTenantAccessor` are contracts only — this package registers nothing. `Headless.Core` supplies the default `AsyncLocal`-backed implementations (`CurrentTenant`, `AsyncLocalCurrentTenantAccessor`, `NullCurrentTenant`) and their DI registration. `ITenantStore`, `ITenantDirectory`, and `ICurrentTenantInfo` are also contracts only — `Headless.MultiTenancy`'s `Catalog(...)` builder wires the in-memory, configuration, or (via `Headless.MultiTenancy.Storage.EntityFramework`) EF Core implementation, plus the catalog service that normalizes and caches store reads.

## Configuration

None. This is an abstractions-only package.

## Dependencies

`Headless.Primitives` (for `TenantInformation` and `ExtraProperties`).

## Side Effects

None.
