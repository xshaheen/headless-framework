# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

## Problem Solved

Provides consistent JSON serialization and validation for Minimal API endpoints matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api.Core`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

## Key Features

- Pre-configured JSON serialization options
- `MinimalApiValidatorFilter` — FluentValidation integration via `.Validate<T>()` on endpoint builders
- API versioning integration
- Endpoint discovery extensions

## Installation

```bash
dotnet add package Headless.Api.MinimalApi
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureMinimalApi();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) => Results.Ok(new { id }))
   .Validate<GetOrderRequest>();

app.Run();
```

## Configuration

No additional configuration required. Uses framework JSON settings automatically.

## Dependencies

- `Headless.Api.Core`
- `Asp.Versioning.Http`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Configures `JsonOptions` for Minimal APIs