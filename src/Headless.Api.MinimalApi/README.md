# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

## Problem Solved

Provides consistent JSON serialization and validation for Minimal API endpoints matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

## Key Features

- Pre-configured JSON serialization options
- `MinimalApiValidatorFilter` - FluentValidation integration
- API versioning integration
- Endpoint discovery extensions

## Installation

```bash
dotnet add package Headless.Api.MinimalApi
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessFramework().ConfigureMinimalApi();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) => Results.Ok(new { id }))
   .Validate<GetOrderRequest>();

app.Run();
```

## Configuration

No additional configuration beyond what `AddHeadlessFramework()` requires. Configure `Headless:StringEncryption` and `Headless:StringHash`.

## Dependencies

- `Headless.Api`
- `Asp.Versioning.Http`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Configures `JsonOptions` for Minimal APIs