---
domain: API & Web
packages: Api.Abstractions, Api.Core, Api.ServiceDefaults, Api.DataProtection, Api.Idempotency, Api.Logging.Serilog, Api.MinimalApi, Api.Mvc
---

# API & Web

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Bootstrap model: ServiceDefaults vs Core](#bootstrap-model-servicedefaults-vs-core)
    - [Request context: `IRequestContext`](#request-context-irequestcontext)
    - [Problem details and error codes](#problem-details-and-error-codes)
    - [Standard middleware order](#standard-middleware-order)
    - [Idempotency as HTTP middleware](#idempotency-as-http-middleware)
- [Headless.Api.Abstractions](#headlessapiabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Api.Core](#headlessapicore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Api.ServiceDefaults](#headlessapiservicedefaults)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Api.DataProtection](#headlessapidataprotection)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Api.Idempotency](#headlessapiidempotency)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Api.Logging.Serilog](#headlessapiloggingserilog)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Api.MinimalApi](#headlessapiminimalapi)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Api.Mvc](#headlessapimvc)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)

> ASP.NET Core API infrastructure: service registration, JWT, middleware, validation, logging, and endpoint integration for Minimal API and MVC.

## Quick Orientation

The package split:

- **`Headless.Api.ServiceDefaults`** — the one-line bootstrap. Call `AddHeadless()` to register compression, health checks, problem details, JWT, identity, validation, JSON defaults, OpenTelemetry, OpenAPI, service discovery, and HttpClient resilience in one shot. Antiforgery is opt-in (`options.Antiforgery.Enabled = true`) and the middleware is consumer-owned. Use `UseHeadless()` for the standard middleware order and `MapHeadlessEndpoints()` for `/health`, `/alive`, OpenAPI JSON, and static assets. Pull this for the happy path — it transitively brings in `Headless.Api.Core`.
- **`Headless.Api.Core`** — the building blocks only. Pull this when composing your own pipeline without the framework orchestrator, or when you only need individual primitives like `AddHeadlessProblemDetails()`, `AddHeadlessAntiforgery()`, or `AddHeadlessApiResponseCompression()`.

Choose an endpoint style:

- **Minimal API** (recommended for new projects): Add `Headless.Api.MinimalApi` and call `ConfigureMinimalApi()` for JSON config, validation filters, and exception handling.
- **MVC/Controllers**: Add `Headless.Api.Mvc` and call `ConfigureMvc()` for base controllers, exception filters, and URL canonicalization.

Use `Headless.Api.Abstractions` when you only need interfaces (`IRequestContext`, `IWebClientInfoProvider`) without pulling in the full API stack.

Additional packages:

- `Headless.FluentValidation` — general validation plus `IFormFile` upload validators. `Headless.Api.Core` owns validators for its `PhoneNumberRequest`, `GeoCoordinateRequest`, and `PageMetadataRequest` contracts.
- `Headless.Api.DataProtection` — persist ASP.NET Core Data Protection keys to any `IBlobStorage` provider.
- `Headless.Api.Logging.Serilog` — enrich Serilog logs with per-request context (IP, user agent, user ID, tenant ID, correlation ID).
- `Headless.Api.Idempotency` — Stripe-style idempotency middleware: cache full HTTP responses on first execution and replay them byte-equivalent on identical retries. See [mediator.md](mediator.md) for why idempotency is HTTP middleware and not a Mediator behavior.

## Agent Instructions

- Default install for any new Headless API: `Headless.Api.ServiceDefaults`. It transitively pulls in `Headless.Api.Core`. Only reach for `Headless.Api.Core` directly when you specifically want primitives without the orchestrator.
- Use `AddHeadless()` on `WebApplicationBuilder` for bootstrapping; do not manually register compression, security headers, JSON defaults, OpenTelemetry, OpenAPI, or problem details. `AddHeadless(configureServices: options => ...)` accepts a `HeadlessServiceDefaultsOptions` callback for Aspire-style toggles (OTel, OpenAPI, service discovery, validation, antiforgery). Antiforgery is opt-in — set `options.Antiforgery.Enabled = true` for cookie-auth apps and wire `app.UseAntiforgery()` yourself after `UseAuthentication()`/`UseAuthorization()`; bearer-token APIs leave it disabled.
- Use `UseHeadless()` for the default middleware order (`UseStatusCodePages()` before `UseExceptionHandler()`), then add auth/tenant middleware, then map endpoints. `UseHeadless` and `MapHeadlessEndpoints` are idempotent.
- For tenant-aware HTTP apps, configure `builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()))` and place `app.UseHeadlessTenancy()` after app-owned `UseAuthentication()` and before app-owned `UseAuthorization()`.
- For idempotent-replay middleware, register `services.AddIdempotency(o => { ... })` and place `app.UseIdempotency()` AFTER `UseAuthorization()` and AFTER `UseHeadlessTenancy()`. Idempotency reads `ICurrentTenant.Id` for cache-key composition; tenant and auth must be resolved first so unauthenticated/unauthorized requests do not allocate cache slots. `InFlightStrategy = WaitAndReplay` requires `IDistributedLock`; the DI startup validator fails fast if it is missing.
- Basic and API-key handlers authenticate only credentials supplied for their own scheme. Do not rely on an existing cookie/bearer principal to satisfy an endpoint that explicitly requires `Basic` or `ApiKey`.
- API-key query-string authentication is opt-in (`AllowApiKeyInQueryString = true`); the dynamic scheme provider ignores `?api_key=` unless the API-key handler would accept it.
- Use `MapHeadlessEndpoints()` to expose `/health`, `/alive`, OpenAPI JSON, and static web assets. `AddHeadless()` registers a `self` health check tagged `live`.
- Keep `TrustForwardedHeadersFromAnyProxy` disabled unless the service is reachable only through trusted proxy infrastructure.
- `Headless.Api.ServiceDefaults` validates by default that `UseHeadless()`, `UseStatusCodesRewriter()`, and `MapHeadlessEndpoints()` were applied at startup. For custom/manual pipelines, disable via `options.Validation.RequireUseHeadless = false`, `options.Validation.RequireStatusCodesRewriter = false`, and `options.Validation.RequireMapHeadlessEndpoints = false`.
- `AddHeadless()` invokes `SetupApi.ConfigureGlobalSettings()` automatically (idempotent) to set regex timeout, FluentValidation, and JWT defaults. Call it manually only if you need those defaults applied before `AddHeadless()` runs.
- Prefer `Headless.Api.MinimalApi` over `Headless.Api.Mvc` for new projects. Use `.Validate<T>()` on endpoints for FluentValidation integration.
- For MVC, inherit from `ApiControllerBase` — it provides common utilities. Use `ConfigureMvc()` not manual `MvcOptions` configuration.
- Use `Headless.FluentValidation` for file rules (`FileNotEmpty()`, `LessThanOrEqualTo()`, `ContentTypes()`, `HaveSignatures()`). Use the `Headless.Api.Core` contract extensions (`PhoneNumber()`, `GeoCoordinate()`, `PageMetadata()`) for framework API requests; do not write manual boundary-validation logic.
- Use `PersistKeysToBlobStorage()` from `Headless.Api.DataProtection` to persist Data Protection keys in distributed/containerized environments.
- For Serilog enrichment, call `AddSerilogEnrichers()` on services and `UseSerilogEnrichers()` on the app — place the middleware early in the pipeline.
- Inject `IRequestContext` (from Abstractions) for request-scoped user, tenant, locale, timezone, and correlation ID — never access `HttpContext` directly in service code.
- `AddHeadless()` auto-binds `Headless:StringEncryption` and `Headless:StringHash` through `Headless.Security`, and also exposes explicit overloads for configuration sections and option callbacks when the defaults are not suitable. When the hash callback is omitted, it still binds `Headless:StringHash` by default.
- Place `UseResponseCompression()` **before** `UseIdempotency()` in the pipeline. Compression middleware registered inside idempotency records compressed bytes in the cache; replaying those bytes without re-encoding them produces garbled or double-encoded responses.
- `HeaderName` per-endpoint overrides via `.WithIdempotency()` are silently ignored — the middleware reads the request header before resolving endpoint metadata. Change the header name globally via `AddIdempotency(o => o.HeaderName = ...)` only.
- `TenantRequirement` must be in `DefaultPolicy` or `FallbackPolicy` for framework-level enforcement; placing it in a named policy is not detected by the startup validator.
- `UseHeadlessTenancy()` / `UseTenantResolution()` must run after `UseRouting()` so `HttpContext.GetEndpoint()` returns metadata when `[SkipTenantResolution]` is checked.

## Core Concepts

### Bootstrap model: ServiceDefaults vs Core

`Headless.Api.ServiceDefaults` is the orchestrator. It wires together the primitives in `Headless.Api.Core` with Aspire-style conventions (OpenTelemetry, OpenAPI, service discovery, HttpClient resilience) and exposes three entry points:

- `builder.AddHeadless()` — registers all services in one call.
- `app.UseHeadless()` — applies the framework's standard middleware order.
- `app.MapHeadlessEndpoints()` — maps `/health`, `/alive`, OpenAPI JSON, static assets.

`Headless.Api.Core` exposes the same primitives individually (`AddHeadlessProblemDetails()`, `AddHeadlessApiResponseCompression()`, `AddHeadlessAntiforgery()`, `ConfigureHeadlessDefaultApi()`, etc.). Pull Core directly only when you must compose your own pipeline or toggle individual features; otherwise ServiceDefaults is the right default.

### Request context: `IRequestContext`

`IRequestContext` (from `Headless.Api.Abstractions`, implemented in `Headless.Api.Core`) is the single abstraction for all request-scoped facts: user ID and claims, tenant ID, preferred locale, timezone, correlation ID, and request start time. Inject it in services instead of `IHttpContextAccessor`; the implementation reads from `HttpContext` behind the interface so services remain testable without a real HTTP context.

### Problem details and error codes

`AddHeadlessProblemDetails()` registers `IProblemDetailsCreator` (for building structured ProblemDetails responses) and `HeadlessApiExceptionHandler` (a single `IExceptionHandler` covering all framework-known exceptions). The creator adds standard extensions to every ProblemDetails: `traceId`, `buildNumber`, `commitNumber`, `instance`, `timestamp`. Error codes follow the `g:lower_snake_case` shape (`g:tenant_required`, `g:cross_tenant_write`, `g:idempotency_key_reused`). Every framework-emitted code — including FluentValidation validator failures (both the built-in codes mapped by `FluentValidationErrorCodeMapper` and the Headless validators surfaced by `FluentValidatorErrorDescriber`) — uses this single `g:` shape, so clients see one consistent code namespace in `errors[].code`. Stable codes are exposed as compile-time `public const string` on `*ErrorCodes` holders (`GeneralErrorCodes`, `IdentityErrorCodes` in `Headless.Api.Resources`; `IdempotencyErrorCodes`) — branch on these constants. Clients should route on the stable `error.code` and `status` values, not on `title` or `detail` which are human-readable and may be localized.

The exception table (see `# Headless.Api.Core` below) covers MVC actions and Minimal-API endpoints. Middleware running before `UseExceptionHandler`, hosted/background services, and SignalR hubs need their own catch sites.

### Standard middleware order

`UseHeadless()` applies the following order (consumer-placed middleware sits between `MapHeadlessEndpoints` and endpoint handlers):

1. `UseForwardedHeaders()`
2. `UseResponseCompression()`
3. `UseStatusCodePages()`
4. `UseStatusCodesRewriter()` — intercepts bare 401, 403, 404 and writes structured `application/problem+json`
5. `UseExceptionHandler()` — runs `HeadlessApiExceptionHandler` for framework-known exceptions
6. `UseHttpsRedirection()`
7. `UseHsts()` (outside Development)
8. No-cache header middleware (injects `Cache-Control: no-cache,no-store,must-revalidate` when response omits `Cache-Control`)

Consumer inserts authentication, tenancy, and authorization after step 7, then calls `MapHeadlessEndpoints()`.

`UseStatusCodePages()` runs before `UseExceptionHandler()` so bare status responses from middleware (including ASP.NET Core's `RequestTimeoutsMiddleware`-issued 408s) are normalized through `IProblemDetailsCreator.Normalize` before `UseExceptionHandler` fills them.

### Idempotency as HTTP middleware

Idempotency is an HTTP-layer concern, not a Mediator pipeline behavior. The middleware captures the full HTTP response (status code, allowlisted headers, body bytes) and replays it byte-equivalent on retry. A Mediator behavior has no access to the raw HTTP response after it leaves the handler — it would need to serialize/deserialize the action result, losing headers and body encoding, which breaks the byte-equivalent replay guarantee. See [mediator.md](mediator.md) for the full doctrine.

---

## Headless.Api.Abstractions

Defines core interfaces and contracts for HTTP request context, user identity, web client information, ProblemDetails construction, and absolute-URL building in ASP.NET Core applications.

### Problem Solved

Provides a standardized abstraction layer for accessing request-scoped context (user, tenant, locale, timezone, client info) without coupling application code to ASP.NET Core's `HttpContext` directly.

### Key Features

- `IRequestContext` — unified access to request-scoped information (user, tenant, locale, timezone, correlation ID)
- `IWebClientInfoProvider` — client detection (IP address, user agent, device info)
- `IRequestedApiVersion` — API versioning abstraction
- `IProblemDetailsCreator` — contract for building normalized RFC 7807 `ProblemDetails` responses (implemented in `Headless.Api.Core`)
- `IAbsoluteUrlFactory` — contract for building absolute URLs from the current request (implemented in `Headless.Api.Core`)
- Framework constants for HTTP headers and common values

### Installation

```bash
dotnet add package Headless.Api.Abstractions
```

### Quick Start

Inject `IRequestContext` to access request-scoped information:

```csharp
public sealed class OrderService(IRequestContext context)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var userId = context.User.Id;
        var tenantId = context.Tenant.Id;
        var correlationId = context.CorrelationId;

        return await _repository
            .CreateAsync(
                new Order
                {
                    UserId = userId,
                    TenantId = tenantId,
                    CreatedAt = context.DateStarted,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
```

### Configuration

No configuration required. This package contains interfaces only.

### Dependencies

- `Headless.Core`
- `Microsoft.AspNetCore.App` (framework reference) — required by `IProblemDetailsCreator` (`ProblemDetails`) and `IAbsoluteUrlFactory` (`HttpContext`)

### Side Effects

None. This is an abstractions-only package.

---

## Headless.Api.Core

Building blocks for ASP.NET Core APIs — primitives only. Provides service registration helpers, middleware, problem details, JWT, identity, security headers, and request-context abstractions. `Headless.Api.ServiceDefaults` is the orchestrator that composes these into a single `AddHeadless()` call.

### Problem Solved

Exposes each API primitive individually so teams that need à-la-carte composition can register only what they need (e.g., problem details without the full ServiceDefaults bootstrap, or status-codes rewriting without OpenTelemetry). Also provides the HTTP-layer tenant resolution, tenant authorization, and antiforgery primitives that ServiceDefaults wires together.

### Key Features

- `AddHeadlessProblemDetails()` — registers `IProblemDetailsCreator`, `HeadlessApiExceptionHandler`, and the `CustomizeProblemDetails` hook that normalizes every response
- `AddHeadlessApiResponseCompression()` — Brotli + Gzip at `Fastest` level; extends MIME list with `application/problem+json`, `image/svg+xml`, `image/x-icon`
- `AddHeadlessAntiforgery()` — antiforgery service registration
- `AddStatusCodesRewriterMiddleware()` + `UseStatusCodesRewriter()` — rewrites bare 401, 403, 404 to structured `application/problem+json` via `IProblemDetailsCreator`
- `ConfigureHeadlessDefaultApi()` — Kestrel limits (no `Server` header, 30 MB body, 40 headers), HSTS (365-day max-age, subdomain, preload), lowercase route URLs, form limits (4 MB value, 16 KB multipart headers, 30 MB multipart body), default `self` liveness health check
- `AddHeadlessJsonService()` — `IJsonOptionsProvider`, `IJsonSerializer`, `ITextSerializer`, `ISerializer` (all `TryAddSingleton` — safe to override)
- `AddHeadlessTimeService()` — `TimeProvider.System`, `ITimezoneProvider` (all `TryAddSingleton`)
- `AddServerTimingMiddleware()` + `UseServerTiming()` — appends `Server-Timing` trailer when response supports trailers
- `UseNoCacheWhenMissingCacheHeaders()` — injects `Cache-Control: no-cache,no-store,must-revalidate` when response omits the header
- Basic/API-key authentication helpers — `AddBasicSchema()` and `AddApiKey()` register the canonical `Basic` and `ApiKey` schemes; handlers only authenticate credentials supplied for their own scheme
- HTTP tenant resolution: `ResolveFromClaims()`, `UseHeadlessTenancy()`, `[SkipTenantResolution]`, `.SkipTenantResolution()`
- HTTP tenant authorization: `TenantRequirement`, `[AllowMissingTenant]`, `.AllowMissingTenant()`, `[RequireTenant]`, `.RequireTenant()`
- Diagnostic listeners: `AddHeadlessApiDiagnosticListeners()`, `BadRequestDiagnosticAdapter`, `MiddlewareAnalysisDiagnosticAdapter`
- FluentValidation extensions for API-owned `PhoneNumberRequest`, `GeoCoordinateRequest`, and `PageMetadataRequest` contracts

### Design Notes

- `IProblemDetailsCreator` factory methods normalize Headless fields (`traceId`, build metadata, `instance`, timestamp) but leave consumer `ProblemDetailsOptions.CustomizeProblemDetails` callbacks to the final response writer. Exception-handler and status-code-rewriter responses run consumer customization once through ASP.NET Core's `IProblemDetailsService`; MVC direct `ObjectResult` responses built from Headless-normalized ProblemDetails are customized once by `Headless.Api.Mvc`.
- `HeadlessApiExceptionHandler` honors `Accept` quality values when deciding whether to write JSON ProblemDetails. A request that rejects JSON, or explicitly rejects `application/problem+json`, with `q=0` is left for downstream/default handlers instead of receiving a JSON body.
- Basic authentication delegates password validation to `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, so configured ASP.NET Core Identity lockout policies apply to failed Basic credentials.
- Batch `IFormFile.SaveAsync(...)` preserves result ordering while bounding concurrent file stream copies to `Environment.ProcessorCount` to avoid unbounded file-handle and disk pressure on large multipart requests.
- `IFormFile.GetAllBytesAsync(CancellationToken cancellationToken = default)` propagates optional cancellation through asynchronous upload buffering; callers can omit the token.
- API request-contract validators live with their contracts in `Headless.Api.Core`; reusable rules and `IFormFile` validators remain in `Headless.FluentValidation`.

### Installation

```bash
dotnet add package Headless.Api.Core
```

### Quick Start

Composing primitives without ServiceDefaults:

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

HTTP tenant resolution (used internally by ServiceDefaults via `ResolveFromClaims()`):

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

// In pipeline — after UseAuthentication(), before UseAuthorization()
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();

// Opt out of tenant claim extraction for a single endpoint
app.MapGet("/webhook", handler).SkipTenantResolution().AllowMissingTenant();
```

### Configuration

Exception mapping from `AddHeadlessProblemDetails()`:

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

`StatusCodesRewriterMiddleware` is required for the `g:tenant_required` discriminator on 403 authorization rejections. It is wired by ServiceDefaults; apps that skip ServiceDefaults must call `UseStatusCodesRewriter()` themselves. `TenantRequirement` must live in `DefaultPolicy` or `FallbackPolicy` — the startup validator does not inspect named policies. `UseHeadlessTenancy()` / `UseTenantResolution()` must run after `UseRouting()` so endpoint metadata is available when `[SkipTenantResolution]` is evaluated.

`AddBasicSchema()` defaults to the canonical `Basic` authentication scheme and `AddApiKey()` defaults to `ApiKey`. `DynamicAuthenticationSchemeProvider` selects those same canonical names. API keys are read from the configured header by default; query-string keys are routed and accepted only when `ApiKeyAuthenticationSchemeOptions.AllowApiKeyInQueryString` is `true`.

### Dependencies

- `Headless.Api.Abstractions`
- `Headless.Core`
- `Headless.MultiTenancy`
- `Headless.Security.Abstractions`
- `Headless.Security`
- `Headless.Caching.Abstractions`
- `Headless.FluentValidation`
- `Headless.Hosting`
- `Asp.Versioning.Http`
- `DeviceDetector.NET`
- `FluentValidation`
- `Microsoft.Extensions.Http.Resilience`
- `NetEscapades.AspNetCore.SecurityHeaders`

### Side Effects

- Registers `HttpContextAccessor` (via `AddHeadlessProblemDetails`)
- Configures response compression providers (Brotli, Gzip)
- Configures Kestrel limits and disables `Server` response header (via `ConfigureHeadlessDefaultApi`)
- Configures route options (lowercase URLs, no trailing slash)
- Configures form options (value limit, multipart limits)
- Configures HSTS options
- Registers `self` liveness health check tagged `live`

---

## Headless.Api.ServiceDefaults

The one-line bootstrap for Headless APIs. Combines `Headless.Api.Core` primitives with Aspire-style host conventions (OpenTelemetry, OpenAPI, service discovery, HttpClient resilience).

### Problem Solved

Consolidates the entire ASP.NET Core API setup (compression, security headers, problem details, JWT, identity, validation, JSON defaults, OpenTelemetry, OpenAPI, service discovery, HttpClient resilience) into a single `AddHeadless()` call plus `UseHeadless()` / `MapHeadlessEndpoints()` for the pipeline. Antiforgery is opt-in via `options.Antiforgery.Enabled` (cookie-auth apps), with the middleware consumer-owned.

### Key Features

- One-call service registration via `AddHeadless()` (covers primitives + Aspire conventions)
- One-call middleware defaults via `UseHeadless()`
- One-call endpoint mapping via `MapHeadlessEndpoints()` (`/health`, `/alive`, OpenAPI JSON, static assets)
- Service-provider validation on startup
- Startup filter validates `UseHeadless()` and `MapHeadlessEndpoints()` were called
- OpenTelemetry logging, metrics, tracing with sensible defaults
- OpenAPI document registration and mapping
- Service discovery and HttpClient resilience
- MVC and Minimal API JSON defaults
- ASP.NET Core source-generated input validation
- Transitively brings in all `Headless.Api.Core` primitives

### Installation

```bash
dotnet add package Headless.Api.ServiceDefaults
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register all framework API services + Aspire conventions.
// Also applies global defaults (regex timeout, FluentValidation, JWT) via ConfigureGlobalSettings().
builder.AddHeadless();

var app = builder.Build();

// Optional: Add diagnostic listeners for debugging
using var _ = app.AddHeadlessApiDiagnosticListeners();

app.UseHeadless();
app.MapHeadlessEndpoints();

app.Run();
```

### Configuration

#### Pipeline and Endpoint Behavior

`UseHeadless()` applies Headless' standard ASP.NET Core middleware order:

- `UseForwardedHeaders()`
- `UseResponseCompression()`
- `UseStatusCodePages()`
- `UseStatusCodesRewriter()`
- `UseExceptionHandler()`
- `UseHttpsRedirection()`
- `UseHsts()` outside Development
- no-cache response header when the response did not set `Cache-Control`

Antiforgery is **opt-in and consumer-owned**: `AddHeadless()` does not register the antiforgery service unless `options.Antiforgery.Enabled = true`, and `UseHeadless()` never wires `app.UseAntiforgery()` (cookie-auth consumers call it themselves after auth/authz so the middleware sees the authenticated principal). Bearer-token APIs have no CSRF surface and leave the flag false.

`UseStatusCodePages()` intentionally runs before `UseExceptionHandler()` so bare status responses, including middleware-emitted 408s, can be normalized by `IProblemDetailsCreator.Normalize`. `UseStatusCodesRewriter()` runs inside it so bare 401, 403, and unmatched-route 404 responses use the framework-specific `IProblemDetailsCreator` factories before the generic status-code page fills the response.

`TrustForwardedHeadersFromAnyProxy` defaults to `false`. Turn it on only when the app is not directly reachable by untrusted clients; otherwise clients can spoof forwarded host/scheme values.

`MapHeadlessEndpoints()` maps `/health` for all health checks with a JSON body containing `status` and per-check `results`, and `/alive` for checks tagged `live`. It also maps OpenAPI JSON documents and static web assets when configured. Health endpoints are named, excluded from OpenAPI descriptions, and allow anonymous requests by default. `AddHeadless()` registers the default `self` liveness check, disables Kestrel's `Server` response header, and applies conservative Kestrel limits: 30MB max request body and 40 request headers. Both `UseHeadless` and `MapHeadlessEndpoints` are idempotent.

#### Service Options

```csharp
builder.AddHeadless(configureServices: options =>
{
    options.OpenTelemetry.Enabled = true;
    options.OpenTelemetry.RecordException = true; // record exceptions on spans (default: true)
    // Tracing filter: null (default) skips /health and /alive via SkipOperationalEndpointFunc.
    // Set to a custom predicate to fully replace — compose SkipOperationalEndpointFunc to keep the default skip:
    //   options.OpenTelemetry.Filter = ctx => !options.OpenTelemetry.SkipOperationalEndpointFunc(ctx) && ...;
    // For full AspNetCoreTraceInstrumentationOptions control (runs AFTER framework defaults):
    //   options.OpenTelemetry.ConfigureAspNetCoreInstrumentation = instr => { instr.RecordException = false; };

    options.OpenApi.Enabled = true;
    options.HttpClient.UseServiceDiscovery = true;
    options.HttpClient.UseStandardResilienceHandler = true;
    options.StaticAssets.Enabled = true;
    options.Validation.ValidateServiceProviderOnStartup = true;
    options.Validation.RequireUseHeadless = true;
    options.Validation.RequireMapHeadlessEndpoints = true;
    options.Validation.RequireStatusCodesRewriter = true; // validated on IStartupFilter path only
    // Antiforgery defaults to false. Set to true for cookie-auth apps; bearer-token APIs leave it off.
    options.Antiforgery.Enabled = false;
});
```

#### String Encryption

```json
{
    "Headless": {
        "StringEncryption": {
            "DefaultPassPhrase": "YourPassPhrase123",
            "InitVectorBytes": "WW91ckluaXRWZWN0b3IxNg==",
            "DefaultSalt": "WW91clNhbHQ="
        }
    }
}
```

#### String Hashing

```json
{
    "Headless": {
        "StringHash": {
            "Iterations": 600000,
            "Size": 128,
            "Algorithm": "SHA256",
            "DefaultSalt": "DefaultSalt"
        }
    }
}
```

### Dependencies

- `Headless.Api.Core`
- `FileSignatures`
- `Microsoft.AspNetCore.OpenApi`
- `Microsoft.Extensions.Http.Resilience`
- `Microsoft.Extensions.ServiceDiscovery`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Instrumentation.Runtime`

### Side Effects

- Enables service-provider validation on startup (`ValidateOnBuild`, `ValidateScopes`).
- Registers all core primitives from `Headless.Api.Core` including problem details, response compression, JWT, identity, status-code rewriting, and default API conventions.
- Registers antiforgery services only when `options.Antiforgery.Enabled` is `true`.
- Configures MVC and Minimal API JSON serializer defaults.
- Registers ASP.NET Core source-generated input validation (`services.AddValidation()`).
- Registers OpenTelemetry logging, metrics, and tracing when `OpenTelemetry.Enabled` is `true`.
- Registers OpenAPI services when `OpenApi.Enabled` is `true`.
- Configures service discovery when `HttpClient.UseServiceDiscovery` is `true`.
- Configures HttpClient defaults for standard resilience, service discovery, and application User-Agent.
- Adds a startup filter that validates `UseHeadless()`, `UseStatusCodesRewriter()`, and `MapHeadlessEndpoints()` usage.

---

## Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

### Problem Solved

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

### Key Features

- `PersistKeysToBlobStorage()` extension for `IDataProtectionBuilder`
- Works with any `IBlobStorage` implementation
- Ensures the `DataProtection` container before writes when an `IBlobContainerManager` is registered or supplied
- Supports factory-based storage resolution for DI scenarios, including keyed/named stores via a `serviceKey` overload
- Enforces container provisioning up front: with no manager and a provisioning-requiring storage (`IBlobStorage.RequiresContainerProvisioning`), configuration throws unless `provisioning: BlobContainerProvisioning.PreProvisioned` acknowledges out-of-band provisioning
- `ValidateKeyRingAtStartup()` — opt-in startup gate (runs before other hosted services) that exercises the key ring, verifies write access with a real sentinel write, and fails an empty key ring on read-only nodes — converting lazy first-write/rotation failures into deploy-time failures
- `AddDataProtectionKeyRing()` — opt-in readiness health check, the continuous complement to the startup gate: re-validates the key-ring store on every health probe, catching a container deleted or write permission revoked after boot. The default probe is the definitive sentinel write (so `Healthy` uniformly means "the key ring can be persisted"); `KeyRingProbeStyle.ContainerExistence` is a cheap explicit opt-down that does not verify write access
- Container ensure runs inside the same retry pipeline as the key upload; terminal write failures surface as `InvalidOperationException` naming the `DataProtection` container, whether a manager was wired, and the remediation (original exception as inner)

### Installation

```bash
dotnet add package Headless.Api.DataProtection
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage()
    // Opt-in: probe the key ring at startup so a missing container / bad credentials fails the deploy,
    // not the first key write or the ~90-day rotation months later.
    .ValidateKeyRingAtStartup();

// Opt-in continuous complement to the startup gate: a readiness health check that re-validates the
// key-ring store on every health probe — see "Key-ring health check" in Configuration.
builder.Services.AddHealthChecks().AddDataProtectionKeyRing();

// Or with explicit storage instance. No manager is involved, so for provisioning-requiring backends this throws
// at config time unless you acknowledge that the DataProtection container exists — see Configuration.
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, provisioning: BlobContainerProvisioning.PreProvisioned);

// Or with explicit storage + container manager (ensures the DataProtection container before writes)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, containerManager);

// Or with factory
builder.Services.AddDataProtection().PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());

// Or against a named/keyed blob store (resolves the keyed IBlobStorage + IBlobContainerManager)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(serviceKey: "keys");
```

### Configuration

No specific configuration. Depends on the underlying `IBlobStorage` configuration. Cloud/object-store providers should also register or pass the matching `IBlobContainerManager` so the `DataProtection` container is created before the first key write.

The missing-container failure mode is **enforced at configuration time**, not just documented: whenever the effective container manager is `null` and the storage reports `IBlobStorage.RequiresContainerProvisioning == true`, `PersistKeysToBlobStorage` throws `InvalidOperationException` (at call time for the storage-instance overload; at first options resolution for the DI/factory/keyed overloads) unless `provisioning: BlobContainerProvisioning.PreProvisioned` acknowledges that the `DataProtection` container was provisioned out-of-band (portal, CLI, IaC). When a manager is present it is always used to ensure the container — `PreProvisioned` never disables it.

Provisioning matrix: **managed** — a manager is registered/keyed/passed, the container is ensured before writes, no acknowledgment needed; **explicit pre-provisioned** — no manager on a provisioning-requiring backend (AWS, Azure, FileSystem, SSH — and Cloudflare R2, where this is the *only* option because R2 ships no `IBlobContainerManager`), provision the container out-of-band and pass `provisioning: BlobContainerProvisioning.PreProvisioned`; **exempt** — Redis reports `RequiresContainerProvisioning == false` (the backing hash materializes on first write), so the storage-only overload works with no acknowledgment.

**Startup validation** — key writes are lazy (first boot + ~90-day rotation), so misconfiguration can stay hidden for months post-deploy. `ValidateKeyRingAtStartup(Action<DataProtectionStartupValidationOptions>? configure = null)` registers an opt-in startup gate: an `IHostedLifecycleService` whose probe runs in `StartingAsync`, before any registered `IHostedService.StartAsync`. With `KeyManagementOptions.AutoGenerateKeys == true` (default) it protects/unprotects a payload through the real provider — on a fresh deployment this generates a key and drives the full persistence path (container ensure + upload); with `AutoGenerateKeys == false` (designated-key-writer topologies) it performs a read-only `IKeyManager.GetAllKeys()` probe and never forces key generation — and a reachable-but-empty key ring FAILS validation (the node would have no usable key; the message asks whether the designated key writer has run / the container is right). Unless `DataProtectionStartupValidationOptions.ProbeWritePath` is disabled (default `true`), BOTH modes also verify write access with a real write: a reserved sentinel blob (`startup-write-probe.xml`) is uploaded and deleted through the same ensure + retry pipeline the key writes use — the only way to catch lost write permission when a valid key already exists (the round-trip performs no write then) and the only write guarantee on read-only nodes; the sentinel is always excluded from key-ring loading, and the probe is skipped with a debug log for non-blob repositories. `DataProtectionStartupValidationOptions.Mode` selects `StartupValidationMode.Throw` (default — `StartingAsync` throws an actionable `InvalidOperationException` naming the `DataProtection` container and the provisioning/manager remediation, failing host start) or `StartupValidationMode.LogOnly` (log at `Critical`, continue). Registration is idempotent.

**Key-ring health check** — `ValidateKeyRingAtStartup` is a one-shot boot gate; it cannot see a container deleted or write permission revoked AFTER the host started. `AddDataProtectionKeyRing(this IHealthChecksBuilder, string name = "dataprotection-keyring", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null, KeyRingProbeStyle probeStyle = KeyRingProbeStyle.WriteProbe)` registers the opt-in continuous complement: a readiness health check that re-validates the key-ring store on every health probe. The probe is selected by `KeyRingProbeStyle`, not by the wiring, so `Healthy` has one meaning per registration (each probe reports a distinct description so operators can tell which ran): `KeyRingProbeStyle.WriteProbe` (default) is the definitive sentinel write probe — the reserved `startup-write-probe.xml` blob is uploaded and deleted through the same ensure + retry pipeline the key writes use, manager or not (crash-safe; the sentinel is always excluded from key-ring loading), so `Healthy` means the key ring can actually be persisted (what the ~90-day rotation needs); `KeyRingProbeStyle.ContainerExistence` is the explicit opt-down — a cheap `ContainerExistsAsync("DataProtection")` existence check via the wired `IBlobContainerManager` (a missing container fails the check — key rotation would fail) that does NOT verify write access: revoked write permission still reports `Healthy` while the next rotation write would fail. With `ContainerExistence` and no manager wired (pre-provisioned mode), the check falls back to the write probe — the only probe possible — and says so in its description (that legitimate wiring does not report `Degraded`). Statuses: `Healthy` — the probe that ran succeeded (with the default style that means the full persistence path is verified); `Degraded` — `KeyManagementOptions.XmlRepository` is not the blob-backed repository (registration misuse, nothing to check — not an outage); `Unhealthy` (default failure status, override via `failureStatus:`) — container missing, existence check threw, or the sentinel write failed, with the probe exception attached. Probe-interval note: the default write probe performs a real write + delete per readiness ping — pair it with a probe interval you are comfortable with, or opt down to `probeStyle: KeyRingProbeStyle.ContainerExistence` and accept its weaker guarantee; the check deliberately does no caching or throttling of its own.

**Write resilience & failure context** — the container ensure runs inside the same retry pipeline as the key upload (transient ensure failures are retried under the same predicate), and a terminal write failure is wrapped in `InvalidOperationException` naming the `DataProtection` container, whether a manager was wired, and the remediation, with the original backend exception as the inner exception (context only — no failure-kind guessing).

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Checks`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- `Microsoft.AspNetCore.DataProtection`
- `Microsoft.Extensions.Diagnostics.HealthChecks`
- `Microsoft.Extensions.Hosting.Abstractions`

### Side Effects

- Configures `KeyManagementOptions.XmlRepository` to use blob storage
- `ValidateKeyRingAtStartup()` registers an `IHostedLifecycleService` that probes the key ring in `StartingAsync`, before other hosted services start (with `AutoGenerateKeys`, the first key may be created at boot instead of at first use; with `ProbeWritePath`, a sentinel blob is written and deleted each boot)
- `AddDataProtectionKeyRing()` adds an `IHealthCheck` registration (default name `dataprotection-keyring`); with the default `KeyRingProbeStyle.WriteProbe`, each probe writes and deletes the sentinel blob

---

## Headless.Api.Idempotency

Stripe-style HTTP idempotency middleware for ASP.NET Core. Cache full HTTP responses (status, allowlisted headers, byte body) on first execution and replay them byte-equivalent on identical retries.

### Problem Solved

Legacy "idempotency as uniqueness guard" (409 on any duplicate key) makes the receipt unrecoverable: a client whose first response was lost on the wire retries with the same key and gets 409 instead of the original 201. This package implements the standard contract (Stripe, AWS, PayPal, Square, IETF `draft-ietf-httpapi-idempotency-key-header`):

- **Same key + same body** → replay original response (status, allowlisted headers, body bytes) with `Idempotent-Replayed: true`
- **Same key + different body** → 422 Unprocessable Content (`g:idempotency_key_reused`)
- **Same key, original in-flight** → 409 Conflict (`g:idempotency_in_flight`), or `WaitAndReplay` with a distributed lock
- **Same key, lock acquisition timed out under `WaitAndReplay`** → 409 Conflict (`g:idempotency_in_flight_timeout`)
- **`Idempotency-Key` header malformed** (length over 255, control characters, multi-valued) → 400 Bad Request (`g:idempotency_key_malformed`)
- **New key** → execute fresh

### Key Features

- Byte-equivalent replay of cached responses
- Two in-flight strategies: `InFlightStrategy.Reject` (default, no extra dependencies) and `InFlightStrategy.WaitAndReplay` (requires `IDistributedLock`)
- Independent request-body memory threshold and fingerprinting cap, with `OversizeBehavior.Reject` (413) or `OversizeBehavior.PassThrough` behaviors
- Header allowlist filters `Set-Cookie`, `traceparent`, and other sensitive or per-request headers from cached responses
- Tenant-aware default cache key: `idem:{tenant}:{userId}:{METHOD}:{path}{?query}:{key}` (query string included so endpoints branching on query params don't cross-replay)
- Per-endpoint overrides via `.WithIdempotency(o => ...)`
- Custom hooks: `KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse`
- Default cache predicate: 2xx + selected 4xx; never 5xx, 1xx, 3xx, or transient 4xx (408/425/429)
- Startup-time DI validation: `WaitAndReplay` without `IDistributedLock` fails fast with `OptionsValidationException`
- `IdempotencyErrorCodes` static class: `KeyReused`, `InFlight`, `InFlightTimeout`, `BodyTooLarge`, `KeyMalformed` as `public const string`

### Design Notes

The middleware uses a lock-before-insert ordering under `WaitAndReplay`: the winner acquires the distributed lock **before** inserting the `InFlight` sentinel marker. Inserting the marker first creates a window where an arriving loser sees the marker, grabs the lock before the winner, then blocks on the same lock it already holds — leaving the winner unlocked and the loser stuck observing the `InFlight` marker until timeout. Lock-before-insert closes that window.

`HeaderName` per-endpoint overrides via `.WithIdempotency()` are deliberately ignored — the middleware reads the request header before resolving endpoint metadata. Changing the header for a single endpoint would require a custom middleware that runs before idempotency, which complicates pipeline ordering without a realistic use case. Change `HeaderName` globally via `AddIdempotency(o => o.HeaderName = ...)`.

`RequestBodyBufferThreshold` controls when request buffering spills from memory to a temporary file; `MaxBodySizeForHashing` independently controls which bodies are eligible for idempotency. The default remains 1 MiB + 1 byte: corrected non-seekable request-body benchmarks showed that 30/64/128 KiB thresholds reduced managed allocations but missed the latency gate at concurrency 1/32/128. Lower it only after measuring the memory, temporary-file I/O, and latency trade-off under representative concurrency.

> **Upgrade note.** These two options were previously coupled: the in-memory buffer threshold was derived from `MaxBodySizeForHashing`, so raising `MaxBodySizeForHashing` above the 1 MiB default also raised the memory-vs-disk spill point for free. They are now independent. A deployment that set a non-default `MaxBodySizeForHashing` will, after upgrading, spill request bodies between 1 MiB + 1 byte and its configured `MaxBodySizeForHashing` to a temporary file during buffering (previously they stayed in memory). This is a latency/temp-file characteristic change only — the fingerprint and oversize behavior are unchanged. To preserve the prior in-memory headroom, set `RequestBodyBufferThreshold` explicitly to match the old effective threshold (`MaxBodySizeForHashing` + 1).

### Installation

```bash
dotnet add package Headless.Api.Idempotency
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory()); // or setup.UseRedis(...)
builder.Services.AddIdempotency(o =>
{
    o.IdempotencyKeyExpiration = TimeSpan.FromHours(24);
    o.InFlightStrategy = InFlightStrategy.Reject;
});

var app = builder.Build();

app.UseAuthentication();
app.UseHeadlessTenancy(); // tenant must be resolved before idempotency
app.UseAuthorization();
app.UseResponseCompression(); // must be OUTSIDE (before) UseIdempotency
app.UseIdempotency(); // installs response-capture stream; place AFTER auth and tenancy

app.MapPost("/disbursements", CreateDisbursement);
app.Run();
```

Per-endpoint overrides:

```csharp
app.MapPost("/webhooks", HandleWebhook)
    .WithIdempotency(o =>
    {
        o.IdempotencyKeyExpiration = TimeSpan.FromDays(7);
        o.MismatchStatusCode = StatusCodes.Status409Conflict;
    });
```

### Configuration

| Property | Default | Purpose |
|----------|---------|---------|
| `IdempotencyKeyExpiration` | 24 hours | TTL for completed records. |
| `HeaderName` | `Idempotency-Key` | Request header carrying the key (per IETF `draft-ietf-httpapi-idempotency-key-header`). |
| `Methods` | POST, PUT, PATCH, DELETE | HTTP methods that participate in idempotency. GET is never valid. |
| `InFlightStrategy` | `Reject` | `Reject` returns 409 on concurrent same-key requests. `WaitAndReplay` blocks on a distributed lock and replays the winner. |
| `InFlightLockTimeout` | 30s | Lock-acquisition timeout for `WaitAndReplay`. Validator-capped at 1 minute. |
| `WinnerLockLease` | 5 minutes | Lease duration for the winner's distributed lock under `WaitAndReplay`. Must be >= `InFlightLockTimeout`. Capped at 1 hour. |
| `MaxBodySizeForHashing` | 1 MiB | Maximum body size eligible for fingerprinting. Capped at 64 MiB. |
| `RequestBodyBufferThreshold` | 1 MiB + 1 byte | Request bytes retained in memory before buffering spills to a temporary file. Capped at 64 MiB + 1 byte. |
| `OversizeBehavior` | `Reject` | `Reject` returns 413 (`g:idempotency_body_too_large`). `PassThrough` runs the handler without idempotency guarantees. |
| `OnCacheError` | `FailOpen` | `FailOpen` logs a warning and bypasses idempotency for pre-handler cache failures; post-handler finalize failures remove the marker and preserve the handler response. `Throw` propagates the exception as 5xx. |
| `RequireUserIdentity` | `true` | When `true`, the default cache key requires an authenticated user; tenant-only anonymous requests pass through. Set `false` for webhook receivers / OAuth callbacks. |
| `MismatchStatusCode` | 422 | Status code for fingerprint mismatch. Must be 409 or 422. |
| `ReplayHeaderAllowlist` | Content-Type, Content-Language, Content-Encoding, Content-Disposition, Location, Link, ETag, Last-Modified, Cache-Control, Vary | Response headers copied into the cached record. `Set-Cookie` and `traceparent` are excluded by design. |
| `ShouldCacheResponse` | `DefaultCachePredicate.Instance` | Predicate deciding whether a completed response is cached. |
| `ShouldApply` | `null` | Per-request opt-out hook. |
| `KeyDeriver` | `null` (uses `idem:{tenant}:{userId}:{METHOD}:{path}{?query}:{key}`) | Custom cache-key derivation. |
| `RequestFingerprint` | `null` (uses SHA-256 of buffered body) | Custom fingerprint computation. Must return non-empty bytes. |

### Dependencies

- `Headless.Api.Abstractions`
- `Headless.Api.Core`
- `Headless.Caching.Abstractions` (you supply the implementation: in-memory, Redis, etc.)
- `Headless.DistributedLocks.Abstractions` (required only when using `InFlightStrategy.WaitAndReplay`)
- `Headless.Core`
- `Headless.FluentValidation`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference)

### Side Effects

- Reads `ICurrentTenant.Id` and authenticated `ICurrentUser.UserId` for cache-key composition; when both are absent and no `KeyDeriver` is configured, the middleware passes through without applying idempotency.
- Buffers the request body via `HttpRequest.EnableBuffering`; bytes beyond `RequestBodyBufferThreshold` spill to a temporary file.
- On replay, writes `Idempotent-Replayed: true` to the response. Pre-existing allowlisted response headers set by upstream middleware are removed before captured headers are written for byte-equivalent replay.
- On cache miss, inserts an `InFlight` sentinel marker before invoking the handler, then promotes it to the `Complete` record afterward using compare-and-swap (`TryReplaceIfEqualAsync`). The marker uses the same TTL as `IdempotencyKeyExpiration`.
- When the **response** body exceeds `MaxBodySizeForHashing` (`captureStream.TruncatedCapture`), the completed record is not stored and replay does not apply. `OversizeBehavior` controls **request**-body handling only.

---

## Headless.Api.Logging.Serilog

Serilog integration for ASP.NET Core APIs with custom enrichers for request context.

### Problem Solved

Enriches Serilog log events with HTTP request context (client IP, user agent, user ID, tenant ID, correlation ID) for better observability and debugging in web applications.

### Key Features

- Custom Serilog enricher middleware
- Client info enrichment (IP, user agent)
- Request context enrichment (user, tenant, correlation ID)
- Integration with `Headless.Logging.Serilog` configuration

### Installation

```bash
dotnet add package Headless.Api.Logging.Serilog
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register enrichers
builder.Services.AddSerilogEnrichers();

var app = builder.Build();

// Use enrichers middleware (place early in pipeline)
app.UseSerilogEnrichers();

app.Run();
```

### Configuration

Inherits Serilog configuration from `Headless.Logging.Serilog`. See that package for sink and enricher configuration.

### Dependencies

- `Headless.Api.Abstractions`
- `Headless.Logging.Serilog`
- `Serilog.Enrichers.ClientInfo`
- `Microsoft.AspNetCore.App` (framework reference)

### Side Effects

- Adds middleware to the request pipeline
- Enriches log context per-request

---

## Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

### Problem Solved

Provides consistent JSON serialization and validation for Minimal API endpoints matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api.Core`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

### Key Features

- Pre-configured JSON serialization options
- `MinimalApiValidatorFilter` — FluentValidation integration via `.Validate<T>()` on endpoint builders
- API versioning integration
- Endpoint discovery extensions

### Installation

```bash
dotnet add package Headless.Api.MinimalApi
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureMinimalApi();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) => Results.Ok(new { id })).Validate<GetOrderRequest>();

app.Run();
```

### Configuration

No additional configuration required. Uses framework JSON settings automatically.

### Dependencies

- `Headless.Api.Core` (and `Headless.Api.ServiceDefaults` if you want the orchestrator)
- `Asp.Versioning.Http`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Configures `JsonOptions` for Minimal APIs

---

## Headless.Api.Mvc

Framework integration for ASP.NET Core MVC/Web API with controllers, filters, JSON configuration, and common utilities.

### Problem Solved

Provides consistent MVC configuration, base controllers, and URL canonicalization for traditional controller-based APIs. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api.Core`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`), so MVC actions get the same response shape as Minimal-API endpoints.

### Key Features

- `ApiControllerBase` — base controller with common utilities
- Environment-based action filters (`BlockInEnvironmentAttribute`, `RequireEnvironmentAttribute`)
- URL canonicalization middleware (`RedirectToCanonicalUrlRule`)
- Pre-configured JSON and MVC options
- Direct MVC `ObjectResult` responses carrying Headless-normalized `ProblemDetails` run `ProblemDetailsOptions.CustomizeProblemDetails` once before serialization
- API versioning integration with API Explorer

### Installation

```bash
dotnet add package Headless.Api.Mvc
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureMvc();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run();
```

#### Controller Example

```csharp
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ApiControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAsync(int id, CancellationToken ct)
    {
        var order = await _service.GetAsync(id, ct).ConfigureAwait(false);
        return order is null ? NotFound() : Ok(order);
    }
}
```

### Configuration

No additional configuration required.

### Dependencies

- `Headless.Api.Core` (and `Headless.Api.ServiceDefaults` if you want the orchestrator)
- `Asp.Versioning.Mvc`
- `Asp.Versioning.Mvc.ApiExplorer`

### Side Effects

- Configures `MvcOptions` and `JsonOptions` for controllers
- Adds a result filter that applies ProblemDetails customization to Headless-generated MVC object results
