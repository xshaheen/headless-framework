# Headless.Api

Core ASP.NET Core API infrastructure providing service registration, middleware, security, JWT handling, and common API utilities.

## Problem Solved

Consolidates repetitive ASP.NET Core API setup (compression, security headers, problem details, JWT, identity, validation) into a single cohesive registration, ensuring consistent configuration across all API projects.

## Key Features

- One-call service registration via `AddHeadlessApi()`
- Multi-tenancy primitives via `AddHeadlessMultiTenancy()` and `UseTenantResolution()`
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
builder.AddHeadlessApi();
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

`AddHeadlessApi()` requires the `Headless:StringEncryption` and `Headless:StringHash` configuration sections.

If you do not want the default `Headless:*` binding, `AddHeadlessApi()` also exposes explicit overloads for:

- `IConfiguration stringEncryptionConfig, IConfiguration stringHashConfig`
- `Action<StringEncryptionOptions>, Action<StringHashOptions>?`
- `Action<StringEncryptionOptions, IServiceProvider>, Action<StringHashOptions, IServiceProvider>?`

When the hash callback is omitted, `AddHeadlessApi(...)` still binds `Headless:StringHash` by default.

## Multi-Tenancy

`AddHeadlessApi()` registers `CurrentTenant` by default, and `Headless.Orm.EntityFramework` now uses the same default for `AddHeadlessDbContextServices()`. For claim-based HTTP tenant resolution, opt in with:

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
`AddHeadlessApi()` binds both `Headless:StringEncryption` and `Headless:StringHash`, and requires both sections to exist.

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
