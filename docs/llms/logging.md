---
domain: Logging
packages: Logging.Serilog
---

# Logging

> Serilog configuration factory with preconfigured sinks, enrichers, and structured logging defaults for ASP.NET Core applications.

## Orientation

Single package: `Headless.Logging.Serilog`. Provides `SerilogFactory` — a static factory that supplies two distinct `LoggerConfiguration` setups via extension methods:

1. **Bootstrap** (`ConfigureBootstrapLoggerConfiguration`) — minimal logger active before DI is built; catches startup crashes to file and console.
2. **Reloadable** (`ConfigureReloadableLoggerConfiguration`) — full production logger wired into `IConfiguration`; respects runtime level changes in `appsettings.json` and enriches with application metadata.

Both paths accept an optional `SerilogOptions` parameter to control the console format (text or compact JSON) and file output behavior (directory, rolling interval, retention, buffering).

```csharp
// Program.cs
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration().CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders(); // Serilog owns the console; clear before AddHeadless() adds OpenTelemetry
builder.AddHeadless();

builder.Host.UseSerilog(
    (ctx, services, cfg) =>
        cfg.ConfigureReloadableLoggerConfiguration(services, ctx.Configuration, ctx.HostingEnvironment),
    writeToProviders: true // forward every event to the OpenTelemetry logger provider
);
```

## Agent Rules

- Call `SerilogFactory.CreateBootstrapLoggerConfiguration()` (or the extension `loggerConfiguration.ConfigureBootstrapLoggerConfiguration()`) **before** `WebApplication.CreateBuilder()` — the bootstrap logger must be assigned to `Log.Logger` early to capture DI and configuration-loading exceptions.
- Call `ConfigureReloadableLoggerConfiguration()` inside `UseSerilog(…)` — that delegate receives the fully-built `IServiceProvider`, enabling `ReadFrom.Services(services)` for DI-registered enrichers/sinks.
- With OpenTelemetry logging (`Headless.Api.ServiceDefaults` `AddHeadless()`, or your own `builder.Logging.AddOpenTelemetry(...)`), pass `writeToProviders: true` to `UseSerilog(…)`. Without it, Serilog replaces the `ILoggerFactory` and no `ILogger` record reaches the OpenTelemetry exporter; logs still print to the console, so nothing looks wrong locally. The forwarded records keep their structured attributes.
- With `writeToProviders: true`, call `builder.Logging.ClearProviders()` **before** `AddHeadless()`. The default Console, Debug, and EventSource providers would otherwise print every event a second time beside Serilog's console sink, and clearing after `AddHeadless()` removes the OpenTelemetry provider too.
- Configure log levels exclusively via `Serilog:MinimumLevel` in `appsettings.json`; do not call `loggerConfiguration.MinimumLevel.*` in code when using the reloadable path — `ReadFrom.Configuration` owns the level.
- The bootstrap path does **not** call `ReadFrom.Configuration`; log levels there are fixed (all events) at bootstrap time.
- Pass a `SerilogOptions` instance to either method only to override the console format or file-output defaults — directory, rolling interval, retention count, flush interval. Do not reconfigure the named sinks manually.
- The console writes text in Development and compact JSON (one CLEF object per line: `@t`, `@mt`, `@l`, `@x`, and every property as a field) in every other environment. Set `SerilogOptions.ConsoleFormat` to `SerilogConsoleFormat.Text` or `Json` to pin it; pass the same options to both the bootstrap and reloadable calls so the two phases agree. Do not add a second console sink to get JSON.
- The bootstrap logger runs before the host, so with `ConsoleFormat` unset it reads the environment from `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT` (unset = Production). An environment given only on the command line (`--environment`) does not reach it; pin `ConsoleFormat` in that case.
- Set `WriteToFiles = false` in `SerilogOptions` to suppress file output (e.g., in container environments where stdout suffices).
- Log files are written under `Logs/` by default: `fatal-.log`, `error-.log`, `warning-.log` (reloadable path); `bootstrap-.log` (bootstrap path). Each file receives only its exact level — fatal does not cascade to error.
- The Debug sink is active only in `#DEBUG` builds (conditional via `[Conditional("DEBUG")]`); do not guard it yourself.
- In text format the console theme is `AnsiConsoleTheme.Code` in Development and `ConsoleTheme.None` elsewhere — driven by `IHostEnvironment.IsDevelopment()`. JSON output carries no theme.
- `IPAddress` values are automatically destructured to their string representation (`ip?.ToString() ?? string.Empty`) — no custom destructuring policy needed.
- All enrichers (`Environment`, `Thread`, `Process`, `Machine`, `Span`, `LogContext`, `Application`, `Version`, `CommitHash`) are registered internally; **do not add them again** via separate `Serilog.Enrichers.*` calls.
- `Headless.Extensions` is a transitive dependency via `Headless.Logging.Serilog`; do not reference it separately just for `AssemblyInformation`.
- The output template is exposed as `SerilogFactory.OutputTemplate` (a `public const string`) — reference it if you write a custom sink that must match the standard format.

---
## Headless.Logging.Serilog

### API and behavior

