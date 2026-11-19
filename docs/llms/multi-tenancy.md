---
domain: Multi-Tenancy
packages: MultiTenancy.Abstractions, MultiTenancy
---

# Multi-Tenancy

> Ambient tenant context for HTTP requests, EF Core, blobs, caches, jobs, and messages, plus an opt-in tenant catalog that maps public identifiers to tenants and serves tenant metadata.

## Orientation

Headless tenancy keeps one canonical tenant id ambient for the current async flow (`ICurrentTenant`), and each feature reads it: EF Core query filters and guards, tenant-scoped blobs and caches, job and message propagation, logs, and spans. `builder.AddHeadlessTenancy(...)` is the one place a host turns each feature's tenancy on.

| Package | Role |
| --- | --- |
| `Headless.MultiTenancy.Abstractions` | `ICurrentTenant`, `ICurrentTenantAccessor`, the tenant exceptions, `ITenantWriteGuardBypass`, and the catalog contracts (`TenantInfo`, `ITenantStore`, `ITenantDirectory`, `ICurrentTenantInfo`, `TenantResolutionOutcome`). Namespace `Headless.MultiTenancy`. |
| `Headless.MultiTenancy` | `AddHeadlessTenancy(...)`, the startup validation of the tenancy posture, `AddTenantScopedCache<T>()`, tenant telemetry options, and the opt-in catalog (`.Catalog(...)`) with its in-memory and configuration stores. |
| `Headless.MultiTenancy.Storage.EntityFramework` | The EF Core catalog store. See [Tenant catalog](multi-tenancy/catalog.md#headlessmultitenancystorageentityframework). |
| `Headless.Api` | HTTP resolution: `.Http(http => http.ResolveFromClaims())` with `app.UseHeadlessTenancy()`, or catalog identifier resolution with `app.UseHeadlessTenantCatalogResolution()`. The tenant authorization requirement: `.Authorization(auth => auth.RequireTenant())`. |
| `Headless.EntityFramework` | Tenant query filters, `.EntityFramework(ef => ef.GuardTenantWrites().GuardTenantReads())`. |
| `Headless.Messaging`, `Headless.Jobs` | `.Messaging(...)` and `.Jobs(...)`: capture the tenant when a message is published or a job is scheduled, restore it around the handler, and optionally require one. |
| `Headless.Blobs.MultiTenancy` | `.Blobs(blobs => blobs.ScopeByTenant())`: tenant-scoped blob paths. |
| `Headless.Tus` | `.Tus(tus => tus.ScopeByTenant())`: every resumable upload needs a tenant, and the `UseS3` and `UseAzure` stores keep each tenant's uploads under their own path. See [TUS tenant scoping](tus.md#tenant-scoping). |
| `Headless.Permissions` | Scopes permission grant cache keys by tenant with no setup. |

Pick how HTTP requests get their tenant:

| Resolution path | Use when | Registration |
| --- | --- | --- |
| Claim | The access token carries the canonical tenant id. The default. | `.Http(http => http.ResolveFromClaims())`, `app.UseHeadlessTenancy()` after `UseAuthentication()` |
| Catalog identifier | The tenant comes from a host name, route segment, or header before sign-in (subdomains, custom domains, pre-login pages). | `.Catalog(...)` plus `.Http(http => http.ResolveFromCatalog(...))`, `app.UseHeadlessTenantCatalogResolution()` before `UseAuthentication()`. See [Identifier resolution](multi-tenancy/identifier-resolution.md). |

Both can run in one pipeline. A request that carries both an identifier and a tenant claim must resolve to the same tenant.

### Default setup

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

// RequireTenant() fails startup unless DefaultPolicy or FallbackPolicy carries TenantRequirement.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantRequirement())
        .Build();
});

var app = builder.Build();

app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

Call `UseHeadlessTenancy()` after your own `UseAuthentication()` and before your own `UseAuthorization()`. Headless calls neither. `AddHeadless()` alone turns on no tenancy. Remove the seams the host does not use: each one you call is validated at startup.

**Verify:** run the host's [`check` verb](utilities.md#health-check-command) (`app.RunHeadlessAsync(args)`). It runs the tenancy startup validation without starting the host, and a misconfigured posture prints `Host check failed: Headless tenancy validation failed: <CODE>: <message>` and exits 1. Then send an authenticated request with no tenant claim to an endpoint without `[AllowMissingTenant]`: it returns 403 with `error.code` `g:tenant_required`.

