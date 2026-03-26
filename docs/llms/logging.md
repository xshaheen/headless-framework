---
domain: Logging
packages: Logging.Serilog
---

# Logging

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Logging.Serilog](#headlessloggingserilog)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration)
    - [appsettings.json](#appsettingsjson)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)

> Serilog configuration factory with preconfigured sinks, enrichers, and structured logging defaults.

## Quick Orientation

Single package: `Headless.Logging.Serilog`. Provides `SerilogFactory` for bootstrap and production logger setup.

Typical registration:
```csharp
// Bootstrap logger (catches startup errors)
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration().CreateLogger();

// Production logger with hot-reload from IConfiguration
builder.Host.UseSerilog((context, services, config) =>
    config.ConfigureReloadableLoggerConfiguration(
        services,
        context.Configuration,
        context.HostingEnvironment
    )
);
```

Preconfigured sinks: Console (themed), File (rolling — separate files for fatal, error, warning). Enrichers: Environment, Thread, Process, Machine, Span, app version, commit hash.

## Agent Instructions

- Use `SerilogFactory.CreateBootstrapLoggerConfiguration()` early in `Program.cs` before `WebApplication.CreateBuilder()` — this captures startup crashes.
- Use `ConfigureReloadableLoggerConfiguration()` for the production logger — this enables runtime log level changes via `appsettings.json`.
- Configure log levels via `Serilog:MinimumLevel` in `appsettings.json`, not in code.
- Log files are written to the `Logs/` directory by default (fatal, error, warning — each in separate rolling files).
- Do NOT configure Serilog manually when using this package — it will conflict with the preconfigured enrichers and sinks.
- Dependencies include multiple `Serilog.Enrichers.*` and `Serilog.Sinks.*` packages — these are transitive; do not add them separately.

---
# Headless.Logging.Serilog

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
dotnet add package Headless.Logging.Serilog
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

- `Headless.Extensions`
- `Serilog`
- `Serilog.AspNetCore`
- `Serilog.Enrichers.*`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`

## Side Effects

- Writes log files to `Logs/` directory (fatal, error, warning)
- Console output with structured template
