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
builder.AddHeadlessApi(encryption =>
{
    encryption.DefaultPassPhrase = "YourPassPhrase123";
    encryption.InitVectorBytes = "YourInitVector16"u8.ToArray();
    encryption.DefaultSalt = "YourSalt"u8.ToArray();
});
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

### String Hashing (required)

```json
{
  "StringHash": {
    "Secret": "your-secret-key-min-32-chars-long"
  }
}
```

### String Encryption (required)

```json
{
  "StringEncryption": {
    "Key": "your-encryption-key"
  }
}
```

## Dependencies

- `Headless.Api.Abstractions`
- `Headless.Core`
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
