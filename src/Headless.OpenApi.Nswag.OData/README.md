# Headless.OpenApi.Nswag.OData

NSwag operation filter that injects OData query parameters into the OpenAPI spec for endpoints that support OData queries.

## Problem Solved

When ASP.NET Core OData endpoints accept `ODataQueryOptions` or carry `[EnableQuery]`, NSwag does not automatically document the OData query string parameters. This package detects those endpoints and injects the seven standard OData parameters into their OpenAPI operation objects.

## Key Features

- `ODataOperationFilter : IOperationProcessor` — detects endpoints via `ODataQueryOptions` parameter type or `[EnableQuery]` attribute and injects seven OData parameters: `$select`, `$expand`, `$filter`, `$search`, `$top`, `$skip`, `$orderby`
- The raw `ODataQueryOptions` parameter is removed from the operation so it does not appear as an undocumented parameter alongside the injected ones
- Detection works on both the method and the declaring controller type for `[EnableQuery]`

## Installation

```bash
dotnet add package Headless.OpenApi.Nswag.OData
```

## Quick Start

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
    setupHeadlessAction: options =>
    {
        options.AddBearerSecurity = true;
    },
    setupGeneratorActions: (settings, serviceProvider) =>
    {
        settings.OperationProcessors.Add(new ODataOperationFilter());
    }
);
```

## Configuration

None.

## Dependencies

- `Headless.OpenApi.Nswag`
- `Microsoft.AspNetCore.OData`

## Side Effects

None. `ODataOperationFilter` is instantiated and registered manually inside `setupGeneratorActions`; no DI registrations are made.
