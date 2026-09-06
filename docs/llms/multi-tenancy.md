---
domain: Multi-Tenancy
packages: MultiTenancy.Abstractions, MultiTenancy, MultiTenancy.Storage.EntityFramework, Api.Core, Api.ServiceDefaults, Core, Messaging.Core, Jobs.Core, EntityFramework, Permissions.Core
---

# Multi-Tenancy

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Tenant Catalog Store](#choosing-a-tenant-catalog-store)
- [HTTP Setup](#http-setup)
- [Skipping Tenant Resolution](#skipping-tenant-resolution)
- [HTTP Failure Mapping](#http-failure-mapping)
- [HTTP Authorization Requirement](#http-authorization-requirement)
- [Tenant Semantics](#tenant-semantics)
- [Tenant Catalog](#tenant-catalog)
    - [Accessor-only setup](#accessor-only-setup)
    - [Identifier-based resolution setup](#identifier-based-resolution-setup)
    - [Mismatch enforcement (R19)](#mismatch-enforcement-r19)
    - [Failure mapping](#failure-mapping)
    - [Migration Guidance](#migration-guidance)
    - [DoS and rate limiting](#dos-and-rate-limiting)
- [EF Core Integration](#ef-core-integration)
- [Permissions and Caching](#permissions-and-caching)
- [Non-HTTP Execution Paths](#non-http-execution-paths)
    - [Background Jobs](#background-jobs)
    - [Message Consumers](#message-consumers)
    - [SignalR](#signalr)
- [Failure Modes to Watch](#failure-modes-to-watch)
- [Headless.MultiTenancy.Abstractions](#headlessmultitenancyabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.MultiTenancy](#headlessmultitenancy)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.MultiTenancy.Storage.EntityFramework](#headlessmultitenancystorageentityframework)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> End-to-end tenant context setup for HTTP requests, EF Core global filters, permission caching, non-HTTP execution paths, and the opt-in tenant catalog (identifier resolution and tenant metadata).

## Quick Orientation

Headless multi-tenancy is built from these pieces:

- `Headless.MultiTenancy.Abstractions` holds the family's contract surface: `ICurrentTenant`, `ICurrentTenantAccessor`, the tenant write-guard types and exceptions (moved from `Headless.Core`), and the opt-in tenant catalog's store SPI and models (`TenantInfo`, `ITenantStore`, `ITenantDirectory`, `ICurrentTenantInfo`, `TenantResolutionOutcome`) — all under the `Headless.MultiTenancy` namespace.
- `Headless.Core` supplies the default tenant-context implementations (`CurrentTenant`, `AsyncLocalCurrentTenantAccessor`, `NullCurrentTenant`, `TenantWriteGuardBypass`) that hold the current tenant in an `AsyncLocal` scope.
- `Headless.MultiTenancy` provides the root `AddHeadlessTenancy(...)` composition surface, a shared, non-PII tenant posture manifest, and — opt-in via `.Catalog(...)` — the tenant catalog service, caching, error codes, and the in-memory and configuration-backed stores. See [Tenant Catalog](#tenant-catalog).
- `Headless.MultiTenancy.Storage.EntityFramework` ships an EF Core-backed tenant store as a separate package so apps that only need in-memory or configuration stores take no EF dependency.
- `Headless.Api.Core` resolves tenant context for HTTP requests via `UseHeadlessTenancy()` (claim-based, post-authentication) and, when a catalog is configured, via `UseHeadlessTenantCatalogResolution()` (identifier-based, pre-authentication). It can enforce tenant presence before endpoint execution through `.Authorization(auth => auth.RequireTenant())`.
- `Headless.Messaging.Core` propagates tenant context across message publish/consume and can require tenant context on publish.
- `Headless.Jobs.Core` persists a tenant on time jobs — capturing the ambient tenant at schedule time and restoring it around every execution attempt — and can require a tenant on enqueue. Cron stays system-scope.
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
        .Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
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
- For background jobs, adopt the Jobs tenancy seam (`.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())`) so time jobs capture the ambient tenant at schedule time and restore it around every execution attempt. Cron is always system-scope: fan out one explicit-tenant time job per tenant from application code — see [Background Jobs](#background-jobs).
- For message consumers, use the Messaging seam. When not using a seam on either path, set tenant explicitly with `using (currentTenant.Change(tenantId)) { ... }`.
- Do not assume HTTP middleware covers SignalR hubs, background jobs, or messaging consumers. Those execution paths need their own tenant resolution.
- The tenant catalog is opt-in and off by default (R5): without `.Catalog(...)`, `ICurrentTenantInfo` is a no-op that always returns `null`, and claim-based resolution behaves exactly as it did before the catalog existed.
- Never reassign an existing tenant identifier to a different tenant within the cache-lifetime window (`TenantCatalogOptions.CacheExpiration`, default 5 minutes) — the store SPI is read-only from the framework's perspective, so there is no cache-invalidation path, and a still-cached identifier→id mapping would silently route requests to the wrong tenant. Wait at least `CacheExpiration` after retiring an identifier before reusing it. See [Tenant Catalog](#tenant-catalog).
- Do not treat `ICurrentTenantInfo` reads as an authorization check. Reads never reject — a disabled tenant's metadata still returns with `IsEnabled = false`. Rejection on disablement happens only at identifier-resolution time (`TenantCatalogResolutionMiddleware` / `ITenantCatalogService.ResolveAsync`), never on an accessor read.
- Never treat `TenantResolutionKind.None` as a resolution result. It is the enum's reserved zero value — the state of an uninitialized `default(TenantResolutionOutcome)`, an auto-valued test double, or a misbehaving custom `ITenantCatalogService` — not a sixth outcome. The catalog itself never produces it; a store or catalog-service override that returns it has a bug, not a legitimate "unresolved" case. Use `Unknown` for that.
- Register `UseHeadlessTenantCatalogResolution()` after `UseRouting()` and before `UseAuthentication()` — a different slot from `UseHeadlessTenancy()` (after `UseAuthentication()`, before `UseAuthorization()`). The two middlewares serve different resolution paths (identifier vs. claim) and both can be active in the same pipeline.
- Register `UseStatusCodesRewriter()` so it wraps `UseAuthorization()` on any catalog-resolution host. It writes the R19 mismatch rejection the authorization tier only marks; omitting it fails startup (`CATALOG_RESOLUTION_WITHOUT_REWRITER`), and placing it after `UseAuthorization()` passes startup but still leaks the mismatch as a distinguishable bare 403.
- Do not add per-tenant application configuration to `TenantInfo.ExtraProperties` or a queryable column just because it is convenient. If a value needs to be queried or indexed, it belongs in Settings/Features/Permissions keyed by the canonical tenant id, or in a typed column of your own `ITenantStore` implementation — never in the catalog's read-along bag.

## Core Concepts

- **Tenant identifier vs. canonical tenant id.** A *tenant identifier* is the public, resolution-facing name of a tenant — a host label (`acme` from `acme.example.com`), a route value, or a header value. It may change over a tenant's life (rebrands, custom domains) and is normalized (trim, lowercase invariant) before any lookup; it is never used to key persistence or authorization. The *canonical tenant id* is the stable id that keys everything tenant-owned: ambient `ICurrentTenant.Id`, EF filters and write guards, Jobs/Messaging propagation, and per-tenant Settings/Features/Permissions state. A JWT tenant claim carries the canonical id directly (R8) — claim-based resolution never touches the catalog. Identifier-based resolution exists specifically to map a public identifier to the canonical id before ambient context is set.
- **The catalog owns identity, routing, and lifecycle only.** `TenantInfo` carries `Id`, `Identifier`, `Name`, `IsEnabled`, and `ExtraProperties`. `ExtraProperties` is read-along payload — it never participates in lookups or caching keys. Per-tenant application configuration (feature flags, limits, branding) belongs in Settings/Features/Permissions keyed by the canonical id, not on the tenant model — the framework already ships cached, managed per-tenant stores for that, and a rich tenant model would compete with them.
- **Three extension tiers**, from least to most structural, so the pipeline itself never becomes generic:
    1. **`ExtraProperties` bag** — read-along data with no query/index need. The fastest path, but non-queryable by construction (the repo's EF convention maps it through an opaque serialized column).
    2. **Subclass `TenantInfo`** (non-sealed) from an app-owned `ITenantStore` implementation, adding real typed, queryable columns. This is the "queryable-by ⇒ first-class" rule: an attribute that needs an index is either a tenant identifier or a column in your own store, never a value stuffed into `ExtraProperties`.
    3. **Opt-in typed leaf accessor** — `ICurrentTenantInfo<T>`, registered via `services.AddTypedCurrentTenantInfo<T>(projection)`. Only this accessor is generic; the store SPI, cache, catalog service, and outcome types stay non-generic. The projection delegate builds `T` from the base `TenantInfo` shape, with a downcast fast path when the store already returned the subtype directly.
- **Store SPI is read-only and minimal.** `ITenantStore` has exactly two members (`FindByIdentifierAsync`, `FindByIdAsync`); an optional `ITenantDirectory.GetAllAsync()` capability adds enumeration. Normalization, shape validation, and caching are owned once by the catalog service — stores never see raw caller input and never re-normalize. Implementing `ITenantStore` directly over an app-owned tenant aggregate is a documented first-class path, not a fallback; the shipped in-memory, configuration, and EF Core stores are convenience defaults, not a canonical schema every app must adopt.
- **Staleness bounds.** `TenantCatalogOptions.CacheExpiration` (default 5 minutes) bounds how long a resolved identifier→id mapping and an id→`TenantInfo` entry stay cached before the catalog service re-reads the store — a disable or metadata change propagates within this window. `UnknownIdentifierCacheExpiration` (default 30 seconds) is a separate, shorter negative-cache window: a newly created tenant becomes resolvable within it once its identifier stops returning a cached "unknown" result. The store SPI is read-only, so there is no framework write path to invalidate either cache early. The combined disabled-tenant exposure window — the longest time a disabled tenant can still be treated as active by some code path — is `max(claim lifetime, CacheExpiration)`: a claim-resolved request bypasses the catalog entirely (R8), so a disabled tenant's still-valid JWT keeps passing `TenantRequirementHandler` for the rest of the token's lifetime regardless of the catalog; an identifier-resolved request is bounded by `CacheExpiration` instead.
- **Identifier no-reuse rule.** Because there is no cache-invalidation path, reassigning an identifier to a different tenant while a stale mapping could still be cached is unsafe. Retire an identifier for at least `CacheExpiration` before re-pointing it at a different tenant.
- **Secure-by-default rejection.** Unknown, disabled, and identifier/claim-mismatch outcomes all collapse to one generic response (`g:tenant_resolution_failed`, 404). The guarantee this buys is narrower than full tenant-enumeration resistance: a rejected caller cannot tell whether the identifier is unknown, belongs to a disabled tenant, or conflicts with its own tenant claim — the three rejection outcomes become mutually indistinguishable. It does not hide the existence of an *enabled* tenant — that request is never rejected by this collapse; it proceeds to the endpoint and returns the application's own status, so existence stays observable in one request either way. `TenantCatalogOptions.DetailedResolutionErrors = true` gives up only the rejection-indistinguishability guarantee, restoring granular codes and statuses (`g:tenant_unknown` 404, `g:tenant_disabled` 403, `g:tenant_identifier_mismatch` 403) for development and trusted environments only. Invalid-shape identifiers always keep their own code (`g:tenant_identifier_invalid`, 400) regardless of the option, since shape validation reveals nothing tenant-specific. Store faults are never mapped to these codes — they propagate as ordinary server errors; a cache fault degrades to a miss and falls through to the store.
- **Accessor semantics.** `ICurrentTenantInfo.GetAsync()` reads the ambient `ICurrentTenant.Id` fresh on every call — there is no per-scope memoization — so nested `ICurrentTenant.Change(...)` scopes (Jobs retry, Messaging consume, admin flows) always observe the inner tenant's info while the scope is active and the outer tenant's info again once it disposes. Reads never throw for an absent tenant: `null` covers "no ambient tenant", "no catalog store configured", and "id has no catalog row" alike. A disabled tenant's metadata still reads normally (`IsEnabled = false`) — rejecting a disabled tenant is a resolution-time concern only. When the ambient display name and the catalog name differ (for example after a Jobs/Messaging-restored scope carries a stale name), the accessor's catalog value is authoritative.

## Choosing a Tenant Catalog Store

The tenant catalog (`Catalog(...)`) requires exactly one storage provider. Registering zero or more than one fails startup. It also requires a caching provider registered via `AddHeadlessCaching(...)` — see [Tenant Catalog](#tenant-catalog).

| Store | Use when | Avoid when | Trade-off |
|---|---|---|---|
| **In-memory** (`UseInMemory(...)`, ships in `Headless.MultiTenancy`) | Tests, small apps, a fixed or rarely-changing tenant list defined in code. | The tenant list must survive process restarts without redeploying, or changes at runtime. | Immutable snapshot built once from seed `TenantInfo` entries at startup; zero external dependency. |
| **Configuration** (`UseConfiguration(...)`, ships in `Headless.MultiTenancy`) | The tenant list is operator-managed data (e.g. a `Headless:MultiTenancy:Tenants` config section) that should change without a code deploy, but reload-on-change is not required. | Tenants are created/edited by end users or an admin UI at runtime. | Bound once via the options system at startup (no reflection-based construction); a config change after startup requires a process restart — there is no change-token re-binding in v1. |
| **Entity Framework Core** (`UseEntityFramework<TContext>()`, separate `Headless.MultiTenancy.Storage.EntityFramework` package) | Tenants are created, edited, or onboarded at runtime through application code, and you already run EF Core. | You only need a static or operator-managed list — taking the EF dependency and owning migrations is unnecessary overhead. | App-owned schema and migrations (single `Tenants` table, unique index on the normalized identifier pinned to a deterministic collation); read-only from the framework's side — inserts/updates go through your own `DbContext`. |

Apps with requirements none of these fit (a richer aggregate, a different persistence technology, a remote directory service) implement `ITenantStore` directly — see [Core Concepts](#core-concepts).

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

## Tenant Catalog

The tenant catalog is opt-in (R5): a host that never calls `.Catalog(...)` behaves exactly as it did before the catalog existed. Configuring a catalog is two independent decisions:

1. **Metadata access** — `.Catalog(...)` alone makes `ICurrentTenantInfo` resolve `TenantInfo` for whatever tenant is already ambient (typically via claim resolution). This is a valid, non-failing posture (`catalog-accessor`).
2. **Identifier-based resolution** — `Headless.Api.Core`'s `.Http(http => http.ResolveFromCatalog(...))` plus `app.UseHeadlessTenantCatalogResolution()` additionally resolves the ambient tenant from a public identifier before authentication runs (`catalog-resolution`). This requires a store to be configured, an identifier source registered, and `app.UseStatusCodesRewriter()` in the pipeline; enabling it without any of the three fails startup (R18) — the last with `CATALOG_RESOLUTION_WITHOUT_REWRITER`, because the rewriter is what writes the R19 mismatch rejection.

Both decisions share one prerequisite: **a caching provider must be registered**. `TenantCatalogService` resolves the identifier→id and id→`TenantInfo` caches as `ICache<T>` constructor dependencies, and `Headless.MultiTenancy` references only `Headless.Caching.Abstractions` — the open-generic `ICache<>` implementation comes from `Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`. Configuring a store without calling `AddHeadlessCaching(...)` fails startup with `CATALOG_WITHOUT_CACHING_PROVIDER`; this applies to accessor-only hosts too, because `ICurrentTenantInfo` reads go through the same service and the same caches.

### Accessor-only setup

```csharp
builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy
        .Http(http => http.ResolveFromClaims()) // existing claim-based resolution, unchanged
        .Catalog(catalog =>
            catalog.UseInMemory(options =>
            {
                options.Tenants.Add(new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme Inc", isEnabled: true));
            })
        );
});
```

```csharp
public sealed class ProfileService(ICurrentTenantInfo tenantInfo)
{
    public async Task<string?> GetTenantDisplayNameAsync(CancellationToken cancellationToken) =>
        (await tenantInfo.GetAsync(cancellationToken))?.Name;
}
```

No HTTP pipeline change is required for this — `TenantResolutionMiddleware` (the existing claim middleware) sets the ambient id exactly as before; `ICurrentTenantInfo` now has a store to look it up in.

### Identifier-based resolution setup

```csharp
using Headless.MultiTenancy; // TenantInfo, Catalog(...)
using Headless.Api;          // ResolveFromCatalog(...), UseHeadlessTenantCatalogResolution()

// v1 ships no built-in ITenantIdentifierSource — implement one for your resolution strategy
// (host label, route segment, header, ...). Synchronous, side-effect free; return null when this
// request carries no identifier for this source.
public sealed class HostLabelTenantIdentifierSource : ITenantIdentifierSource
{
    public string? GetIdentifier(HttpContext context) => context.Request.Host.Host.Split('.')[0];
}

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy
        .Catalog(catalog => catalog.UseInMemory(options => options.Tenants.Add(/* ... */)))
        .Http(http => http.ResolveFromCatalog(resolution => resolution.AddSource<HostLabelTenantIdentifierSource>()));
});

var app = builder.Build();

app.UseStatusCodesRewriter();             // must wrap UseAuthorization() — writes the R19 mismatch rejection
app.UseRouting();
app.UseHeadlessTenantCatalogResolution(); // after UseRouting, before UseAuthentication
app.UseAuthentication();
app.UseHeadlessTenancy();                 // unchanged: after UseAuthentication, before UseAuthorization
app.UseAuthorization();
```

`Headless.Api.ServiceDefaults`' `app.UseHeadless()` already registers the rewriter in the right slot, so call `UseStatusCodesRewriter()` explicitly only on hosts that do not use ServiceDefaults — or that turned `HeadlessServiceDefaultsOptions.UseStatusCodePages` off, which also suppresses the rewriter.

**Middleware ordering contract** — `UseHeadlessTenantCatalogResolution()` and `UseHeadlessTenancy()` occupy different, non-interchangeable pipeline slots:

| Middleware | Placement | Resolves from |
|---|---|---|
| `UseHeadlessTenantCatalogResolution()` | After `UseRouting()`, before `UseAuthentication()` | Public identifier (`ITenantIdentifierSource`), via the catalog |
| `UseHeadlessTenancy()` (`TenantResolutionMiddleware`) | After `UseAuthentication()`, before `UseAuthorization()` | Authenticated JWT tenant claim |

The identifier middleware must run before authentication because the identifier itself often determines *which* tenant's authentication configuration applies. Placing it before `UseRouting()` means `[SkipTenantResolution]` endpoint metadata is not yet resolvable — the middleware logs a once-per-process warning and no-ops rather than guessing. Both middlewares can be active in the same pipeline: when they are, a request carrying both a resolved identifier and an authenticated tenant claim must canonicalize to the same tenant (R19) — see below.

### Mismatch enforcement (R19)

When a request carries both a resolved identifier and an authenticated tenant claim, the two must resolve to the same canonical tenant. Enforcement is two-tier, because neither tier alone covers every host:

1. **Pre-auth check (primary).** The identifier middleware materializes the default-scheme principal itself (`AuthenticateAsync()`, the same authentication the pipeline performs later, cached per request) and rejects a mismatch immediately. This is what makes R19 hold on endpoints where authorization never runs — `[AllowAnonymous]`, or no authorize metadata with no fallback policy — including catalog-only hosts that never call `ResolveFromClaims()`. Hosts with no default authenticate scheme skip this tier rather than being forced to configure one.
2. **Post-authorization check.** `TenantIdentifierIntegrityHandler`, an `IAuthorizationHandler`, runs during authorization after `PolicyEvaluator` has materialized the principal, which is what makes it correct for endpoint-scoped or non-default authentication schemes that the pre-auth tier cannot see. It resolves the request through `IHttpContextAccessor` when the authorization resource is an `Endpoint` rather than an `HttpContext`.

The claim middleware also carries a fast-path check comparing against the preserved per-request identifier-resolution result, never against the ambient `ICurrentTenant.Id`, which its own `Change()` may have already overwritten. The tiers are mutually exclusive per request, so a mismatch is never written twice. A mismatch produces the same secure-by-default rejection as an unknown/disabled identifier (byte-identical unless `DetailedResolutionErrors` is on, in which case it is `g:tenant_identifier_mismatch` at 403). Claim-only requests (no identifier resolution happened) are untouched — R19 only applies when both paths ran.

A mismatch that is visible only to a non-default scheme is rejected by the authorization tier, whose forbid result is collapsed to the generic 404 by `StatusCodesRewriterMiddleware`. That tier writes no response of its own — it only fails the evaluation and marks the request — so the rewriter is load-bearing, not cosmetic, and its absence leaves a distinguishable 403 next to the unknown identifier's 404: a tenant-enumeration oracle. Startup therefore fails with `CATALOG_RESOLUTION_WITHOUT_REWRITER` when `catalog-resolution` is configured and `UseStatusCodesRewriter()` was never called.

The residual the diagnostic cannot cover is **placement**: the posture manifest records that the rewriter was registered, not where. `UseStatusCodesRewriter()` must be added to the pipeline before `UseAuthorization()` so that it wraps it — a rewriter placed after authorization is never reached, because a failed evaluation short-circuits, and the mismatch stays a bare 403 on a host that passes startup validation.

### Failure mapping

Identifier resolution outcomes map to ProblemDetails per this table (`TenantCatalogOptions.DetailedResolutionErrors` defaults to `false`):

| Outcome | Default response | `DetailedResolutionErrors = true` |
|---|---|---|
| Unknown identifier | 404, `g:tenant_resolution_failed` | 404, `g:tenant_unknown` |
| Disabled tenant | 404, `g:tenant_resolution_failed` (byte-identical to Unknown) | 403, `g:tenant_disabled` |
| Identifier/claim mismatch (R19) | 404, `g:tenant_resolution_failed` (byte-identical to Unknown/Disabled) | 403, `g:tenant_identifier_mismatch` |
| Invalid identifier shape | 400, `g:tenant_identifier_invalid` (always, regardless of the option) | same |
| Ignored identifier (`IgnoredIdentifiers`) | not a rejection — resolution continues with no tenant, store never called | same |

Store faults during resolution (an `ITenantStore` implementation throwing) are never mapped to a tenant code — they propagate as ordinary unhandled exceptions (typically a 500). A cache read or write fault degrades to a miss/no-op so the store-derived outcome is unaffected.

### Migration Guidance

Apps that start with claim-direct resolution (`.Http(http => http.ResolveFromClaims())`, no catalog) can add a catalog later with no change to existing behavior:

1. **Nothing breaks by not migrating.** The catalog is opt-in end to end (R5) — skip this section until you actually need identifier-based resolution or centralized tenant metadata.
2. **Add metadata access first, independently of resolution.** Call `.Catalog(catalog => catalog.UseInMemory(...) / .UseConfiguration(...) / .UseEntityFramework<TContext>())` with no `.ResolveFromCatalog(...)`. `ICurrentTenantInfo` starts resolving real data for claim-resolved tenants immediately; nothing else in the pipeline changes.
3. **Add identifier-based resolution when you need it** (custom domains, subdomain-per-tenant routing, an unauthenticated pre-login tenant lookup): implement `ITenantIdentifierSource` for your strategy, call `.Http(http => http.ResolveFromCatalog(resolution => resolution.AddSource<TSource>()))`, add `app.UseHeadlessTenantCatalogResolution()` after `UseRouting()` and before `UseAuthentication()`, and make sure `app.UseStatusCodesRewriter()` wraps `UseAuthorization()` (ServiceDefaults' `UseHeadless()` already does).
4. **R19 mismatch enforcement activates automatically** with `ResolveFromCatalog(...)` — it also registers `IHttpContextAccessor` — and applies to any request that carries both a resolved identifier and a tenant claim, including on catalog-only hosts that never call `ResolveFromClaims()`. No extra wiring is needed, and there is no behavior change for hosts that resolve tenants only from claims.
5. **Choose a store per [Choosing a Tenant Catalog Store](#choosing-a-tenant-catalog-store)** — starting with in-memory or configuration and moving to EF Core later is a same-shape swap (`UseInMemory` → `UseEntityFramework<TContext>()`); the catalog service, caching, and HTTP wiring do not change.

### DoS and rate limiting

The pre-auth identifier-resolution path is reachable by unauthenticated callers by construction (it runs before `UseAuthentication()`). DoS and rate-limiting protection for this path is a consumer responsibility, mirroring the framework's existing input-validation delegation (cache key length, message payload size) — the framework validates identifier *shape* (R21: length and character-set bounds, rejected before any cache or store lookup) but does not rate-limit callers. Use ASP.NET Core's built-in rate limiting middleware or an edge control (reverse proxy, WAF, CDN) in front of hosts that expose identifier-based resolution to the public internet. `UnknownIdentifierCacheExpiration` bounds repeated-probe cost against the *store* for a fixed identifier, but rotating through many distinct unknown identifiers still costs one store read each — that traffic class is what rate limiting is for, not caching.

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

Missing tenant context uses the shared `Headless.MultiTenancy.MissingTenantContextException`, so HTTP hosts using `UseExceptionHandler()` get the existing normalized 403 mapping. Cross-tenant mutation uses `Headless.MultiTenancy.CrossTenantWriteException` (defined in `Headless.MultiTenancy.Abstractions` to keep the failure shared across packages without forcing an Api → EF project reference).

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

`Headless.Jobs` persists a length-bounded `TenantId` on time jobs. The tenant is resolved once at schedule time and restored around every execution attempt, so a job scheduled from tenant `t1` runs its handler — and each retry — under `t1` without threading the id through the request payload. Cron definitions stay system-scope (see [Cron Fan-Out](#cron-fan-out)). Registration mirrors the Messaging seam:

```csharp
using Headless.Jobs;

builder.AddHeadlessTenancy(tenancy =>
    tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
);

builder.Services.AddHeadlessJobs(options =>
{ /* ... */
});
```

Register a real `ICurrentTenant` source (HTTP claim resolution, `AddHeadlessDbContextServices()`, or a custom implementation) before `AddHeadlessJobs` so propagation resolves a live tenant rather than the framework's `NullCurrentTenant` fallback. See [docs/llms/jobs.md](jobs.md#tenant-propagation) for the full resolution and chain-propagation semantics.

#### Automatic Propagation (`PropagateTenant`)

`PropagateTenant()` records a `Propagating` posture and enables two behaviors:

- **Schedule-side capture** — when an enqueue supplies no explicit `EnqueueOptions.TenantId` (or entity `TenantId`), the schedule middleware captures the ambient `ICurrentTenant.Id` onto the job row in the same atomic write that persists it. Nothing recaptures after commit.
- **Execute-side restoration** — the execute middleware wraps every handler attempt in `ICurrentTenant.Change(job.TenantId)` and disposes the scope on success, fault, or cancellation. Because Polly re-dispatches the execute pipeline per attempt, each retry is freshly scoped and no scope leaks between attempts.

An explicit `EnqueueOptions.TenantId` always wins over ambient capture — even when it differs from the ambient tenant (the mismatch logs a warning). In-process code already holds `ICurrentTenant.Change`, so an explicit value adds no new escalation vector; this matches the Messaging publish middleware. Hosts that want hard lateral isolation opt in with `RejectCrossTenantEnqueue()`, which rejects an explicit tenant differing from a present ambient tenant while still honoring explicit values from system scope (cron fan-out keeps working).

#### Strict Enqueue (`RequireTenantOnEnqueue`)

`RequireTenantOnEnqueue()` records an `Enforcing` posture. A time-job enqueue that resolves no explicit or ambient tenant is rejected with `Headless.MultiTenancy.MissingTenantContextException` — the Jobs sibling of the EF write guard and the HTTP authorization requirement — unless the job opts out as a system job.

Set `IsSystemJob = true` (on `EnqueueOptions` or the entity) to schedule a deliberate tenantless job that bypasses the strict check. To keep tenant code from escalating into system scope, `IsSystemJob = true` is **rejected** with `JobValidatorException` when an ambient tenant is present, or when an explicit `TenantId` is also supplied; the system-job decision is logged at schedule time. `IsSystemJob` is transient — a schedule-time authorization concept with no execution-time meaning — and is never persisted.

Structural validation — cron-scope rejection, the system-job contradictions, and blank/over-length bounds on explicitly supplied tenant values — runs whenever the middleware dispatches, independent of the options. Only ambient capture (`PropagateTenant`) and missing-tenant rejection (`RequireTenantOnEnqueue`) are gated by the seam flags, so tenant-to-system escalation and tenant-scoped cron are always rejected.

#### Manual Propagation

If you opt out of `PropagateTenant()`, pass the tenant explicitly on every enqueue. An explicit `EnqueueOptions.TenantId` is persisted regardless of the flag and **is still restored around the job handler at execute time** — the handler, and each retry, runs under that tenant, so you do not restore it inside the handler. Manual restoration is only needed for work that runs *outside* the Jobs execute pipeline (inline code, other background paths):

```csharp
// Explicit capture at schedule time — no ambient dependency. The handler runs under `tenantId`
// even with PropagateTenant() off, because a persisted tenant is always restored at execute time.
await scheduler.EnqueueAsync(request, new EnqueueOptions { TenantId = tenantId }, ct);

// Inline work OUTSIDE the Jobs execute pipeline still needs an explicit scope.
using (currentTenant.Change(tenantId))
{
    await processor.RunAsync();
}
```

#### Cron Fan-Out

Cron definitions and occurrences are always system-scope; scheduling a cron definition whose `TenantId` is non-null throws `JobValidatorException`. To run tenant-scoped recurring work, enumerate tenants in application code inside a system-scope cron handler and schedule one tenant-scoped time job per tenant with an **explicit** `EnqueueOptions.TenantId`:

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.MultiTenancy; // ITenantDirectory — only when a tenant catalog is configured

// A tenant-scoped time job. When it runs, the execute middleware has already restored
// ICurrentTenant to the job's TenantId, so tenant-scoped services (EF global filters,
// permission cache) observe the right tenant automatically.
public sealed record TenantReportRequest(string ReportKind);

[JobFunction("GenerateTenantReport")]
public sealed class GenerateTenantReport(IReportService reports)
{
    public Task ExecuteAsync(JobFunctionContext<TenantReportRequest> context, CancellationToken ct) =>
        reports.BuildAsync(context.Request.ReportKind, ct);
}

// A system-scope cron that fans out one tenant-scoped time job per tenant.
[JobFunction("NightlyReportFanOut", cronExpression: "0 2 * * *")]
public static async Task FanOutAsync(IServiceProvider sp, CancellationToken ct)
{
    var scheduler = sp.GetRequiredService<IJobScheduler>();

    // ITenantDirectory is the framework's optional catalog enumeration capability (see
    // [Tenant Catalog](#tenant-catalog)) — available only when a tenant catalog store is
    // configured via `.Catalog(...)`. An app that has not configured a catalog enumerates
    // tenants through its own means instead (a direct query against its own tenant table, an
    // app-owned directory service, etc.) — the loop below is identical either way.
    var tenants = sp.GetRequiredService<ITenantDirectory>();

    foreach (var tenant in await tenants.GetAllAsync(ct))
    {
        if (!tenant.IsEnabled)
        {
            continue;
        }

        // Explicit TenantId is REQUIRED: the cron handler runs system-scope, so there is no
        // ambient tenant for PropagateTenant() to capture here. Relying on ambient capture
        // inside a cron handler would silently persist tenantless jobs.
        await scheduler.EnqueueAsync(
            new TenantReportRequest("nightly"),
            new EnqueueOptions { TenantId = tenant.Id, Description = $"nightly-report-{tenant.Id}" },
            ct
        );
    }
}
```

The framework ships no per-tenant cron rows or per-tenant cron expressions — cron itself always stays system-scope, and the fan-out loop above is application code either way. What changed: the framework now ships an *optional* enumeration capability, `ITenantDirectory.GetAllAsync()`, implemented by all v1 tenant-catalog stores (in-memory, configuration, EF Core) — available only when a catalog is configured. This supersedes the previous "the framework owns no tenant enumeration" position; the framework still builds no fan-out orchestration on top of enumeration (no scheduling, no batching, no built-in cron-to-tenant mapping) — only the list-of-tenants primitive. An app that has not configured a catalog still owns enumeration entirely itself, exactly as before. Do not confuse the framework's `ITenantDirectory` (the catalog capability shown above) with an app's own tenant-enumeration abstraction if you already have one under a similar name — they serve the same purpose but only the framework's is wired to the catalog stores.

#### Startup Diagnostics

The Jobs seam contributes three startup diagnostics through `HeadlessTenancyStartupValidator` (mirroring the Messaging seam), each fired in `StartingAsync` before any hosted service runs:

| Code | Severity | Fires when |
|---|---|---|
| `HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_ISOLATED` | Warning | `RequireTenantOnEnqueue()` is configured but Jobs propagation is off and no other tenancy seam or consumer-supplied `ICurrentTenant` contributes the ambient tenant — every non-system enqueue that omits an explicit tenant would fail. A warning, not an error, so a host that always passes an explicit `TenantId` is not blocked. |
| `HEADLESS_TENANCY_JOBS_PROPAGATION_NULL_CURRENT_TENANT` | Error | `PropagateTenant()` is configured but no tenant source is registered, so the resolved `ICurrentTenant` is only the accessor fallback whose `Id` stays null — propagation would silently no-op. |
| `HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_DISABLED` | Error | The seam recorded `require-tenant-on-enqueue` but `JobsTenancyOptions.TenantContextRequired` resolved to `false` at startup, typically because a later `Configure<JobsTenancyOptions>` clobbered the seam's `PostConfigure`. |
| `HEADLESS_TENANCY_JOBS_REJECT_CROSS_TENANT_DISABLED` | Error | The seam recorded `reject-cross-tenant-enqueue` but `JobsTenancyOptions.RejectCrossTenantEnqueue` resolved to `false` at startup — same clobber shape as above. |

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

Use `.RequireTenantOnPublish()` to require every publish to resolve a tenant identifier. When enabled, the publish wrapper checks `PublishOptions.TenantId` first, then falls back to the ambient `ICurrentTenant.Id`. If neither resolves a value, the publish fails with `Headless.MultiTenancy.MissingTenantContextException`. This is the messaging sibling of the EF write guard (#234) and the HTTP authorization requirement.

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

## Headless.MultiTenancy.Abstractions

### Problem Solved

Provides a storage- and host-independent contract surface for reading and scoping the ambient tenant identity, and for looking up tenant metadata by identifier or canonical id, so packages across the framework (EF Core, Jobs, Messaging, Api, Permissions, Settings, Features, ...) can depend on one shared set of tenant types without pulling in an implementation package. Splits into two halves: the tenant-context contracts (`ICurrentTenant` and friends — always relevant) and the tenant-catalog contracts (`ITenantStore` and friends — relevant only to hosts that opt in to catalog resolution; see [Tenant Catalog](#tenant-catalog)).

### Key Features

- **Tenant context**:
    - `ICurrentTenant` — reads the ambient tenant id/name for the current async execution scope and scopes a temporary override via `Change(id, name)`
    - `ICurrentTenantAccessor` — low-level read/write slot for the ambient `TenantInformation`, intended for framework infrastructure (for example middleware that sets the tenant from a JWT claim)
    - `ITenantWriteGuardBypass` — tracks an operation-local bypass for intentional host or admin tenant-owned writes
    - `CrossTenantWriteException` — thrown when a tenant write guard detects a tenant-owned write that does not match the current tenant context
    - `MissingTenantContextException` — thrown when an operation requires an ambient tenant context but none is available
- **Tenant catalog** (opt-in; see `Headless.MultiTenancy`'s `Catalog(...)` for wiring):
    - `TenantInfo` — non-generic, non-sealed canonical tenant metadata: `Id` (canonical id), `Identifier` (public-facing, pre-normalized), `Name`, `IsEnabled`, `ExtraProperties` (read-along payload, never queried)
    - `ITenantStore` — read-only store SPI: `FindByIdentifierAsync(normalizedIdentifier)`, `FindByIdAsync(id)`
    - `ITenantDirectory` — optional enumeration capability (`GetAllAsync()`) a store implements alongside `ITenantStore`; all v1 stores implement it
    - `ICurrentTenantInfo` — reads catalog `TenantInfo` for the ambient tenant (`GetAsync()`); resolves per read, never throws for an absent tenant
    - `TenantResolutionOutcome` / `TenantResolutionKind` — the closed outcome set produced by identifier-based resolution: `Resolved`, `Unknown`, `Disabled`, `Ignored`, `Invalid`. `TenantResolutionKind.None` is the reserved zero value — it marks an uninitialized outcome (a bare `default(TenantResolutionOutcome)`, an auto-valued test double, or a consumer-supplied `ITenantCatalogService` returning it) rather than a sixth resolution outcome; the catalog itself never produces it, and consumers should treat seeing it as a contract violation

### Installation

```bash
dotnet add package Headless.MultiTenancy.Abstractions
```

Most applications receive this package transitively through `Headless.Core` (which implements the tenant-context contracts) or through a seam package (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`, `Headless.MultiTenancy`). Add it directly only when authoring a package that needs these contracts without pulling in an implementation — for example a custom `ITenantStore` over an app-owned tenant aggregate.

### Quick Start

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
```

`ICurrentTenant` and `ICurrentTenantAccessor` are contracts only — this package registers nothing. `Headless.Core` supplies the default `AsyncLocal`-backed implementations. `ITenantStore`, `ITenantDirectory`, and `ICurrentTenantInfo` are also contracts only — `Headless.MultiTenancy`'s `Catalog(...)` builder wires the store implementation and the catalog service.

### Configuration

None. This is an abstractions-only package.

### Dependencies

- `Headless.Primitives` (for `TenantInformation` and `ExtraProperties`)

### Side Effects

None.

---

## Headless.MultiTenancy

### Problem Solved

Provides one composition surface for tenant posture across Headless packages while keeping each package in charge of its own behavior. It owns the root builder, shared manifest, and validator contracts, plus the opt-in tenant catalog: a family-owned service that normalizes tenant identifiers, caches read-through lookups, and canonicalizes identifier→id before ambient context is set. It does not itself resolve tenants over HTTP, enforce authorization, propagate messages, or guard EF writes — seam packages (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`) contribute their own fluent extensions on top of this builder, and `Headless.Api.Core` is what turns catalog resolution into an HTTP pipeline behavior.

### Key Features

- **Posture composition**:
    - `AddHeadlessTenancy(Action<HeadlessTenancyBuilder> configure)` — root configuration entry point; registers the shared manifest and startup validator, then invokes the configure callback.
    - `HeadlessTenancyBuilder` — root builder passed to the configure callback. Exposes `ApplicationBuilder`, `Services`, `Manifest`, and `RecordSeam(...)`. Seam packages extend it with their own methods (`.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, `.Jobs(...)`, `.EntityFramework(...)`, `.Catalog(...)`).
    - `TenantPostureManifest` — thread-safe, singleton, non-PII record of seam posture: status (`TenantPostureStatus`), capability labels, and runtime markers. Diagnostic breadcrumb only; records do not create enforcement.
    - `TenantPostureStatus` — enum whose ordinal is posture precedence: `Configured(0) < Propagating(1) < Guarded(2) < Enforcing(3)`. `RecordSeam` always keeps the strongest status across contributions.
    - `IHeadlessTenancyValidator` / `HeadlessTenancyDiagnostic` — extension hook for seam packages to emit startup diagnostics. Diagnostics can be `Information`, `Warning`, or startup-blocking `Error`.
    - `HeadlessTenancyStartupValidator` — `IHostedLifecycleService` that runs all registered validators in `StartingAsync` before any other hosted service starts; throws `HeadlessTenancyValidationException` (an `InvalidOperationException`) on any `Error` diagnostic.
    - `HeadlessTenancyValidationContext` — context record passed to validators: `Services` (the app `IServiceProvider`) + `Manifest`.
- **Tenant catalog** (opt-in; see [Tenant Catalog](#tenant-catalog) for the concepts and extension tiers):
    - `HeadlessTenancyBuilder.Catalog(Action<HeadlessTenancyCatalogSetupBuilder> configure)` — configures `TenantCatalogOptions`, registers exactly one storage provider (`UseInMemory`/`UseConfiguration`/`UseEntityFramework`, guarded — a second registration fails startup), and wires the catalog service and the `ICurrentTenantInfo` accessor.
    - `InMemoryTenantStore` / `UseInMemory(...)` — seeded, immutable snapshot store for tests and small apps; rejects duplicate normalized identifiers or ids at startup. Three overloads: `Action<InMemoryTenantStoreOptions>`, `Action<InMemoryTenantStoreOptions, IServiceProvider>`, and a raw `InMemoryTenantStoreOptions` instance — deliberately no `UseInMemory(IConfiguration)` overload, because `TenantInfo` has no parameterless constructor for the options binder to construct from. Bind an operator-managed tenant list from configuration with `UseConfiguration(...)` instead.
    - `ConfigurationTenantStore` / `UseConfiguration(...)` — options-bound, read-only snapshot store (three overloads: `IConfiguration`, `Action<T>`, `Action<T, IServiceProvider>`); reload requires a process restart. The Entity Framework Core store ships separately in `Headless.MultiTenancy.Storage.EntityFramework`.
    - `ITenantCatalogService` (default `TenantCatalogService`) — HTTP-agnostic resolution: normalize → shape-validate → ignored-check → cache/store lookup, returning a `TenantResolutionOutcome`; also serves `TenantInfo` lookups by canonical id for the accessor. Store exceptions propagate unwrapped; a cache read or write fault degrades to a miss/no-op rather than failing the resolution.
    - `TenantCatalogOptions` — `CacheExpiration` (default 5 min), `UnknownIdentifierCacheExpiration` (negative-cache window, default 30 s, `TimeSpan.Zero` disables it), `IgnoredIdentifiers`, `MaxIdentifierLength` (default 63), `IdentifierPattern` (default DNS-label slug), `DetailedResolutionErrors` (default `false`).
    - `ICurrentTenantInfo` — registered by default as a no-op (`GetAsync()` always returns `null`) until `Catalog(...)` replaces it with the catalog-backed implementation. `AddTypedCurrentTenantInfo<T>(projection)` registers the opt-in `ICurrentTenantInfo<T>` typed leaf accessor.
    - `TenancyErrorCodes` / `TenancyMessageDescriber` — the `g:tenant_resolution_failed` / `g:tenant_unknown` / `g:tenant_disabled` / `g:tenant_identifier_mismatch` / `g:tenant_identifier_invalid` ProblemDetails codes consumed by `Headless.Api.Core`'s rejection mapping.
    - `TenantCatalogPosture` — shared, non-PII seam/capability constants (`Catalog` seam, `catalog-accessor`/`catalog-resolution` capabilities) that this package and `Headless.Api.Core` both write to and that `TenantCatalogPostureValidator` cross-checks at startup.

### Design Notes

- **`HeadlessTenancyStartupValidator`** is registered as an `IHostedLifecycleService` (not a plain `IHostedService`) so `StartingAsync` runs before any other hosted service's `StartAsync`. This ordering guarantees that a misconfigured posture fails the host before background workers or messaging consumers begin processing under the wrong assumptions. The validation itself is synchronous inside `StartingAsync` — the task is only faulted if the host's own startup continuation throws; the validated diagnostics surface as the typed `HeadlessTenancyValidationException` before the task is awaited.
- **Two independent cache namespaces, one shared expiration.** The catalog caches the identifier→id mapping and the id→`TenantInfo` shape as separate `ICache<T>` item types, both defaulting to `TenantCatalogOptions.CacheExpiration`. A single store hit from an identifier lookup populates both namespaces in one pass. The cache always holds the base `TenantInfo` shape — a subclass returned by an app-owned store is cloned down before caching and re-hydrated (or downcast, when the store returns the subtype directly) on read, so no polymorphic instance is ever serialized into the cache.
- **Accessor-only is a first-class, non-failing posture.** A host can call `Catalog(catalog => catalog.UseInMemory(...))` without ever calling `Headless.Api.Core`'s `.Http(http => http.ResolveFromCatalog(...))`. That combination records only the `catalog-accessor` capability — `ICurrentTenantInfo` metadata reads work, but no HTTP identifier resolution runs. `TenantCatalogPostureValidator` treats this as valid and never fails startup for it; it only fails when `catalog-resolution` is recorded without a configured store, without an actually-wired resolution pipeline, or without the status-codes rewriter that writes the R19 rejection.
- **A caching provider is a hard prerequisite of `Catalog(...)`.** This package references `Headless.Caching.Abstractions` only; the open-generic `ICache<>` implementation ships with a caching *provider* (`Headless.Caching.InMemory`, `.Redis`, `.Hybrid`). Since `TenantCatalogService` takes both cache item types as constructor dependencies, a host that configures a store but never calls `AddHeadlessCaching(...)` would start clean and then fail every tenant lookup — including plain `ICurrentTenantInfo` reads on accessor-only hosts. `TenantCatalogPostureValidator` therefore probes for a registered `ICache<T>` whenever the `catalog-accessor` capability is present and fails startup with `CATALOG_WITHOUT_CACHING_PROVIDER` when none is. It probes the registration rather than resolving the cache, so validation never builds the backing cache singleton early.
- **`UseStatusCodesRewriter()` is a hard prerequisite of catalog *resolution*.** R19's second enforcement tier (`TenantIdentifierIntegrityHandler`) only fails the authorization evaluation and marks the request; the generic tenant rejection that keeps a mismatch byte-identical to an unknown identifier is written afterwards by `StatusCodesRewriterMiddleware`. A resolution host that never registers it answers a mismatch with a bare authorization failure while an unknown identifier still gets the 404 rejection — a tenant-enumeration oracle — so `TenantCatalogPostureValidator` fails startup with `CATALOG_RESOLUTION_WITHOUT_REWRITER` whenever `catalog-resolution` is recorded without the rewriter's runtime marker. This gate is resolution-scoped, not accessor-scoped like the caching one: an accessor-only host has no tier-2 path to collapse. The marker records presence, not position — the rewriter must also be added to the pipeline *before* `UseAuthorization()` so it wraps it, since a rewriter placed downstream never observes an evaluation that short-circuits.
- **Exactly-one-storage-provider guard.** `Catalog(...)` reuses the same `GuardSingleStorageProvider` mechanism as `Headless.Settings.Core` — registering zero or more than one of `UseInMemory`/`UseConfiguration`/`UseEntityFramework` in the same `Catalog(...)` callback fails startup immediately rather than silently picking one.

### Installation

```bash
dotnet add package Headless.MultiTenancy
```

Most applications receive this package transitively through the seam packages that contribute tenancy extensions (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`). Add it directly only when authoring a custom `IHeadlessTenancyValidator`, a custom seam, or a custom `ITenantStore` without pulling in one of those packages. Add `Headless.MultiTenancy.Storage.EntityFramework` separately for the EF Core-backed catalog store.

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless();

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims())
        .Authorization(auth => auth.RequireTenant())
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
        .Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
        .EntityFramework(ef => ef.GuardTenantWrites())
);

var app = builder.Build();

app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy(); // after UseAuthentication, before UseAuthorization
app.UseAuthorization();
```

`AddHeadlessTenancy` is the only call owned by this package; the `.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, `.Jobs(...)`, and `.EntityFramework(...)` extensions are contributed by the respective seam packages once they are installed. See [Tenant Catalog](#tenant-catalog) for adding `.Catalog(...)`.

### Configuration

`Headless.MultiTenancy`'s posture surface has no options class — the builder is purely a composition surface; every seam package owns its own options and configuration binding.

`TenantCatalogOptions` (bound via `Catalog(catalog => catalog.Configure(options => ...))`) — see the table in [Tenant Catalog](#tenant-catalog).

`TenantPostureManifest` is populated at DI build time by the `configure` callback in `AddHeadlessTenancy`. Seam packages call `builder.RecordSeam(seam, status, capabilities)` to register their posture. `MarkRuntimeApplied(seam, marker)` is called by seam middleware at request time (for example, `UseHeadlessTenancy()` marks the HTTP seam's runtime slot) so startup validators can verify middleware placement.

Custom validators implement `IHeadlessTenancyValidator` and register themselves in DI before `AddHeadlessTenancy` is called. `HeadlessTenancyStartupValidator` resolves all `IHeadlessTenancyValidator` registrations from DI via `IEnumerable<IHeadlessTenancyValidator>`.

### Dependencies

- `Headless.Caching.Abstractions` — contracts only. Hosts that call `Catalog(...)` must additionally install a caching provider (`Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`) and register it with `AddHeadlessCaching(...)`.
- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Hosting`
- `Headless.MultiTenancy.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

### Side Effects

- Registers a singleton `TenantPostureManifest` via `services.AddSingleton(manifest)`.
- Registers `HeadlessTenancyStartupValidator` as `IHostedService` (via `TryAddEnumerable`; safe to call multiple times).
- Registers a default no-op scoped `ICurrentTenantInfo` (`NullCurrentTenantInfo`).
- `AddHeadlessTenancy` also invokes the caller's `configure` callback, which may register additional services from seam packages.
- `Catalog(...)` registers `TenantCatalogOptions` (validated, `ValidateOnStart`), the selected storage provider's services, `ITenantCatalogService` (scoped, backed by `TenantCatalogService`), replaces the default `ICurrentTenantInfo` with the catalog-backed implementation, and registers `TenantCatalogPostureValidator`.

---

## Headless.MultiTenancy.Storage.EntityFramework

### Problem Solved

Provides an EF Core-backed `ITenantStore` using the consumer's own `DbContext`, with schema managed through EF migrations — a shipped, convenience-default schema, not a canonical one. Apps with richer requirements can implement `ITenantStore` directly over their own aggregate instead.

### Key Features

- `setup.UseEntityFramework<TContext>()` — registers the EF storage provider via `HeadlessTenancyCatalogSetupBuilder`. It also registers a startup gate (`IHostedLifecycleService`) that validates `TContext`'s model was configured through `modelBuilder.AddHeadlessTenancyCatalog(this)`; a `DbContext` missing that call fails host startup with an actionable message instead of failing lazily the first time the catalog resolves a tenant.
- `modelBuilder.AddHeadlessTenancyCatalog(DbContext context)` — applies the `TenantRecord` entity configuration, reading the active EF Core provider so the unique identifier index can be pinned to a deterministic collation
- `TenantRecord` — the single-table entity: `Id`, `Identifier`, `NormalizedIdentifier`, `Name`, `IsEnabled`, `ExtraProperties`
- Unique index on `NormalizedIdentifier`, pinned to a case- and accent-sensitive collation (`Latin1_General_100_BIN2` on SQL Server, `C` on PostgreSQL) so a lookup never matches a row differing only by case — SQL Server's default collation is case-insensitive and would otherwise break the catalog service's ordinal lookup contract

### Design Notes

`TenantRecord` derives `NormalizedIdentifier` from `Identifier` itself through `SetIdentifier(...)` — there is no public setter for `NormalizedIdentifier`, so app-seeded rows and identifier rebrands can never carry a stale or hand-written normalized value. The entity deliberately does not implement `IMultiTenant`: the catalog sits outside the EF tenant query filter by construction.

This package ships no framework write path — read-only `FindByIdentifierAsync`/`FindByIdAsync`/`GetAllAsync` only, matching `ITenantStore`/`ITenantDirectory`. Apps insert, update, and migrate `TenantRecord` directly against their own `DbContext`.

Read paths use `IDbContextFactory<TContext>` and `AsNoTracking()`, matching `Headless.Settings.Storage.EntityFramework`.

### Installation

```bash
dotnet add package Headless.MultiTenancy.Storage.EntityFramework
```

### Quick Start

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessTenancyCatalog(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy.Catalog(catalog => catalog.UseEntityFramework<AppDbContext>());
});
```

### Configuration

None. This package binds no options of its own — `UseEntityFramework<TContext>()` takes only the `DbContext` type argument. Cache and identifier-shape behavior is controlled by `TenantCatalogOptions` on `Headless.MultiTenancy`'s `Catalog(...)` builder, not by this package.

### Dependencies

- `Headless.MultiTenancy`
- `Headless.EntityFramework`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Registers `EfTenantStore<TContext>` as a singleton, exposed as both `ITenantStore` and `ITenantDirectory`
- Registers `TenantCatalogEntityValidationStartupGate<TContext>` as an `IHostedService` (via `TryAddEnumerable`) that validates the model configuration at host startup
