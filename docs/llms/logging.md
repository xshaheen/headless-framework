---
domain: Logging
packages: Logging.Redaction, Logging.Redaction.AspNetCore, Logging.Serilog
---

# Logging

> Serilog configuration factory with preconfigured sinks, enrichers, and structured logging defaults for ASP.NET Core applications.

## Orientation

Three packages. `Headless.Logging.Redaction` holds the data-classification taxonomy and its redactors, which decide what a log may carry. `Headless.Logging.Redaction.AspNetCore` applies declared credential paths to ASP.NET Core's HTTP logging and to OpenTelemetry spans, and wires Microsoft's HTTP logging redaction with the taxonomy. `Headless.Logging.Serilog` provides `SerilogFactory` — a static factory that supplies two distinct `LoggerConfiguration` setups via extension methods:

1. **Bootstrap** (`ConfigureBootstrapLoggerConfiguration`) — minimal logger active before DI is built; catches startup crashes to file and console.
2. **Reloadable** (`ConfigureReloadableLoggerConfiguration`) — full production logger wired into `IConfiguration`; respects runtime level changes in `appsettings.json` and enriches with application metadata.

Both paths accept an optional `SerilogOptions` parameter to control the console format (text or compact JSON) and file output behavior (directory, rolling interval, retention, buffering).

```csharp
// Program.cs
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration().CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders(); // Serilog owns the console; clear before AddHeadless() adds OpenTelemetry
builder.AddHeadless();
builder.AddHeadlessSerilog(); // reloadable profile, every event forwarded to OpenTelemetry, redaction applied
```

## Agent Rules

