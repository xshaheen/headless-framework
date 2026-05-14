---
domain: Multi-Tenancy
packages: MultiTenancy, Api, Core, Mediator, Messaging.Core, Orm.EntityFramework, Permissions.Core
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

Headless multi-tenancy is built from these pieces:

- `Headless.MultiTenancy` provides the root `AddHeadlessTenancy(...)` composition surface and a shared, non-PII tenant posture manifest.
- `ICurrentTenant` and `ICurrentTenantAccessor` live in the `Headless.Abstractions` namespace (implemented in `src/Headless.Core/Abstractions`) and hold the current tenant in an `AsyncLocal` scope.
- `Headless.Api` resolves tenant context for HTTP requests via `UseHeadlessTenancy()` when HTTP tenancy is configured.
- `Headless.Mediator` enforces tenant presence at request dispatch boundaries via `.Mediator(mediator => mediator.RequireTenant())` or the lower-level `AddTenantRequiredBehavior()`.
- `Headless.Messaging.Core` propagates tenant context across message publish/consume and can require tenant context on publish.
- `Headless.Orm.EntityFramework` reads `ICurrentTenant.Id` in global query filters for `IMultiTenant` entities and can opt in to a save-time tenant write guard.
- `Headless.Permissions.Core` scopes permission grant cache keys by tenant via `ScopedCache<PermissionGrantCacheItem>`.

For tenant-aware hosts, the recommended setup is:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless();
builder.AddHeadlessTenancy(tenancy => tenancy
    .Http(http => http.ResolveFromClaims())
    .Mediator(mediator => mediator.RequireTenant())
    .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
    .EntityFramework(ef => ef.GuardTenantWrites()));

var app = builder.Build();

app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

`UseHeadlessTenancy()` must run after app-owned `UseAuthentication()` and before app-owned `UseAuthorization()`. Headless tenancy APIs do not call either middleware internally.

`AddHeadless()` registers base API infrastructure only. It does not enable tenant posture. It also requires `Headless:StringEncryption` and `Headless:StringHash` to be configured.

## Agent Instructions

- Use `ICurrentTenant` for tenant-aware application logic; do not pass tenant ID around manually once the execution context is established.
- In tenant-aware hosts, prefer `builder.AddHeadlessTenancy(...)` so HTTP, Mediator, Messaging, and EF posture is visible in one block.
- In HTTP apps, use `.Http(http => http.ResolveFromClaims())` and `app.UseHeadlessTenancy()` in the middleware pipeline.
- For Mediator request boundaries, use `.Mediator(mediator => mediator.RequireTenant())` or the lower-level `services.AddTenantRequiredBehavior()`, and mark only intentional host-level requests with `[AllowMissingTenant]`.
- The default claim type is `tenant_id`. Override it with `ResolveFromClaims(options => options.ClaimType = "...")` only when your identity system uses a different claim name.
- When no tenant claim is present, the middleware intentionally skips `Change(null)`. This preserves the distinction between "never set" and "explicitly null".
- For EF Core, inherit from `HeadlessDbContext` and let the built-in model processor apply tenant filters to `IMultiTenant` entities.
- Enable strict EF tenant writes with `.EntityFramework(ef => ef.GuardTenantWrites())` or the lower-level `services.AddHeadlessTenantWriteGuard()` when tenant-owned saves must fail without a matching tenant context.
- Use `ITenantWriteGuardBypass.BeginBypass()` only around intentional admin or host-level writes. `IgnoreMultiTenancyFilter()` affects reads only; it does not bypass guarded writes.
- Permission cache scoping depends on `ICurrentTenant.Id`. Host-level operations with no tenant use the shared `t:` scope by design.
- For background jobs and message consumers, set tenant explicitly with `using (currentTenant.Change(tenantId)) { ... }`.
- Do not assume HTTP middleware covers SignalR hubs, background jobs, or messaging consumers. Those execution paths need their own tenant resolution.

## HTTP Setup

`AddHeadless()` and `AddHeadlessDbContextServices()` register `CurrentTenant` by default, so `ICurrentTenant` behaves correctly once tenant scope is established. The primary HTTP setup path is:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy
    .Http(http => http.ResolveFromClaims(options =>
    {
        options.ClaimType = UserClaimTypes.TenantId; // default
    })));

