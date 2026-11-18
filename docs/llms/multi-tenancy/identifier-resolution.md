---
domain: Multi-Tenancy
packages:
---

# Identifier resolution

> Part of [Multi-Tenancy](../multi-tenancy.md). Resolve the tenant from a host name, route segment, or header before authentication, through the [tenant catalog](catalog.md), and harden that unauthenticated path.

## Rules for identifier resolution

- Place `UseHeadlessTenantCatalogResolution()` after `UseRouting()` and before `UseAuthentication()`. This slot differs from `UseHeadlessTenancy()` (after `UseAuthentication()`, before `UseAuthorization()`). Both can run in one pipeline.
- Make `UseStatusCodesRewriter()` wrap `UseAuthorization()`: call it before `UseAuthorization()`. It writes the mismatch rejection. Without it, startup fails with `CATALOG_RESOLUTION_WITHOUT_REWRITER`. A call after `UseAuthorization()` fails startup with `CATALOG_RESOLUTION_REWRITER_AFTER_AUTHORIZATION`. `UseHeadless()` from ServiceDefaults adds it in the right slot.
- Put `UseForwardedHeaders()` (with `KnownProxies` or `KnownNetworks`, behind a proxy), host filtering with `AllowedHosts` scoped to the tenant suffix, and `UseCors()` ahead of the resolution middleware.
- Prefer the built-in sources (`AddHostSource`, `AddRouteSource`, `AddHeaderSource`) or the `AddSource(Func<HttpContext, string?>)` delegate. Write an `ITenantIdentifierSource` class only when a source must report ambiguous input.
- Register sources in the order they should win. Register the host source before a header source wherever host names carry perimeter controls ([Header source and perimeter controls](#header-source-and-perimeter-controls)).
- Put ignored identifiers such as `www` on `TenantCatalogOptions.IgnoredIdentifiers`. Endpoints that need a tenant still need `TenantRequirement`, because an ignored or unmatched identifier runs as host context.
- Rate-limit this path in the application or at the edge ([DoS and rate limiting](#dos-and-rate-limiting)). Headless bounds no unauthenticated traffic.

## Secure-by-default rejection

An unknown identifier, a disabled tenant, and an identifier that conflicts with the caller's tenant claim all get one response: 404 `g:tenant_resolution_failed`. A rejected caller cannot tell the three apart. The collapse does not hide that an enabled tenant exists: such a request is never rejected, and it returns the application's own status. `TenantCatalogOptions.DetailedResolutionErrors = true` gives up only this indistinguishability and returns `g:tenant_unknown` (404), `g:tenant_disabled` (403), and `g:tenant_identifier_mismatch` (403). Use it in development and trusted environments only. A malformed identifier always gets 400 `g:tenant_identifier_invalid`, because its shape reveals nothing about any tenant.

## Identifier-based resolution setup

```csharp
using Headless.MultiTenancy; // TenantInfo, Catalog(...)
using Headless.Api;          // ResolveFromCatalog(...), AddHostSource(...), UseHeadlessTenantCatalogResolution()

builder.Services.AddHeadlessCaching(caching => caching.UseInMemory()); // the catalog's hard prerequisite

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy
        .Catalog(catalog =>
            catalog
                .Configure(options => options.IgnoredIdentifiers.Add("www")) // www.example.com -> host context
                .UseInMemory(options =>
                    options.Tenants.Add(new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme Inc", isEnabled: true))
                )
        )
        .Http(http =>
            http.ResolveFromCatalog(sources =>
                sources
                    .AddHostSource("{tenant}.example.com") // acme.example.com -> "acme"
                    .AddRouteSource()                      // /{tenant}/orders  -> route value "tenant"
                    .AddHeaderSource()                     // X-Tenant: acme
                    .AddSource(context => context.Request.Query["tenant"].ToString()) // null/blank -> not mine
            )
        );
});

builder.Services.AddAuthorization(); // UseAuthorization() below requires it

var app = builder.Build();

app.UseForwardedHeaders();                // behind a proxy only, with KnownProxies/KnownNetworks configured
app.UseStatusCodesRewriter();             // must wrap UseAuthorization() — writes the mismatch rejection
app.UseRouting();
app.UseCors();                            // before resolution so preflights short-circuit
app.UseHeadlessTenantCatalogResolution(); // after UseRouting, before UseAuthentication
app.UseAuthentication();
app.UseHeadlessTenancy();                 // unchanged: after UseAuthentication, before UseAuthorization
app.UseAuthorization();
```

`UseHeadless()` from `Headless.Api.ServiceDefaults` already adds `UseForwardedHeaders()` and the status-codes rewriter in the right slots. Call them yourself only on a host without ServiceDefaults, or one that set `HeadlessServiceDefaultsOptions.UseStatusCodePages` to `false`, which also drops the rewriter.

Startup fails when resolution is configured without a store (`CATALOG_RESOLUTION_WITHOUT_STORE`), without an identifier source or without `UseHeadlessTenantCatalogResolution()` (`CATALOG_RESOLUTION_WITHOUT_PIPELINE`), or without the rewriter (`CATALOG_RESOLUTION_WITHOUT_REWRITER`). On a host with only `.Catalog(...)` and no `ResolveFromCatalog(...)`, `UseHeadlessTenantCatalogResolution()` does nothing. A second call adds nothing.

`Headless.OpenApi` documents catalog resolution on every endpoint without `[SkipTenantResolution]`: each configured tenant header as a header parameter, the 400 and 404 rejections (404 and 403 with `DetailedResolutionErrors`) with their `Cache-Control: no-store` header, and a `servers` entry with a `tenant` variable for each host template without wildcards. A header is required only on an endpoint that requires a tenant, when that single header is the only source. See [OpenAPI](../openapi.md#design-constraints).

**Verify:** a request to `acme.example.com` reaches the endpoint with `ICurrentTenant.Id == "ten_123"`. A request to `nope.example.com` returns 404 `g:tenant_resolution_failed` with `Cache-Control: no-store`. A request to `www.example.com` runs as host context. The `check` verb passes the tenancy startup validation.

### Middleware ordering contract

| Middleware | Placement | Resolves from |
|---|---|---|
| `UseHeadlessTenantCatalogResolution()` | After `UseRouting()`, before `UseAuthentication()` | A public identifier, through the catalog |
| `UseHeadlessTenancy()` | After `UseAuthentication()`, before `UseAuthorization()` | The authenticated tenant claim |

The identifier middleware runs before authentication, because the identifier often decides which tenant's authentication setup applies. Placed before `UseRouting()`, it still resolves from the host, headers, and delegates, still rejects unknown and disabled identifiers, and still enforces mismatches, but `[SkipTenantResolution]` stops working. It reports that with the `HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING` warning, once per process. A route source finds nothing before routing, so every request runs as host context, and the middleware logs the Error-level `HEADLESS_TENANT_CATALOG_ROUTE_SOURCE_MISORDERED` event instead. Both events fire only once routing later assigns an endpoint, so a misordered host whose requests all 404 or all get rejected logs nothing. Do not treat a quiet log as proof of correct order.

## Source contract

`ITenantIdentifierSource.GetIdentifier(HttpContext)` returns a `TenantIdentifierSourceResult`, whose `default` is `None`:

| Result | Meaning | Middleware behavior |
|---|---|---|
| `TenantIdentifierSourceResult.None` | This source has no identifier for the request. | Tries the next source. When every source returns `None`, the request runs as host context with no store call. |
| `TenantIdentifierSourceResult.Found(identifier)` | The source read a raw identifier. | Stops. The catalog normalizes, validates, and resolves it. `Found` turns a `null`, empty, or white-space value into `None`, which matters because `Request.Headers["X-Tenant"].ToString()` returns `""` for an absent header. |
| `TenantIdentifierSourceResult.Invalid` | The input is present but ambiguous, such as two `X-Tenant` values. | Rejects with 400 `g:tenant_identifier_invalid` before any catalog call. Later sources never run. |

Keep a source synchronous and free of side effects. Do not trim, lowercase, or shape-check in a source: the catalog does that.

`AddSource(Func<HttpContext, string?>)` wraps a delegate. A `null` or blank return is `None`. An exception propagates unchanged, like a store fault, and never maps to a tenant outcome. A delegate cannot return `Invalid`: implement the interface for that.

`[SkipTenantResolution]` and `.SkipTenantResolution()` skip both resolution and rejection for the endpoint.

## Built-in sources

**Host.** `AddHostSource("{tenant}.example.com")`, `AddHostSource(options => options.Templates.Add("{tenant}.*.example.com"))`, or `AddHostSource(configuration.GetSection("Tenant:Host"))` (a section with a `Templates` array).

- `HostTenantIdentifierSourceOptions.Templates` are tried in order against the request host. The first match yields its `{tenant}` capture.
- Grammar: dot-separated labels. `{tenant}` captures exactly one label, `?` matches exactly one label, `*` matches zero or more labels, and any other label is a literal. A bare `{tenant}` matches the whole host ([Whole-host identifiers](#whole-host-custom-domain-identifiers)).
- Matching ignores case and the port, and strips one trailing dot. An IP literal or a host longer than 253 characters yields `None`.
- Startup rejects an empty template list or a template that does not parse.
- Each template match has a 100 ms timeout. A timeout, practically unreachable, yields `Invalid` and logs the `HEADLESS_TENANT_HOST_TEMPLATE_TIMEOUT` warning once per process, naming the template only, never the host.
- The source reads `Request.Host` after forwarding, never `X-Forwarded-Host`.

**Route.** `AddRouteSource()`, `AddRouteSource("org")`, `AddRouteSource(options => ...)`, or `AddRouteSource(configuration.GetSection("Tenant:Route"))` (a section with a `RouteValueName` key).

- Reads the string route value named by `RouteTenantIdentifierSourceOptions.RouteValueName`, default `tenant` (`RouteTenantIdentifierSourceOptions.DefaultRouteValueName`). A missing or non-string value is `None`: a typed route default is never converted.
- Startup rejects a blank name.
- The middleware must run after `UseRouting()` for this source to see a value ([Middleware ordering contract](#middleware-ordering-contract)).
- Every overload also makes generated links keep the tenant segment ([Link generation with the route source](#link-generation-with-the-route-source)).

**Header.** `AddHeaderSource()`, `AddHeaderSource("X-Legacy")`, `AddHeaderSource(options => ...)`, or `AddHeaderSource(configuration.GetSection("Tenant:Header"))` (a section with a `HeaderNames` array).

- Reads `HeaderTenantIdentifierSourceOptions.HeaderNames`, default `X-Tenant` (`HeaderTenantIdentifierSourceOptions.DefaultHeaderName`). The default is not `X-Tenant-Id`, because the value is a public identifier, never a tenant id.
- A requested name replaces the untouched default, then later names append: `AddHeaderSource("X-Legacy")` reads only `X-Legacy`, and `AddHeaderSource("X-Tenant").AddHeaderSource("X-Legacy")` reads both. A configuration section that lists names follows the same rule. A section without `HeaderNames` keeps the default.
- All names share one duplicate check. Exactly one non-blank value across them is the identifier. A second non-blank value, from a repeated line or from two names both present, is `Invalid`. A blank line counts as absent. A name listed twice, compared without case, is read once.
- A single line `a,b` is one value, passed to the catalog unchanged.
- Every request the source examines gets each configured name appended to its `Vary` response header, whatever the outcome.
- Startup rejects a name that is not a valid HTTP token.

## Ordering and composition

Sources run in the order they were first registered, and the first `Found` wins.

- `AddSource<T>()` and the built-in registrations count once per type. A second registration of the same type keeps the first position, but its options still apply: `AddHostSource("{tenant}.a.com").AddHostSource("{tenant}.b.com")` gives one host source that matches both.
- `AddSource(instance)` and `AddSource(Func<HttpContext, string?>)` always append. Two instances of one type both run.

In a host composed from libraries, the library that registers a source type first fixes its position:

```csharp
// Shared library, called first: registers the header source.
sources.AddHeaderSource();

// Application code, called afterwards.
sources.AddHostSource("{tenant}.example.com");

// Result: header first, host second. To make the host source win, register it before the library's
// registration runs — the position is fixed by the first call for that source type.
```

## Ignored identifiers and apex hosts

`TenantCatalogOptions.IgnoredIdentifiers` applies to every source. There is no per-source list. With `{tenant}.example.com` and `IgnoredIdentifiers = ["www"]`, `www.example.com` resolves to host context with no store call. An apex host (`example.com`), and any host no template matches, yields `None` and also runs as host context. Resolution never requires a tenant: endpoints that need one must sit under `TenantRequirement` in `DefaultPolicy` or `FallbackPolicy`.

## Whole-host (custom domain) identifiers

A bare `{tenant}` template captures the whole host, dots included (`shop.acme-corp.com`). The default identifier shape rejects that: `MaxIdentifierLength` is 63 and `IdentifierPattern` is the DNS-label slug. For whole-host templates, raise `MaxIdentifierLength` to 253 and set a host-name pattern that has a match timeout. Startup rejects `Regex.InfiniteMatchTimeout`.

```csharp
tenancy.Catalog(catalog =>
    catalog.Configure(options =>
    {
        options.MaxIdentifierLength = 253;
        options.IdentifierPattern = new Regex(
            @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$", // consumer-owned shape
            RegexOptions.None,
            TimeSpan.FromMilliseconds(100)
        );
    })
);
```

These settings apply to the whole catalog. A host that mixes a subdomain source with a custom-domain source accepts the wider shape from both. Negative caching (`UnknownIdentifierCacheExpiration`) and your rate limiting bound the cost of hostile identifiers on the wider shape ([DoS and rate limiting](#dos-and-rate-limiting)).

## Hardening the pre-auth path

- Every rejection from this path (unknown, disabled, invalid, or mismatch) carries `Cache-Control: no-store`, so a shared cache never serves one caller's rejection to the next.
- Behind a proxy, run `UseForwardedHeaders()` with `KnownProxies` or `KnownNetworks` before the resolution middleware, and scope `AllowedHosts` to the tenant suffix. The host source reads `Request.Host` after forwarding and ignores `X-Forwarded-Host`, so an unconfigured proxy cannot smuggle a host and a forged header has no effect.
- Run `UseCors()` before the resolution middleware, so preflight requests end there instead of being resolved or rejected.
- The unknown, disabled, and mismatch rejections are byte-identical by default, but not equal in timing. A recently resolved disabled tenant answers from the cache, while an unknown identifier outside its negative-cache window costs a store read, so latency can reveal that a disabled tenant exists. Headless does not equalize timing. Rate limiting caps how many samples a caller collects.
- No tenancy log event carries a raw host, route value, header value, or identifier. The ordering and timeout events name the source type, and the timeout event also names the template. Each fires once per host (per pipeline or source instance, so a second test host in the same process logs its own), so they cannot flood logs.
- CDN caveats: host and route tenancy need the full URL, host included, in the CDN cache key. A cache key that omits the host serves one tenant's response to another. Many CDNs refuse to cache a response that varies on an unknown header, so a header source's `Vary: X-Tenant` can turn off edge caching.
- In-app output caching (`AddOutputCache()` with `UseOutputCache()`) ignores the response `Vary` header. Catalog resolution therefore adds the resolved tenant id (empty for host context) to every output-cache key as the vary-by value `Headless.Tenant`. It only varies the key and never turns caching on for an endpoint. Call `UseOutputCache()` after `UseHeadlessTenantCatalogResolution()`. A request that reaches output caching first logs the Error-level `HEADLESS_TENANT_OUTPUT_CACHE_MISORDERED` event once per host, and its cached responses do not vary by tenant.

## Header source and perimeter controls

A header can select only an existing, enabled tenant, and mismatch enforcement rejects an authenticated caller whose tenant claim disagrees with it. A header does bypass every control bound to a tenant's host name, such as a WAF rule, an mTLS policy, an IP allowlist, or a CDN rule on `acme.example.com`: the caller presents one tenant's host name and selects another tenant with the header. Where host names carry such controls, register the host source first so the host name decides, or strip or overwrite the tenant header at the edge.

Mismatch enforcement has limits. A principal with no tenant claim passes any source-selected tenant unchecked, and so does any endpoint where authorization never runs (`[AllowAnonymous]`, or no authorize metadata and no fallback policy). Map such credentials (service accounts, API keys) to a tenant claim so the check applies, and never derive authorization from the ambient tenant on such endpoints.

## Link generation with the route source

ASP.NET Core drops a leading `{tenant}` segment on most link-generation calls: `Url.Action` and `LinkGenerator.GetPathByAction` lose it when linking to another action, and `GetPathByName` passes no ambient values at all. A registered route source fixes that: generated links copy the current request's tenant segment (the value named by `RouteValueName`) into the explicit values. An explicit `tenant` value always wins, so a link to another tenant still works. Turn this off with `RouteTenantIdentifierSourceOptions.PromoteAmbientRouteValue = false`. It is read per call, so it works through every `AddRouteSource` overload, including an `IConfiguration` bind.

- A link from a tenant request to an endpoint with no `{tenant}` segment carries the tenant as a query string (`/plain?tenant=acme`).
- A link from a request with no tenant segment to a tenant endpoint returns `null` unless you pass the tenant explicitly.

## Mismatch enforcement

When a request carries both a resolved identifier and an authenticated tenant claim, both must name the same canonical tenant. `ResolveFromCatalog(...)` turns this on, and it applies even on hosts that never call `ResolveFromClaims()`. Requests resolved only from a claim are unaffected. Two checks cover different hosts, and only one rejects a given request:

1. **Before authentication.** The resolution middleware authenticates the default scheme itself (the result is cached for the request) and rejects a mismatch at once. This covers endpoints where authorization never runs. A host with no default authenticate scheme skips this check.
2. **During authorization.** An authorization handler checks endpoint-specific and non-default schemes, which the first check cannot see. It writes no response. It fails authorization and marks the request, and the status-codes rewriter turns that into the generic 404. That is why the rewriter is required: without it, a mismatch would return a bare 403 beside the unknown identifier's 404, which tells a caller the tenant exists.

A mismatch gets the same response as an unknown or disabled identifier: 404 `g:tenant_resolution_failed`, or 403 `g:tenant_identifier_mismatch` with `DetailedResolutionErrors`.

`UseStatusCodesRewriter()` must come before `UseAuthorization()` so it wraps it. A rewriter after authorization never sees a failed evaluation, which ends the request early. Startup fails with `CATALOG_RESOLUTION_REWRITER_AFTER_AUTHORIZATION` for any rewriter call after `UseAuthorization()`, even when another call sits in the right place. The check cannot see two placements: the authorization middleware `WebApplication` adds when the host never calls `UseAuthorization()`, which runs ahead of all your middleware, and authorization added inside a `UseWhen` branch that rejoins. Call `UseAuthentication()` and `UseAuthorization()` explicitly on the main pipeline.

**Verify:** a token with `tenant_id = "ten_123"` sent to `other.example.com`, where `other` is an enabled tenant, returns 404 `g:tenant_resolution_failed`. The same token to `acme.example.com` succeeds.

## Failure mapping

`TenantCatalogOptions.DetailedResolutionErrors` defaults to `false`.

| Outcome | Default response | `DetailedResolutionErrors = true` |
|---|---|---|
| Unknown identifier | 404, `g:tenant_resolution_failed` | 404, `g:tenant_unknown` |
| Disabled tenant | 404, `g:tenant_resolution_failed` (byte-identical to Unknown) | 403, `g:tenant_disabled` |
| Identifier and claim mismatch | 404, `g:tenant_resolution_failed` (byte-identical to Unknown and Disabled) | 403, `g:tenant_identifier_mismatch` |
| Invalid identifier shape | 400, `g:tenant_identifier_invalid` | same |
| Ambiguous source input (`TenantIdentifierSourceResult.Invalid`, such as two `X-Tenant` values) | 400, `g:tenant_identifier_invalid`, before any store call | same |
| Ignored identifier (`IgnoredIdentifiers`) | Not a rejection: host context, no store call | same |
| No source found an identifier (apex host, unmatched host, absent header) | Not a rejection: host context, no store call | same |

Every rejection carries `Cache-Control: no-store`. An exception from your `ITenantStore` is never mapped to a tenant code. It propagates as an unhandled exception, usually a 500. A cache fault counts as a miss and leaves the store answer unchanged.

## DoS and rate limiting

Unauthenticated callers reach identifier resolution by design, because it runs before `UseAuthentication()`. Headless validates the identifier's length and characters before any cache or store lookup, but does not rate-limit callers. Protect hosts that expose identifier resolution to the internet with rate limiting in the application or at the edge (a reverse proxy, WAF, or CDN). `UnknownIdentifierCacheExpiration` bounds the store cost of probing one unknown identifier repeatedly. Rotating through many distinct unknown identifiers still costs one store read each.

The same rotation fills the shared `ICache` with one negative entry per identifier, each living for `UnknownIdentifierCacheExpiration`, and can push useful entries out of a bounded cache. Headless adds no entry cap, because `ICache` implementations enforce no size limits. Bound the traffic instead. `Headless.RateLimiting`'s `IAttemptLimiter` gives every replica one shared budget per client when it runs on a shared cache (`UseRedis` or `UseHybrid`):

```csharp
var tenantResolutionQuota = new AttemptQuota(limit: 300, window: TimeSpan.FromMinutes(1));

// After UseForwardedHeaders() (so RemoteIpAddress is the client) and UseCors(), before resolution.
app.Use(async (context, next) =>
{
    var limiter = context.RequestServices.GetRequiredService<IAttemptLimiter>();
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var attempt = await limiter.AcquireAsync("tenant-resolution", clientIp, tenantResolutionQuota, context.RequestAborted);
    attempt.ThrowIfRejected(); // 429 with Retry-After through the Headless exception handler

    await next(context);
});
app.UseHeadlessTenantCatalogResolution();
```

This charges every request that reaches resolution, so size the quota for legitimate traffic per client. See [Rate Limiting](../rate-limiting.md) for quota behavior and key privacy. ASP.NET Core's built-in rate limiter also works, but it counts per replica.

**Verify:** in a test, send more than the quota's requests from one client inside the window and assert the next one returns 429 with a `Retry-After` header.
