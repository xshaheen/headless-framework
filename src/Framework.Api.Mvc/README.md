# Framework.Api.Mvc

Framework integration for ASP.NET Core MVC/Web API with controllers, filters, JSON configuration, and common utilities.

## Problem Solved

Provides consistent MVC configuration, base controllers, exception filters, and URL canonicalization for traditional controller-based APIs.

## Key Features

- `ApiControllerBase` - Base controller with common utilities
- `MvcApiExceptionFilter` - Standardized exception handling
- `MvcProblemDetailsNormalizer` - Consistent problem details formatting
- Environment-based action filters (`BlockInEnvironmentAttribute`, `RequireEnvironmentAttribute`)
- URL canonicalization middleware (`RedirectToCanonicalUrlRule`)
- Pre-configured JSON and MVC options
- API versioning integration with API Explorer

## Installation

```bash
dotnet add package Framework.Api.Mvc
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddFrameworkMvcOptions();
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
        var order = await _service.GetAsync(id, ct).AnyContext();
        return order is null ? NotFound() : Ok(order);
    }
}
```

## Configuration

No additional configuration required.

## Dependencies

- `Framework.Api`
- `Asp.Versioning.Mvc`
- `Asp.Versioning.Mvc.ApiExplorer`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Configures `MvcOptions` and `JsonOptions` for controllers
- Registers `MvcProblemDetailsNormalizer` singleton
