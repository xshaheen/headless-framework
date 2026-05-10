---
domain: Multi-Tenancy
packages: Api, Core, Mediator, Orm.EntityFramework, Permissions.Core
---

# Multi-Tenancy

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [HTTP Setup](#http-setup)
- [HTTP Failure Mapping](#http-failure-mapping)
- [Mediator-Boundary Enforcement](#mediator-boundary-enforcement)
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

Headless multi-tenancy is built from five pieces:

- `ICurrentTenant` and `ICurrentTenantAccessor` live in the `Headless.Abstractions` namespace (implemented in `src/Headless.Core/Abstractions`) and hold the current tenant in an `AsyncLocal` scope.
- `Headless.Api` resolves tenant context for HTTP requests via `UseTenantResolution()`.
- `Headless.Mediator` enforces tenant presence at request dispatch boundaries via `AddTenantRequiredBehavior()`.
- `Headless.Orm.EntityFramework` reads `ICurrentTenant.Id` in global query filters for `IMultiTenant` entities.
- `Headless.Permissions.Core` scopes permission grant cache keys by tenant via `ScopedCache<PermissionGrantCacheItem>`.

For HTTP apps, the recommended setup is:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessInfrastructure();
builder.AddHeadlessMultiTenancy();

var app = builder.Build();

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();
```

`UseTenantResolution()` must run after `UseAuthentication()` and before `UseAuthorization()`.

`AddHeadlessInfrastructure()` also requires `Headless:StringEncryption` and `Headless:StringHash` to be configured.

## Agent Instructions

- Use `ICurrentTenant` for tenant-aware application logic; do not pass tenant ID around manually once the execution context is established.
- In HTTP apps, call `AddHeadlessMultiTenancy()` on the builder and `UseTenantResolution()` in the middleware pipeline.
- For Mediator request boundaries, call `services.AddTenantRequiredBehavior()` and mark only intentional host-level requests with `[AllowMissingTenant]`.
- The default claim type is `tenant_id`. Override it with `AddHeadlessMultiTenancy(options => options.ClaimType = "...")` only when your identity system uses a different claim name.
- When no tenant claim is present, the middleware intentionally skips `Change(null)`. This preserves the distinction between "never set" and "explicitly null".
- For EF Core, inherit from `HeadlessDbContext` and let the built-in model processor apply tenant filters to `IMultiTenant` entities.
- Permission cache scoping depends on `ICurrentTenant.Id`. Host-level operations with no tenant use the shared `t:` scope by design.
- For background jobs and message consumers, set tenant explicitly with `using (currentTenant.Change(tenantId)) { ... }`.
- Do not assume HTTP middleware covers SignalR hubs, background jobs, or messaging consumers. Those execution paths need their own tenant resolution.

## HTTP Setup

`AddHeadlessInfrastructure()` and `AddHeadlessDbContextServices()` now register `CurrentTenant` by default, so `ICurrentTenant` behaves correctly once tenant scope is established. `AddHeadlessMultiTenancy()` is the opt-in helper that:

- Ensures `ICurrentTenant` resolves to `CurrentTenant`
- Registers `ICurrentTenantAccessor` if needed
- Configures `MultiTenancyOptions` for the HTTP middleware

`UseTenantResolution()` reads the authenticated principal and:

- Uses `tenant_id` by default
- Uses `MultiTenancyOptions.ClaimType` when configured
- Calls `currentTenant.Change(tenantId)` only when the principal is authenticated and the tenant claim is not blank
- Restores the previous tenant automatically when the request finishes

## HTTP Failure Mapping

`MissingTenantContextException` is the cross-layer guard exception raised when an operation requires a tenant but none is available — by the EF write guard (#234), the Mediator behavior (#236), the messaging publish guard (U10/#238), or any consumer code that calls into a tenant-required path. The framework maps it to a normalized 400 ProblemDetails through `HeadlessApiExceptionHandler` — a single `IExceptionHandler` auto-registered by `AddHeadlessProblemDetails()` (called by `AddHeadlessInfrastructure()`). The same handler covers MVC actions, Minimal-API endpoints, middleware, hosted services, and SignalR hubs.

```csharp
builder.AddHeadlessInfrastructure();
// AddHeadlessInfrastructure() calls AddHeadlessProblemDetails() which auto-registers
// HeadlessApiExceptionHandler. No opt-in needed.

var app = builder.Build();
app.UseExceptionHandler();
```

Resulting response shape (same for both surfaces):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "bad-request",
  "status": 400,
  "detail": "An operation required an ambient tenant context but none was set.",
  "error": {
    "code": "g:tenant-required",
    "description": "An operation required an ambient tenant context but none was set."
  },
  "traceId": "...",
  "buildNumber": "...",
  "commitNumber": "...",
  "instance": "/path",
  "timestamp": "..."
}
```

The body surfaces only `type`, `title`, `status`, `detail`, the optional `error` discriminator, plus the standard normalized extensions (`traceId`, `buildNumber`, `commitNumber`, `timestamp`, `instance`). The exception's `Message`, `Data`, and `InnerException` are NOT included in the response — they belong in server logs. External callers branch on the stable `error.code` value.

Prerequisites:

- Call `app.UseExceptionHandler()` yourself to wire the `IExceptionHandler` chain into the pipeline.
- Handler-chain ordering matters: the tenancy handler is registered by `AddHeadlessProblemDetails()`, so it wins against any catch-all registered after that call. If a consumer needs their own catch-all to win, they must register it **before** `AddHeadlessProblemDetails()` (or before `AddHeadlessInfrastructure()`, which calls it).

The same shape is reachable without going through the handler via `IProblemDetailsCreator.TenantRequired()` (parameterless) for direct callers — e.g., a request-pipeline pre-check that returns `Results.Problem(...)` without throwing.

## Mediator-Boundary Enforcement

`Headless.Mediator` provides tenant enforcement at the Mediator request boundary:

- Register with `services.AddTenantRequiredBehavior()`.
- Register a real `ICurrentTenant` separately; the package does not own tenant resolution.
- Requests require `ICurrentTenant.Id` to be non-blank by default.
- Mark intentional host-level, public, system, or console-bootstrap requests with `[AllowMissingTenant]`.
- Do not add runtime opt-out flags or handler-level policy checks; the marker attribute is the enrollment surface.

```csharp
builder.Services.AddTenantRequiredBehavior();

public sealed record CreateInvoice(Guid CustomerId) : IRequest<CreateInvoiceResponse>;

[AllowMissingTenant]
public sealed record RebuildSearchIndex : IRequest<RebuildSearchIndexResponse>;

public sealed record RebuildSearchIndexResponse(int DocumentCount);
```

When a non-opted-out request runs without a tenant, `TenantRequiredBehavior<TRequest, TResponse>` throws `MissingTenantContextException`. HTTP hosts that use `UseExceptionHandler()` get the same normalized 400 response documented in [HTTP Failure Mapping](#http-failure-mapping). Non-HTTP hosts should let the exception fail the dispatch or handle it at their process boundary.

Recommended Mediator pipeline order:

```text
Auth -> TenantRequired -> Idempotency
```

Ordering is consumer-owned and not framework-enforced. Register the tenant guard after the identity/auth behavior that establishes tenant context and before idempotency, caching, or side-effect behaviors that could persist host-scoped state.

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

The `TenantId` envelope property is populated automatically from the canonical `headless-tenant-id` wire header (see `Headers.TenantId`). On the publish side, set `PublishOptions.TenantId` rather than writing the header directly. The publish pipeline enforces a strict 4-case integrity policy: raw-only writes and writes that disagree with the typed property are rejected with `InvalidOperationException`; a raw write that matches the typed property is accepted as a no-op. Consume-side values are untrusted wire data — validate them before downstream use.

#### Automatic Propagation (`AddTenantPropagation`)

For end-to-end propagation, opt in to the built-in filter pair:

```csharp
using Headless.Messaging.MultiTenancy;

builder.Services.AddHeadlessMessaging(options =>
{
    // ...
})
.AddTenantPropagation();
```

This registers `TenantPropagationPublishFilter` (stamps `PublishOptions.TenantId` from ambient `ICurrentTenant.Id` at publish time) and `TenantPropagationConsumeFilter` (calls `ICurrentTenant.Change(...)` on the resolved `ConsumeContext<T>.TenantId` for the lifetime of the consume — including both success and exception paths). Caller-set values on `PublishOptions.TenantId` are preserved verbatim; system messages can override propagation by setting `TenantId` explicitly or by publishing with no ambient tenant.

The extension is idempotent — calling `AddTenantPropagation()` more than once does not double-register either filter. Without a real `ICurrentTenant` registered (the framework's `NullCurrentTenant` fallback always returns `Id = null`), the publish-side filter is a silent no-op.

**Trust boundary.** The consume filter trusts the inbound envelope. The framework assumes the message bus is internal-only; topics exposed to external producers must layer envelope validation or signing in front of this filter. Otherwise an attacker who can publish to the bus can impersonate any tenant.

#### Manual Propagation

If you need finer-grained control (or you opted out of `AddTenantPropagation`), establish the scope manually inside your consumer:

```csharp
var tenantId = context.TenantId;

using (currentTenant.Change(tenantId))
{
    await handler.HandleAsync(message, cancellationToken);
}
```

#### Strict Publish Tenancy (`TenantContextRequired`)

Set `MessagingOptions.TenantContextRequired = true` to require every publish to resolve a tenant identifier. When enabled, the publish wrapper checks `PublishOptions.TenantId` first, then falls back to the ambient `ICurrentTenant.Id`. If neither resolves a value, the publish fails with `Headless.Abstractions.MissingTenantContextException`. This is the messaging sibling of the EF write guard (#234) and the Mediator behavior (#236).

Defaults to `false` to preserve today's behavior. The U2 raw-header integrity rules above (`ReservedTenantHeader`, `TenantIdMismatch`) always apply and run before the strict-tenancy fallback, so injection attempts cannot bypass the guard by enabling the flag.

To remediate a `MissingTenantContextException` from a background worker or `IHostedService`:

```csharp
// Option A: explicit per-publish tenant
await publisher.PublishAsync(
    message,
    new PublishOptions { TenantId = tenantId },
    cancellationToken);

// Option B: ambient scope around the publish
using (currentTenant.Change(tenantId))
{
    await publisher.PublishAsync(message, cancellationToken);
}
```

Register a real `ICurrentTenant` (the default `AddHeadlessInfrastructure()` / `AddHeadlessDbContextServices()` registration is sufficient) so the ambient fallback can resolve a value when option B is used.

### SignalR

SignalR hub invocations start new execution flows after the initial upgrade request. HTTP middleware does not preserve tenant context for later hub method calls. Use a hub-specific solution such as an `IHubFilter`.

## Failure Modes to Watch

- Missing `UseTenantResolution()` means HTTP requests stay at host scope even when the JWT contains a tenant claim.
- Registering middleware before `UseAuthentication()` means no authenticated principal is available yet.
- Forgetting `using` around `currentTenant.Change()` in non-HTTP code can leak tenant context within the current async flow.
- Assuming host-level cache scope `t:` is tenant-isolated is incorrect; it is intentionally shared.
