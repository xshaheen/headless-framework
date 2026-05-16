# Headless.Api.ServiceDefaults

The one-line bootstrap for Headless APIs. Combines the [`Headless.Api.Core`](../Headless.Api.Core/README.md) primitives with Aspire-style host conventions: OpenTelemetry, OpenAPI document mapping, service discovery, HttpClient resilience, and startup validation.

If you want the happy-path API bootstrap, install this package. It transitively pulls in `Headless.Api.Core`.

## Installation

```bash
dotnet add package Headless.Api.ServiceDefaults
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

ApiSetup.ConfigureGlobalSettings();
builder.AddHeadless();

var app = builder.Build();

app.UseHeadless();
app.MapHeadlessEndpoints();
app.Run();
```

That's the whole minimal setup. `AddHeadless()` registers all core primitives plus the Aspire-style conventions. `UseHeadless()` applies the default middleware order. `MapHeadlessEndpoints()` maps `/health`, `/alive`, OpenAPI JSON, and static web assets.

## Configuration

```csharp
builder.AddHeadless(configureServices: options =>
{
    options.Validation.ValidateServiceProviderOnStartup = true;
    options.Validation.RequireUseHeadless = true;
    options.Validation.RequireMapHeadlessEndpoints = true;

    options.OpenTelemetry.Enabled = true;
    options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured = true;
    options.OpenTelemetry.ConfigureMetrics = metrics => metrics.AddMeter("MyApp.*");

    options.OpenApi.Enabled = true;
    options.OpenApi.RoutePattern = "/openapi/{documentName}.json";
    options.OpenApi.CacheDocument = true;

    options.HttpClient.UseServiceDiscovery = true;
    options.HttpClient.UseStandardResilienceHandler = true;
    options.HttpClient.AddApplicationUserAgent = true;

    options.StaticAssets.Enabled = true;
});
```

`AddHeadless()` also accepts overloads for explicit string-encryption and string-hash configuration:

- `AddHeadless(IConfiguration stringEncryptionConfig, IConfiguration stringHashConfig, ...)`
- `AddHeadless(Action<StringEncryptionOptions>, Action<StringHashOptions>?, ...)`
- `AddHeadless(Action<StringEncryptionOptions, IServiceProvider>, Action<StringHashOptions, IServiceProvider>?, ...)`

Without those overloads, `AddHeadless()` binds `Headless:StringEncryption` and `Headless:StringHash` sections from `IConfiguration` by default.

## What's Registered

`AddHeadless()` performs the following:

- Service-provider validation on startup (`ValidateOnBuild`, `ValidateScopes`)
- All core primitives from [`Headless.Api.Core`](../Headless.Api.Core/README.md) (problem details, response compression, JWT, identity, status codes rewriter, default API conventions). Antiforgery service registration is opt-in via `options.Antiforgery.Enabled` (cookie-auth apps only).
- MVC and Minimal API JSON serializer defaults
- ASP.NET Core source-generated input validation (`services.AddValidation()`)
- OpenTelemetry logging, metrics, and tracing (when `OpenTelemetry.Enabled`)
- OpenAPI service registration (when `OpenApi.Enabled`)
- Service discovery (when `HttpClient.UseServiceDiscovery`)
- HttpClient defaults — standard resilience handler, service discovery, application User-Agent
- Startup filter that validates `UseHeadless()` and `MapHeadlessEndpoints()` were called

## Pipeline & Endpoints

`UseHeadless()` applies the default middleware order:

- `UseForwardedHeaders()`
- `UseResponseCompression()`
- `UseStatusCodePages()` + `UseStatusCodesRewriter()` (rewrites bare 401/403/404 into ProblemDetails)
- `UseExceptionHandler()`
- `UseHttpsRedirection()`
- `UseHsts()` outside Development
- no-cache response header when the response did not set `Cache-Control`

Antiforgery is consumer-owned and **opt-in**. By default `AddHeadless()` does *not* register the antiforgery service and `UseHeadless()` does not wire the middleware — CSRF protection is meaningful only for cookie-based authentication, and most "headless" APIs use bearer tokens / API keys where there is no CSRF surface. Cookie-auth consumers explicitly opt in and wire the middleware after `UseAuthentication()`/`UseAuthorization()`:

```csharp
builder.AddHeadless(configureServices: options =>
{
    options.Antiforgery.Enabled = true;
});

// ...

app.UseHeadless();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
```

`MapHeadlessEndpoints()` maps:

- `/health` — aggregate health endpoint (JSON body with `status` and per-check `results`)
- `/alive` — liveness endpoint (checks tagged `live`)
- OpenAPI JSON document at the configured route pattern (when `OpenApi.Enabled`)
- Static web assets when their manifest exists (when `StaticAssets.Enabled`)

All operational endpoints are named, excluded from OpenAPI descriptions, and allow anonymous requests by default. Both `UseHeadless` and `MapHeadlessEndpoints` are idempotent.

## Multi-Tenancy

`AddHeadless()` does not enable HTTP tenant resolution by default. Add tenancy explicitly:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()));

var app = builder.Build();
app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy();   // between auth and authz
app.UseAuthorization();
app.MapHeadlessEndpoints();
// Cookie-auth apps that opted into `options.Antiforgery.Enabled` should also
// call `app.UseAntiforgery()` here, after authorization. See the Antiforgery
// section above. Bearer-token APIs leave it out.
```

## Startup Validation

By default, the runtime validates that `UseHeadless()` and `MapHeadlessEndpoints()` were called before the host starts. This catches forgotten middleware/endpoint wiring in deployment. Disable per environment if you build a custom pipeline:

```csharp
builder.AddHeadless(configureServices: options =>
{
    options.Validation.RequireUseHeadless = false;
    options.Validation.RequireMapHeadlessEndpoints = false;
});
```

## Configuration Sections

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

Both sections are required.

## Dependencies

- `Headless.Api.Core` (transitive: brings all core primitives)
- `Microsoft.AspNetCore.OpenApi`
- `Microsoft.Extensions.Http.Resilience`
- `Microsoft.Extensions.ServiceDiscovery`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Instrumentation.Runtime`
