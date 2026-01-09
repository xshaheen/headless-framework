# Framework.OpenApi.Nswag

NSwag OpenAPI document generation with framework-specific processors and defaults.

## Problem Solved

Provides pre-configured NSwag OpenAPI generation with FluentValidation schema enhancement, standard response processors, and framework primitive type mappings for consistent API documentation.

## Key Features

- `FluentValidationSchemaProcessor` - Extracts validation rules into OpenAPI schema
- `NullabilityAsRequiredSchemaProcessor` - Marks non-nullable as required
- Operation processors: Unauthorized, Forbidden, ProblemDetails responses
- Bearer and API key security scheme support
- Framework primitive type mappings (Money, Month, UserId, AccountId)
- API versioning support

## Installation

```bash
dotnet add package Framework.OpenApi.Nswag
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessNswagOpenApi(options =>
{
    options.AddBearerSecurity = true;
    options.AddPrimitiveMappings = true;
});

var app = builder.Build();

app.MapFrameworkNswagOpenApi();
// or for versioned APIs:
app.MapFrameworkNswagOpenApiVersions();
```

## Configuration

### Options

```csharp
services.AddHeadlessNswagOpenApi(options =>
{
    options.AddBearerSecurity = true;        // JWT Bearer auth
    options.AddApiKeySecurity = true;        // API Key auth
    options.ApiKeyHeaderName = "X-Api-Key";  // Header name
    options.AddPrimitiveMappings = true;     // Framework types
});
```

## Dependencies

- `Framework.Api.Abstractions`
- `NSwag.AspNetCore`
- `FluentValidation`

## Side Effects

- Adds OpenAPI document middleware
- Adds Swagger UI middleware
