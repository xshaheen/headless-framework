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
- Opt-in strong ETag responses and `If-Match` request validation

## Installation

```bash
dotnet add package Headless.Api.Mvc
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureMvc();
builder.Services.AddControllers();
builder.Services.AddHeadlessMvcEntityTagConcurrency();

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

### ETag concurrency

`AddHeadlessMvcEntityTagConcurrency()` adds an `ETag` response field when a successful MVC `ObjectResult` implements `IHasEntityTag`. Mark a write action with `[RequireIfMatch]` to require exactly one strong entity tag. The parsed value is available through scoped `IIfMatchContext`.

```csharp
[HttpPut("{id:guid}")]
[RequireIfMatch]
public Task<OrderDto> Update(
    Guid id,
    UpdateOrder request,
    [FromServices] IIfMatchContext ifMatch,
    CancellationToken ct
) => service.Update(id, request, ifMatch.EntityTag!, ct);
```

Missing preconditions return 428 with `g:if_match_required`; malformed, weak, wildcard, or multiple tags return 400 with `g:if_match_invalid`. EF concurrency failures continue to return 409 with `g:concurrency_failure`.

`EntityTag` identifies the HTTP representation rather than the database row. Keep the persistence version provider-native—`uint` mapped to PostgreSQL `xmin`, or `byte[]` mapped to SQL Server `rowversion`—then use `EntityTag.FromUInt32(...)` or `EntityTag.FromBytes(...)` at the response boundary. Implement `GetEntityTag()` on the response DTO; because it is a method, the metadata is not added to the JSON body.

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
- When opted in, adds a result filter that emits ETags for `IHasEntityTag` responses
