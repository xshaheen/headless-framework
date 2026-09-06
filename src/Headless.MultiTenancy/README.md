# Headless.MultiTenancy

## Problem Solved

Provides one composition surface for tenant posture across Headless packages while keeping each package in charge of its own behavior. It owns the root builder, shared manifest, and validator contracts, plus the opt-in tenant catalog: a family-owned service that normalizes tenant identifiers, caches read-through lookups, and canonicalizes identifier→id before ambient context is set. It does not itself resolve tenants over HTTP, enforce authorization, propagate messages, or guard EF writes — seam packages (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`) contribute their own fluent extensions on top of this builder, and `Headless.Api.Core` is what turns catalog resolution into an HTTP pipeline behavior.

## Key Features

- **Posture composition**:
    - `AddHeadlessTenancy(Action<HeadlessTenancyBuilder> configure)` — root configuration entry point; registers the shared manifest and startup validator, then invokes the configure callback.
    - `HeadlessTenancyBuilder` — root builder passed to the configure callback. Exposes `ApplicationBuilder`, `Services`, `Manifest`, and `RecordSeam(...)`. Seam packages extend it with their own methods (`.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, `.Jobs(...)`, `.EntityFramework(...)`, `.Catalog(...)`).
    - `TenantPostureManifest` — thread-safe, singleton, non-PII record of seam posture: status (`TenantPostureStatus`), capability labels, and runtime markers. Diagnostic breadcrumb only; records do not create enforcement.
    - `TenantPostureStatus` — enum whose ordinal is posture precedence: `Configured(0) < Propagating(1) < Guarded(2) < Enforcing(3)`. `RecordSeam` always keeps the strongest status across contributions.
    - `IHeadlessTenancyValidator` / `HeadlessTenancyDiagnostic` — extension hook for seam packages to emit startup diagnostics. Diagnostics can be `Information`, `Warning`, or startup-blocking `Error`.
    - `HeadlessTenancyStartupValidator` — `IHostedLifecycleService` that runs all registered validators in `StartingAsync` before any other hosted service starts; throws `HeadlessTenancyValidationException` (an `InvalidOperationException`) on any `Error` diagnostic.
- **Tenant catalog** (opt-in; see the "Tenant Catalog" section of the multi-tenancy domain doc for the concepts and extension tiers):
    - `HeadlessTenancyBuilder.Catalog(Action<HeadlessTenancyCatalogSetupBuilder> configure)` — configures `TenantCatalogOptions`, registers exactly one storage provider (`UseInMemory`/`UseConfiguration`/`UseEntityFramework`, guarded — a second registration fails startup), and wires the catalog service and the `ICurrentTenantInfo` accessor.
    - `InMemoryTenantStore` / `UseInMemory(...)` — seeded, immutable snapshot store for tests and small apps; rejects duplicate normalized identifiers or ids at startup. Three overloads: `Action<InMemoryTenantStoreOptions>`, `Action<InMemoryTenantStoreOptions, IServiceProvider>`, and a raw `InMemoryTenantStoreOptions` instance — deliberately no `UseInMemory(IConfiguration)` overload, because `TenantInfo` has no parameterless constructor for the options binder to construct from. Bind an operator-managed tenant list from configuration with `UseConfiguration(...)` instead.
    - `ConfigurationTenantStore` / `UseConfiguration(...)` — options-bound, read-only snapshot store (three overloads: `IConfiguration`, `Action<T>`, `Action<T, IServiceProvider>`); reload requires a process restart. The Entity Framework Core store ships separately in `Headless.MultiTenancy.Storage.EntityFramework`.
    - `ITenantCatalogService` (default `TenantCatalogService`) — HTTP-agnostic resolution: normalize → shape-validate → ignored-check → cache/store lookup, returning a `TenantResolutionOutcome`; also serves `TenantInfo` lookups by canonical id for the accessor. Store exceptions propagate unwrapped; a cache read or write fault degrades to a miss/no-op rather than failing the resolution.
    - `TenantCatalogOptions` — `CacheExpiration` (default 5 min), `UnknownIdentifierCacheExpiration` (negative-cache window, default 30 s, `TimeSpan.Zero` disables it), `IgnoredIdentifiers`, `MaxIdentifierLength` (default 63), `IdentifierPattern` (default DNS-label slug), `DetailedResolutionErrors` (default `false`).
    - `ICurrentTenantInfo` — registered by default as a no-op (`GetAsync()` always returns `null`) until `Catalog(...)` replaces it with the catalog-backed implementation. `AddTypedCurrentTenantInfo<T>(projection)` registers the opt-in `ICurrentTenantInfo<T>` typed leaf accessor.
    - `TenancyErrorCodes` / `TenancyMessageDescriber` — the `g:tenant_resolution_failed` / `g:tenant_unknown` / `g:tenant_disabled` / `g:tenant_identifier_mismatch` / `g:tenant_identifier_invalid` ProblemDetails codes consumed by `Headless.Api.Core`'s rejection mapping.
    - `TenantCatalogPosture` — shared, non-PII seam/capability constants (`Catalog` seam, `catalog-accessor`/`catalog-resolution` capabilities) that this package and `Headless.Api.Core` both write to and that `TenantCatalogPostureValidator` cross-checks at startup.

