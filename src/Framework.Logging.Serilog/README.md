# Framework.Logging.Serilog

Serilog configuration factory with sensible defaults and structured logging setup.

## Problem Solved

Provides pre-configured Serilog logger configurations for bootstrap and production logging, with standard enrichers, output templates, and file/console sinks configured for typical application needs.

## Key Features

- Bootstrap logger configuration for startup errors
- Reloadable logger configuration from `IConfiguration`
- Standard enrichers: Environment, Thread, Process, Machine, Span
- File sinks with rolling (fatal, error, warning logs)
- Console sink with themed output
- Application version and commit hash enrichment

## Installation

```bash
dotnet add package Framework.Logging.Serilog
```

## Quick Start

```csharp
// Program.cs - Bootstrap logger
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Production logger with configuration reload
builder.Host.UseSerilog((context, services, config) =>
    config.ConfigureReloadableLoggerConfiguration(
        services,
        context.Configuration,
        context.HostingEnvironment
    )
);
```

## Configuration

### appsettings.json

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

## Dependencies

- `Framework.Base`
- `Serilog`
- `Serilog.AspNetCore`
- `Serilog.Enrichers.*`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`

## Side Effects

- Writes log files to `Logs/` directory (fatal, error, warning)
- Console output with structured template
