---
domain: Multi-Tenancy
packages: MultiTenancy, Api.Core, Api.ServiceDefaults, Core, Messaging.Core, EntityFramework, Permissions.Core
---

# Multi-Tenancy

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [HTTP Setup](#http-setup)
- [Skipping Tenant Resolution](#skipping-tenant-resolution)
- [HTTP Failure Mapping](#http-failure-mapping)
- [HTTP Authorization Requirement](#http-authorization-requirement)
- [Tenant Semantics](#tenant-semantics)
- [EF Core Integration](#ef-core-integration)
- [Permissions and Caching](#permissions-and-caching)
- [Non-HTTP Execution Paths](#non-http-execution-paths)
    - [Background Jobs](#background-jobs)
    - [Message Consumers](#message-consumers)
    - [SignalR](#signalr)
- [Failure Modes to Watch](#failure-modes-to-watch)
- [Headless.MultiTenancy](#headlessmultitenancy)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)

> End-to-end tenant context setup for HTTP requests, EF Core global filters, permission caching, and non-HTTP execution paths.

## Quick Orientation

Headless multi-tenancy is built from these pieces:

- `Headless.MultiTenancy` provides the root `AddHeadlessTenancy(...)` composition surface and a shared, non-PII tenant posture manifest.
- `ICurrentTenant` and `ICurrentTenantAccessor` live in the `Headless.Abstractions` namespace (implemented in `src/Headless.Core/Abstractions`) and hold the current tenant in an `AsyncLocal` scope.
- `Headless.Api.Core` resolves tenant context for HTTP requests via `UseHeadlessTenancy()` and can enforce tenant presence before endpoint execution through `.Authorization(auth => auth.RequireTenant())`.
- `Headless.Messaging.Core` propagates tenant context across message publish/consume and can require tenant context on publish.
- `Headless.EntityFramework` reads `ICurrentTenant.Id` in global query filters for `IMultiTenant` entities and can opt in to a save-time tenant write guard.
- `Headless.Permissions.Core` scopes permission grant cache keys by tenant via `ScopedCache<PermissionGrantCacheItem>`.

For tenant-aware hosts, the recommended setup is:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless();
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims())
        .Authorization(auth => auth.RequireTenant())
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
        .EntityFramework(ef => ef.GuardTenantWrites())
);

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
- In tenant-aware hosts, prefer `builder.AddHeadlessTenancy(...)` so HTTP, Authorization, Messaging, and EF posture is visible in one block.
- In HTTP apps, use `.Http(http => http.ResolveFromClaims())` and `app.UseHeadlessTenancy()` in the middleware pipeline.
- For HTTP request boundaries, use `.Authorization(auth => auth.RequireTenant())`, add `TenantRequirement` to the app's `FallbackPolicy` or `DefaultPolicy`, mark intentional host-level endpoints with `[AllowMissingTenant]` or `.AllowMissingTenant()`, and use `[RequireTenant]` / `.RequireTenant()` to opt back in under broader allow-missing metadata.
- Use `[SkipTenantResolution]` / `.SkipTenantResolution()` to opt an endpoint out of claim extraction entirely (not just authorization enforcement). The middleware skips `ICurrentTenant.Change(...)` — if no other resolver runs, `ICurrentTenant.IsAvailable` stays false. Apply when an endpoint is reached by principals that legitimately carry a tenant claim but must not have `ICurrentTenant` populated — for example, admin or cross-tenant endpoints where the claim would silently scope EF global filters to a single tenant. Combine with `.AllowMissingTenant()` when the endpoint also sits under a tenant-required policy.
- The default claim type is `tenant_id`. Override it with `ResolveFromClaims(options => options.ClaimType = "...")` only when your identity system uses a different claim name.
- Mint the tenant claim only on principals that are actually scoped to a tenant. Host-level, admin, service-account, or cross-tenant principal types should not carry the claim — `ICurrentTenant.IsAvailable` stays false for them by design.
- When no tenant claim is present, the middleware intentionally skips `Change(null)`. This preserves the distinction between "never set" and "explicitly null".
- For EF Core, inherit from `HeadlessDbContext` and let the built-in model processor apply tenant filters to `IMultiTenant` entities.
- Declare `IMultiTenant` on aggregates owned by exactly one tenant. Keep platform-level entities (cross-tenant infrastructure, audit/outbox rows, shared catalogs, materialized cross-tenant projections) outside the filter. See [Entity Ownership](#entity-ownership).
- When using `IgnoreMultiTenancyFilter()`, add an inline `// MULTI-TENANCY-BYPASS: <reason>` comment naming the approved scenario (cross-tenant snapshot, admin lookup, system maintenance, etc.) so reviewers and post-incident readers can distinguish legitimate bypasses from drift.
- Enable strict EF tenant writes with `.EntityFramework(ef => ef.GuardTenantWrites())` or the lower-level `services.AddHeadlessTenantWriteGuard()` when tenant-owned saves must fail without a matching tenant context.
- Use `ITenantWriteGuardBypass.BeginBypass()` only around intentional admin or host-level writes. `IgnoreMultiTenancyFilter()` affects reads only; it does not bypass guarded writes.
- Permission cache scoping depends on `ICurrentTenant.Id`. Host-level operations with no tenant use the shared `t:` scope by design.
- For background jobs and message consumers, set tenant explicitly with `using (currentTenant.Change(tenantId)) { ... }`.
- Do not assume HTTP middleware covers SignalR hubs, background jobs, or messaging consumers. Those execution paths need their own tenant resolution.

## HTTP Setup

`AddHeadless()` and `AddHeadlessDbContextServices()` register `CurrentTenant` by default, so `ICurrentTenant` behaves correctly once tenant scope is established. The primary HTTP setup path is:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http =>
            http.ResolveFromClaims(options =>
            {
                options.ClaimType = UserClaimTypes.TenantId; // default
            })
        )
        .Authorization(auth => auth.RequireTenant())
);

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantRequirement())
        .Build();
});

app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

`.Http(http => http.ResolveFromClaims(...))` delegates to the API package and:

- Ensures `ICurrentTenant` resolves to `CurrentTenant`
- Registers `ICurrentTenantAccessor` if needed
- Configures `MultiTenancyOptions` for the HTTP middleware

`.Authorization(auth => auth.RequireTenant())` delegates to the API package and:

- Registers `TenantRequirementHandler`
- Decorates ASP.NET Core's effective authorization result handler with a wrapper that only intercepts tenant failures
- Records an `Authorization` seam with the `require-tenant` capability
- Adds startup validation that fails fast when neither `DefaultPolicy` nor `FallbackPolicy` includes `TenantRequirement`

`UseHeadlessTenancy()` reads the shared tenant posture manifest and applies HTTP tenant resolution only when HTTP tenancy was configured. It marks the middleware slot as applied so startup validation can fail fast when HTTP tenancy was configured but the middleware was omitted.

`UseTenantResolution()` remains as a lower-level compatibility API. It reads the authenticated principal and:

- Uses `tenant_id` by default
- Uses `MultiTenancyOptions.ClaimType` when configured
- Calls `currentTenant.Change(tenantId)` only when the principal is authenticated and the tenant claim is not blank
- Restores the previous tenant automatically when the request finishes

## Skipping Tenant Resolution

Apply `[SkipTenantResolution]` or `.SkipTenantResolution()` to opt an endpoint, route group, or MVC controller/action out of HTTP claim extraction. `TenantResolutionMiddleware` still marks the request as processed (`HeadlessTenancyResolutionApplied`) but returns immediately without calling `ICurrentTenant.Change(...)` — if no other resolver runs, `ICurrentTenant.Id` stays unset and `IsAvailable` stays false for the entire request.

This marker is HTTP-layer only — Mediator tenant guards, EF write guards, and messaging publish guards still enforce `ICurrentTenant.Id`. A handler running under this marker that calls a tenant-required downstream service will still throw `MissingTenantContextException`.

```csharp
// Minimal API — single endpoint
app.MapGet("/health", () => Results.Ok()).SkipTenantResolution();

// Minimal API — route group
var publicGroup = app.MapGroup("/public").SkipTenantResolution();
publicGroup.MapGet("/status", () => Results.Ok());

// MVC — controller (applies to all actions)
[SkipTenantResolution]
[Route("admin")]
public sealed class AdminController : ControllerBase { ... }

// MVC — individual action
[Route("users")]
public sealed class UsersController : ControllerBase
{
    [HttpGet("me")]
    [SkipTenantResolution]
    public IActionResult Profile() => Ok();
}
```

When the endpoint also lives under a tenant-required authorization policy, compose `.SkipTenantResolution()` with `.AllowMissingTenant()` so the authorization requirement is satisfied:

```csharp
app.MapGet("/webhook", (ICurrentTenant t) => Results.Ok()).SkipTenantResolution().AllowMissingTenant();
```

**When to use** — prefer `[SkipTenantResolution]` over `[AllowMissingTenant]` when the endpoint must not even attempt claim extraction, for example:

- The authenticated principal type can never carry a tenant claim (service-account, monitoring agent, webhook receiver).
- Claim extraction itself has measurable overhead on a hot path and the request is guaranteed to not need tenant context.
- The endpoint is unauthenticated and you want to suppress the middleware ordering warning.
- The endpoint uses a non-claim tenant resolver (subdomain, path segment, webhook signature) and you want to prevent the claim-based middleware from overriding it.

For endpoints that may or may not carry a tenant claim depending on the caller, `[AllowMissingTenant]` is the right choice — it runs extraction and simply permits the authorization requirement to pass when no claim is found.

**Ordering requirement** — `UseHeadlessTenancy()` (or the lower-level `UseTenantResolution()`) must run after `UseRouting()` so endpoint metadata is available when the middleware checks for `[SkipTenantResolution]`. Without that ordering, `HttpContext.GetEndpoint()` returns `null` and the skip marker silently has no effect — claim extraction runs as if the marker were absent. The recommended `UseAuthentication() -> UseHeadlessTenancy() -> UseAuthorization()` pipeline already satisfies this when `WebApplication` auto-injects routing for you, but consumers calling `UseRouting()` explicitly must place it before `UseHeadlessTenancy()`.

## HTTP Failure Mapping

`MissingTenantContextException` is the cross-layer guard exception raised when an operation requires a tenant but none is available — by the EF write guard (#234), the messaging publish guard (U10/#238), or any consumer code that calls into a tenant-required path. The framework maps it to a normalized 403 ProblemDetails through `HeadlessApiExceptionHandler` — a single `IExceptionHandler` auto-registered by `AddHeadlessProblemDetails()` (called by `AddHeadless()`). The same handler covers unhandled exceptions that bubble to ASP.NET Core's exception-handler middleware: typically MVC actions, Minimal-API endpoints, and middleware running after `UseExceptionHandler`; hosted/background services, SignalR hubs, and middleware before `UseExceptionHandler` need their own catch sites.

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
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "forbidden",
  "status": 403,
  "detail": "An operation required an ambient tenant context but none was set.",
  "error": {
    "code": "g:tenant_required",
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

The same shape is reachable without going through the handler via `IProblemDetailsCreator.Forbidden(detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired, error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired)` for direct callers — e.g., a request-pipeline pre-check that returns `Results.Problem(...)` without throwing.

## HTTP Authorization Requirement

`Headless.Api.Core` provides tenant enforcement at the ASP.NET Core authorization boundary:

- Register through `.Authorization(auth => auth.RequireTenant())` on the root tenancy surface.
- Add `new TenantRequirement()` to the app's `FallbackPolicy` or `DefaultPolicy`.
- Requests require `ICurrentTenant.Id` to be non-blank by default.
- Mark intentional host-level, public, system, or console-bootstrap endpoints with `[AllowMissingTenant]` or `.AllowMissingTenant()`.
- Use `[RequireTenant]` or `.RequireTenant()` when an endpoint/action must opt back into tenant enforcement under broader allow-missing metadata, such as a route group or controller marked public.
- Keep `UseAuthentication() -> UseHeadlessTenancy() -> UseAuthorization()` ordering so the requirement sees the resolved tenant.

### Limitations

- **Named-policy enforcement is the consumer's responsibility.** `TenantRequirement` is only validated by `HeadlessAuthorizationTenancyValidator` when it appears in `DefaultPolicy` or `FallbackPolicy`. Named policies (`options.AddPolicy("name", policy => ...)`) are NOT inspected — putting `TenantRequirement` there does NOT satisfy the framework's enforcement guarantee.
- **Per ASP.NET Core's combinator semantics, `[Authorize("NamedPolicy")]` endpoints bypass `DefaultPolicy` and `FallbackPolicy`.** Tenant enforcement on such endpoints requires the consumer to compose `TenantRequirement` into every named policy they apply, or to also tag the endpoints with a policy that includes it. The framework cannot validate this composition.
- **`StatusCodesRewriterMiddleware` is required for the `g:tenant_required` discriminator.** The structured 403 body is produced by the framework's status-codes rewriter reading a `HttpContext.Items` marker stashed by `TenantRequirementHandler`. The rewriter is wired in by `Headless.Api.ServiceDefaults`; apps that do not use ServiceDefaults must call `UseStatusCodesRewriter()` themselves or the 403 will return a generic Forbidden body without the discriminator.
- **`[AllowAnonymous]` endpoints bypass the authorization pipeline entirely**, so `TenantRequirement` does not fire. If such a handler reads `ICurrentTenant.Id`, it triggers `MissingTenantContextException`, which `HeadlessApiExceptionHandler` remaps to a 403 with the same `g:tenant_required` body shape. Safer pattern: anonymous endpoints should NOT read `ICurrentTenant.Id`. Use `[AllowMissingTenant]` only when the authorization-pipeline opt-out is what you want.

Apply `[AllowMissingTenant]` or `.AllowMissingTenant()` to every endpoint whose HTTP path can legitimately run without a tenant. Typical categories:

- **Anonymous / public endpoints** (login, password reset, sign-up, public lookups).
- **Admin, system, or console-bootstrap endpoints** dispatched under a host-level identity rather than a tenant-scoped one.
- **Authenticated endpoints reachable by non-tenant-scoped principal types** (admin, partner, service-account, cross-tenant principals — any identity that does not mint the tenant claim).

Forgetting the opt-out on one of these surfaces produces a 403 `g:tenant_required` for legitimate callers. Tenant-scoped endpoints that genuinely require tenant context must omit it.

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
);

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantRequirement())
        .Build();
});

var publicGroup = app.MapGroup("/public").AllowMissingTenant();
publicGroup.MapGet("/status", () => Results.Ok());
publicGroup.MapGet("/tenant-data", () => Results.Ok()).RequireTenant();

[AllowMissingTenant]
public sealed class PublicBootstrapController : ControllerBase
{
    [RequireTenant]
    public IActionResult TenantScopedAction() => Ok();
}
```

When a non-opted-out HTTP request runs without a tenant, `TenantRequirementHandler` fails the authorization context with the `TenantContextRequired` reason and stashes a marker on `HttpContext.Items`. ASP.NET Core's default `IAuthorizationMiddlewareResultHandler` then produces a bare 403; `StatusCodesRewriterMiddleware` reads the marker and substitutes the normalized 403 response documented in [HTTP Failure Mapping](#http-failure-mapping). Other 403s flow through the same rewriter without the discriminator. Consumers are free to register any `IAuthorizationMiddlewareResultHandler` in any order — the framework does not decorate or replace it, so the tenant discriminator is independent of the result-handler pipeline.

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

`Headless.EntityFramework` applies tenant-aware global filters for `IMultiTenant` entities through its model conventions. To participate:

- Inherit from `HeadlessDbContext`
- Call `base.OnModelCreating(modelBuilder)`
- Ensure your entity implements `IMultiTenant`

With tenant resolution active, queries automatically filter on `TenantId == ICurrentTenant.Id`. The filter is wired by `HeadlessDbContextRuntime._ConfigureQueryFilters` and registered under the constant `HeadlessQueryFilters.MultiTenancyFilter` (whose literal string value is `"MultiTenantFilter"`). Because `IQueryable<T>.ExecuteUpdate(...)` and `IQueryable<T>.ExecuteDelete(...)` consume the same `IQueryable<T>`, bulk update and bulk delete inherit the tenant predicate and are scoped to the current tenant by default. Per-query opt-out is `IgnoreMultiTenancyFilter()`, which audit-logs the bypass via `HeadlessQueryFilters._LogFilterBypassed`.

### Entity Ownership

The decision of whether an entity declares `IMultiTenant` is per-aggregate and load-bearing — it controls whether the global query filter and (if enabled) the write guard cover it.

- **Tenant-owned aggregates** (rows whose lifetime and visibility belong to exactly one tenant) declare `: IMultiTenant`. Headless then scopes reads, `ExecuteUpdate`, `ExecuteDelete`, and guarded saves automatically.
- **Platform-level entities** do not declare `IMultiTenant`. These cover cross-tenant infrastructure (outbox rows, audit log events, system schedules), shared catalogs (vendor / product / lookup tables that span tenants), and materialized cross-tenant read models or crosswalks. Filtering them per-tenant would either hide rows from legitimate readers or force every consumer to bypass the filter.
- **The entity that defines the tenant boundary itself** — the row whose `Id` is the `TenantId` — is a deliberate special case. Marking it `IMultiTenant` forces every lookup through `ICurrentTenant`, which usually breaks admin and bootstrap paths (tenant onboarding, support tooling, cross-tenant administration). Treat this as a deferred design decision; protect those rows with admin-policy authorization rather than the query filter unless you have an explicit reason to do otherwise.

When retrofitting `IMultiTenant` onto an existing entity, ship the type change and the schema change in the same PR. The EF migration must:

1. Add a `TenantId` column as `NOT NULL` using the same width as the rest of your tenancy schema (e.g., `text` for free-form IDs, `varchar(N)` when fixed-length parity matters for joins or indexes).
2. Backfill `TenantId` in the same migration from the existing owning-tenant relationship, so the `NOT NULL` constraint can be enforced atomically.
3. Add a covering index shaped like `(TenantId, ...existing-key-columns)` so the new filter predicate does not regress existing query plans.

Splitting these across PRs leaves the entity in a state where the query filter is active but the column is missing or unindexed, which manifests as runtime exceptions or sequential scans rather than a clean failure.

### EF Tenant Write Guard

The EF write guard is opt-in and disabled by default for compatibility. Enable it from the root tenancy surface:

```csharp
builder.Services.AddHeadlessDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
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

Missing tenant context uses the shared `Headless.Abstractions.MissingTenantContextException`, so HTTP hosts using `UseExceptionHandler()` get the existing normalized 403 mapping. Cross-tenant mutation uses `Headless.Abstractions.CrossTenantWriteException` (located in `Headless.Core` to keep the failure shared across packages without forcing an Api → EF project reference).

`HeadlessApiExceptionHandler` (registered by `AddHeadlessProblemDetails()`) maps `CrossTenantWriteException` to HTTP 409 Conflict with the `g:cross_tenant_write` error descriptor and emits a structured warning log (event name `CrossTenantWriteException`). No exception data is leaked into the response body — only the descriptor code and title.

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

## Messaging Exhausted Callbacks

When messaging tenant propagation is enabled, exhausted callbacks restore `ICurrentTenant` from the message envelope before invoking `RetryPolicy.OnExhausted`. This applies to publish failures, consume failures, and poisoned-on-arrival messages that bypass normal consumer execution. Missing, whitespace, or oversized tenant headers resolve to no tenant, matching consume-side lenient header handling.

### Defense Layers and Known Gaps

`IMultiTenant` writes are protected by two complementary layers, plus paths that remain out of scope:

1. **Global query filter** — always on for `IMultiTenant` entities. Registered as `HeadlessQueryFilters.MultiTenancyFilter` (string value `"MultiTenantFilter"`). Scopes reads, `IQueryable<T>.ExecuteUpdate(...)`, and `IQueryable<T>.ExecuteDelete(...)` to the current tenant. Opt-out is `IgnoreMultiTenancyFilter()` (audit-logged).
2. **`SaveChanges` write guard** — opt-in via `.EntityFramework(ef => ef.GuardTenantWrites())`. Operates on EF's `ChangeTracker`. Catches `Add` / `Update` / `Remove` / tracked-property-mutation paths and rejects unsafe writes with `CrossTenantWriteException` before persistence.

Known gaps:

- **Attach-then-modify.** An attacker-controlled `Attach` populates `OriginalValue` from caller-supplied state, so the in-memory guard's `OriginalValue == currentTenantId` check passes for a row that actually belongs to another tenant. The global query filter does not cover this path because the attacker never queries the row. A SQL-level concurrency-style `WHERE TenantId = @currentTenantId` predicate on the SaveChanges-generated UPDATE/DELETE is the planned follow-up, tracked in the security follow-up issue on the project tracker.
- **Raw SQL and out-of-band data access** are out of scope for both layers. This covers EF's own raw paths (`DbContext.Database.ExecuteSql(...)`, `ExecuteSqlInterpolated(...)`, `ExecuteSqlRaw(...)`, `FromSqlRaw(...)`, `SqlQueryRaw(...)`), stored procedures, triggers, and any code that opens its own command or connection — Dapper, other micro-ORMs, and direct `DbContext.Database.GetDbConnection()` usage all bypass the query filter and write guard entirely, including `MultiTenantFilter`. Consumers issuing raw SQL against `IMultiTenant` tables must scope manually with a `WHERE "TenantId" = @currentTenantId` predicate sourced from `ICurrentTenant.Id`, or wrap the call in `ITenantWriteGuardBypass.BeginBypass()` under an authenticated, audited host context.

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

For end-to-end propagation, opt in to the built-in middleware pair:

```csharp
using Headless.Messaging.MultiTenancy;

builder.AddHeadlessTenancy(tenancy =>
    tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);

builder.Services.AddHeadlessMessaging(options =>
{ /* ... */
});
```

This registers `TenantPropagationPublishMiddleware` (stamps `PublishOptions.TenantId` from ambient `ICurrentTenant.Id` at publish time) and `TenantPropagationConsumeMiddleware` (calls `ICurrentTenant.Change(...)` on the resolved `ConsumeContext<T>.TenantId` for the lifetime of the consume — including both success and exception paths). Caller-set values on `PublishOptions.TenantId` are preserved verbatim; system messages can override propagation by setting `TenantId` explicitly or by publishing with no ambient tenant.

Tenant propagation is composed exclusively through the root tenancy seam — the previous `MessagingBuilder.AddTenantPropagation()` extension has been removed. The seam registration is idempotent and fails fast at startup when propagation is enabled with only the framework's `NullCurrentTenant` fallback registered.

**Trust boundary.** The consume middleware trusts the inbound envelope. The framework assumes the message bus is internal-only; message names exposed to external producers must layer envelope validation or signing in front of this middleware. Otherwise an attacker who can publish to the bus can impersonate any tenant.

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

Use `.RequireTenantOnPublish()` to require every publish to resolve a tenant identifier. When enabled, the publish wrapper checks `PublishOptions.TenantId` first, then falls back to the ambient `ICurrentTenant.Id`. If neither resolves a value, the publish fails with `Headless.Abstractions.MissingTenantContextException`. This is the messaging sibling of the EF write guard (#234) and the HTTP authorization requirement.

The lower-level equivalent is `MessagingOptions.TenantContextRequired = true`. Defaults to `false` to preserve today's behavior. The U2 raw-header integrity rules above (`ReservedTenantHeader`, `TenantIdMismatch`) always apply and run before the strict-tenancy fallback, so injection attempts cannot bypass the guard by enabling the flag.

To remediate a `MissingTenantContextException` from a background worker or `IHostedService`:

```csharp
// Option A: explicit per-publish tenant
await publisher.PublishAsync(message, new PublishOptions { TenantId = tenantId }, cancellationToken);

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

Tests that assert the normalized 403 `g:tenant_required` ProblemDetails (or any other `HeadlessApiExceptionHandler` failure shape) must build the host with a production-style environment. The common ASP.NET pattern only calls `app.UseExceptionHandler()` outside Development, so a default-Development test client lets the exception escape as the developer error page instead of the handler's ProblemDetails response. Use a `WebApplicationFactory` variant that sets `Environment = Production` (or your repo's equivalent helper) when the assertion target is the handler's output.

## Failure Modes to Watch

- Missing `UseHeadlessTenancy()` means HTTP requests stay at host scope even when the JWT contains a tenant claim; startup validation fails when HTTP tenancy was configured through `AddHeadlessTenancy(...)`.
- Calling `.Authorization(auth => auth.RequireTenant())` without putting `TenantRequirement` in `DefaultPolicy` or `FallbackPolicy` records an enforcing posture that would not execute. Startup validation fails with `HEADLESS_TENANCY_AUTHORIZATION_POLICY_MISSING`.
- Registering `UseHeadlessTenancy()` before `UseAuthentication()` means no authenticated principal is available yet.
- Forgetting `using` around `currentTenant.Change()` in non-HTTP code can leak tenant context within the current async flow.
- Assuming host-level cache scope `t:` is tenant-isolated is incorrect; it is intentionally shared.
- Assuming `IgnoreMultiTenancyFilter()` bypasses write protection is incorrect; it only affects reads.

---

## Headless.MultiTenancy

### Problem Solved

Provides one composition surface for tenant posture across Headless packages while keeping each package in charge of its own behavior. It owns the root builder, shared manifest, and validator contracts only — it does not resolve tenants, enforce HTTP authorization, propagate messages, or guard EF writes. Seam packages (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`) contribute their own fluent extensions on top of this builder.

### Key Features

- `AddHeadlessTenancy(Action<HeadlessTenancyBuilder> configure)` — root configuration entry point; registers the shared manifest and startup validator, then invokes the configure callback.
- `HeadlessTenancyBuilder` — root builder passed to the configure callback. Exposes `ApplicationBuilder`, `Services`, `Manifest`, and `RecordSeam(...)`. Seam packages extend it with their own methods (`.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, `.EntityFramework(...)`).
- `TenantPostureManifest` — thread-safe, singleton, non-PII record of seam posture: status (`TenantPostureStatus`), capability labels, and runtime markers. Diagnostic breadcrumb only; records do not create enforcement.
- `TenantPostureStatus` — enum whose ordinal is posture precedence: `Configured(0) < Propagating(1) < Guarded(2) < Enforcing(3)`. `RecordSeam` always keeps the strongest status across contributions.
- `IHeadlessTenancyValidator` / `HeadlessTenancyDiagnostic` — extension hook for seam packages to emit startup diagnostics. Diagnostics can be `Information`, `Warning`, or startup-blocking `Error`.
- `HeadlessTenancyStartupValidator` — `IHostedLifecycleService` that runs all registered validators in `StartingAsync` before any other hosted service starts; throws `HeadlessTenancyValidationException` (an `InvalidOperationException`) on any `Error` diagnostic.
- `HeadlessTenancyValidationContext` — context record passed to validators: `Services` (the app `IServiceProvider`) + `Manifest`.

### Design Notes

`HeadlessTenancyStartupValidator` is registered as an `IHostedLifecycleService` (not a plain `IHostedService`) so `StartingAsync` runs before any other hosted service's `StartAsync`. This ordering guarantees that a misconfigured posture fails the host before background workers or messaging consumers begin processing under the wrong assumptions. The validation itself is synchronous inside `StartingAsync` — the task is only faulted if the host's own startup continuation throws; the validated diagnostics surface as the typed `HeadlessTenancyValidationException` before the task is awaited.

### Installation

```bash
dotnet add package Headless.MultiTenancy
```

Most applications receive this package transitively through the seam packages that contribute tenancy extensions (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`). Add it directly only when authoring a custom `IHeadlessTenancyValidator` or a custom seam without pulling in one of those packages.

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless();

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims())
        .Authorization(auth => auth.RequireTenant())
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
        .EntityFramework(ef => ef.GuardTenantWrites())
);

var app = builder.Build();

app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy(); // after UseAuthentication, before UseAuthorization
app.UseAuthorization();
```

`AddHeadlessTenancy` is the only call owned by this package; the `.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, and `.EntityFramework(...)` extensions are contributed by the respective seam packages once they are installed.

### Configuration

`Headless.MultiTenancy` has no options class and binds no configuration section. The builder is purely a composition surface — every seam package owns its own options and configuration binding.

`TenantPostureManifest` is populated at DI build time by the `configure` callback in `AddHeadlessTenancy`. Seam packages call `builder.RecordSeam(seam, status, capabilities)` to register their posture. `MarkRuntimeApplied(seam, marker)` is called by seam middleware at request time (for example, `UseHeadlessTenancy()` marks the HTTP seam's runtime slot) so startup validators can verify middleware placement.

Custom validators implement `IHeadlessTenancyValidator` and register themselves in DI before `AddHeadlessTenancy` is called. `HeadlessTenancyStartupValidator` resolves all `IHeadlessTenancyValidator` registrations from DI via `IEnumerable<IHeadlessTenancyValidator>`.

### Dependencies

- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

### Side Effects

- Registers a singleton `TenantPostureManifest` via `services.AddSingleton(manifest)`.
- Registers `HeadlessTenancyStartupValidator` as `IHostedService` (via `TryAddEnumerable`; safe to call multiple times).
- `AddHeadlessTenancy` also invokes the caller's `configure` callback, which may register additional services from seam packages.
