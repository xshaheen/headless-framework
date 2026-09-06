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

Representation validation is optional. The default accepts any strong entity tag.

## Dependencies

- `Headless.Api.Core`
- `Asp.Versioning.Http`

## Side Effects

- Configures `JsonOptions` for Minimal APIs
- Returning `ToHttpResult(...)` makes the full ApiResult response set discoverable by OpenAPI without manual
  `.Produces(...)` calls
- When opted in, endpoint filters emit ETags for successful `IHasEntityTag` results and reject missing or invalid `If-Match` preconditions before invoking the handler
