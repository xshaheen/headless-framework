# Headless.OpenApi.Nswag

NSwag OpenAPI document generation with framework processors, FluentValidation schema integration, security schemes, and primitive type mappings.

## Problem Solved

Configuring NSwag from scratch requires wiring multiple schema and operation processors, handling nullable generics, reflecting FluentValidation rules into JSON Schema, and adding standard security/error response shapes — all in the correct order. This package does all of that behind a single `AddNswagOpenApi()` call.

## Key Features

- `AddNswagOpenApi(Action<HeadlessNswagOptions>?, Action<AspNetCoreOpenApiDocumentGeneratorSettings>?)` — registers NSwag with all framework processors; accepts optional per-doc generator customisation
- `AddNswagOpenApi(Action<HeadlessNswagOptions>?, Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider>?)` — same with service-provider access in the generator callback
- `MapNswagOpenApi(...)` — maps the OpenAPI JSON endpoint (`/openapi/{documentName}.json`) and Swagger UI (`/swagger`)
- `MapNswagOpenApiVersions(...)` — maps a versioned set of OpenAPI endpoints (one per API version) and a unified Swagger UI
- `AddBuildingBlocksPrimitiveMappings(JsonSchemaGeneratorSettings)` — extension to add `Money`, `Month`, `AccountId`, `UserId` type mappers
- `AddPrimitivesSwaggerMappings(JsonSchemaGeneratorSettings, Assembly[])` — discovers and applies primitive mappings from specific assemblies via `[PrimitiveAssembly]`
- `AddAllPrimitivesSwaggerMappings(JsonSchemaGeneratorSettings)` — discovers and applies primitive mappings from all loaded assemblies marked with `[PrimitiveAssembly]`
- Schema processors: `FluentValidationSchemaProcessor`, `GenericNullabilitySchemaProcessor`, `NullabilityAsRequiredSchemaProcessor`
- Operation processors: `ApiExtraInformationOperationProcessor`, `CamelCaseQueryParameterOperationProcessor`, `UnauthorizedResponseOperationProcessor`, `ForbiddenResponseOperationProcessor`, `ProblemDetailsOperationProcessor`

## Design Notes

**Schema processor ordering is load-bearing.** `GenericNullabilitySchemaProcessor` must run before `NullabilityAsRequiredSchemaProcessor`. The generic nullability processor writes `IsNullableRaw = true` on properties whose generic type argument is annotated `T?`; the required processor then reads that flag to determine which properties are required. Reversing the order causes non-nullable generic type properties to be incorrectly marked required when the instantiation uses a nullable argument (e.g., `DataEnvelope<string?>`).

**User `setupGeneratorActions` runs between the two framework configuration passes** (`_ConfigureGeneratorSettings` then user callback then `_ConfigureHeadlessGeneratorSettings`). Security scheme registration and primitive mappings are applied after the user callback, so custom processors added in `setupGeneratorActions` run before security scope processors but after the core schema/operation processors.

**`MapNswagOpenApi` also mounts Swagger UI.** If you call `MapScalarOpenApi()` from `Headless.OpenApi.Scalar` on the same app, both UIs are served. If you want only Scalar, use NSwag's lower-level `app.UseOpenApi(...)` to expose only the JSON endpoint without the Swagger UI.

## Installation

```bash
dotnet add package Headless.OpenApi.Nswag
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNswagOpenApi(options =>
{
    options.AddBearerSecurity = true;
    options.AddPrimitiveMappings = true;
});

var app = builder.Build();

app.MapNswagOpenApi();
// or for versioned APIs:
app.MapNswagOpenApiVersions();
```

Custom generator settings (e.g., add a custom operation processor):

```csharp
builder.Services.AddNswagOpenApi(
    setupHeadlessAction: options =>
    {
        options.AddBearerSecurity = true;
        options.AddApiKeySecurity = true;
        options.ApiKeyHeaderName = "X-API-Key";
    },
    setupGeneratorActions: (settings, serviceProvider) =>
    {
        settings.Title = "My API";
        settings.Version = "v1";
    }
);
```

## Configuration

`HeadlessNswagOptions` properties (all have defaults — only set what differs):

| Property | Default | Description |
|---|---|---|
| `AddBearerSecurity` | `true` | Registers JWT Bearer security scheme and scope processor |
| `AddApiKeySecurity` | `false` | Registers API Key security scheme |
| `ApiKeyHeaderName` | `"X-API-Key"` | Header name for the API key scheme |
| `AddPrimitiveMappings` | `true` | Maps `Money`, `Month`, `AccountId`, `UserId` to primitive OpenAPI types |
| `ThrowOnSchemaProcessingError` | `false` | Throw on FluentValidation schema errors instead of logging |

## Dependencies

- `Headless.Api.Core` (transitive: `FluentValidation`, `Headless.Api.Abstractions`, `Headless.Core`, and others)
- `Headless.Core`
- `NSwag.AspNetCore`
- `NSwag.Annotations`
- `Asp.Versioning.Mvc.ApiExplorer`

## Side Effects

- Registers NSwag OpenAPI document generator via `services.AddOpenApiDocument(...)`
- `MapNswagOpenApi()` mounts the OpenAPI JSON endpoint at `/openapi/{documentName}.json` and Swagger UI at `/swagger`
- `MapNswagOpenApiVersions()` mounts one OpenAPI JSON endpoint per API version at `/openapi/{groupName}.json` and a single Swagger UI at `/swagger`
