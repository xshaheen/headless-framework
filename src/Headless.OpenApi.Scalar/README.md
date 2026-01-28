# Headless.OpenApi.Scalar

Scalar API documentation UI integration for OpenAPI.

## Problem Solved

Provides a modern, beautiful API documentation UI using Scalar as an alternative to Swagger UI, with sensible defaults and framework integration.

## Key Features

- Modern, responsive API documentation UI
- Dark mode support with toggle
- Multiple code generation targets (C#, Go, JavaScript, Node, PowerShell, Shell)
- Multiple HTTP clients (HttpClient, Curl, Axios, Fetch, etc.)
- Customizable layout and sorting

## Installation

```bash
dotnet add package Headless.OpenApi.Scalar
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add OpenAPI generation (NSwag or other)
builder.Services.AddHeadlessNswagOpenApi();

var app = builder.Build();

app.MapHeadlessScalarOpenApi();
```

## Configuration

### Options

```csharp
app.MapFrameworkScalarOpenApi(options =>
{
    options.DarkMode = true;
    options.Layout = ScalarLayout.Modern;
}, endpointPrefix: "/docs");
```

## Dependencies

- `Scalar.AspNetCore`

## Side Effects

- Adds Scalar UI middleware at configured endpoint (default: `/scalar`)
