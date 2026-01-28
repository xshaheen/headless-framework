# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

## Problem Solved

Provides consistent JSON serialization, validation, and exception handling for Minimal API endpoints matching the framework's conventions.

## Key Features

- Pre-configured JSON serialization options
- `MinimalApiValidatorFilter` - FluentValidation integration
- `MinimalApiExceptionFilter` - Standardized exception-to-problem-details mapping
- API versioning integration
- Endpoint discovery extensions

## Installation

```bash
dotnet add package Headless.Api.MinimalApi
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessApi().ConfigureHeadlessMinimalApi();

var app = builder.Build();

app.MapGet("/orders/{id}", (int id) => Results.Ok(new { id }))
   .WithValidation<GetOrderRequest>();

app.Run();
```

## Configuration

No additional configuration required. Uses framework JSON settings automatically.

## Dependencies

- `Headless.Api`
- `Asp.Versioning.Http`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Configures `JsonOptions` for Minimal APIs