## Design Notes

- **`HeadlessTenancyStartupValidator`** is registered as an `IHostedLifecycleService` (not a plain `IHostedService`) so `StartingAsync` runs before any other hosted service's `StartAsync`. This ordering guarantees that a misconfigured posture fails the host before background workers or messaging consumers begin processing under the wrong assumptions.
- **Two independent cache namespaces, one shared expiration.** The catalog caches the identifier→id mapping and the id→`TenantInfo` shape as separate `ICache<T>` item types, both defaulting to `TenantCatalogOptions.CacheExpiration`. A single store hit from an identifier lookup populates both namespaces in one pass. The cache always holds the base `TenantInfo` shape — a subclass returned by an app-owned store is cloned down before caching and re-hydrated (or downcast, when the store returns the subtype directly) on read, so no polymorphic instance is ever serialized into the cache.
- **Identifier resolution reads through `GetOrAddAsync`; two paths deliberately do not.** The identifier namespace uses the cache's factory-backed read, so an expiry rollover under concurrent load for one identifier costs a single store read rather than one per caller (in-process single-flight; cross-node deduplication would additionally require an `ICacheFactoryLockProvider`). The factory writes the id namespace as a side effect of the same store hit, so one store read still populates both. A factory-backed read always persists whatever the factory returns, which two paths cannot accept: the id namespace must never cache an id that has no catalog row (`FindByIdAsync` would then keep answering `null` for a tenant that has since appeared), and a host that sets `UnknownIdentifierCacheExpiration = TimeSpan.Zero` must not write unknown identifiers at all — a zero duration is a write followed by an immediate eviction, not a skipped write. Both keep the plain read-then-conditional-write shape and trade single-flight away for their write rule.
- **Accessor-only is a first-class, non-failing posture.** A host can call `Catalog(catalog => catalog.UseInMemory(...))` without ever calling `Headless.Api.Core`'s `.Http(http => http.ResolveFromCatalog(...))`. That combination records only the `catalog-accessor` capability — `ICurrentTenantInfo` metadata reads work, but no HTTP identifier resolution runs. `TenantCatalogPostureValidator` treats this as valid and never fails startup for it; it only fails when `catalog-resolution` is recorded without a configured store, without an actually-wired resolution pipeline, or without the status-codes rewriter that writes the R19 rejection.
- **A caching provider is a hard prerequisite of `Catalog(...)`.** This package references `Headless.Caching.Abstractions` only; the open-generic `ICache<>` implementation ships with a caching *provider* (`Headless.Caching.InMemory`, `.Redis`, `.Hybrid`). Since `TenantCatalogService` takes both cache item types as constructor dependencies, a host that configures a store but never calls `AddHeadlessCaching(...)` would start clean and then fail every tenant lookup — including plain `ICurrentTenantInfo` reads on accessor-only hosts. `TenantCatalogPostureValidator` therefore probes for a registered `ICache<T>` whenever the `catalog-accessor` capability is present and fails startup with `CATALOG_WITHOUT_CACHING_PROVIDER` when none is. It probes the registration rather than resolving the cache, so validation never builds the backing cache singleton early.
- **`UseStatusCodesRewriter()` is a hard prerequisite of catalog *resolution*.** R19's second enforcement tier (`TenantIdentifierIntegrityHandler`) only fails the authorization evaluation and marks the request; the generic tenant rejection that keeps a mismatch byte-identical to an unknown identifier is written afterwards by `StatusCodesRewriterMiddleware`. A resolution host that never registers it answers a mismatch with a bare authorization failure while an unknown identifier still gets the 404 rejection — a tenant-enumeration oracle — so `TenantCatalogPostureValidator` fails startup with `CATALOG_RESOLUTION_WITHOUT_REWRITER` whenever `catalog-resolution` is recorded without the rewriter's runtime marker. This gate is resolution-scoped, not accessor-scoped like the caching one: an accessor-only host has no tier-2 path to collapse. The marker records presence, not position — the rewriter must also be added to the pipeline *before* `UseAuthorization()` so it wraps it, since a rewriter placed downstream never observes an evaluation that short-circuits.
- **Exactly-one-storage-provider guard.** `Catalog(...)` reuses the same `GuardSingleStorageProvider` mechanism as `Headless.Settings.Core` — registering zero or more than one of `UseInMemory`/`UseConfiguration`/`UseEntityFramework` in the same `Catalog(...)` callback fails startup immediately rather than silently picking one.

## Installation

```bash
dotnet add package Headless.MultiTenancy
```

