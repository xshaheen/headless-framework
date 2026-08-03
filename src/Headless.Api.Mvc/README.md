# Headless.Api.Mvc

Framework integration for ASP.NET Core MVC/Web API with controllers, filters, JSON configuration, and common utilities.

## Problem Solved

Provides consistent MVC configuration, base controllers, and URL canonicalization for traditional controller-based APIs. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api.Core`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`), so MVC actions get the same response shape as Minimal-API endpoints.

## Key Features

- `ApiControllerBase` - Base controller with common utilities
- Environment-based action filters (`BlockInEnvironmentAttribute`, `RequireEnvironmentAttribute`)
- URL canonicalization middleware (`RedirectToCanonicalUrlRule`, registered via `UseRedirectToCanonicalUrl()`)
- Pre-configured JSON and MVC options
- Direct MVC `ObjectResult` responses carrying Headless-normalized `ProblemDetails` run `ProblemDetailsOptions.CustomizeProblemDetails` once before serialization
- `ApiResult<T>.ToActionResult(...)` / `ApiResult.ToActionResult(...)` — maps expected failures to the same
  ProblemDetails shapes as `HeadlessApiExceptionHandler`
- API versioning integration with API Explorer

## Installation

```bash
dotnet add package Headless.Api.Mvc
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureMvc();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run();
```

### Controller Example

```csharp
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(IOrderService service, IProblemDetailsCreator problems) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetAsync(int id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct).ConfigureAwait(false);
        return result.ToActionResult(this, problems);
    }
}
```

### URL Canonicalization

`RedirectToCanonicalUrlRule` answers non-canonical GET requests with a 301 to a single canonical URL: a trailing slash appended or stripped per `RouteOptions.AppendTrailingSlash`, path and query string lower-cased per `RouteOptions.LowercaseUrls`. `ConfigureHeadlessDefaultApi()` sets those to `false` / `true` respectively.

Register it **after `UseRouting()`**:

```csharp
app.UseRouting();
app.UseRedirectToCanonicalUrl(); // or: UseRedirectToCanonicalUrl(appendTrailingSlash: false, lowercaseUrls: true)
```

Two attributes opt an endpoint out, and both are read from endpoint metadata, which exists only once routing has matched the request:

- `[NoTrailingSlash]` — stops the rule from appending a trailing slash. (The same attribute is a resource filter that 404s trailing-slash requests.)
- `[NoLowercaseQueryString]` — preserves query-string casing for case-sensitive tokens such as OAuth `state`/`code` values or signed URLs.

Requests with no routed endpoint are left untouched. Registered before `UseRouting()` — or for a URL that matches no endpoint — the rule performs no canonicalization at all rather than redirecting past an opt-out it cannot see, and logs a one-time `HEADLESS_CANONICAL_URL_ENDPOINT_UNAVAILABLE` warning naming the ordering requirement.

## Configuration

No additional configuration required.

## Dependencies

- `Headless.Api.Core`
- `Asp.Versioning.Mvc`
- `Asp.Versioning.Mvc.ApiExplorer`

## Side Effects

- Configures `MvcOptions` and `JsonOptions` for controllers
- Adds a result filter that applies ProblemDetails customization to Headless-generated MVC object results
