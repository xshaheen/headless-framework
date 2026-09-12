# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

## Problem Solved

Provides consistent JSON serialization and validation for Minimal API endpoints matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api.Core`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

## Key Features

- Pre-configured JSON serialization options
- `MinimalApiValidatorFilter` — FluentValidation integration via `.Validate<T>()` on endpoint builders
- Entity-tag concurrency via `.WithEntityTag()` and `.RequireIfMatch()` endpoint filters
- `ApiResult<T>.ToHttpResult(...)` / `ApiResult.ToHttpResult(...)` — maps expected failures to the same
  ProblemDetails shapes as the exception handler and publishes 200/204 plus 401/403/404/409/422 OpenAPI metadata
- API versioning integration
- Endpoint discovery extensions

## Installation

```bash
dotnet add package Headless.Api.MinimalApi
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureMinimalApi();
builder.Services.AddHeadlessMinimalApiEntityTagConcurrency();

var app = builder.Build();

app.MapGet(
    "/orders/{id:guid}",
    async (Guid id, IOrderService service, IProblemDetailsCreator problems, CancellationToken ct) =>
        (await service.GetAsync(id, ct)).ToHttpResult(problems)
).WithEntityTag();

app.MapPut(
    "/orders/{id:guid}",
    async (Guid id, UpdateOrder request, IIfMatchContext ifMatch, IOrderService service, CancellationToken ct) =>
        await service.UpdateAsync(id, request, ifMatch.EntityTag!, ct)
).RequireIfMatch();

app.Run();
```

Use `AddHeadlessMinimalApiEntityTagConcurrency(options => ...)` to validate the parsed strong tag against an API-wide representation format. For PostgreSQL `xmin`, set `options.IfMatchValidator = static tag => tag.TryGetUInt32(out _)`.

## Configuration

### API surfaces

After `AddHeadlessApiSurfaces(...)`, `app.MapApiSurface("portal")` returns a native `RouteGroupBuilder` with the configured prefix and named authorization policy. The optional callback and returned builder support ordinary ASP.NET Core endpoint conventions.

```csharp
var portal = app.MapApiSurface("portal");
portal.MapGet("profile", () => "profile");
portal.MapGet("bootstrap", () => "bootstrap").AllowMissingTenant();
portal.MapGet("status", () => "ready").AllowAnonymous();
```

Endpoint tenancy choices override surface defaults. Native authorization policies remain additive, and `AllowAnonymous` bypasses them. `RequireTenant` metadata needs the policy and services described in `Headless.Api.Core`. Unknown surface names throw during mapping; nested groups with conflicting surface identities throw when endpoints are built. API Explorer version groups remain independent.

Representation validation is optional. The default accepts any strong entity tag.

## Dependencies

- `Headless.Api.Core`
- `Asp.Versioning.Http`

## Side Effects

- `MapApiSurface` adds group route, authorization, and tenancy conventions using the shared surface registry.
- Configures `JsonOptions` for Minimal APIs
- Returning `ToHttpResult(...)` makes the full ApiResult response set discoverable by OpenAPI without manual
  `.Produces(...)` calls
- When opted in, endpoint filters emit ETags for successful `IHasEntityTag` results and reject missing or invalid `If-Match` preconditions before invoking the handler