Most applications receive this package transitively through the seam packages that contribute tenancy extensions (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.EntityFramework`). Add it directly only when authoring a custom `IHeadlessTenancyValidator`, a custom seam, or a custom `ITenantStore` without pulling in one of those packages. Add `Headless.MultiTenancy.Storage.EntityFramework` separately for the EF Core-backed catalog store.

## Quick Start

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

`AddHeadlessTenancy` is the only call owned by this package; the `.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, `.Jobs(...)`, and `.EntityFramework(...)` extensions are contributed by the respective seam packages once they are installed.

### Adding the tenant catalog

The catalog reads through `ICache<T>`, so the host must also register a caching provider — `Headless.Caching.Abstractions` alone is not enough:

```csharp
builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy.Catalog(catalog =>
        catalog.UseInMemory(options =>
        {
            options.Tenants.Add(new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme Inc", isEnabled: true));
        })
    );
});
```

Omitting `AddHeadlessCaching(...)` fails startup with the `CATALOG_WITHOUT_CACHING_PROVIDER` diagnostic.

This alone makes `ICurrentTenantInfo` resolve tenant metadata for whatever tenant `.Http(http => http.ResolveFromClaims())` already put in `ICurrentTenant.Id` — a valid, non-failing accessor-only posture. Reading tenant metadata by id anywhere in the app:

```csharp
public sealed class ProfileService(ICurrentTenantInfo tenantInfo)
{
    public async Task<string?> GetTenantDisplayNameAsync(CancellationToken cancellationToken) =>
        (await tenantInfo.GetAsync(cancellationToken))?.Name;
}
```

To also resolve the ambient tenant from a public identifier (host, route, header) before authentication runs, add `Headless.Api.Core`'s `.Http(http => http.ResolveFromCatalog(...))` and `app.UseHeadlessTenantCatalogResolution()`, and make sure `app.UseStatusCodesRewriter()` wraps `app.UseAuthorization()` — see the "Tenant Catalog" section of the multi-tenancy domain doc.

## Configuration

`Headless.MultiTenancy`'s posture surface has no options class — the builder is purely a composition surface; every seam package owns its own options and configuration binding.

`TenantCatalogOptions` (bound via `Catalog(catalog => catalog.Configure(options => ...))`):

| Property | Default | Notes |
|---|---|---|
| `CacheExpiration` | 5 minutes | Shared by the identifier→id and id→`TenantInfo` cache namespaces. Bounds staleness for both. |
| `UnknownIdentifierCacheExpiration` | 30 seconds | Negative-cache window for unknown identifiers. `TimeSpan.Zero` disables negative caching, and with it the identifier namespace's read-through single-flight (see Design Notes). |
| `IgnoredIdentifiers` | `[]` | Identifiers (compared case-insensitively) that end resolution with no store call and no tenant. |
| `MaxIdentifierLength` | 63 | Maximum normalized-identifier length accepted before any cache or store lookup. |
| `IdentifierPattern` | `RegexPatterns.Slug` | DNS-label shape (lowercase letters, digits, single hyphens between segments). |
| `DetailedResolutionErrors` | `false` | When `true`, surfaces granular `g:` codes/statuses instead of the secure-by-default generic rejection. Development/trusted environments only. |

`TenantPostureManifest` is populated at DI build time by the `configure` callback in `AddHeadlessTenancy`. Seam packages call `builder.RecordSeam(seam, status, capabilities)` to register their posture. `MarkRuntimeApplied(seam, marker)` is called by seam middleware at request time (for example, `UseHeadlessTenancy()` marks the HTTP seam's runtime slot) so startup validators can verify middleware placement.

Custom validators implement `IHeadlessTenancyValidator` and register themselves in DI before `AddHeadlessTenancy` is called. `HeadlessTenancyStartupValidator` resolves all `IHeadlessTenancyValidator` registrations from DI via `IEnumerable<IHeadlessTenancyValidator>`.

## Dependencies

- `Headless.Caching.Abstractions` — contracts only. Hosts that call `Catalog(...)` must additionally install a caching provider (`Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`) and register it with `AddHeadlessCaching(...)`.
- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Hosting`
- `Headless.MultiTenancy.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

## Side Effects

- Registers a singleton `TenantPostureManifest` via `services.AddSingleton(manifest)`.
- Registers `HeadlessTenancyStartupValidator` as `IHostedService` (via `TryAddEnumerable`; safe to call multiple times).
- Registers a default no-op scoped `ICurrentTenantInfo` (`NullCurrentTenantInfo`).
- `AddHeadlessTenancy` also invokes the caller's `configure` callback, which may register additional services from seam packages.
- `Catalog(...)` registers `TenantCatalogOptions` (validated, `ValidateOnStart`), the selected storage provider's services, `ITenantCatalogService` (scoped, backed by `TenantCatalogService`), replaces the default `ICurrentTenantInfo` with the catalog-backed implementation, and registers `TenantCatalogPostureValidator`.
