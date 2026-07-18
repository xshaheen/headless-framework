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
- `AddServerTimingMiddleware()` + `UseServerTiming()` — appends `Server-Timing` trailer when response supports trailers
- `UseNoCacheWhenMissingCacheHeaders()` — injects `Cache-Control: no-cache,no-store,must-revalidate` when response omits the header
- Basic/API-key authentication helpers — `AddBasicSchema()` and `AddApiKey()` register the canonical `Basic` and `ApiKey` schemes; handlers only authenticate credentials supplied for their own scheme
- HTTP tenant resolution: `ResolveFromClaims()`, `UseHeadlessTenancy()`, `[SkipTenantResolution]`, `.SkipTenantResolution()`
- HTTP tenant authorization: `TenantRequirement`, `[AllowMissingTenant]`, `.AllowMissingTenant()`, `[RequireTenant]`, `.RequireTenant()`
- Diagnostic listeners: `AddHeadlessApiDiagnosticListeners()`, `BadRequestDiagnosticAdapter`, `MiddlewareAnalysisDiagnosticAdapter`

## Design Notes

- `IProblemDetailsCreator` factory methods normalize Headless fields (`traceId`, build metadata, `instance`, timestamp) but leave consumer `ProblemDetailsOptions.CustomizeProblemDetails` callbacks to the final response writer. Exception-handler and status-code-rewriter responses run consumer customization once through ASP.NET Core's `IProblemDetailsService`; MVC direct `ObjectResult` responses built from Headless-normalized ProblemDetails are customized once by `Headless.Api.Mvc`.
- `HeadlessApiExceptionHandler` honors `Accept` quality values when deciding whether to write JSON ProblemDetails. A request that rejects JSON, or explicitly rejects `application/problem+json`, with `q=0` is left for downstream/default handlers instead of receiving a JSON body.
- Basic authentication delegates password validation to `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, so configured ASP.NET Core Identity lockout policies apply to failed Basic credentials.
- Batch `IFormFile.SaveAsync(...)` preserves result ordering while bounding concurrent file stream copies to `Environment.ProcessorCount` to avoid unbounded file-handle and disk pressure on large multipart requests.
- `IFormFile.GetAllBytesAsync(CancellationToken cancellationToken = default)` propagates optional cancellation through asynchronous upload buffering; callers can omit the token.

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

## Configuration

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

`StatusCodesRewriterMiddleware` is required for the `g:tenant_required` discriminator on 403 authorization rejections. `Headless.Api.ServiceDefaults` wires it automatically; apps that skip ServiceDefaults must call `UseStatusCodesRewriter()` themselves. `TenantRequirement` must live in `DefaultPolicy` or `FallbackPolicy` — the startup validator does not inspect named policies. `UseHeadlessTenancy()` / `UseTenantResolution()` must run after `UseRouting()` so endpoint metadata is available when `[SkipTenantResolution]` is evaluated.

`AddBasicSchema()` defaults to the canonical `Basic` authentication scheme and `AddApiKey()` defaults to `ApiKey`. `DynamicAuthenticationSchemeProvider` selects those same canonical names. API keys are read from the configured header by default; query-string keys are routed and accepted only when `ApiKeyAuthenticationSchemeOptions.AllowApiKeyInQueryString` is `true`.

## Dependencies

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

## Side Effects

- Registers `HttpContextAccessor` (via `AddHeadlessProblemDetails`)
- Configures response compression providers (Brotli, Gzip)
- Configures Kestrel limits and disables `Server` response header (via `ConfigureHeadlessDefaultApi`)
- Configures route options (lowercase URLs, no trailing slash)
- Configures form options (value limit, multipart limits)
- Configures HSTS options (365-day max-age, subdomain inclusion, preload)
- Registers `self` liveness health check tagged `live`