- `SerilogFactory.CreateBootstrapLoggerConfiguration()` / `ConfigureBootstrapLoggerConfiguration()` — early-startup logger covering console, debug (DEBUG builds), and an async file sink for Fatal/Error/Warning events.
- `SerilogFactory.CreateReloadableLoggerConfiguration()` / `ConfigureReloadableLoggerConfiguration()` — production logger reading from `Serilog:` config section, enriched with application metadata, wiring three level-specific async file sinks and an environment-aware console sink.
- Standard enrichers applied automatically: `LogContext`, `Span` (OpenTelemetry), `EnvironmentName`, `ThreadId`, `ProcessId`, `ProcessName`, `MachineName`, `Application`, `Version`, `CommitHash`.
- `IPAddress` destructuring policy converts IP addresses to strings.
- `SerilogOptions` to choose the console format (`ConsoleFormat`) and tune file output: directory, rolling interval, retention limit, buffering, flush interval.
- `SerilogConsoleFormat` — `Text` (rendered with `OutputTemplate`) or `Json` (`CompactJsonFormatter` from `Serilog.Formatting.Compact`).
- `SerilogFactory.OutputTemplate` — shared `public const string` matching `[{UtcTimestamp:yyyy-MM-dd HH:mm:ss.fff}Z {Level:u3}] {RequestPath} {SourceContext} {Message:lj}{NewLine}{Exception}`, used by the text console, Debug, and file sinks.
- Debug sink active only in `#DEBUG` builds — no production overhead.
- Console format switches automatically: text with `AnsiConsoleTheme.Code` in Development, compact JSON in other environments, unless `ConsoleFormat` pins one.

### Design constraints

**Two-phase logger approach**: ASP.NET Core's host-building phase can throw before `IConfiguration` or DI is ready. The bootstrap logger captures those crashes. The reloadable logger replaces it once the host is built and can call `ReadFrom.Services(services)` to pick up any DI-registered sinks or enrichers.

**Level-segregated file sinks**: Each file (`fatal-.log`, `error-.log`, `warning-.log`) receives exactly one level — it is not a cumulative log. This makes triage faster (open `error-.log` to see all errors, without Fatal noise) and limits file sizes per severity. The async wrapper (`Serilog.Sinks.Async`) decouples I/O from the logging hot path.

**`WriteToFiles` defaults to `true`** but the option exists to support container deployments where structured logs go to stdout only. When `false`, the three level-specific sinks are skipped entirely; the console sink and Debug sink remain.

**Environment-aware console format**: outside Development the console writes compact JSON, because container platforms and log aggregators (Datadog, CloudWatch, Loki) read stdout line by line: a JSON line keeps every structured property as a field, and a multi-line exception stays inside one event instead of splitting into several records. In Development the themed text template stays readable. File and Debug sinks always write text with `OutputTemplate`.

**`SerilogFactory.OutputTemplate`** is `public const` so custom downstream sinks or formatters can reference the canonical template without duplicating the string.

### Install

```bash
dotnet add package Headless.Logging.Serilog
```

### Setup and use

```csharp
// Program.cs

// 1. Bootstrap logger — before WebApplication.CreateBuilder
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 2. Drop the default providers (Serilog owns the console) before AddHeadless() adds OpenTelemetry
    builder.Logging.ClearProviders();
    builder.AddHeadless();

    // 3. Replace with reloadable production logger; writeToProviders feeds the OpenTelemetry provider
    builder.Host.UseSerilog(
        (ctx, services, cfg) =>
            cfg.ConfigureReloadableLoggerConfiguration(services, ctx.Configuration, ctx.HostingEnvironment),
        writeToProviders: true
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
        cfg.ConfigureReloadableLoggerConfiguration(services, ctx.Configuration, ctx.HostingEnvironment, serilogOptions),
    writeToProviders: true
);
```

### Configuration

#### appsettings.json

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

#### Options

`SerilogOptions` controls the console format and file-sink behaviour. Pass an instance to either factory method. Invalid values fail fast at construction: `LogDirectory` must be non-whitespace, and `RetainedFileCountLimit` / `MaxHeaderLength` must be positive when set.

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConsoleFormat` | `SerilogConsoleFormat?` | `null` | `null` = `Text` in Development, `Json` elsewhere. The bootstrap path reads the environment from `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`. |
| `WriteToFiles` | `bool` | `true` | Set `false` to skip all file sinks (stdout-only environments). |
| `LogDirectory` | `string` | `"Logs"` | Relative or absolute path for log files. |
| `Buffered` | `bool` | `true` | Enable buffered writes for lower I/O overhead. |
| `FlushToDiskInterval` | `TimeSpan` | `1 second` | How often the async sink flushes buffered events to disk. |
| `RollingInterval` | `RollingInterval` | `Day` | Serilog rolling interval (Hour, Day, Month, …). |
| `RetainedFileCountLimit` | `int?` | `5` | How many rolled files to keep per log category; `null` retains all files indefinitely. |
| `MaxHeaderLength` | `int` | `512` | Reserved; not currently used by the file sinks. |

### Runtime behavior

- Creates `Logs/` directory and writes rolling log files (`fatal-.log`, `error-.log`, `warning-.log`) when `WriteToFiles = true` (default).
- Writes `Logs/bootstrap-.log` for Fatal/Error/Warning events during the bootstrap phase.
- Writes to the Debug output window when running in `#DEBUG` builds.
- Emits one log event per line to stdout via the console sink: compact JSON outside Development, themed text in Development (or the format `ConsoleFormat` pins).
- With `writeToProviders: true`, also hands each event to every registered `ILoggerProvider`, which is how OpenTelemetry log export receives it. Without it, only Serilog's own sinks see `ILogger` output.
