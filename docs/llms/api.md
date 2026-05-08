---
domain: API & Web
packages: Api, Api.Abstractions, Api.DataProtection, Api.FluentValidation, Api.Logging.Serilog, Api.MinimalApi, Api.Mvc
---

# API & Web

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Api](#headlessapi)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Tenant-Context Exception Mapping](#tenant-context-exception-mapping)
    - [Configuration](#configuration)
        - [String Encryption](#string-encryption)
        - [String Hashing](#string-hashing)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Api.Abstractions](#headlessapiabstractions)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Usage](#usage)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Api.DataProtection](#headlessapidataprotection)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Api.FluentValidation](#headlessapifluentvalidation)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Api.Logging.Serilog](#headlessapiloggingserilog)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Api.MinimalApi](#headlessapiminimalapi)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Api.Mvc](#headlessapimvc)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-5)
        - [Controller Example](#controller-example)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)

> ASP.NET Core API infrastructure: service registration, JWT, middleware, validation, logging, and endpoint integration for Minimal API and MVC.

## Quick Orientation

The core package is `Headless.Api` — call `AddHeadlessFramework()` to register compression, security headers, problem details, JWT, identity, and validation in one shot. Then choose an endpoint style:

- **Minimal API** (recommended for new projects): Add `Headless.Api.MinimalApi` and call `ConfigureMinimalApi()` for JSON config, validation filters, and exception handling.
- **MVC/Controllers**: Add `Headless.Api.Mvc` and call `ConfigureMvc()` for base controllers, exception filters, and URL canonicalization.

Use `Headless.Api.Abstractions` when you only need interfaces (`IRequestContext`, `IWebClientInfoProvider`) without pulling in the full API stack.

Additional packages:

- `Headless.Api.FluentValidation` — validators for `IFormFile` uploads (size, content type, magic bytes).
- `Headless.Api.DataProtection` — persist ASP.NET Core Data Protection keys to any `IBlobStorage` provider.
- `Headless.Api.Logging.Serilog` — enrich Serilog logs with per-request context (IP, user agent, user ID, tenant ID, correlation ID).

## Agent Instructions

- Use `AddHeadlessFramework()` on `WebApplicationBuilder` for bootstrapping; do not manually register compression, security headers, or problem details.
- Call `ApiSetup.ConfigureGlobalSettings()` before `AddHeadlessFramework()` to set regex timeout, FluentValidation, and JWT defaults.
- Prefer `Headless.Api.MinimalApi` over `Headless.Api.Mvc` for new projects. Use `.Validate<T>()` on endpoints for FluentValidation integration.
- For MVC, inherit from `ApiControllerBase` — it provides common utilities. Use `ConfigureMvc()` not manual `MvcOptions` configuration.
- Use `Headless.Api.FluentValidation` validators (`FileNotEmpty()`, `LessThanOrEqualTo()`, `ContentTypes()`, `HaveSignatures()`) for `IFormFile` validation — do not write manual file validation logic.
- Use `PersistKeysToBlobStorage()` from `Headless.Api.DataProtection` to persist Data Protection keys in distributed/containerized environments.
- For Serilog enrichment, call `AddSerilogEnrichers()` on services and `UseSerilogEnrichers()` on the app — place the middleware early in the pipeline.
- Inject `IRequestContext` (from Abstractions) for request-scoped user, tenant, locale, timezone, and correlation ID — never access `HttpContext` directly in service code.
- `AddHeadlessFramework()` auto-binds `Headless:StringEncryption` and `Headless:StringHash` through `Headless.Security`, and also exposes explicit overloads for configuration sections and option callbacks when the defaults are not suitable. When the hash callback is omitted, it still binds `Headless:StringHash` by default.

---

# Headless.Api

Core ASP.NET Core API infrastructure providing service registration, middleware, security, JWT handling, and common API utilities.

## Problem Solved

Consolidates repetitive ASP.NET Core API setup (compression, security headers, problem details, JWT, identity, validation) into a single cohesive registration, ensuring consistent configuration across all API projects.

## Key Features

- One-call service registration via `AddHeadlessFramework()`
- Response compression (Brotli, Gzip) with optimized settings
- Problem details standardization
- Unified exception-to-ProblemDetails mapping via `HeadlessApiExceptionHandler` (auto-registered by `AddHeadlessProblemDetails()`): covers tenancy, conflict, validation, not-found, EF concurrency, timeout, not-implemented, and cancellation across MVC, Minimal API, middleware, hosted services, and hubs
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
builder.AddHeadlessFramework();

var app = builder.Build();

// Optional: Add diagnostic listeners for debugging
using var _ = app.AddHeadlessApiDiagnosticListeners();

app.UseResponseCompression();
app.UseHsts();

app.Run();
```

## Exception Mapping

`AddHeadlessProblemDetails()` (called by `AddHeadlessFramework()`) auto-registers a single `IExceptionHandler` (`HeadlessApiExceptionHandler`) that maps framework-known exceptions to normalized ProblemDetails responses. Covers any unhandled exception that bubbles to ASP.NET Core's exception-handler middleware — typically MVC actions and Minimal-API endpoints. Middleware running before `UseExceptionHandler`, hosted/background services, and SignalR hubs need their own catch sites.

| Exception | Response |
|-----------|----------|
| `Headless.Abstractions.MissingTenantContextException` | 400 (standard `bad-request` title; identified by `error.code: g:tenant-required`) |
| `Headless.Exceptions.ConflictException` | 409 with `errors` |
| `FluentValidation.ValidationException` | 422 with field errors |
| `Headless.Exceptions.EntityNotFoundException` | 404 |
| EF Core `DbUpdateConcurrencyException` (matched by type name) | 409 with concurrency-failure error |
| `TimeoutException` (code-thrown — HTTP client, downstream call, custom timer) | 408 with full ProblemDetails body |
| `NotImplementedException` | 501 |
| `OperationCanceledException` **and** `HttpContext.RequestAborted.IsCancellationRequested` is true (walked recursively through `InnerException` and `AggregateException.InnerExceptions`) | 499 (no body — client closed request) |
| `OperationCanceledException` from any other source (server-side cancel, library-thrown OCE) | passes through (handler returns `false`) |
| Anything else | passes through (handler returns `false`) |

**Cancellation vs timeout — three flavors:** the table covers two of three OCE paths. The third — server-side request timeouts via ASP.NET Core's `RequestTimeoutsMiddleware` — never reaches this handler: the middleware translates its CTS-fired OCE into a bare 408 status. Pair `app.UseStatusCodePages()` (registered **before** `app.UseRequestTimeouts()` and `app.UseExceptionHandler()`) with `IProblemDetailsCreator.Normalize`'s 408 backfill so the bare-status path produces the same `Title`/`Type`/`Detail` shape as the `case TimeoutException` arm. Full pattern, including the AggregateException trap and pipeline ordering, lives in [`docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md`](../solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md).

```csharp
builder.AddHeadlessFramework();

var app = builder.Build();
app.UseExceptionHandler(); // required — wires the IExceptionHandler chain into the pipeline.
```

Tenancy response shape:

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

**Information-disclosure invariant:** the body contains only the framework-owned `type`, `title`, `status`, `detail`, optional `error` discriminator, plus the standard normalized extensions. Exception `Message`, `Data`, and `InnerException` content are deliberately NOT surfaced — they belong in server logs. Clients route on stable `error.code` (when present) and `status` values. The `error` extension is opt-in: factories only stamp it when the caller supplies an `ErrorDescriptor`; the tenancy mapping uses `HeadlessProblemDetailsConstants.Errors.TenantContextRequired`.

**Prerequisites:**

- Call `app.UseExceptionHandler()` to wire the `IExceptionHandler` chain into the pipeline.

**Handler-chain ordering:** `IExceptionHandler` instances run in registration order. The framework handler is registered by `AddHeadlessProblemDetails()`, so it wins against any catch-all registered after that call. If a consumer needs their own catch-all to win, register it **before** `AddHeadlessProblemDetails()` (or before `AddHeadlessFramework()`, which calls it).

**Related factory:** `IProblemDetailsCreator.TenantRequired()` (parameterless) produces the same tenancy response shape for direct callers (e.g., a request-pipeline pre-check that wants to short-circuit without throwing).

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

---

# Headless.Api.Abstractions

Defines core interfaces and contracts for HTTP request context, user identity, and web client information in ASP.NET Core applications.

## Problem Solved

Provides a standardized abstraction layer for accessing request-scoped context (user, tenant, locale, timezone, client info) without coupling application code to ASP.NET Core's `HttpContext` directly.

## Key Features

- `IRequestContext` - Unified access to request-scoped information (user, tenant, locale, timezone, correlation ID)
- `IWebClientInfoProvider` - Client detection (IP address, user agent, device info)
- `IRequestedApiVersion` - API versioning abstraction
- Framework constants for HTTP headers and common values

## Installation

```bash
dotnet add package Headless.Api.Abstractions
```

## Usage

Inject `IRequestContext` to access request-scoped information:

```csharp
public sealed class OrderService(IRequestContext context)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var userId = context.User.Id;
        var tenantId = context.Tenant.Id;
        var correlationId = context.CorrelationId;

        // Use context information for auditing, logging, multi-tenancy
        return await _repository.CreateAsync(new Order
        {
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = context.DateStarted
        }, ct).ConfigureAwait(false);
    }
}
```

## Configuration

No configuration required. This package contains interfaces only.

## Dependencies

- `Headless.Core`

## Side Effects

## None. This is an abstractions-only package.

# Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

## Problem Solved

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

## Key Features

- `PersistKeysToBlobStorage()` extension for `IDataProtectionBuilder`
- Works with any `IBlobStorage` implementation
- Supports factory-based storage resolution for DI scenarios

## Installation

```bash
dotnet add package Headless.Api.DataProtection
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage();

// Or with explicit storage instance
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage(storageInstance);

// Or with factory
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());
```

## Configuration

No specific configuration. Depends on the underlying `IBlobStorage` configuration.

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Checks`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- `Microsoft.AspNetCore.DataProtection`

## Side Effects

- Configures `KeyManagementOptions.XmlRepository` to use blob storage

---

# Headless.Api.FluentValidation

FluentValidation extensions for validating ASP.NET Core `IFormFile` uploads including size, content type, and file signature verification.

## Problem Solved

Provides reusable, type-safe validators for file uploads with proper error messages, eliminating boilerplate validation code for common file upload scenarios and preventing extension spoofing attacks.

## Key Features

- `FileNotEmpty()` - Validates file has content
- `GreaterThanOrEqualTo(bytes)` - Minimum file size validation
- `LessThanOrEqualTo(bytes)` - Maximum file size validation
- `ContentTypes(list)` - MIME type whitelist validation
- `HaveSignatures(inspector, predicate)` - Magic bytes/file signature validation
- Localized error messages (English, Arabic)

## Installation

```bash
dotnet add package Headless.Api.FluentValidation
```

## Quick Start

```csharp
using FluentValidation;
using Headless.FluentValidation;
using FileSignatures;
using FileSignatures.Formats;

public sealed class UploadRequestValidator : AbstractValidator<UploadRequest>
{
    public UploadRequestValidator(IFileFormatInspector inspector)
    {
        RuleFor(x => x.Avatar)
            .FileNotEmpty()
            .LessThanOrEqualTo(5 * 1024 * 1024) // 5MB
            .ContentTypes(["image/jpeg", "image/png"])
            .HaveSignatures(inspector, format => format is Jpeg or Png);
    }
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.FluentValidation`
- `FileSignatures`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

## None.

# Headless.Api.Logging.Serilog

Serilog integration for ASP.NET Core APIs with custom enrichers for request context.

## Problem Solved

Enriches Serilog log events with HTTP request context (client IP, user agent, user ID, tenant ID, correlation ID) for better observability and debugging in web applications.

## Key Features

- Custom Serilog enricher middleware
- Client info enrichment (IP, user agent)
- Request context enrichment (user, tenant, correlation ID)
- Integration with `Headless.Logging.Serilog` configuration

## Installation

```bash
dotnet add package Headless.Api.Logging.Serilog
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register enrichers
builder.Services.AddSerilogEnrichers();

var app = builder.Build();

// Use enrichers middleware (place early in pipeline)
app.UseSerilogEnrichers();

app.Run();
```

## Configuration

Inherits Serilog configuration from `Headless.Logging.Serilog`. See that package for sink and enricher configuration.

## Dependencies

- `Headless.Api.Abstractions`
- `Headless.Logging.Serilog`
- `Serilog.Enrichers.ClientInfo`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

- Adds middleware to the request pipeline
- Enriches log context per-request

---

# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

## Problem Solved

Provides consistent JSON serialization and validation for Minimal API endpoints matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

## Key Features

- Pre-configured JSON serialization options
- `MinimalApiValidatorFilter` - FluentValidation integration
- API versioning integration
- Endpoint discovery extensions

## Installation

```bash
dotnet add package Headless.Api.MinimalApi
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessFramework().ConfigureMinimalApi();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) => Results.Ok(new { id }))
   .Validate<GetOrderRequest>();

app.Run();
```

## Configuration

No additional configuration required. Uses framework JSON settings automatically.

## Dependencies

- `Headless.Api`
- `Asp.Versioning.Http`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Configures `JsonOptions` for Minimal APIs

---

# Headless.Api.Mvc

Framework integration for ASP.NET Core MVC/Web API with controllers, filters, JSON configuration, and common utilities.

## Problem Solved

Provides consistent MVC configuration, base controllers, and URL canonicalization for traditional controller-based APIs. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`), so MVC actions get the same response shape as Minimal-API endpoints.

## Key Features

- `ApiControllerBase` - Base controller with common utilities
- Environment-based action filters (`BlockInEnvironmentAttribute`, `RequireEnvironmentAttribute`)
- URL canonicalization middleware (`RedirectToCanonicalUrlRule`)
- Pre-configured JSON and MVC options
- API versioning integration with API Explorer

## Installation

```bash
dotnet add package Headless.Api.Mvc
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessFramework().ConfigureMvc();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run();
```

### Controller Example

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

## Configuration

No additional configuration required.

## Dependencies

- `Headless.Api`
- `Asp.Versioning.Mvc`
- `Asp.Versioning.Mvc.ApiExplorer`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Configures `MvcOptions` and `JsonOptions` for controllers
