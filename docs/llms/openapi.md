---
domain: OpenAPI
packages: OpenApi.Nswag, OpenApi.Nswag.OData, OpenApi.Scalar
---

# OpenAPI

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Headless.OpenApi.Nswag](#headlessopenapinswag)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.OpenApi.Nswag.OData](#headlessopenapinswagodata)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.OpenApi.Scalar](#headlessopenapiscalar)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> OpenAPI document generation via NSwag (with FluentValidation schema integration and framework processors) plus OData query-parameter documentation and Scalar UI rendering.

## Quick Orientation

These three packages are **complementary**, not competing. Each has a distinct role:

| Package | Role | When you need it |
|---|---|---|
| `Headless.OpenApi.Nswag` | **Generates** the OpenAPI JSON document — wires processors, security schemes, and primitive type mappings onto NSwag | Every API that wants an OpenAPI spec |
| `Headless.OpenApi.Nswag.OData` | **Extends** the generated spec with OData query parameters (`$filter`, `$orderby`, `$top`, `$skip`, `$select`, `$expand`, `$search`) | Only when endpoints accept `ODataQueryOptions` or `[EnableQuery]` |
| `Headless.OpenApi.Scalar` | **Renders** the generated spec as an interactive docs UI using Scalar | When you want a docs UI (replaces Swagger UI bundled in NSwag) |

Typical full setup uses Nswag + Scalar together:

```csharp
// Services
builder.Services.AddNswagOpenApi(options =>
{
    options.AddBearerSecurity = true;
    options.AddPrimitiveMappings = true;
});

// Middleware — Nswag for the spec, Scalar for the UI
app.MapNswagOpenApi();
app.MapScalarOpenApi();
```

For versioned APIs, replace `app.MapNswagOpenApi()` with `app.MapNswagOpenApiVersions()`.

## Agent Instructions

- Call `AddNswagOpenApi()` to register OpenAPI document generation. Do NOT call NSwag's `AddOpenApiDocument()` directly — the framework wires all processors in the correct order.
- **Processor registration order matters**: `GenericNullabilitySchemaProcessor` is registered before `NullabilityAsRequiredSchemaProcessor` intentionally. If you inject processors via `setupGeneratorActions`, add them after the framework processors (they run last) unless the intent is to override defaults.
- `AddNswagOpenApi` takes an optional `setupGeneratorActions` callback that receives the NSwag `AspNetCoreOpenApiDocumentGeneratorSettings`. The framework processors are added first (in `_ConfigureGeneratorSettings`), then your callback runs, then security/primitive-mapping finalisation runs. Use this ordering to avoid conflicts.
- For OData endpoints: add `ODataOperationFilter` inside `setupGeneratorActions`, not as a standalone service. Detection is automatic — any endpoint with an `ODataQueryOptions` parameter or `[EnableQuery]` attribute will have the seven OData parameters injected.
- Do NOT call `app.UseSwaggerUi()` separately — `MapNswagOpenApi()` and `MapNswagOpenApiVersions()` both call it internally. If you also call `MapScalarOpenApi()`, Swagger UI and Scalar UI will both be served; pick one or the other unless you explicitly want both.
- `HeadlessNswagOptions.AddBearerSecurity` defaults to `true`. If your API uses no auth, set it to `false` to remove the security scheme from the spec.
- `HeadlessNswagOptions.AddPrimitiveMappings` defaults to `true`. It maps `Money`, `Month`, `AccountId`, and `UserId` from `Headless.Primitives`. Disable only if your API does not expose those types.
- `HeadlessNswagOptions.ThrowOnSchemaProcessingError` defaults to `false` — schema errors are logged and skipped. Set to `true` in development to surface FluentValidation configuration mistakes early.
- `MapScalarOpenApi` sets `OpenApiRoutePattern` to `/openapi/{documentName}.json` which matches the path that `MapNswagOpenApi`/`MapNswagOpenApiVersions` expose. If you change the NSwag document path, update Scalar's `OpenApiRoutePattern` accordingly via the `setupAction` callback.
- The `ApiKeyHeaderName` default is `"X-API-Key"` (note: the old docs showed `"X-Api-Key"` — the actual default is `"X-API-Key"`).

## Core Concepts

**Two roles, three packages.** NSwag fills the *generation* role: it inspects ASP.NET Core routing metadata and produces an OpenAPI JSON document. The framework adds *processors* — NSwag extension points — to enrich that document automatically. Scalar fills the *rendering* role: it consumes the generated JSON document and renders a browser-based interactive UI. The OData package adds to generation by injecting extra parameters on eligible endpoints.

**Schema processors vs operation processors.** NSwag distinguishes two extension points:

- *Schema processors* (`ISchemaProcessor`) run per-type and modify JSON schema definitions. The framework registers three: `FluentValidationSchemaProcessor` (pulls validation rules from DI-registered `IValidator<T>` instances), `GenericNullabilitySchemaProcessor` (fixes `T?` nullability on generic types — a known NSwag limitation), and `NullabilityAsRequiredSchemaProcessor` (promotes non-nullable properties to `required`).
- *Operation processors* (`IOperationProcessor`) run per-endpoint and modify individual operation objects. The framework registers four: `ApiExtraInformationOperationProcessor` (mirrors deprecation flags, response content types, and parameter defaults from API Explorer metadata), `CamelCaseQueryParameterOperationProcessor` (normalises query parameter casing; `$`-prefixed OData parameters are exempt), `UnauthorizedResponseOperationProcessor` (injects a 401 response on endpoints with `[Authorize]` or `DenyAnonymousAuthorizationRequirement`), `ForbiddenResponseOperationProcessor` (injects a 403 response when an endpoint has a policy, role, or claim requirement).

**ProblemDetails enrichment.** `ProblemDetailsOperationProcessor` pre-registers concrete `ProblemDetails` schemas (`BadRequestProblemDetails`, `UnprocessableEntityProblemDetails`, `ConflictProblemDetails`, etc.) in the document definitions and sets typed schema references plus worked examples on the matching response entries (400, 401, 403, 404, 409, 422, 429). This makes error responses self-documenting without any per-endpoint annotation.

**FluentValidation schema integration.** `FluentValidationSchemaProcessor` resolves `IValidator<T>` from DI at document-generation time (using a scoped service provider) and applies validation rules (min/max length, range, required, pattern, etc.) as JSON Schema constraints. If a type has no registered validator, it is skipped silently (or throws if `ThrowOnSchemaProcessingError = true`). Include-rule chains are traversed recursively.

---
## Headless.OpenApi.Nswag

NSwag OpenAPI document generation with framework processors, FluentValidation schema integration, security schemes, and primitive type mappings.

### Problem Solved

Configuring NSwag from scratch requires wiring multiple schema and operation processors, handling nullable generics, reflecting FluentValidation rules into JSON Schema, and adding standard security/error response shapes — all in the correct order. This package does all of that behind a single `AddNswagOpenApi()` call.

### Key Features

- `AddNswagOpenApi(Action<HeadlessNswagOptions>?, Action<AspNetCoreOpenApiDocumentGeneratorSettings>?)` — registers NSwag with all framework processors; accepts optional per-doc generator customisation
- `AddNswagOpenApi(Action<HeadlessNswagOptions>?, Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider>?)` — same with service-provider access in the generator callback
- `MapNswagOpenApi(...)` — maps the OpenAPI JSON endpoint (`/openapi/{documentName}.json`) and Swagger UI (`/swagger`)
- `MapNswagOpenApiVersions(...)` — maps a versioned set of OpenAPI endpoints (one per API version) and a unified Swagger UI
- `AddBuildingBlocksPrimitiveMappings(JsonSchemaGeneratorSettings)` — extension on `JsonSchemaGeneratorSettings` to add `Money`, `Month`, `AccountId`, `UserId` type mappers
- `AddPrimitivesSwaggerMappings(JsonSchemaGeneratorSettings, Assembly[])` — discovers and applies primitive mappings from specific assemblies via `[PrimitiveAssembly]`
- `AddAllPrimitivesSwaggerMappings(JsonSchemaGeneratorSettings)` — discovers and applies primitive mappings from all loaded assemblies marked with `[PrimitiveAssembly]`
- Schema processors: `FluentValidationSchemaProcessor`, `GenericNullabilitySchemaProcessor`, `NullabilityAsRequiredSchemaProcessor`
- Operation processors: `ApiExtraInformationOperationProcessor`, `CamelCaseQueryParameterOperationProcessor`, `UnauthorizedResponseOperationProcessor`, `ForbiddenResponseOperationProcessor`, `ProblemDetailsOperationProcessor`

### Design Notes

**Schema processor ordering is load-bearing.** `GenericNullabilitySchemaProcessor` must run before `NullabilityAsRequiredSchemaProcessor`. The generic nullability processor writes `IsNullableRaw = true` on properties whose generic type argument is annotated `T?`; the required processor then reads that flag to determine which properties are required. Reversing the order causes non-nullable generic type properties to be incorrectly marked required when the instantiation uses a nullable argument (e.g., `DataEnvelope<string?>`).

**User `setupGeneratorActions` runs between the two framework configuration passes** (see `_ConfigureGeneratorSettings` then user callback then `_ConfigureHeadlessGeneratorSettings`). Security scheme registration and primitive mappings are applied after the user callback, so custom processors added in `setupGeneratorActions` run before security scope processors but after the core schema/operation processors.

**`MapNswagOpenApi` also mounts Swagger UI.** If you call `MapScalarOpenApi()` from `Headless.OpenApi.Scalar` on the same app, both UIs are served. This is intentional (you may want Swagger UI for internal tooling and Scalar for external docs), but if you want only Scalar, you can use NSwag's lower-level `app.UseOpenApi(...)` to expose only the JSON endpoint without the Swagger UI.

### Installation

```bash
dotnet add package Headless.OpenApi.Nswag
```

### Quick Start

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

### Configuration

`HeadlessNswagOptions` properties (all have defaults — only set what differs):

| Property | Default | Description |
|---|---|---|
| `AddBearerSecurity` | `true` | Registers JWT Bearer security scheme and scope processor |
| `AddApiKeySecurity` | `false` | Registers API Key security scheme |
| `ApiKeyHeaderName` | `"X-API-Key"` | Header name for the API key scheme |
| `AddPrimitiveMappings` | `true` | Maps `Money`, `Month`, `AccountId`, `UserId` to primitive OpenAPI types |
| `ThrowOnSchemaProcessingError` | `false` | Throw on FluentValidation schema errors instead of logging |

### Dependencies

- `Headless.Api.Core` (transitive: `FluentValidation`, `Headless.Api.Abstractions`, `Headless.Core`, and others)
- `Headless.Core`
- `NSwag.AspNetCore`
- `NSwag.Annotations`
- `Asp.Versioning.Mvc.ApiExplorer`

### Side Effects

- Registers NSwag OpenAPI document generator via `services.AddOpenApiDocument(...)`
- `MapNswagOpenApi()` mounts the OpenAPI JSON endpoint at `/openapi/{documentName}.json` and Swagger UI at `/swagger`
- `MapNswagOpenApiVersions()` mounts one OpenAPI JSON endpoint per API version at `/openapi/{groupName}.json` and a single Swagger UI at `/swagger`

---
## Headless.OpenApi.Nswag.OData

NSwag operation filter that injects OData query parameters into the OpenAPI spec for endpoints that support OData queries.

### Problem Solved

When ASP.NET Core OData endpoints accept `ODataQueryOptions` or carry `[EnableQuery]`, NSwag does not automatically document the OData query string parameters. This package detects those endpoints and injects the seven standard OData parameters into their OpenAPI operation objects.

### Key Features

- `ODataOperationFilter : IOperationProcessor` — detects endpoints via `ODataQueryOptions` parameter type or `[EnableQuery]` attribute and injects seven OData parameters: `$select`, `$expand`, `$filter`, `$search`, `$top`, `$skip`, `$orderby`
- The raw `ODataQueryOptions` parameter is removed from the operation so it does not appear as an undocumented parameter alongside the injected ones
- Detection works on both the method and the declaring controller type for `[EnableQuery]`

### Installation

```bash
dotnet add package Headless.OpenApi.Nswag.OData
```

### Quick Start

```csharp
builder.Services.AddNswagOpenApi(
    setupHeadlessAction: null,
    setupGeneratorActions: settings =>
    {
        settings.OperationProcessors.Add(new ODataOperationFilter());
    }
);
```

With access to the service provider:

```csharp
builder.Services.AddNswagOpenApi(
    setupHeadlessAction: options => { options.AddBearerSecurity = true; },
    setupGeneratorActions: (settings, serviceProvider) =>
    {
        settings.OperationProcessors.Add(new ODataOperationFilter());
    }
);
```

### Configuration

None.

### Dependencies

- `Headless.OpenApi.Nswag`
- `Microsoft.AspNetCore.OData`

### Side Effects

None. `ODataOperationFilter` is instantiated and registered manually inside `setupGeneratorActions`; no DI registrations are made.

---
## Headless.OpenApi.Scalar

Scalar API documentation UI integration — renders the OpenAPI document generated by `Headless.OpenApi.Nswag` as an interactive browser-based UI.

### Problem Solved

NSwag's bundled Swagger UI is functional but dated. This package mounts Scalar as a modern alternative at a configurable endpoint, pre-configured with sensible defaults (dark mode, alphabetical tag sorting, method-based operation sorting, and a curated set of code generation targets and HTTP clients).

### Key Features

- `MapScalarOpenApi(Action<ScalarOptions>?, string endpointPrefix)` — mounts Scalar at the given prefix (default `/scalar`), connected to the NSwag JSON endpoint pattern `/openapi/{documentName}.json`
- Dark mode enabled by default with toggle visible
- Layout: `ScalarLayout.Modern`
- Tag sort: alphabetical (`TagSorter.Alpha`)
- Operation sort: by HTTP method (`OperationSorter.Method`)
- Code generation targets: C#, Go, JavaScript, Node, PowerShell, Shell
- HTTP client targets: `HttpClient`, `Curl`, `Axios`, `Fetch`, `XHR`, `WebRequest`, `Wget`, `HTTPie`
- All Scalar options are overridable via the `setupAction` callback

### Design Notes

**Route pattern coupling.** `MapScalarOpenApi` hard-codes `options.OpenApiRoutePattern = "/openapi/{documentName}.json"` before passing control to the user callback. This matches what `MapNswagOpenApi` and `MapNswagOpenApiVersions` expose. If you change the NSwag JSON path (via `documentSettings` on those methods), you must override `OpenApiRoutePattern` in the Scalar `setupAction` or the UI will point at a 404.

**No `AddNswagOpenApi` dependency at runtime.** The `Headless.OpenApi.Scalar` package does not reference `Headless.OpenApi.Nswag`. It calls `Scalar.AspNetCore`'s `MapScalarApiReference` which works with any OpenAPI JSON source. You can pair it with a different generator as long as the JSON endpoint path matches.

### Installation

```bash
dotnet add package Headless.OpenApi.Scalar
```

### Quick Start

```csharp
var app = builder.Build();

// Serve the OpenAPI JSON (from Headless.OpenApi.Nswag)
app.MapNswagOpenApi();

// Serve the Scalar UI
app.MapScalarOpenApi();
```

With custom options:

```csharp
app.MapScalarOpenApi(
    setupAction: options =>
    {
        options.DarkMode = false;
        options.Layout = ScalarLayout.Classic;
    },
    endpointPrefix: "/docs"
);
```

### Configuration

`MapScalarOpenApi` accepts an optional `Action<ScalarOptions>` and an `endpointPrefix` string. The framework pre-sets the following before invoking the callback (override any of them inside `setupAction`):

| Setting | Framework default |
|---|---|
| `OpenApiRoutePattern` | `/openapi/{documentName}.json` |
| `DarkMode` | `true` |
| `HideDarkModeToggle` | `false` |
| `Layout` | `ScalarLayout.Modern` |
| `TagSorter` | `TagSorter.Alpha` |
| `OperationSorter` | `OperationSorter.Method` |
| `EnabledTargets` | C#, Go, JavaScript, Node, PowerShell, Shell |
| `EnabledClients` | HttpClient, Curl, Axios, Fetch, XHR, WebRequest, Wget, HTTPie |

The `endpointPrefix` parameter (default `"/scalar"`) controls where Scalar is mounted.

### Dependencies

- `Scalar.AspNetCore`

### Side Effects

- Mounts Scalar UI at `{endpointPrefix}` (default `/scalar`) via `MapScalarApiReference`
