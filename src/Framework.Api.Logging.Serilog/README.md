# Framework.Api.Logging.Serilog

Serilog integration for ASP.NET Core APIs with custom enrichers for request context.

## Problem Solved

Enriches Serilog log events with HTTP request context (client IP, user agent, user ID, tenant ID, correlation ID) for better observability and debugging in web applications.

## Key Features

- Custom Serilog enricher middleware
- Client info enrichment (IP, user agent)
- Request context enrichment (user, tenant, correlation ID)
- Integration with `Framework.Logging.Serilog` configuration

## Installation

```bash
dotnet add package Framework.Api.Logging.Serilog
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register enrichers
builder.Services.AddHeadlessSerilogEnrichers();

var app = builder.Build();

// Use enrichers middleware (place early in pipeline)
app.UseHeadlessSerilogEnrichers();

app.Run();
```

## Configuration

Inherits Serilog configuration from `Framework.Logging.Serilog`. See that package for sink and enricher configuration.

## Dependencies

- `Framework.Api.Abstractions`
- `Framework.Logging.Serilog`
- `Serilog.Enrichers.ClientInfo`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

- Adds middleware to the request pipeline
- Enriches log context per-request
