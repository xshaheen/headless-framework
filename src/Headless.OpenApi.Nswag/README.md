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
- Operation processors: `ApiExtraInformationOperationProcessor`, `CamelCaseQueryParameterOperationProcessor`, `UnauthorizedResponseOperationProcessor`, `ForbiddenResponseOperationProcessor`, `ProblemDetailsOperationProcessor`, `IfMatchOperationProcessor`

`IfMatchOperationProcessor` documents a required `If-Match` header and a 428 response for MVC actions or Minimal API endpoints marked with `[RequireIfMatch]` metadata.

## Design Notes

**Schema processor ordering is load-bearing.** `GenericNullabilitySchemaProcessor` must run before `NullabilityAsRequiredSchemaProcessor`. The generic nullability processor writes `IsNullableRaw = true` on properties whose generic type argument is annotated `T?`; the required processor then reads that flag to determine which properties are required. Reversing the order causes non-nullable generic type properties to be incorrectly marked required when the instantiation uses a nullable argument (e.g., `DataEnvelope<string?>`).

**User `setupGeneratorActions` runs between the two framework configuration passes** (`_ConfigureGeneratorSettings` then user callback then `_ConfigureHeadlessGeneratorSettings`). Security scheme registration and primitive mappings are applied after the user callback, so custom processors added in `setupGeneratorActions` run before security scope processors but after the core schema/operation processors.

**`MapNswagOpenApi` also mounts Swagger UI.** If you call `MapScalarOpenApi()` from `Headless.OpenApi.Scalar` on the same app, both UIs are served. If you want only Scalar, use NSwag's lower-level `app.UseOpenApi(...)` to expose only the JSON endpoint without the Swagger UI.

**Exclusive numeric bounds are written in the OpenAPI 3.0 form, never the JSON Schema draft-6 one.** NSwag emits `openapi: 3.0.0`, where `exclusiveMinimum`/`exclusiveMaximum` are *booleans* that modify `minimum`/`maximum`. NJsonSchema exposes both dialects on the same `JsonSchema` object — `ExclusiveMinimum` (draft-6 numeric) alongside `IsExclusiveMinimum` (the boolean 3.0 adopted) — and writing the numeric form leaves the document with no `minimum` at all, so 3.0 tooling drops the bound and strict client generators reject the schema. `ComparisonRule` and `BetweenRule` go through `SetMinimum`/`SetMaximum`, which always write the boolean form and clear the numeric one. Custom rules that set bounds should use those helpers rather than assigning the NJsonSchema properties directly.

**`RuleForEach` constraints land on `items`, not on the array.** FluentValidation reports a collection rule under the collection property's own name, exactly like a non-collection rule, so `RuleContext` carries `IsCollectionRule` and `PropertySchema` resolves to the property's item schema when it is set. Without that redirect a `RuleForEach(x => x.Scores).GreaterThan(0)` writes `minimum` onto the array — where JSON Schema sizes arrays with `minItems`/`maxItems` and `minimum`/`maxLength`/`pattern` have no meaning — and generators emit nonsense such as `z.array(...).gt(0)`. A plain `RuleFor` on a collection property still targets the array itself.

**Problem-details schemas declare every member the response actually carries.** `IProblemDetailsCreator` writes an `error` descriptor on 400/401/403/404/429 responses (the framework's own tenant-required 403 emits `g:tenant_required` there) and a `retryAfter` on 429, so `BadRequestProblemDetails`, `UnauthorizedProblemDetails`, `ForbiddenProblemDetails`, `EntityNotFoundProblemDetails`, and `TooManyRequestsProblemDetails` declare those members — `error` optional, `retryAfter` required. This matters because `SetupNswag` sets `FlattenInheritanceHierarchy = true` and NJsonSchema defaults `AlwaysAllowAdditionalObjectProperties` to `false`: each problem definition is one flat object with `additionalProperties: false`, so an undeclared member makes a real response fail validation against the schema this package published for it. When adding a creator method that writes a new extension member, add the matching property to its model in the same change.

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
