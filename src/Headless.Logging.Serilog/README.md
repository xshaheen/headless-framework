# Headless.Logging.Serilog

## Problem Solved

Setting up Serilog correctly for ASP.NET Core requires wiring a bootstrap logger (to catch startup failures), then replacing it with a reloadable production logger that reads from `IConfiguration`, applies enrichers, routes levels to separate files, and adjusts the console theme per environment. `SerilogFactory` encodes this two-phase setup as tested, opinionated extension methods so applications get correct defaults without repeating the configuration boilerplate.

## Key Features

- `SerilogFactory.CreateBootstrapLoggerConfiguration()` / `ConfigureBootstrapLoggerConfiguration()` — early-startup logger covering console, debug (DEBUG builds), and an async file sink for Fatal/Error/Warning events.
- `SerilogFactory.CreateReloadableLoggerConfiguration()` / `ConfigureReloadableLoggerConfiguration()` — production logger reading from `Serilog:` config section, enriched with application metadata, wiring three level-specific async file sinks and an environment-aware console sink.
- Standard enrichers applied automatically: `LogContext`, `Span` (OpenTelemetry), `EnvironmentName`, `ThreadId`, `ProcessId`, `ProcessName`, `MachineName`, `Application`, `Version`, `CommitHash`.
- `IPAddress` destructuring policy converts IP addresses to strings.
- `SerilogOptions` to tune file output: directory, rolling interval, retention limit, buffering, flush interval.
- `SerilogFactory.OutputTemplate` — shared `public const string` matching `[{Timestamp:HH:mm:ss.fff zzz} {Level:u3}] {RequestPath} {SourceContext} {Message:lj}{NewLine}{Exception}`.
- Debug sink active only in `#DEBUG` builds — no production overhead.
- Console theme switches automatically: `AnsiConsoleTheme.Code` (Development) vs `ConsoleTheme.None` (other environments).

## Design Notes

**Two-phase logger approach**: ASP.NET Core's host-building phase can throw before `IConfiguration` or DI is ready. The bootstrap logger captures those crashes. The reloadable logger replaces it once the host is built and can call `ReadFrom.Services(services)` to pick up any DI-registered sinks or enrichers.

**Level-segregated file sinks**: Each file (`fatal-.log`, `error-.log`, `warning-.log`) receives exactly one level — it is not a cumulative log. This makes triage faster and limits file sizes per severity. The async wrapper (`Serilog.Sinks.Async`) decouples I/O from the logging hot path.

**`WriteToFiles` defaults to `true`** but the option exists to support container deployments where structured logs go to stdout only. When `false`, the three level-specific sinks are skipped entirely; the console sink and Debug sink remain.

**Environment-aware console theme**: Plain text (`ConsoleTheme.None`) in non-Development environments avoids ANSI escape codes leaking into log aggregators. `AnsiConsoleTheme.Code` improves readability locally.

**`SerilogFactory.OutputTemplate`** is `public const` so custom downstream sinks or formatters can reference the canonical template without duplicating the string.

## Installation

```bash
dotnet add package Headless.Logging.Serilog
```

## Quick Start

```csharp
// Program.cs

// 1. Bootstrap logger — before WebApplication.CreateBuilder
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 2. Replace with reloadable production logger
    builder.Host.UseSerilog(
        (ctx, services, cfg) =>
            cfg.ConfigureReloadableLoggerConfiguration(services, ctx.Configuration, ctx.HostingEnvironment)
    );

    var app = builder.Build();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
```

To suppress file output (container/stdout-only):

```csharp
var serilogOptions = new SerilogOptions { WriteToFiles = false };

Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration(serilogOptions).CreateLogger();

builder.Host.UseSerilog(
    (ctx, services, cfg) =>
        cfg.ConfigureReloadableLoggerConfiguration(services, ctx.Configuration, ctx.HostingEnvironment, serilogOptions)
);
```

## Configuration

### appsettings.json

Log level changes here take effect at runtime without restart (hot-reload via `ReadFrom.Configuration`):

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "System": "Warning"
      }
    }
  }
}
```

### Options

`SerilogOptions` controls file-sink behaviour. Pass an instance to either factory method.

| Property | Type | Default | Notes |
|---|---|---|---|
| `WriteToFiles` | `bool` | `true` | Set `false` to skip all file sinks (stdout-only environments). |
| `LogDirectory` | `string` | `"Logs"` | Relative or absolute path for log files. |
| `Buffered` | `bool` | `true` | Enable buffered writes for lower I/O overhead. |
| `FlushToDiskInterval` | `TimeSpan` | `1 second` | How often the async sink flushes buffered events to disk. |
| `RollingInterval` | `RollingInterval` | `Day` | Serilog rolling interval (Hour, Day, Month, …). |
| `RetainedFileCountLimit` | `int` | `5` | How many rolled files to keep per log category. |
| `MaxHeaderLength` | `int` | `512` | Reserved; not currently used by the file sinks. |

## Dependencies

- `Headless.Extensions` (provides `AssemblyInformation` for version/commit enrichment)
- `Serilog`
- `Serilog.Enrichers.Environment`
- `Serilog.Enrichers.Process`
- `Serilog.Enrichers.Span`
- `Serilog.Enrichers.Thread`
- `Serilog.Extensions.Hosting`
- `Serilog.Settings.Configuration`
- `Serilog.Sinks.Async`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.Debug`
- `Serilog.Sinks.File`

## Side Effects

- Creates `Logs/` directory and writes rolling log files (`fatal-.log`, `error-.log`, `warning-.log`) when `WriteToFiles = true` (default).
- Writes `Logs/bootstrap-.log` for Fatal/Error/Warning events during the bootstrap phase.
- Writes to the Debug output window when running in `#DEBUG` builds.
- Emits structured log lines to stdout/stderr via the console sink.