app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

`.Http(http => http.ResolveFromClaims(...))` delegates to the API package and:

- Ensures `ICurrentTenant` resolves to `CurrentTenant`
- Registers `ICurrentTenantAccessor` if needed
- Configures `MultiTenancyOptions` for the HTTP middleware

`UseHeadlessTenancy()` reads the shared tenant posture manifest and applies HTTP tenant resolution only when HTTP tenancy was configured. It marks the middleware slot as applied so startup validation can fail fast when HTTP tenancy was configured but the middleware was omitted.

`UseTenantResolution()` remains as a lower-level compatibility API. It reads the authenticated principal and:

- Uses `tenant_id` by default
- Uses `MultiTenancyOptions.ClaimType` when configured
- Calls `currentTenant.Change(tenantId)` only when the principal is authenticated and the tenant claim is not blank
- Restores the previous tenant automatically when the request finishes

## HTTP Failure Mapping

`MissingTenantContextException` is the cross-layer guard exception raised when an operation requires a tenant but none is available — by the EF write guard (#234), the Mediator behavior (#236), the messaging publish guard (U10/#238), or any consumer code that calls into a tenant-required path. The framework maps it to a normalized 400 ProblemDetails through `HeadlessApiExceptionHandler` — a single `IExceptionHandler` auto-registered by `AddHeadlessProblemDetails()` (called by `AddHeadless()`). The same handler covers MVC actions, Minimal-API endpoints, middleware, hosted services, and SignalR hubs.

```csharp
builder.AddHeadless();
// AddHeadless() calls AddHeadlessProblemDetails() which auto-registers
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
- Handler-chain ordering matters: the tenancy handler is registered by `AddHeadlessProblemDetails()`, so it wins against any catch-all registered after that call. If a consumer needs their own catch-all to win, they must register it **before** `AddHeadlessProblemDetails()` (or before `AddHeadless()`, which calls it).

The same shape is reachable without going through the handler via `IProblemDetailsCreator.TenantRequired()` (parameterless) for direct callers — e.g., a request-pipeline pre-check that returns `Results.Problem(...)` without throwing.

## Mediator-Boundary Enforcement

`Headless.Mediator` provides tenant enforcement at the Mediator request boundary:

- Register through `.Mediator(mediator => mediator.RequireTenant())` on the root tenancy surface.
- Register a real `ICurrentTenant` separately; the package does not own tenant resolution.
- Requests require `ICurrentTenant.Id` to be non-blank by default.
- Mark intentional host-level, public, system, or console-bootstrap requests with `[AllowMissingTenant]`.
- Do not add runtime opt-out flags or handler-level policy checks; the marker attribute is the enrollment surface.

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy
    .Mediator(mediator => mediator.RequireTenant()));

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

For package-level wiring without the root tenancy surface, the lower-level registration remains available:

```csharp
builder.Services.AddTenantRequiredBehavior();
```

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

`Headless.Orm.EntityFramework` applies tenant-aware global filters for `IMultiTenant` entities through its model conventions. To participate:

- Inherit from `HeadlessDbContext`
- Call `base.OnModelCreating(modelBuilder)`
- Ensure your entity implements `IMultiTenant`

With tenant resolution active, queries automatically filter on `TenantId == ICurrentTenant.Id`. The filter is wired by `HeadlessDbContextRuntime._ConfigureQueryFilters` and registered under the constant `HeadlessQueryFilters.MultiTenancyFilter` (whose literal string value is `"MultiTenantFilter"`). Because `IQueryable<T>.ExecuteUpdate(...)` and `IQueryable<T>.ExecuteDelete(...)` consume the same `IQueryable<T>`, bulk update and bulk delete inherit the tenant predicate and are scoped to the current tenant by default. Per-query opt-out is `IgnoreMultiTenancyFilter()`, which audit-logs the bypass via `HeadlessQueryFilters._LogFilterBypassed`.

### EF Tenant Write Guard

The EF write guard is opt-in and disabled by default for compatibility. Enable it from the root tenancy surface:

```csharp
builder.Services.AddHeadlessDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.AddHeadlessTenancy(tenancy => tenancy
    .EntityFramework(ef => ef.GuardTenantWrites()));
```

For package-level wiring without the root tenancy surface, the lower-level registration remains available:

```csharp
builder.Services.AddHeadlessTenantWriteGuard();
```

When enabled, `SaveChanges()` and `SaveChangesAsync()` reject unsafe `IMultiTenant` writes before persistence, audit capture, and domain-message publishing:

- Added tenant-owned entities require a non-blank `ICurrentTenant.Id`. If `TenantId` is empty, the processor stamps the current tenant before saving.
- Added tenant-owned entities with a different explicit `TenantId` fail with `CrossTenantWriteException`.
- Modified, soft-deleted, and physically deleted tenant-owned entities must belong to the current tenant or fail with `CrossTenantWriteException`.
- Non-tenant entities are not blocked by the guard.

Missing tenant context uses the shared `Headless.Abstractions.MissingTenantContextException`, so HTTP hosts using `UseExceptionHandler()` get the existing normalized 400 mapping. Cross-tenant mutation uses `Headless.Abstractions.CrossTenantWriteException` (located in `Headless.Core` to keep the failure shared across packages without forcing an Api → EF project reference).

`HeadlessApiExceptionHandler` (registered by `AddHeadlessApi()`) maps `CrossTenantWriteException` to HTTP 409 Conflict with the `g:cross-tenant-write` error descriptor and emits a structured warning log (event name `CrossTenantWriteException`). No exception data is leaked into the response body — only the descriptor code and title.

`CrossTenantWriteException` is non-transient and must NOT be retried. Catch-all retry policies (for example `Policy.Handle<Exception>()`) should exclude it explicitly; retrying a cross-tenant write either fails identically or — if the ambient tenant context changes between attempts — persists the unsafe write.

For intentional admin or host-level maintenance writes, keep the bypass narrow:

```csharp
var bypass = serviceProvider.GetRequiredService<ITenantWriteGuardBypass>();

using (bypass.BeginBypass())
{
    await dbContext.SaveChangesAsync(cancellationToken);
}
```

`IgnoreMultiTenancyFilter()` is only a read-side query-filter bypass. Loading a row through `IgnoreMultiTenancyFilter()` does not permit cross-tenant updates or deletes when the write guard is enabled; wrap only the intended write in `ITenantWriteGuardBypass.BeginBypass()`.

### Defense Layers and Known Gaps

`IMultiTenant` writes are protected by two complementary layers, plus paths that remain out of scope:

1. **Global query filter** — always on for `IMultiTenant` entities. Registered as `HeadlessQueryFilters.MultiTenancyFilter` (string value `"MultiTenantFilter"`). Scopes reads, `IQueryable<T>.ExecuteUpdate(...)`, and `IQueryable<T>.ExecuteDelete(...)` to the current tenant. Opt-out is `IgnoreMultiTenancyFilter()` (audit-logged).
2. **`SaveChanges` write guard** — opt-in via `.EntityFramework(ef => ef.GuardTenantWrites())`. Operates on EF's `ChangeTracker`. Catches `Add` / `Update` / `Remove` / tracked-property-mutation paths and rejects unsafe writes with `CrossTenantWriteException` before persistence.

Known gaps:

- **Attach-then-modify.** An attacker-controlled `Attach` populates `OriginalValue` from caller-supplied state, so the in-memory guard's `OriginalValue == currentTenantId` check passes for a row that actually belongs to another tenant. The global query filter does not cover this path because the attacker never queries the row. A SQL-level concurrency-style `WHERE TenantId = @currentTenantId` predicate on the SaveChanges-generated UPDATE/DELETE is the planned follow-up, tracked in the security follow-up issue on the project tracker.
- **Raw SQL** (`DbContext.Database.ExecuteSql(...)`, `ExecuteSqlInterpolated(...)`, `ExecuteSqlRaw(...)`, stored procedures, triggers) is out of scope for both layers. Consumers calling raw SQL against `IMultiTenant` tables must include their own `WHERE TenantId = @currentTenantId` predicate or wrap the call in `ITenantWriteGuardBypass.BeginBypass()` under an authenticated, audited host context.

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

#### Automatic Propagation

For end-to-end propagation, opt in to the built-in filter pair:

```csharp
using Headless.Messaging.MultiTenancy;

builder.AddHeadlessTenancy(tenancy => tenancy
    .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish()));

