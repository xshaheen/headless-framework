# Headless.Api.Core

Building blocks for ASP.NET Core APIs — primitives only. Provides service registration helpers, middleware, problem details, JWT, identity, security headers, and request-context abstractions.

> Looking for `AddHeadless()`, `UseHeadless()`, `MapHeadlessEndpoints()`? Those live in [`Headless.Api.ServiceDefaults`](../Headless.Api.ServiceDefaults/README.md). This package is the parts catalog; ServiceDefaults is the assembly.

## When to Use This Package

Pull `Headless.Api.Core` directly when you need à la carte primitives without the framework orchestrator — for example, registering `AddHeadlessProblemDetails()` alone, or composing your own middleware pipeline without the default order.

For the standard one-line bootstrap, use [`Headless.Api.ServiceDefaults`](../Headless.Api.ServiceDefaults/README.md) instead. It transitively brings in this package.

## Installation

```bash
dotnet add package Headless.Api.Core
```

## Key Features

- Problem details standardization via `AddHeadlessProblemDetails()` (registers `HeadlessApiExceptionHandler` covering tenancy, conflict, validation, not-found, EF concurrency, timeout, not-implemented, and cancellation)
- Response compression (Brotli, Gzip) via `AddHeadlessApiResponseCompression()`
- Antiforgery via `AddHeadlessAntiforgery()`
- JWT token factory and claims principal handling
- HSTS security configuration
- API versioning integration
- Device detection (`IWebClientInfoProvider`)
- Idempotency middleware (`AddIdempotencyMiddleware()`)
- Server timing middleware
- Request cancellation handling
- Diagnostic listeners for debugging (`AddHeadlessApiDiagnosticListeners`)
- Status codes rewriter (`AddStatusCodesRewriterMiddleware()`)
- Kestrel limits and default API conventions via `ConfigureHeadlessDefaultApi()`

## Building Blocks Quick Reference

```csharp
services.AddHeadlessProblemDetails();
services.AddHeadlessApiResponseCompression();
services.AddHeadlessAntiforgery();
services.AddStatusCodesRewriterMiddleware();
services.ConfigureHeadlessDefaultApi();   // Kestrel limits, lowercase URLs, HSTS, default 'self' health check
services.AddIdempotencyMiddleware(...);
services.AddServerTimingMiddleware();
```

Tenant resolution primitives:

```csharp
builder.AddHeadlessMultiTenancy(options => options.ClaimType = UserClaimTypes.TenantId);
app.UseTenantResolution();
```

## Exception Mapping

`AddHeadlessProblemDetails()` registers a single `IExceptionHandler` (`HeadlessApiExceptionHandler`) that maps framework exceptions to normalized ProblemDetails responses. Covers any unhandled exception that bubbles to ASP.NET Core's exception-handler middleware — typically MVC actions and Minimal-API endpoints. Middleware running before `UseExceptionHandler`, hosted/background services, and SignalR hubs need their own catch sites.

| Exception | Response |
|-----------|----------|
| `MissingTenantContextException` | 400 (identified by `error.code: g:tenant-required`) |
| `CrossTenantWriteException` | 409 (identified by `error.code: g:cross-tenant-write`) |
| `ConflictException` | 409 with `errors` |
| `FluentValidation.ValidationException` | 422 with field errors |
| `EntityNotFoundException` | 404 |
| EF Core `DbUpdateConcurrencyException` (matched by type name) | 409 with concurrency-failure error |
| `TimeoutException` | 408 |
| `NotImplementedException` | 501 |
| `OperationCanceledException` (or inner OCE at any depth) when `HttpContext.RequestAborted` is the source | 499 (no body — client closed request) |

OCEs from other sources (server-side timeouts surfaced by `RequestTimeoutsMiddleware`, library-thrown OCEs whose cancellation token is not `RequestAborted`) are intentionally not mapped here — the handler returns `false` so the platform default or a downstream handler can render them.

Prerequisites: call `app.UseExceptionHandler()` to wire the `IExceptionHandler` chain into the pipeline.

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

The body deliberately surfaces no entity name, exception message, `Exception.Data` tag, or inner exception — those belong in server logs, not API responses. Clients route on the stable `error.code` and `status` values.

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
- `Microsoft.Extensions.Http.Resilience`
- `NetEscapades.AspNetCore.SecurityHeaders`
