# Framework.OpenApi.Nswag.OData

NSwag operation filter for OData query parameter documentation.

## Problem Solved

Automatically documents OData query parameters ($filter, $orderby, $top, $skip, $select) in OpenAPI specifications for endpoints that support OData queries.

## Key Features

- `ODataOperationFilter` - Adds OData query parameters to OpenAPI docs
- Automatic parameter detection
- Standard OData parameter descriptions

## Installation

```bash
dotnet add package Framework.OpenApi.Nswag.OData
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFrameworkNswagOpenApi(
    setupFrameworkAction: null,
    setupGeneratorActions: settings =>
    {
        settings.OperationProcessors.Add(new ODataOperationFilter());
    }
);
```

## Configuration

No configuration required.

## Dependencies

- `Framework.OpenApi.Nswag`

## Side Effects

None.
