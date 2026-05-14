# Headless.Api

Core ASP.NET Core API infrastructure providing service registration, middleware, security, JWT handling, and common API utilities.

## Problem Solved

Consolidates repetitive ASP.NET Core API setup (compression, security headers, problem details, JWT, identity, validation) into a single cohesive registration, ensuring consistent configuration across all API projects.

## Key Features

- One-call service registration via `AddHeadless()`
- One-call middleware defaults via `UseHeadless()`
- Operational endpoints via `MapHeadlessEndpoints()` (`/health`, `/alive`)
- HTTP tenant resolution through the root `AddHeadlessTenancy(...).Http(...)` surface and `UseHeadlessTenancy()`
- Unified exception-to-ProblemDetails mapping via `HeadlessApiExceptionHandler` (auto-registered by `AddHeadlessProblemDetails()`): covers tenancy, conflict, validation, not-found, EF concurrency, timeout, not-implemented, and cancellation for any unhandled exception that bubbles to ASP.NET Core's exception-handler middleware (typically MVC actions and Minimal-API endpoints)
- Response compression (Brotli, Gzip) with optimized settings
- Problem details standardization
- JWT token factory and claims principal handling
- HSTS security configuration
- API versioning integration
- Device detection
- Idempotency middleware
- Server timing middleware
- Request cancellation handling
- Diagnostic listeners for debugging

## Installation

```bash
dotnet add package Headless.Api
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure global settings (regex timeout, FluentValidation, JWT)
ApiSetup.ConfigureGlobalSettings();

// Register all framework API services
builder.AddHeadless();
builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()));

var app = builder.Build();

// Optional: Add diagnostic listeners for debugging
using var _ = app.AddHeadlessApiDiagnosticListeners();

app.UseHeadless(options =>
{
    // Enable only when the app is reachable exclusively through trusted proxy infrastructure.
    options.TrustForwardedHeadersFromAnyProxy = false;
});
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
app.MapHeadlessEndpoints();

app.Run();
```

`AddHeadless()` requires the `Headless:StringEncryption` and `Headless:StringHash` configuration sections.

If you do not want the default `Headless:*` binding, `AddHeadless()` also exposes explicit overloads for:

- `Action<HeadlessApiInfrastructureOptions>`
- `IConfiguration stringEncryptionConfig, IConfiguration stringHashConfig`
- `Action<StringEncryptionOptions>, Action<StringHashOptions>?`
- `Action<StringEncryptionOptions, IServiceProvider>, Action<StringHashOptions, IServiceProvider>?`

The security overloads also accept an optional infrastructure-options callback. When the hash callback is omitted, `AddHeadless(...)` still binds `Headless:StringHash` by default.

## Multi-Tenancy

`AddHeadless()` registers base API infrastructure. Tenant posture is configured separately through the root tenancy surface:

```csharp
builder.AddHeadlessTenancy(
    tenancy => tenancy.Http(http => http.ResolveFromClaims(options =>
    {
        options.ClaimType = UserClaimTypes.TenantId; // default
    }))
);
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

Place `UseHeadlessTenancy()` after app-owned `UseAuthentication()` and before app-owned `UseAuthorization()`. Headless does not call authentication or authorization internally.

HTTP tenancy registration is now exclusively via `AddHeadlessTenancy(...).Http(http => http.ResolveFromClaims())`. `UseTenantResolution()` remains available as a lower-level HTTP-pipeline API for callers wiring middleware manually without going through `UseHeadlessTenancy()`.

## API Defaults

`UseHeadless()` applies the standard middleware order for Headless APIs:

- `UseForwardedHeaders()`
- `UseResponseCompression()`
- `UseStatusCodePages()`
- `UseStatusCodesRewriter()`
- `UseExceptionHandler()`
- `UseHttpsRedirection()`
- `UseHsts()` outside Development
- `UseAntiforgery()`
- no-cache response header when the response did not set `Cache-Control`

`TrustForwardedHeadersFromAnyProxy` defaults to `false`. Turn it on only when the service is not directly reachable by untrusted clients; otherwise clients can spoof forwarded host/scheme values.

`ConfigureHeadlessDefaultApi()` also applies conservative Kestrel limits: 30MB max request body and 40 request headers by default.

`MapHeadlessEndpoints()` maps:

- `/health` for all registered health checks, with a JSON body containing `status` and per-check `results`
- `/alive` for health checks tagged `live`
- static web assets when the generated static-web-assets endpoint manifest exists
- OpenAPI JSON documents at `/openapi/{documentName}.json`

Both endpoints are named, excluded from OpenAPI descriptions, and allow anonymous requests by default. `AddHeadless()` registers the default `self` liveness check and disables Kestrel's `Server` response header.

## Service Defaults

`AddHeadless()` now includes the upstream-style service defaults directly:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless(options =>
{
    options.OpenTelemetry.Enabled = true;
    options.HttpClient.UseServiceDiscovery = true;
    options.ValidateUseHeadlessDefaultsOnStartup = true;
    options.ValidateMapHeadlessDefaultEndpointsOnStartup = true;
});

var app = builder.Build();

app.UseHeadless();
app.MapHeadlessEndpoints();
app.Run();
```

