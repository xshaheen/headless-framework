# Headless.Api.Core

Building blocks for ASP.NET Core APIs — primitives only. Provides service registration helpers, middleware, problem details, JWT, identity, security headers, and request-context abstractions.

> Looking for `AddHeadless()`, `UseHeadless()`, `MapHeadlessEndpoints()`? Those live in `Headless.Api.ServiceDefaults`. This package is the parts catalog; ServiceDefaults is the assembly.

## Problem Solved

Exposes each API primitive individually so teams that need à-la-carte composition can register only what they need — for example, registering `AddHeadlessProblemDetails()` alone, or composing their own middleware pipeline without the default order. Also provides the HTTP-layer tenant resolution, tenant authorization, and antiforgery primitives that `Headless.Api.ServiceDefaults` wires together.

## Key Features

- `AddHeadlessProblemDetails()` — registers `IProblemDetailsCreator`, `HeadlessApiExceptionHandler`, and the `CustomizeProblemDetails` hook that normalizes every response
- `AddHeadlessApiResponseCompression()` — Brotli + Gzip at `Fastest` level; extends MIME list with `application/problem+json`, `image/svg+xml`, `image/x-icon`
- `AddHeadlessAntiforgery()` — antiforgery service registration
- `AddStatusCodesRewriterMiddleware()` + `UseStatusCodesRewriter()` — rewrites bare 401, 403, 404 to structured `application/problem+json` via `IProblemDetailsCreator`
- `ConfigureHeadlessDefaultApi()` — Kestrel limits (no `Server` header, 30 MB body, 40 headers), HSTS (365-day max-age, subdomain, preload), lowercase route URLs, form limits (4 MB value, 16 KB multipart headers, 30 MB multipart body), default `self` liveness health check
- `AddHeadlessJsonService()` — `IJsonOptionsProvider`, `IJsonSerializer`, `ITextSerializer`, `ISerializer` (all `TryAddSingleton` — safe to override)
- `AddHeadlessTimeService()` — `TimeProvider.System`, `ITimezoneProvider` (all `TryAddSingleton`)
- JWT request contracts — `JwtTokenRequest` groups token creation values, while `JwtTokenValidationRequest` uses required initializers for the token, signing key, issuer, and audience and groups the validation switches for `IJwtTokenFactory.ParseJwtTokenAsync(...)`
- `AddServerTimingMiddleware()` + `UseServerTiming()` — appends `Server-Timing` trailer when response supports trailers
- `UseNoCacheWhenMissingCacheHeaders()` — injects `Cache-Control: no-cache,no-store,must-revalidate` when response omits the header
- Basic/API-key authentication helpers — `AddBasicSchema()` and `AddApiKey()` register the canonical `Basic` and `ApiKey` schemes; handlers only authenticate credentials supplied for their own scheme
- HTTP tenant resolution: `ResolveFromClaims()`, `UseHeadlessTenancy()`, `[SkipTenantResolution]`, `.SkipTenantResolution()`
- HTTP tenant catalog resolution (pre-authentication): `ResolveFromCatalog(...)`, `UseHeadlessTenantCatalogResolution()`, `ITenantIdentifierSource` returning `TenantIdentifierSourceResult` (`None` / `Found` / `Invalid`), and the `HeadlessTenantCatalogResolutionBuilder` members `AddHostSource(...)`, `AddRouteSource(...)`, `AddHeaderSource(...)`, `AddSource<T>()`, `AddSource(instance)`, `AddSource(Func<HttpContext, string?>)` with options `HostTenantIdentifierSourceOptions` (`Templates`), `RouteTenantIdentifierSourceOptions` (`RouteValueName`, `PromoteAmbientRouteValue`), and `HeaderTenantIdentifierSourceOptions` (`HeaderNames`, `DefaultHeaderName` = `X-Tenant`)
- HTTP tenant authorization: `TenantRequirement`, `[AllowMissingTenant]`, `.AllowMissingTenant()`, `[RequireTenant]`, `.RequireTenant()`
- Diagnostic listeners: `AddHeadlessApiDiagnosticListeners()`, `BadRequestDiagnosticAdapter`, `MiddlewareAnalysisDiagnosticAdapter`

## Design Notes

