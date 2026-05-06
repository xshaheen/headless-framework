# Headless.Api

Core ASP.NET Core API infrastructure providing service registration, middleware, security, JWT handling, and common API utilities.

## Problem Solved

Consolidates repetitive ASP.NET Core API setup (compression, security headers, problem details, JWT, identity, validation) into a single cohesive registration, ensuring consistent configuration across all API projects.

## Key Features

- One-call service registration via `AddHeadless()`
- Multi-tenancy primitives via `AddHeadlessMultiTenancy()` and `UseTenantResolution()`
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
builder.AddHeadlessMultiTenancy();

var app = builder.Build();

// Optional: Add diagnostic listeners for debugging
using var _ = app.AddHeadlessApiDiagnosticListeners();

app.UseResponseCompression();
app.UseHsts();
app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.Run();
```

`AddHeadless()` requires the `Headless:StringEncryption` and `Headless:StringHash` configuration sections.

If you do not want the default `Headless:*` binding, `AddHeadless()` also exposes explicit overloads for:

- `IConfiguration stringEncryptionConfig, IConfiguration stringHashConfig`
- `Action<StringEncryptionOptions>, Action<StringHashOptions>?`
- `Action<StringEncryptionOptions, IServiceProvider>, Action<StringHashOptions, IServiceProvider>?`

When the hash callback is omitted, `AddHeadless(...)` still binds `Headless:StringHash` by default.

## Multi-Tenancy

`AddHeadless()` registers `CurrentTenant` by default, and `Headless.Orm.EntityFramework` now uses the same default for `AddHeadlessDbContextServices()`. For claim-based HTTP tenant resolution, opt in with:

```csharp
builder.AddHeadlessMultiTenancy(options =>
{
    options.ClaimType = UserClaimTypes.TenantId; // default
});

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();
```

Place `UseTenantResolution()` after authentication and before authorization.

### Exception Mapping

`AddHeadlessProblemDetails()` (called by `AddHeadless()`) auto-registers a single `IExceptionHandler` (`HeadlessApiExceptionHandler`) that covers any unhandled exception that bubbles to ASP.NET Core's exception-handler middleware — typically MVC actions and Minimal-API endpoints. Middleware running before `UseExceptionHandler`, hosted/background services, and SignalR hubs need their own catch sites.

| Exception | Response |
|-----------|----------|
| `MissingTenantContextException` | 400 (standard `bad-request` title; identified by `code: tenancy.tenant-required`). Previously surfaced as 500 (unhandled); now 400 — terminal, do not retry. |
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
  "code": "tenancy.tenant-required",
  "traceId": "...",
  "buildNumber": "...",
  "commitNumber": "...",
  "instance": "/path",
  "timestamp": "..."
}
```

The body deliberately surfaces no entity name, exception message, `Exception.Data` tag, or inner exception — those belong in server logs, not API responses. Clients route on stable `code` and `status` values.

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
- `Headless.Security.Abstractions`
- `Headless.Security`
- `Headless.Caching.Abstractions`
- `Headless.FluentValidation`
- `Headless.Api.FluentValidation`
- `Headless.Hosting`
- `Asp.Versioning.Http`
- `DeviceDetector.NET`
- `FluentValidation`
- `Mediator.Abstractions`
- `Microsoft.AspNetCore.OpenApi`
- `Microsoft.Extensions.Http.Resilience`
- `NetEscapades.AspNetCore.SecurityHeaders`

## Side Effects

- Registers `HttpContextAccessor`
- Configures response compression providers
- Configures route options (lowercase URLs)
- Configures form options (file upload limits)
- Configures HSTS options
- Adds resilience handler to `HttpClient` defaults