## Agent Rules

- Read the tenant from `ICurrentTenant`. Do not pass a tenant id through method parameters once the execution context has one.
- Turn tenancy on through `builder.AddHeadlessTenancy(...)`, so the HTTP, authorization, messaging, jobs, EF, and blobs posture sits in one block and is validated at startup.
- Keep `ICurrentTenant` a singleton. Replace it only with another singleton. `AddHeadlessDbContextServices()` fails startup with `InvalidServiceLifetimeException` when it is scoped or transient.
- To run code under a tenant outside HTTP, wrap it in `using (currentTenant.Change(tenantId)) { ... }`. Without the `using`, the tenant leaks into the rest of the async flow.
- When an operation names its tenant (a console command, a job, a message for one tenant) and may run with or without an ambient tenant, use `using (currentTenant.EnterOrVerify(tenantId)) { ... }`. It enters the tenant when none is set, keeps the ambient tenant when it is the same id (ordinal), and throws `TenantMismatchException` (403 `g:tenant_mismatch` over HTTP) when it is a different one. It checks consistency, not access: authorize the caller for `tenantId` first.
- Mint the tenant claim (default `tenant_id`) only on principals scoped to one tenant. Host, admin, service-account, and cross-tenant principals carry no claim, so `ICurrentTenant.IsAvailable` stays false for them. Change the claim name with `ResolveFromClaims(options => options.ClaimType = "...")` only when your identity system uses another name.
- Put `new TenantRequirement()` in `FallbackPolicy` or `DefaultPolicy`. Mark endpoints that run without a tenant with `[AllowMissingTenant]` or `.AllowMissingTenant()`, and opt back in under them with `[RequireTenant]` or `.RequireTenant()`. Named policies are not checked: add `TenantRequirement` to each one that must require a tenant. See [HTTP tenancy](multi-tenancy/http.md#http-authorization-requirement).
- Use `[SkipTenantResolution]` or `.SkipTenantResolution()`, not `[AllowMissingTenant]`, when a principal carries a tenant claim but the endpoint must not run under it, such as an admin or cross-tenant endpoint whose EF queries would otherwise be filtered to one tenant. Add `.AllowMissingTenant()` too when the endpoint sits under a tenant-required policy.
- Do not rely on HTTP middleware for SignalR hub calls, jobs, or message consumers. Each needs its own tenant source. See [Background work](multi-tenancy/background-work.md).
- Do not add your own `TenantId` log attribute or `tenant.id` span tag. ServiceDefaults adds both wherever a tenant is ambient. See [Observability](multi-tenancy/observability.md).
- In EF Core, declare `IsTenantOwned()` after `base.OnModelCreating(modelBuilder)`, or implement `IMultiTenant`. Keep platform-level entities (outbox rows, audit events, shared catalogs) unowned.
- Use a fresh `DbContext`, and fresh Identity store and manager scopes, after a tenant change. `FindAsync` can return an already tracked entity without running the tenant filter.
- Write explicit tenant predicates in raw SQL. Neither the query filter nor the write guard touches raw SQL, and `BeginBypass()` changes nothing there.
- Mark every `IgnoreMultiTenancyFilter()` call with a `// MULTI-TENANCY-BYPASS: <reason>` comment naming the approved scenario. It bypasses reads only. Wrap an intentional cross-tenant write in `ITenantWriteGuardBypass.BeginBypass()`.
- Never retry `CrossTenantWriteException`. Exclude it from catch-all retry policies: a retry fails the same way or, if the ambient tenant changed, persists the unsafe write.
- In a tenant-aware host, scope blobs with `.Blobs(blobs => blobs.ScopeByTenant())` and tenant-owned caches with `services.AddTenantScopedCache<T>()`. Files and cache entries have no tenant column. See [Blobs and caches](multi-tenancy/blobs-and-caches.md).
- In a tenant-aware host that serves resumable uploads, add `.Tus(tus => tus.ScopeByTenant())`. An upload endpoint already answers 404 to a caller outside the upload's tenant. The seam refuses an upload created with no tenant (403) and gives each tenant its own storage path. Run a job that reads an upload through a scoped store inside `currentTenant.Change(tenantId)`. See [TUS tenant scoping](tus.md#tenant-scoping).
- Expect the shared `t:` permission cache scope for host-level work with no tenant. It is shared, not isolated.
- For recurring per-tenant work, fan out from a system-scope cron job to one time job per tenant with an explicit `JobOptions.TenantId`. Cron jobs never carry a tenant. See [Cron Fan-Out](multi-tenancy/background-work.md#cron-fan-out).
- Configure `.Catalog(...)` only when you need identifier resolution or tenant metadata. Read [Tenant catalog](multi-tenancy/catalog.md) first: the catalog needs a caching provider, and every store write needs a cache eviction.

## Core Concepts

### Isolation model

Headless implements logical isolation: tenants share a database, a schema, and a deployment, and the ambient canonical tenant id separates them through EF query filters, the write guard, caches, blobs, jobs, and messages. A database or schema per tenant, per-tenant connection strings, dedicated compute, and regional data placement are out of scope: Headless ships no per-tenant connection resolution and never routes a tenant to its own store. Decide isolation needs before you adopt this model. If blast radius, compliance, dedicated capacity, or data residency demand a physical boundary, you build it. Within the logical model, some paths bypass enforcement ([Defense Layers and Known Gaps](multi-tenancy/entity-framework.md#defense-layers-and-known-gaps)), and rate limiting per tenant is yours ([DoS and rate limiting](multi-tenancy/identifier-resolution.md#dos-and-rate-limiting)).

### Tenant Semantics

`ICurrentTenantAccessor.Current` has three states:

1. `Current == null`: no tenant scope was ever set in this execution flow.
2. `Current.TenantId == null`: host context, set explicitly with `Change(null)`.
3. `Current.TenantId == "TENANT-123"`: tenant-scoped execution.

When a request has no tenant claim, the HTTP middleware does not call `Change(null)`. The request stays in state 1, so "never set" and "explicitly host" stay distinct.

## Topics

| Read when | Topic |
| --- | --- |
| You wire claim resolution, the tenant authorization requirement, endpoint opt-outs, or the 403 `g:tenant_required` response. | [HTTP tenancy](multi-tenancy/http.md) |
| You need tenant metadata (`ICurrentTenantInfo`), choose a catalog store, or change tenants at run time. | [Tenant catalog](multi-tenancy/catalog.md) |
| You resolve the tenant from a host name, route segment, or header before authentication. | [Identifier resolution](multi-tenancy/identifier-resolution.md) |
| You make EF Core entities tenant-owned or turn on the write or read guard. | [Entity Framework](multi-tenancy/entity-framework.md) |
| You store files or cache entries per tenant, or reason about permission cache scope. | [Blobs and caches](multi-tenancy/blobs-and-caches.md) |
| You run tenant work in jobs, message consumers, terminal callbacks, or SignalR hubs. | [Background work](multi-tenancy/background-work.md) |
| You query logs, spans, or metrics by tenant, or rename the tenant attributes. | [Observability](multi-tenancy/observability.md) |

## Testing host wiring

A test that builds the host, such as one using `WebApplicationFactory`, runs the tenancy startup validation when the host starts.

- Include `UseHeadlessTenancy()` in the test pipeline when the test exercises HTTP tenancy. Without it, startup fails with `HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING`.
- To skip validation, do not call `AddHeadlessTenancy(...)`, or compose only the seams the test exercises.
- To assert the 403 `g:tenant_required` ProblemDetails, or any other Headless exception-handler response, build the host in a Production environment. The common pattern calls `app.UseExceptionHandler()` only outside Development, so a Development test client shows the developer error page instead.

## Failure Modes to Watch

- Without `UseHeadlessTenancy()`, requests stay in host scope even when the token has a tenant claim. Startup fails with `HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING` when `.Http(...)` configured claim resolution.
- `.Authorization(auth => auth.RequireTenant())` without `TenantRequirement` in `DefaultPolicy` or `FallbackPolicy` fails startup with `HEADLESS_TENANCY_AUTHORIZATION_POLICY_MISSING`.
- `UseHeadlessTenancy()` before `UseAuthentication()` sees no authenticated principal, so no request gets a tenant.
- The host-level permission cache scope `t:` is shared, not tenant-isolated.
- `ScopeByTenant()` does not wrap a store registered directly as `IBlobStorage`, and never wraps `IBlobContainerManager`.
- `.Tus(tus => tus.ScopeByTenant())` gives a per-tenant path only to the `UseS3` and `UseAzure` stores. A store passed to `UseStore` keeps its own layout, and an endpoint mapped with tusdotnet's `MapTus` checks no tenant at all.
- `RemoveByTagAsync`, `ClearAsync`, and `FlushAsync` on a tenant-scoped `ICache<T>` act on the whole underlying cache.
- `IgnoreMultiTenancyFilter()` bypasses reads only. Guarded writes still fail.

---

## Headless.MultiTenancy.Abstractions

The tenant context and catalog contracts, with their default implementations.

### API and behavior

- **Tenant context:**
    - `ICurrentTenant`: the ambient tenant `Id`, `Name`, and `IsAvailable` for the current async flow. `Change(id, name)` scopes an override and restores the previous tenant on dispose. `EnterOrVerify(tenantId)` enters or checks a named tenant.
    - `ICurrentTenantAccessor`: the low-level slot that holds the ambient `TenantInformation`. Middleware and framework infrastructure write it. Application code reads `ICurrentTenant`.
    - `ITenantWriteGuardBypass`: an operation-local bypass of the EF write guard for intentional host or admin writes.
    - `MissingTenantContextException`: an operation needs a tenant and none is ambient. Over HTTP it maps to 403 `g:tenant_required`.
    - `CrossTenantWriteException`: a guarded write targets another tenant's row. Over HTTP it maps to 409 `g:cross_tenant_write`.
    - `TenantMismatchException`: `EnterOrVerify` found a different ambient tenant. Over HTTP it maps to 403 `g:tenant_mismatch`.
    - `TenantStorageSegment.IsValid(tenantId)`: whether a tenant id can be one storage path segment. It starts and ends with a lowercase ASCII letter or digit and holds only lowercase ASCII letters, digits, `.`, `_`, and `-`. Blob path-prefix scoping and TUS tenant scoping both apply it, so an id one accepts the other accepts too.
- **Tenant catalog contracts** (wired by `Headless.MultiTenancy`'s `.Catalog(...)`):
    - `TenantInfo`: non-sealed tenant metadata with `Id` (canonical id), `Identifier` (public name), `Name`, `IsEnabled`, and `ExtraProperties`.
    - `ITenantStore`: the read-only store contract, `FindByIdentifierAsync(normalizedIdentifier)` and `FindByIdAsync(id)`.
    - `ITenantDirectory`: optional enumeration, `GetAllAsync()`. Every shipped store implements it.
    - `ICurrentTenantInfo`: `GetAsync()` returns the catalog `TenantInfo` of the ambient tenant, or `null`.
    - `TenantResolutionOutcome` and `TenantResolutionKind`: the result of identifier resolution, one of `Resolved`, `Unknown`, `Disabled`, `Ignored`, or `Invalid`. `TenantResolutionKind.None` is the zero value of an uninitialized outcome, never a resolution result. A custom store or catalog service that returns it has a bug. Return `Unknown` for an identifier with no tenant.

### Install

```bash
dotnet add package Headless.MultiTenancy.Abstractions
```

Applications get this package through `Headless.Api`, `Headless.Messaging`, `Headless.Jobs`, `Headless.EntityFramework`, or `Headless.MultiTenancy`. Reference it directly only in a library that needs the contracts without an implementation, such as a custom `ITenantStore` over your own tenant aggregate.

### Setup and use

```csharp
public sealed class OrderService(ICurrentTenant currentTenant)
{
    public Order CreateOrder(CreateOrderRequest request)
    {
        return new Order { Id = Guid.NewGuid(), TenantId = currentTenant.Id };
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

This package registers nothing. It ships the default implementations (`CurrentTenant`, `AsyncLocalCurrentTenantAccessor`, `NullCurrentTenant`, `TenantWriteGuardBypass`), and the feature packages register them. `AddHeadless()` and `AddHeadlessDbContextServices()` register `CurrentTenant`. `AddHeadlessTenancy` and `AddHeadlessDbContextServices()` both register `TenantWriteGuardBypass`. A custom store that honors the write guard outside those paths registers it with `services.TryAddSingleton<ITenantWriteGuardBypass, TenantWriteGuardBypass>()`. Keep it a singleton: the bypass state lives on the instance, so a bypass begun on one instance is invisible to another.

**Verify:** in a test, inside `using (currentTenant.Change("t1"))`, `currentTenant.Id` is `"t1"` and `IsAvailable` is `true`. After the `using` ends, the previous tenant is back.

### Configuration

None.

---

## Headless.MultiTenancy

The root tenancy registration, its startup validation, tenant-scoped caches, tenant telemetry options, and the opt-in tenant catalog.

### API and behavior

- `AddHeadlessTenancy(Action<HeadlessTenancyBuilder> configure)`: registers the posture manifest and the tenancy startup validation, then runs `configure`. Feature packages add the builder methods: `.Http(...)` and `.Authorization(...)` (`Headless.Api`), `.Messaging(...)`, `.Jobs(...)`, `.EntityFramework(...)`, `.Blobs(...)` (`Headless.Blobs.MultiTenancy`), `.Tus(...)` (`Headless.Tus`), and `.Catalog(...)` and `.Telemetry(...)` (this package). Calling it more than once is safe.
- **Startup validation.** Before any hosted service starts, every tenancy validator runs. An `Error` diagnostic fails the host with `HeadlessTenancyValidationException` (an `InvalidOperationException`) whose message lists each `CODE: message`. When it is the only failing startup validator, the host sees it unwrapped. Alongside other failures it is an inner exception of a `StartupValidationException`. Warnings and information diagnostics are logged with their code. The `check` and `describe` verbs run the same validation.
- `TenantPostureManifest` and `TenantPostureStatus`: a singleton record of which seams each feature configured, with status `Configured < Propagating < Guarded < Enforcing`. It holds no tenant data. A record creates no enforcement.
- **Custom seams.** `HeadlessTenancyBuilder` exposes `Services`, `Manifest`, and `RecordSeam(seam, status, capabilities)`. When a seam is recorded more than once, the strongest status wins. Middleware calls `TenantPostureManifest.MarkRuntimeApplied(seam, marker)` when it is added to the pipeline, so a validator can check that it was.
- `IHeadlessTenancyValidator`, `HeadlessTenancyDiagnostic`, and `HeadlessTenancyValidationContext`: write your own posture check. Register the validator in DI. A `HeadlessTenancyDiagnostic` is `Information`, `Warning`, or a startup-blocking `Error`.
- `AddTenantScopedCache<T>()`: registers `ICache<T>` keyed by the ambient tenant. See [Application caches](multi-tenancy/blobs-and-caches.md#application-caches).
- `.Telemetry(Action<TenantTelemetryOptions>)` and `TenantTelemetry.TagActivity(activity, options, tenantId)`: see [Observability](multi-tenancy/observability.md).
- `.Catalog(Action<HeadlessTenancyCatalogSetupBuilder>)`, `ICurrentTenantInfo`, `ITenantCatalogCacheInvalidator`, `TenantCatalogOptions`, and the in-memory and configuration stores: see [Tenant catalog](multi-tenancy/catalog.md).
- `TenancyErrorCodes` and `TenancyMessageDescriber`: the catalog rejection codes `g:tenant_resolution_failed`, `g:tenant_unknown`, `g:tenant_disabled`, `g:tenant_identifier_mismatch`, and `g:tenant_identifier_invalid`.

### Install

```bash
dotnet add package Headless.MultiTenancy
```

Applications usually get this package through `Headless.Api`, `Headless.Messaging`, or `Headless.EntityFramework`. Reference it directly to write a custom `IHeadlessTenancyValidator` or `ITenantStore`. Add `Headless.MultiTenancy.Storage.EntityFramework` for the EF Core catalog store.

### Setup and use

The [default setup](#default-setup) is the setup for this package. `AddHeadlessTenancy` and `AddTenantScopedCache<T>()` are the calls it owns. The other builder methods appear once their packages are installed.

**Verify:** as for the [default setup](#default-setup), the `check` verb passes the tenancy startup validation.

### Configuration

The builder has no options class of its own. Each feature package owns its tenancy options. `TenantTelemetryOptions` is set through `.Telemetry(...)` ([Observability](multi-tenancy/observability.md#configuration)). `TenantCatalogOptions` is set through `.Catalog(catalog => catalog.Configure(...))` ([Tenant catalog](multi-tenancy/catalog.md#configuration)).

### Runtime behavior

- Registers a no-op scoped `ICurrentTenantInfo` whose `GetAsync()` returns `null`, until `.Catalog(...)` replaces it.
- Registers `ITenantWriteGuardBypass` as a singleton with `TryAddSingleton`. A registration made earlier, including the one from `AddHeadlessDbContextServices()`, wins, so the host has one instance.