- `IProblemDetailsCreator` factory methods normalize Headless fields (`traceId`, build metadata, `instance`, timestamp) but leave consumer `ProblemDetailsOptions.CustomizeProblemDetails` callbacks to the final response writer. Exception-handler and status-code-rewriter responses run consumer customization once through ASP.NET Core's `IProblemDetailsService`; MVC direct `ObjectResult` responses built from Headless-normalized ProblemDetails are customized once by `Headless.Api.Mvc`.
- `HeadlessApiExceptionHandler` honors `Accept` quality values when deciding whether to write JSON ProblemDetails. A request that rejects JSON, or explicitly rejects `application/problem+json`, with `q=0` is left for downstream/default handlers instead of receiving a JSON body.
- Basic authentication delegates password validation to `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, so configured ASP.NET Core Identity lockout policies apply to failed Basic credentials.
- Batch `IFormFile.SaveAsync(...)` preserves result ordering while bounding concurrent file stream copies to `Environment.ProcessorCount` to avoid unbounded file-handle and disk pressure on large multipart requests.
- `IFormFile.GetAllBytesAsync(CancellationToken cancellationToken = default)` propagates optional cancellation through asynchronous upload buffering; callers can omit the token.
- Tenant identifier sources run in first-registration order and the first `Found` result wins; `AddSource<T>()` and the built-in `AddHostSource` / `AddRouteSource` / `AddHeaderSource` registrations deduplicate by type (the first call fixes the position, every call's options contribution still applies), while `AddSource(instance)` and the delegate overload always append. A source result of `Invalid` (present but ambiguous input, such as two `X-Tenant` values) rejects with 400 `g:tenant_identifier_invalid` before any catalog call; `Found` with a blank value normalizes to `None`. Sources never trim, lowercase, or shape-validate — the catalog does.
- The header source (default `X-Tenant`) can only select an existing enabled tenant and identifier/claim mismatch enforcement still rejects an authenticated caller whose tenant claim disagrees, but it bypasses any WAF rule, mTLS policy, IP allowlist, or CDN configuration bound to a tenant's hostname — register the host source first where hostnames carry such controls, or strip/overwrite the header at the edge. A requested header name replaces the untouched default and appends afterwards (`AddHeaderSource("X-Legacy")` reads only `X-Legacy`); any second value across the configured names is `Invalid`, a single line `a,b` is one value, and every consult appends the configured names to `Vary`.
- `AddRouteSource` (every overload) wraps the routing `LinkGenerator` once so `Url.Action`, `GetPathByAction`, and `GetPathByName` keep the current request's `{tenant}` segment, which ASP.NET Core would otherwise drop (required-value invalidation; the endpoint-name scheme passes no ambient values — only `GetPathByRouteValues` kept it). An explicit `tenant` value always wins; `RouteTenantIdentifierSourceOptions.PromoteAmbientRouteValue = false` opts out per call. A link from a tenant request to an endpoint without a `{tenant}` segment carries the value as a query string (`/plain?tenant=acme`); a link from a request with no tenant segment to a tenant endpoint returns `null` without an explicit value.
- Every catalog rejection carries `Cache-Control: no-store`, and no tenancy log event carries a raw host, route value, header value, or identifier. The host source reads the post-forwarding `Request.Host`, never `X-Forwarded-Host`; a host template match timeout (practically unreachable with the non-backtracking compiled templates) maps to `Invalid` plus a once-per-process warning naming the template only.

## Installation

```bash
dotnet add package Headless.Api.Core
```

## Quick Start

Composing primitives without `Headless.Api.ServiceDefaults`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessProblemDetails();
builder.Services.AddHeadlessApiResponseCompression();
builder.Services.ConfigureHeadlessDefaultApi(); // Kestrel limits + HSTS + health check + routing
builder.Services.AddStatusCodesRewriterMiddleware();
builder.Services.AddServerTimingMiddleware();

var app = builder.Build();
app.UseResponseCompression();
app.UseStatusCodesRewriter(); // before UseExceptionHandler
app.UseExceptionHandler();
app.UseServerTiming();
app.MapHealthChecks("/health");
app.Run();
```

HTTP tenant resolution and authorization:

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

app.UseAuthentication();
app.UseHeadlessTenancy(); // between auth and authz
app.UseAuthorization();