- Call `SerilogFactory.CreateBootstrapLoggerConfiguration()` (or the extension `loggerConfiguration.ConfigureBootstrapLoggerConfiguration()`) **before** `WebApplication.CreateBuilder()` — the bootstrap logger must be assigned to `Log.Logger` early to capture DI and configuration-loading exceptions.
- Register Serilog with `builder.AddHeadlessSerilog(options?, configure?)`. It calls Serilog's `AddSerilog` with `ConfigureReloadableLoggerConfiguration()` (the delegate receives the fully-built `IServiceProvider`, enabling `ReadFrom.Services(services)` for DI-registered enrichers/sinks), passes `writeToProviders: true`, and wraps Serilog's factory with `AddHeadlessRedaction()`, so it works in any order with `AddHeadless()` and `AddHeadlessRedaction`. `configure` runs after the Headless profile, for extra sinks or enrichers.
- If you call `UseSerilog(…)` yourself instead, call `ConfigureReloadableLoggerConfiguration()` inside it, then call `builder.Services.AddHeadlessRedaction()` after it. With OpenTelemetry logging (`Headless.Api.ServiceDefaults` `AddHeadless()`, or your own `builder.Logging.AddOpenTelemetry(...)`), pass `writeToProviders: true` to `UseSerilog(…)`. Without it, Serilog replaces the `ILoggerFactory` and no `ILogger` record reaches the OpenTelemetry exporter; logs still print to the console, so nothing looks wrong locally. The forwarded records keep their structured attributes.
- With `writeToProviders: true`, the OpenTelemetry provider renders each forwarded event's message, because `AddHeadless()` sets `IncludeFormattedMessage = true`; Serilog hands the provider the template and properties, and the provider calls Serilog's renderer to fill the record body. A Serilog sink that also writes the rendered message renders it a second time: the text console (the Development default) on every event, and the level files for warnings and above. The compact JSON console (the default elsewhere) writes the template (`@mt`), not the rendered text, so a production host renders each event once. To render once in Development too, pin `SerilogOptions.ConsoleFormat = SerilogConsoleFormat.Json`, or set `IncludeFormattedMessage = false` through `HeadlessServiceDefaultsOptions.OpenTelemetry.ConfigureLogging` when the exported record may carry the template and attributes without a rendered body.
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
- To keep personal data and secrets out of Serilog output, mark the members with a `DataClassificationAttribute` (the Headless ones are `[SecretData]`, `[PrivateData]`, and `[IdentifierData]`) and register the redactors (`services.AddHeadlessRedaction(...)` from `Headless.Logging.Redaction`, or Microsoft's `services.AddRedaction(...)`). The reloadable profile then redacts those members whenever Serilog destructures a value (`{@Value}`), in every sink and in the events `writeToProviders` forwards to OpenTelemetry. Do not write a Serilog enricher or destructuring policy that masks by property name. A plain `{Value}` hole renders `ToString()` and is not redacted, so a type that holds a secret still overrides `ToString()`.
- A `[LoggerMessage]` parameter that carries a classification attribute is redacted under Serilog only through the `AddHeadlessRedaction` wrapper: `AddHeadlessSerilog` applies it. After a plain `UseSerilog(…)`, Serilog's factory drops the parameter and the rendered text keeps the literal `{Name}` hole, because Microsoft's `EnableRedaction` only runs inside Microsoft's own factory, and the host logs `LogRedactionFactoryReplaced` (Warning) at startup until `AddHeadlessRedaction()` is called after it.
- All enrichers (`Environment`, `Thread`, `Process`, `Machine`, `LogContext`, `Application`, `Version`, `CommitHash`) are registered internally; **do not add them again** via separate `Serilog.Enrichers.*` calls.
- `Headless.Extensions` is a transitive dependency via `Headless.Logging.Serilog`; do not reference it separately just for `AssemblyInformation`.
- The output template is exposed as `SerilogFactory.OutputTemplate` (a `public const string`) — reference it if you write a custom sink that must match the standard format.

---
## Headless.Logging.Redaction

### API and behavior

- `HeadlessDataClassifications` — the taxonomy (`TaxonomyName` = `"Headless"`): `Secret` (credentials and keys), `Private` (personal data about a person), `Identifier` (ids operators correlate on that should not appear raw).
- `[SecretData]`, `[PrivateData]`, `[IdentifierData]` — `DataClassificationAttribute`s for properties, fields, parameters, and types.
- `services.AddHeadlessRedaction(setup?)` — the one entry point. It registers Microsoft's redaction (`IRedactorProvider`) with all three classifications erased, and wraps the registered `ILoggerFactory`, whichever stack owns it, so every entry is redacted before any provider sees it: a classified `[LoggerMessage]` parameter reaches providers redacted as an ordinary property and in the rendered text, a credential path an `ILogPathRedactor` recognizes is replaced in ASP.NET Core's request scope (`RequestPath`) and request lines (`Path`), and the text patterns below are masked. `AddHeadless()` (`Headless.Api.ServiceDefaults`) and `AddHeadlessSerilog()` call it, so a Headless host needs it only to add a key or patterns. Safe to call from an app and from libraries, in any order: each call adds only what its own `setup` asks for, and a factory is never wrapped twice.
- `HeadlessRedactionSetupBuilder` — the `setup` argument:
  - `UseHmac(hmacSection)` / `UseHmac(configureHmac)` — `Private` and `Identifier` become HMAC pseudonyms keyed by `HmacRedactorOptions` (a Base64 `Key` of at least 44 characters, and a `KeyId`). `Secret` stays erased.
  - `MaskText(pattern, replacement = "***", scope?)` — opt-in masking of value shapes no classification reaches (a 14-digit national id, a card number) in each scanned entry's rendered text, property values, and scope values. The `string` overload compiles the pattern culture-invariant with a 100 ms match timeout; the `Regex` overload uses yours. `MaskText(ILogTextRedactor, scope?)` takes a redactor of your own. `scope` (`LogTextRedactionScope`) limits a pattern to `Categories` (prefixes, ordinal, ignoring case, as logging filters match them) and a `MinimumLevel`; a category no pattern applies to gets the logger unwrapped.
  - `ConfigureRedaction(configure)` — Microsoft's `IRedactionBuilder`, after the defaults: map your own classifications or replace a default.
- `PrefixMaskRedactor` — keeps the first characters (8 by default) and appends `***`, for device tokens and similar; map it to a classification with `ConfigureRedaction`.
- `ILogTextRedactor` — a text redactor of your own. One registered directly as an `ILogTextRedactor` singleton scans every entry, so a library can contribute one without the builder.
- `ILogPathRedactor` — declares request paths that carry a credential (`bool TryRedact(string path, out string? redacted)`); register implementations as `ILogPathRedactor` singletons. `UseSignedUrlEndpoint` registers one for signed blob URLs. `ILogPathRedactor.TryRedactAny(redactors, path, out redacted)` and `ILogPathRedactor.RedactAny(redactors, path)` apply the first matching redactor; the API exception handler and the middleware-analysis adapter use them for the paths they log.

```csharp
// Program.cs: pseudonyms keyed from configuration (keep the key in a secret store)
builder.AddHeadless();
builder.AddHeadlessSerilog(); // wraps Serilog's factory itself; any order with AddHeadless and AddHeadlessRedaction

builder.Services.AddHeadlessRedaction(setup => setup
    .UseHmac(builder.Configuration.GetSection("Redaction:Hmac"))
    .MaskText(@"\b\d{14}\b", scope: scope => scope.MinimumLevel = LogLevel.Information));

public sealed record Customer(
    [property: PrivateData] string Email,
    [property: IdentifierData] string AccountNumber,
    [property: SecretData] string ApiKey,
    string Tier
);
```

### Design constraints

- **Secrets are always erased; HMAC applies only to private data and identifiers.** Nobody correlates on a password, so it never gets a pseudonym, even with a key. Every `AddHeadlessRedaction` call maps `Secret` to erasing again.
- **Without a key, everything is erased.** An erased member keeps its name with an empty value. Add a key when operators need "same person" or "same id" across events.
- **A library's bare call never undoes the app's key.** Only `UseHmac` maps `Private` and `Identifier`; without it they use the erasing fallback, so `AddHeadlessRedaction()` from a library, before or after the app's keyed call, leaves the pseudonyms in place.
- **Unmapped classifications fail closed.** A classification with no redactor, or a combination such as `[PrivateData][IdentifierData]` on one member, uses Microsoft's erasing fallback.
- **A pseudonym is still personal data** when it identifies a person: it reduces risk and is not anonymization. Rotating the key breaks correlation across the rotation.
- **The wrapper copies, never holds.** A `[LoggerMessage]` call passes a thread-local state that the generator clears when `Log` returns, so the wrapper copies the plain values and the redacted ones into a fresh state for providers; the raw classified values never leave the call. A level no provider listens to costs nothing.
- **A later logger factory replaces the wrapper.** `AddHeadlessSerilog` wraps its own. After a plain `UseSerilog`, or any other factory registered after `AddHeadlessRedaction`, call `AddHeadlessRedaction()` again; until then the host logs `LogRedactionFactoryReplaced` (Warning) at startup.
- **Only ASP.NET Core's hosting category is path-rewritten.** Every other category passes through untouched, and a logger with nothing to redact (no redactor provider, not the hosting category, no text pattern in scope) is returned unwrapped.
- **The names avoid ASP.NET Core Identity's `[PersonalData]`**, so an app that uses both never hits an ambiguous attribute. `Microsoft.Extensions.Compliance.Testing` declares its own `PrivateDataAttribute`; a test file that imports that namespace and `Headless.Logging` must qualify one.
- **Text redaction is a net, not the control.** It runs on every string of every entry in its scope, and a shape can match an unrelated value that looks the same. Classify first; add a pattern only for shapes that reach logs as free text, such as an interpolated message, and scope it to the categories and levels where they appear. A timed-out match replaces the whole text, so a slow match never lets a value through. It runs after classification and path redaction, so it scans their redacted copies.
- **An entry with nothing to mask is not copied.** When neither the text nor any value matches, providers receive the original state and formatter. Booleans, GUIDs, enums, and date and time values are not formatted to be scanned; integers are, because a 14-digit id held as a `long` renders as that id. A non-string value that matches is replaced by its masked text, so template-rendering providers such as Serilog print the masked form too. Members of a destructured `{@Value}` are not scanned: classify them.
- **Exception text is out of reach.** Providers read an exception's message and stack trace from the exception object, so neither classification nor text redaction rewrites them. Do not put a secret in an exception message.

### Channel coverage

| Channel | What redacts it |
| --- | --- |
| A destructured value (`{@Value}`) under Serilog | `DataClassificationDestructuringPolicy`, which the reloadable Serilog profile adds when `AddHeadlessRedaction`/`AddRedaction` is registered |
| A classified `[LoggerMessage]` / `[LogProperties]` parameter | `AddHeadlessRedaction()` under any logger factory; Microsoft's `EnableRedaction` also covers it when Microsoft's factory is in charge |
| ASP.NET Core's request scope and request lines | `AddHeadlessRedaction()` with an `ILogPathRedactor` (the signed-URL endpoint registers one) |
| A plain `{Value}` hole, an interpolated message | the value's `ToString()`, and `MaskText` for known shapes |
| HTTP logging (`UseHttpLogging`) | `AddHeadlessHttpLoggingRedaction()` (credential headers erased, route parameters you classify), and `AddHeadlessRequestLogRedaction()` for a declared credential path |
| Span tags (`url.path`, `http.target`, `url.full`) | `AddHeadlessRequestLogRedaction()` for the request span; `AddHeadlessLogPathRedaction()` on the tracer provider for every span, outgoing calls included (`AddHeadless()` adds it) |
| Outgoing `HttpClient` logging | Microsoft's `AddExtendedHttpClientLogging` (package `Microsoft.Extensions.Http.Diagnostics`), with the same classifications |
| Exported OTLP logs and spans | everything above, plus the collector backstop below |
| Exception messages and stack traces | nothing: keep secrets out of them |

HTTP logging speaks the same classifications, so one taxonomy covers it; see [Headless.Logging.Redaction.AspNetCore](#headlessloggingredactionaspnetcore). `AddExtendedHttpClientLogging` replaces the default `HttpClient` logger rather than adding beside it.

### Collector backstop

Redact in the app first: a value that reaches the collector has already crossed the network, and console and file logs never reach it. For exported telemetry, add the OpenTelemetry Collector's `redaction` processor as a second line. List the attribute keys the backend may receive in `allowed_keys` so anything else is dropped (fail closed), add `blocked_values` regexes for shapes that must never pass, and set `hash_function` so blocked values are hashed rather than erased, which keeps "same value" searchable. A keyed hash (`hmac-sha256` with `hmac_key`) cannot be reversed by hashing guesses; the processor also accepts `md5`, `sha1`, `sha3`, and `hmac-sha512`. `blocked_values` checks the values of allowed attribute keys (and map-shaped log bodies), not a plain-text body:

```yaml
processors:
  redaction:
    allow_all_keys: false
    allowed_keys: [http.request.method, http.response.status_code, url.path, service.name]
    blocked_values: ["\\b\\d{14}\\b"]
    hash_function: hmac-sha256
    hmac_key: "${env:REDACTION_HMAC_KEY}"
    summary: info
```

### Install

```bash
dotnet add package Headless.Logging.Redaction
```

---
## Headless.Logging.Redaction.AspNetCore

### API and behavior

- `services.AddHeadlessRequestLogRedaction()` — applies every registered `ILogPathRedactor` wherever ASP.NET Core writes the request path: the request scope and request lines (it calls `AddHeadlessRedaction()`), the `Path` field of the HTTP logging record (an `IHttpLoggingInterceptor`), and the request span's `url.path`, `http.target`, and `url.full` tags (an `IStartupFilter` middleware, first in the pipeline). Idempotent, and safe to call from libraries. `AddHeadless()` and `UseSignedUrlEndpoint` call it.
- `tracing.AddHeadlessLogPathRedaction()` — a `TracerProviderBuilder` processor that rewrites the same three tags on every span when it ends, server and client alike, so an outgoing `HttpClient` call to a credential URL is covered too. A matching value loses its query and fragment, because a credential URL can carry the same secret there. Processors run in the order they are added: add it before any exporter. `AddHeadless()` adds it ahead of its user hook and the OTLP exporter.
- `services.AddHeadlessHttpLoggingRedaction(configure?)` — Microsoft's classification-based HTTP logging redaction, plus `AddHeadlessRequestLogRedaction()`. `configure` takes `HttpLoggingRedactionOptions`: `RequestHeaders`, `ResponseHeaders`, and `RouteParameters` (name to `DataClassification`), `ExcludePathStartsWith`, and `IncludeUnmatchedRoutes`. `RequestHeaders` starts with `Authorization`, `Proxy-Authorization`, and `Cookie`, and `ResponseHeaders` with `Set-Cookie`, all `HeadlessDataClassifications.Secret` (logged by name, value erased). Microsoft marks its own API experimental (`EXTEXP0013`); this method maps the Headless options onto it, so callers see no diagnostic. It registers HTTP logging itself; the pipeline still needs `app.UseHttpLogging()`. Idempotent: a second call adds only its `configure`, after the earlier ones.

### Design constraints

- **The request span is redacted when the first middleware runs.** ASP.NET Core and the OpenTelemetry instrumentation set `url.path` when the activity starts and do not set it again, so exporters receive the redacted value. A sampler, or a processor's `OnStart`, runs before the middleware and still sees the raw path.
- **The span processor only sees spans its tracer provider records,** after every instrumentation has set its tags. An exporter added before it exports the raw value.
- **HTTP logging writes no header you do not list.** A listed header is logged by name with its value through its classification's redactor; an unlisted header is left out entirely.

### Install

```bash
dotnet add package Headless.Logging.Redaction.AspNetCore
```

```csharp
builder.Services.AddHeadlessRedaction(setup => setup.UseHmac(builder.Configuration.GetSection("Redaction:Hmac")));
builder.Services.AddHeadlessHttpLoggingRedaction(options =>
    options.RouteParameters["userId"] = HeadlessDataClassifications.Identifier);
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddHeadlessLogPathRedaction() // before the exporter
    .AddOtlpExporter());

var app = builder.Build();
app.UseHttpLogging();
```

---
## Headless.Logging.Serilog

### API and behavior

- `SerilogFactory.CreateBootstrapLoggerConfiguration()` / `ConfigureBootstrapLoggerConfiguration()` — early-startup logger covering console, debug (DEBUG builds), and an async file sink for Fatal/Error/Warning events.
- `SerilogFactory.CreateReloadableLoggerConfiguration()` / `ConfigureReloadableLoggerConfiguration()` — production logger reading from `Serilog:` config section, enriched with application metadata, wiring three level-specific async file sinks and an environment-aware console sink.
- Standard enrichers applied automatically: `LogContext`, `EnvironmentName`, `ThreadId`, `ProcessId`, `ProcessName`, `MachineName`, `Application`, `Version`, `CommitHash`.
- Trace correlation comes from Serilog itself: every event records the current `Activity`'s trace and span IDs (`LogEvent.TraceId`, `LogEvent.SpanId`), which compact JSON writes as `@tr` and `@sp` and text templates read as `{TraceId}` and `{SpanId}`. No `TraceId`, `SpanId`, or `ParentId` event properties are added; a sink or query that reads those properties must read the built-in fields instead.
- `IPAddress` destructuring policy converts IP addresses to strings.
- `DataClassificationDestructuringPolicy` redacts the members of a destructured value that carry a `DataClassificationAttribute`, through the `IRedactorProvider` that `AddRedaction` registers. `ConfigureReloadableLoggerConfiguration` adds it first, ahead of configuration- and DI-registered policies, whenever `services` resolves an `IRedactorProvider`; `loggerConfiguration.Destructure.WithRedaction(provider)` adds it to any other logger. Without a provider, classified members log unchanged.
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

**Classification-driven redaction**: the same `DataClassificationAttribute` and `AddRedaction` mapping that Microsoft logging uses also drives Serilog, so an app states once which members are personal or secret. The policy applies these rules to a destructured value:

- A classified property keeps its name. It holds the redactor's output, so an erasing redactor yields `""` and the log still shows that the property was set, while a `null` value stays `null`.
- A property with several classifications is redacted as one combined set; with no redactor mapped for that set, the provider's fallback redactor applies.
- A classified collection property is redacted element by element, which keeps the count and, with an HMAC redactor, shows which elements repeat. A `string`, `char[]`, or `byte[]` is one value, redacted whole from its content (a `byte[]` as Base64): per-character tokens would act as a substitution cipher over a secret buffer.
- A classification on a type redacts the whole value to one string, its `ToString()` passed through the redactor, even when the type implements `IEnumerable`.
- `DataClassification.None` (`[NoDataClassification]`) counts as unclassified.
- Properties are read the way Serilog reads them: inherited ones are included, overrides inherit the classification from the base declaration, indexers and non-public getters are skipped, and a throwing getter logs a message naming the exception type.
- Values are formatted with the invariant culture, so a pseudonym is the same on every host.

The policy is adapted from Serilog.Redaction (Apache-2.0; see the package's `THIRD-PARTY-NOTICES.md`).

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

    // 3. Replace with reloadable production logger, forwarded to the OpenTelemetry provider and redacted
    builder.AddHeadlessSerilog();

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

builder.AddHeadlessSerilog(serilogOptions);
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