builder.Services.AddHeadlessMessaging(options => { /* ... */ });
```

This registers `TenantPropagationPublishFilter` (stamps `PublishOptions.TenantId` from ambient `ICurrentTenant.Id` at publish time) and `TenantPropagationConsumeFilter` (calls `ICurrentTenant.Change(...)` on the resolved `ConsumeContext<T>.TenantId` for the lifetime of the consume — including both success and exception paths). Caller-set values on `PublishOptions.TenantId` are preserved verbatim; system messages can override propagation by setting `TenantId` explicitly or by publishing with no ambient tenant.

Tenant propagation is composed exclusively through the root tenancy seam — the previous `MessagingBuilder.AddTenantPropagation()` extension has been removed. The seam registration is idempotent and fails fast at startup when propagation is enabled with only the framework's `NullCurrentTenant` fallback registered.

**Trust boundary.** The consume filter trusts the inbound envelope. The framework assumes the message bus is internal-only; topics exposed to external producers must layer envelope validation or signing in front of this filter. Otherwise an attacker who can publish to the bus can impersonate any tenant.

#### Manual Propagation

If you need finer-grained control (or you opted out of `PropagateTenant()`), establish the scope manually inside your consumer:

```csharp
var tenantId = context.TenantId;

using (currentTenant.Change(tenantId))
{
    await handler.HandleAsync(message, cancellationToken);
}
```

#### Strict Publish Tenancy (`TenantContextRequired`)

Use `.RequireTenantOnPublish()` to require every publish to resolve a tenant identifier. When enabled, the publish wrapper checks `PublishOptions.TenantId` first, then falls back to the ambient `ICurrentTenant.Id`. If neither resolves a value, the publish fails with `Headless.Abstractions.MissingTenantContextException`. This is the messaging sibling of the EF write guard (#234) and the Mediator behavior (#236).

The lower-level equivalent is `MessagingOptions.TenantContextRequired = true`. Defaults to `false` to preserve today's behavior. The U2 raw-header integrity rules above (`ReservedTenantHeader`, `TenantIdMismatch`) always apply and run before the strict-tenancy fallback, so injection attempts cannot bypass the guard by enabling the flag.

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

Register a real `ICurrentTenant` (the default `AddHeadless()` / `AddHeadlessDbContextServices()` registration is sufficient) so the ambient fallback can resolve a value when option B is used.

### SignalR

SignalR hub invocations start new execution flows after the initial upgrade request. HTTP middleware does not preserve tenant context for later hub method calls. Use a hub-specific solution such as an `IHubFilter`.

## Testing host wiring

Integration tests that build the host (for example `WebApplicationFactory`) will execute the tenancy startup validator at host start. Tests that exercise HTTP tenancy must include `UseHeadlessTenancy()` in their pipeline so `HeadlessHttpTenancyValidator` sees the runtime marker — otherwise startup fails with `HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING`. Tests that need to skip validation entirely should not call `AddHeadlessTenancy(...)` at all, or should compose only the seams they exercise. The startup validator runs as an `IHostedLifecycleService.StartingAsync` step so it executes before any other hosted service's `StartAsync`.

## Failure Modes to Watch

- Missing `UseHeadlessTenancy()` means HTTP requests stay at host scope even when the JWT contains a tenant claim; startup validation fails when HTTP tenancy was configured through `AddHeadlessTenancy(...)`.
- Registering `UseHeadlessTenancy()` before `UseAuthentication()` means no authenticated principal is available yet.
- Forgetting `using` around `currentTenant.Change()` in non-HTTP code can leak tenant context within the current async flow.
- Assuming host-level cache scope `t:` is tenant-isolated is incorrect; it is intentionally shared.
- Assuming `IgnoreMultiTenancyFilter()` bypasses write protection is incorrect; it only affects reads.