// Opt out of tenant claim extraction for a specific endpoint
app.MapGet("/webhook", handler).SkipTenantResolution().AllowMissingTenant();
```

Identifier-based (pre-authentication) tenant resolution through the tenant catalog — host, route, header, and delegate sources, consulted in registration order:

```csharp
builder.Services.AddHeadlessCaching(caching => caching.UseInMemory()); // catalog prerequisite

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Catalog(catalog =>
            catalog
                .Configure(options => options.IgnoredIdentifiers.Add("www")) // www.example.com -> host context
                .UseInMemory(options => options.Tenants.Add(new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme", isEnabled: true)))
        )
        .Http(http =>
            http.ResolveFromCatalog(sources =>
                sources
                    .AddHostSource("{tenant}.example.com") // acme.example.com -> "acme"
                    .AddRouteSource()                      // /{tenant}/orders  -> route value "tenant"
                    .AddHeaderSource()                     // X-Tenant: acme
                    .AddSource(context => context.Request.Query["tenant"].ToString()) // null/blank -> not mine
            )
        )
);

app.UseForwardedHeaders();                // behind a proxy only, with KnownProxies/KnownNetworks configured
app.UseStatusCodesRewriter();             // must wrap UseAuthorization()
app.UseRouting();
app.UseCors();                            // before resolution so preflights short-circuit
app.UseHeadlessTenantCatalogResolution(); // after UseRouting, before UseAuthentication
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();

// [SkipTenantResolution] also bypasses catalog resolution and rejection for the endpoint.
```

A custom source implements `ITenantIdentifierSource` and returns `TenantIdentifierSourceResult.None`, `.Found(value)`, or `.Invalid`; a source written against the earlier `string?` contract migrates by returning `Found(value)` / `None` instead of the string / `null`. The delegate overload cannot express `Invalid`.

JWT validation uses a request object instead of positional token, key, issuer, audience, and validation arguments:

```csharp
using System.Security.Claims;
using Headless.Api.Security.Jwt;

public sealed class TokenValidator(IJwtTokenFactory tokens)
{
    public Task<ClaimsPrincipal?> ValidateAsync(
        string token,
        string signingKey,
        string issuer,
        string audience,
        CancellationToken cancellationToken
    ) =>
        tokens.ParseJwtTokenAsync(
            new JwtTokenValidationRequest
            {
                Token = token,
                SigningKey = signingKey,
                Issuer = issuer,
                Audience = audience,
                ValidateIssuer = true,
                ValidateAudience = true,
            },
            cancellationToken
        );
}
```

## Configuration

### Tenant identifier sources

Each built-in source has a validated options type (startup fails on an invalid value):

| Options | Property | Default | Notes |
|---|---|---|---|
| `HostTenantIdentifierSourceOptions` | `Templates` | `[]` (at least one required) | Matched in order against the post-forwarding `Request.Host`; `{tenant}` captures one label, `?` one label, `*` zero or more labels, a bare `{tenant}` the whole host. Case-insensitive, port excluded, one trailing dot stripped; IP literals and hosts over 253 characters yield `None`. Compiled once as non-backtracking regexes with the shared 100 ms `RegexPatterns.MatchTimeout`. The string overload appends to the list. |
| `RouteTenantIdentifierSourceOptions` | `RouteValueName` | `tenant` (`DefaultRouteValueName`) | Read from `Request.RouteValues`; a non-string value is `None`. Requires `UseHeadlessTenantCatalogResolution()` after `UseRouting()`. Last contribution wins. |
| `RouteTenantIdentifierSourceOptions` | `PromoteAmbientRouteValue` | `true` | Keeps the current request's tenant segment in generated links; read per call. |
| `HeaderTenantIdentifierSourceOptions` | `HeaderNames` | `["X-Tenant"]` (`DefaultHeaderName`) | Names must be HTTP tokens. A requested name replaces the untouched default and appends afterwards (an already-listed name is not appended again); one duplicate-detection scope across all names, blank lines count as absent; every consult appends the names to `Vary`. |

Each source also accepts an `IConfiguration` section (`AddHostSource(section)` with a `Templates` array, `AddRouteSource(section)` with `RouteValueName`, `AddHeaderSource(section)` with a `HeaderNames` array — a listed array replaces the untouched default, a section without it keeps the default).

Ignored identifiers (for example `www`) stay on `TenantCatalogOptions.IgnoredIdentifiers` in `Headless.MultiTenancy`; there is no per-source list. An ignored identifier, an apex host, and an unmatched host all fall through to host context with no store call, so endpoints that need a tenant must sit under `TenantRequirement`. A whole-host (bare `{tenant}`) template needs `TenantCatalogOptions.MaxIdentifierLength = 253` and an `IdentifierPattern` shaped for hostnames that carries a match timeout; both are catalog-wide, so mixing a subdomain source with a custom-domain source relaxes the subdomain shape too, and hostile identifier cardinality is bounded by negative caching plus the consumer's rate limiting.

### API surfaces

An API surface is a named set of endpoints sharing routing, authorization and tenancy defaults, plus an OpenAPI document. `AddHeadlessApiSurface(name, configure)` runs its optional callback immediately, validates the definition, and registers an immutable descriptor plus a singleton `ApiSurfaceRegistry`. MVC, Minimal API, telemetry, and OpenAPI share these definitions. Configure definitions during registration; the builders do not use the deferred .NET options pipeline. Changes to a retained builder after registration have no effect.

```csharp
using Headless.Api;
using Headless.Api.Surfaces;

builder.Services.AddHeadlessApiSurface("portal", surface =>
{
    surface.RoutePrefix = "api/portal";
    surface.DefaultAuthorizationPolicy = "tenant";
    surface.DefaultTenancyMode = ApiSurfaceTenancyMode.RequireTenant;
});
```

For an atomic batch, use `AddHeadlessApiSurfaces(surfaces => surfaces.Add("portal").Add("console"))`. Its `ApiSurfacesBuilder` validates every definition before registering any from the batch. Both registration methods return `IServiceCollection` for chaining and may be combined before document inference. Document names and titles are inferred; override `surface.OpenApi.DocumentName` and `.Title` only when needed.

`DefaultAuthorizationPolicy` adds a native named policy alongside endpoint policies. `[AllowAnonymous]` / `.AllowAnonymous()` still bypass authorization. `DefaultTenancyMode = ApiSurfaceTenancyMode.RequireTenant` requires a policy containing `TenantRequirement`, the Headless tenant authorization handler, and current-tenant services. The mode alone does not install enforcement. Configure these through `AddHeadlessTenancy(...)` as shown above.

Explicit controller or endpoint `RequireTenant` / `AllowMissingTenant` metadata takes precedence over surface defaults. `SkipTenantResolution` skips HTTP tenant extraction; it does not permit a missing tenant. An explicit tenancy requirement or exemption also suppresses a surface's skip-resolution default.

API surfaces require no middleware call. After routing, use `HttpContext.GetApiSurface()` to read the selected endpoint's immutable defaults. The lookup returns `null` for unmatched or unmarked endpoints and follows endpoint changes during re-execution. A marked endpoint whose surface is not registered throws.

With `Headless.Api.ServiceDefaults` OpenTelemetry enabled, completed request spans receive the `headless.api.surface.name` tag. Unmatched requests use `unknown`; matched endpoints without surface metadata use `unclassified`. The tag reflects the endpoint visible at response completion and is not available during authentication. Custom telemetry callbacks that run after request disposal must capture `ApiSurfaceRegistry` from host services and resolve the selected endpoint's `IApiSurfaceMetadata`. `GetApiSurface()` uses request services and is intended for the active request pipeline.

Surface and document names are case-insensitive identities containing ASCII letters, digits, periods, hyphens, or underscores. `.` and `..` are invalid. Surface names `unknown`, `unclassified`, and `infrastructure` are reserved. Duplicate surface/document names and invalid configuration fail validation. Unknown surface lookups throw. Document names default to the lowercase surface name; titles default to `<surfaceName> API`.

`ApiSurfaceRegistry.GetRequiredSurfaceForDocument(documentName)` resolves the document owner from the same immutable definitions. It matches names case-insensitively and throws when no surface owns the document. Register all surfaces before `AddHeadless()` or a parameterless OpenAPI surface registration. Inference closes surface registration; later additions throw rather than silently missing documents.

Exception mapping registered by `AddHeadlessProblemDetails()`:

| Exception | Response |
|-----------|----------|
| `MissingTenantContextException` | 403 with `error.code: g:tenant_required` |
| `CrossTenantWriteException` | 409 with `error.code: g:cross_tenant_write` |
| `ConflictException` | 409 with `errors` |
| `FluentValidation.ValidationException` | 422 with field errors |
| `EntityNotFoundException` | 404 |
| EF Core `DbUpdateConcurrencyException` (matched by type name) | 409 with concurrency-failure error |
| `TimeoutException` | 408 |
| `NotImplementedException` | 501 |
| `OperationCanceledException` (or inner OCE at any depth) when `HttpContext.RequestAborted` is the source | 499 (no body) |

All other exceptions return `false`; the host default or a downstream handler renders them.

`StatusCodesRewriterMiddleware` is required for the `g:tenant_required` discriminator on 403 authorization rejections. `Headless.Api.ServiceDefaults` wires it automatically; apps that skip ServiceDefaults must call `UseStatusCodesRewriter()` themselves. `TenantRequirement` must live in `DefaultPolicy` or `FallbackPolicy` — the startup validator does not inspect named policies. `UseHeadlessTenancy()` must run after `UseRouting()` so endpoint metadata is available when `[SkipTenantResolution]` is evaluated. `UseHeadlessTenantCatalogResolution()` runs after `UseRouting()` and before `UseAuthentication()`; placed before `UseRouting()` it still resolves host, header, and delegate sources and only loses the `[SkipTenantResolution]` opt-out (a once-per-process warning), but a registered route source then finds nothing and every request runs as host context, logged once per process at Error level as `HEADLESS_TENANT_CATALOG_ROUTE_SOURCE_MISORDERED` — best-effort, since a host whose requests all 404 or all reject never logs. Behind a proxy, `UseForwardedHeaders()` with `KnownProxies`/`KnownNetworks` and host filtering (`AllowedHosts` scoped to the tenant suffix) must precede it, and `UseCors()` must precede it so preflights short-circuit. CDN caveats: a cache key that omits the host breaks host tenancy, and many CDNs refuse to cache on unknown `Vary` values. Identifier/claim mismatch enforcement limits: a principal with no tenant claim, or an endpoint where authorization never runs (`[AllowAnonymous]`, policy-less without a fallback policy), passes a source-selected tenant unchecked — map such credentials to a tenant claim so that enforcement applies, and never derive authorization on such endpoints from the ambient tenant.

`AddBasicSchema()` defaults to the canonical `Basic` authentication scheme and `AddApiKey()` defaults to `ApiKey`. `DynamicAuthenticationSchemeProvider` selects those same canonical names. API keys are read from the configured header by default; query-string keys are routed and accepted only when `ApiKeyAuthenticationSchemeOptions.AllowApiKeyInQueryString` is `true`.

## Dependencies

- `Headless.Api.Abstractions`
- `Headless.Core`
- `Headless.MultiTenancy`
- `Headless.Security.Abstractions`
- `Headless.Security`
- `Headless.FluentValidation`
- `Headless.Hosting`
- `Asp.Versioning.Http`
- `DeviceDetector.NET`
- `FluentValidation`
- `Microsoft.Extensions.Http.Resilience`
- `NetEscapades.AspNetCore.SecurityHeaders`

## Side Effects

- Opt-in surface registration validates options at startup and registers the immutable singleton registry; middleware sets the request feature and activity tag.
- Registers `HttpContextAccessor` (via `AddHeadlessProblemDetails`)
- `ResolveFromCatalog(...)` registers `TenantCatalogResolutionMiddleware`, `TenantIdentifierIntegrityHandler` (`IAuthorizationHandler`, `TryAddEnumerable`), `IHttpContextAccessor`, and `TryAdd` fallbacks for `IProblemDetailsCreator`, `TimeProvider`, and `IBuildInformationAccessor`; `AddHostSource` / `AddRouteSource` / `AddHeaderSource` each register their singleton `ITenantIdentifierSource` (`TryAddEnumerable`, deduplicated by type) and validated options (`ValidateOnStart`); `AddSource(instance)` and the delegate overload append a singleton; `AddRouteSource` also calls `AddRouting()` and decorates the routing `LinkGenerator` once with `TenantAmbientRouteValueLinkGenerator`
- Every catalog rejection response sets `Cache-Control: no-store`; the header source appends its configured names to the response `Vary` header on every consult
- Configures response compression providers (Brotli, Gzip)
- Configures Kestrel limits and disables `Server` response header (via `ConfigureHeadlessDefaultApi`)
- Configures route options (lowercase URLs, no trailing slash)
- Configures form options (value limit, multipart limits)
- Configures HSTS options (365-day max-age, subdomain inclusion, preload)
- Registers `self` liveness health check tagged `live`