The registration bundle includes container validation, antiforgery services, Headless ProblemDetails, OpenAPI, OpenTelemetry logging/metrics/tracing, service discovery, default HttpClient resilience, application `User-Agent`, MVC/Minimal API JSON defaults, ASP.NET validation, and the default `self` health check. `UseHeadless()` applies forwarded headers, response compression, status-code rewriting, exception handling, HTTPS/HSTS, antiforgery, and no-cache fallback. `MapHeadlessEndpoints()` maps `/health`, `/alive`, static web assets when their manifest exists, and OpenAPI JSON documents.

Compatibility aliases remain available: `AddHeadlessInfrastructure()`, `UseHeadlessDefaults()`, and `MapHeadlessDefaultEndpoints()`.

### Exception Mapping

`AddHeadlessProblemDetails()` (called by `AddHeadless()`) auto-registers a single `IExceptionHandler` (`HeadlessApiExceptionHandler`) that covers any unhandled exception that bubbles to ASP.NET Core's exception-handler middleware — typically MVC actions and Minimal-API endpoints. Middleware running before `UseExceptionHandler`, hosted/background services, and SignalR hubs need their own catch sites.

| Exception | Response |
|-----------|----------|
| `MissingTenantContextException` | 400 (standard `bad-request` title; identified by `error.code: g:tenant-required`). Previously surfaced as 500 (unhandled); now 400 — terminal, do not retry. |
| `CrossTenantWriteException` | 409 (identified by `error.code: g:cross-tenant-write`). Non-transient — MUST NOT be retried. |
| `ConflictException` | 409 with `errors` |
| `FluentValidation.ValidationException` | 422 with field errors |
| `EntityNotFoundException` | 404 |
| EF Core `DbUpdateConcurrencyException` (matched by type name) | 409 with concurrency-failure error |
| `TimeoutException` | 408 |
| `NotImplementedException` | 501 |
| `OperationCanceledException` (or inner OCE at any depth) | 499 (no body — client closed request) |

```csharp
builder.AddHeadless();

var app = builder.Build();
app.UseExceptionHandler();
```

Tenancy response shape (other exceptions follow the same normalization):

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

The body deliberately surfaces no entity name, exception message, `Exception.Data` tag, or inner exception — those belong in server logs, not API responses. Clients route on the stable `error.code` and `status` values; the `error` extension is only present when the framework attaches a discriminator (currently the tenancy guard).

Prerequisites: call `app.UseExceptionHandler()` to wire the `IExceptionHandler` chain into the pipeline. The handler is registered by `AddHeadlessProblemDetails()`, so consumers who need their own catch-all to win must register it **before** that call.

The same tenancy shape is reachable for direct callers via `IProblemDetailsCreator.TenantRequired()` (parameterless). The 408 (`RequestTimeout()`) and 501 (`NotImplemented()`) shapes are reachable the same way.

> **Side effects of `AddHeadlessProblemDetails()`**: this method also registers `HeadlessApiExceptionHandler` as an `IExceptionHandler` via `TryAddEnumerable`. Consumer-registered `IExceptionHandler`s placed **before** this call run first.

### Breaking Changes & Migration

- `IProblemDetailsCreator` gained `TenantRequired()`, `RequestTimeout()`, and `NotImplemented()`. Custom implementers must add these members.
- The per-package `MvcApiExceptionFilter` and `MinimalApiExceptionFilter` (and `RouteGroupBuilder.AddExceptionFilter()` / `options.Filters.Add<MvcApiExceptionFilter>()`) were removed. They are replaced by the global `HeadlessApiExceptionHandler` registered via `services.AddHeadlessProblemDetails()` plus `app.UseExceptionHandler()` in the request pipeline.

## Configuration

### String Encryption

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

### String Hashing

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
`AddHeadless()` binds both `Headless:StringEncryption` and `Headless:StringHash`, and requires both sections to exist.

## Dependencies

- `Headless.Api.Abstractions`
- `Headless.Core`
- `Headless.MultiTenancy`
- `Headless.Security.Abstractions`
- `Headless.Security`
- `Headless.Caching.Abstractions`
- `Headless.FluentValidation`
- `Headless.Api.FluentValidation`
- `Headless.Hosting`
- `Asp.Versioning.Http`
- `DeviceDetector.NET`
- `FluentValidation`
- `Microsoft.AspNetCore.OpenApi`
- `Microsoft.Extensions.Http.Resilience`
- `NetEscapades.AspNetCore.SecurityHeaders`

## Side Effects

- Registers `HttpContextAccessor`
- Configures response compression providers
- Configures route options (lowercase URLs)
- Configures form options (file upload limits)
- Configures HSTS options
- Disables Kestrel's `Server` response header
- Registers default `self` health check tagged `live`
- Adds resilience handler to `HttpClient` defaults
