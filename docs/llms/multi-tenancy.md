---
domain: Multi-Tenancy
packages: Api, Core, Orm.EntityFramework, Permissions.Core
---

# Multi-Tenancy

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [HTTP Setup](#http-setup)
- [Tenant Semantics](#tenant-semantics)
- [EF Core Integration](#ef-core-integration)
- [Permissions and Caching](#permissions-and-caching)
- [Non-HTTP Execution Paths](#non-http-execution-paths)
    - [Background Jobs](#background-jobs)
    - [Message Consumers](#message-consumers)
    - [SignalR](#signalr)
- [Failure Modes to Watch](#failure-modes-to-watch)

> End-to-end tenant context setup for HTTP requests, EF Core global filters, permission caching, and non-HTTP execution paths.

## Quick Orientation

Headless multi-tenancy is built from four pieces:

- `ICurrentTenant` and `ICurrentTenantAccessor` live in the `Headless.Abstractions` namespace (implemented in `src/Headless.Core/Abstractions`) and hold the current tenant in an `AsyncLocal` scope.
- `Headless.Api` resolves tenant context for HTTP requests via `UseTenantResolution()`.
- `Headless.Orm.EntityFramework` reads `ICurrentTenant.Id` in global query filters for `IMultiTenant` entities.
- `Headless.Permissions.Core` scopes permission grant cache keys by tenant via `ScopedCache<PermissionGrantCacheItem>`.

For HTTP apps, the recommended setup is:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessApi();
builder.AddHeadlessMultiTenancy();

var app = builder.Build();

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();
```

`UseTenantResolution()` must run after `UseAuthentication()` and before `UseAuthorization()`.

`AddHeadlessApi()` also requires `Headless:StringEncryption` and `Headless:StringHash` to be configured.

## Agent Instructions

- Use `ICurrentTenant` for tenant-aware application logic; do not pass tenant ID around manually once the execution context is established.
- In HTTP apps, call `AddHeadlessMultiTenancy()` on the builder and `UseTenantResolution()` in the middleware pipeline.
- The default claim type is `tenant_id`. Override it with `AddHeadlessMultiTenancy(options => options.ClaimType = "...")` only when your identity system uses a different claim name.
- When no tenant claim is present, the middleware intentionally skips `Change(null)`. This preserves the distinction between "never set" and "explicitly null".
- For EF Core, inherit from `HeadlessDbContext` and let the built-in model processor apply tenant filters to `IMultiTenant` entities.
- Permission cache scoping depends on `ICurrentTenant.Id`. Host-level operations with no tenant use the shared `t:` scope by design.
- For background jobs and message consumers, set tenant explicitly with `using (currentTenant.Change(tenantId)) { ... }`.
- Do not assume HTTP middleware covers SignalR hubs, background jobs, or messaging consumers. Those execution paths need their own tenant resolution.

## HTTP Setup

`AddHeadlessApi()` and `AddHeadlessDbContextServices()` now register `CurrentTenant` by default, so `ICurrentTenant` behaves correctly once tenant scope is established. `AddHeadlessMultiTenancy()` is the opt-in helper that:

- Ensures `ICurrentTenant` resolves to `CurrentTenant`
- Registers `ICurrentTenantAccessor` if needed
- Configures `MultiTenancyOptions` for the HTTP middleware

`UseTenantResolution()` reads the authenticated principal and:

- Uses `tenant_id` by default
- Uses `MultiTenancyOptions.ClaimType` when configured
- Calls `currentTenant.Change(tenantId)` only when the principal is authenticated and the tenant claim is not blank
- Restores the previous tenant automatically when the request finishes

## Tenant Semantics

There are three useful tenant states:

1. `ICurrentTenantAccessor.Current == null`
   No tenant scope has ever been established in the current execution flow.
2. `ICurrentTenantAccessor.Current.TenantId == null`
   Host-level context was set explicitly with `Change(null)`.
3. `ICurrentTenantAccessor.Current.TenantId == "TENANT-123"`
   Tenant-scoped execution is active.

The HTTP middleware preserves state `1` when there is no tenant claim. It does not convert missing claims into explicit host context.

## EF Core Integration

`Headless.Orm.EntityFramework` applies tenant-aware global filters for `IMultiTenant` entities through `HeadlessEntityModelProcessor`. To participate:

- Inherit from `HeadlessDbContext`
- Call `base.OnModelCreating(modelBuilder)`
- Ensure your entity implements `IMultiTenant`

With tenant resolution active, queries automatically filter on `TenantId == ICurrentTenant.Id`.

## Permissions and Caching

`Headless.Permissions.Core` already scopes permission grant cache entries by tenant:

```csharp
() => $"t:{sp.GetRequiredService<ICurrentTenant>().Id}"
```

When no tenant is active, the cache scope is `t:`. This is expected host-level behavior. Once `ICurrentTenant.Id` is set, permission cache entries are isolated per tenant.

## Non-HTTP Execution Paths

### Background Jobs

Resolve tenant explicitly while iterating work:

```csharp
foreach (var tenantId in tenantIds)
{
    using (currentTenant.Change(tenantId))
    {
        await processor.RunAsync();
    }
}
```

### Message Consumers

If your transport carries tenant information on the envelope, read the typed `ConsumeContext.TenantId` and establish scope manually:

```csharp
var tenantId = context.TenantId;

using (currentTenant.Change(tenantId))
{
    await handler.HandleAsync(message, cancellationToken);
}
```

The `TenantId` envelope property is populated automatically from the canonical `headless-tenant-id` wire header (see `Headers.TenantId`). On the publish side, set `PublishOptions.TenantId` rather than writing the header directly — the publish pipeline rejects raw header writes via `InvalidOperationException`.

Automatic tenant propagation in consumer pipelines is intentionally out of scope.

### SignalR

SignalR hub invocations start new execution flows after the initial upgrade request. HTTP middleware does not preserve tenant context for later hub method calls. Use a hub-specific solution such as an `IHubFilter`.

## Failure Modes to Watch

- Missing `UseTenantResolution()` means HTTP requests stay at host scope even when the JWT contains a tenant claim.
- Registering middleware before `UseAuthentication()` means no authenticated principal is available yet.
- Forgetting `using` around `currentTenant.Change()` in non-HTTP code can leak tenant context within the current async flow.
- Assuming host-level cache scope `t:` is tenant-isolated is incorrect; it is intentionally shared.
