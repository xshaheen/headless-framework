---
domain: Logging
packages: Logging.Redaction, Logging.Redaction.AspNetCore, Logging.Serilog
---

# Logging

> Serilog host logging with Headless defaults, and classification-based redaction of logs, HTTP logging, and OpenTelemetry spans.

## Orientation

Three packages:

- **`Headless.Logging.Serilog`** gives a host a Serilog logger in two phases. `SerilogFactory.CreateBootstrapLoggerConfiguration()` builds a fixed logger that runs before the host exists and catches startup crashes. `builder.AddHeadlessSerilog()` registers the reloadable logger the host runs with: levels from `IConfiguration`, application enrichers, level files, an environment-aware console, every event forwarded to the other logging providers (OpenTelemetry), and redaction applied.
- **`Headless.Logging.Redaction`** holds the data-classification taxonomy (`[SecretData]`, `[PrivateData]`, `[IdentifierData]`) and `AddHeadlessRedaction`, which decides what any log entry may carry, whichever logger factory owns the host.
- **`Headless.Logging.Redaction.AspNetCore`** applies the same rules to ASP.NET Core HTTP logging and to OpenTelemetry span tags.

The default path for an API host:

```csharp
// Program.cs
Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration().CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders(); // Serilog owns the console; clear before AddHeadless() adds OpenTelemetry
builder.AddHeadless();
builder.AddHeadlessSerilog(); // reloadable profile, every event forwarded to OpenTelemetry, redaction applied
```

`AddHeadless()` (`Headless.Api.ServiceDefaults`) already calls `AddHeadlessRedaction()` and `AddHeadlessRequestLogRedaction()`, and adds both span processors. Call `AddHeadlessRedaction(setup => ...)` yourself only to add an HMAC key, text patterns, or name classifications.

## Agent Rules

- Assign the bootstrap logger to `Log.Logger` before `WebApplication.CreateBuilder()`, so configuration-loading and DI exceptions are logged.
- Register the host logger with `builder.AddHeadlessSerilog(options?, configure?, preserveStaticLogger?)`, not `UseSerilog(...)`. It works in any order with `AddHeadless()` and `AddHeadlessRedaction()`. Use `configure` (`Action<IServiceProvider, LoggerConfiguration>`, run after the Headless profile) for extra sinks or enrichers.
- If you must call `UseSerilog(...)` yourself, call `ConfigureReloadableLoggerConfiguration()` inside it, pass `writeToProviders: true` when the host exports OpenTelemetry logs, and call `builder.Services.AddHeadlessRedaction()` after it. Without `writeToProviders: true`, no `ILogger` record reaches the OpenTelemetry exporter, while the console still prints, so nothing looks wrong locally. Without the later `AddHeadlessRedaction()`, classified `[LoggerMessage]` parameters are dropped and the message keeps the literal `{Name}` hole, and the host logs `LogRedactionFactoryReplaced` (Warning) at startup.
- Call `builder.Logging.ClearProviders()` before `AddHeadless()`. The default Console, Debug, and EventSource providers otherwise print every forwarded event a second time. Clearing after `AddHeadless()` also removes the OpenTelemetry provider.
- Set log levels only in `Serilog:MinimumLevel` in configuration. Do not call `loggerConfiguration.MinimumLevel.*` on the reloadable path: `ReadFrom.Configuration` owns the level and reloads it at run time. The bootstrap logger does not read configuration and logs every level.
- Pass a `SerilogOptions` instance only to change the console format or file output. Pass the same instance to the bootstrap call and to `AddHeadlessSerilog`, so both phases agree. Do not add a second console sink to get JSON, and do not reconfigure the built-in sinks.
- Do not add `Serilog.Enrichers.*` calls for `Environment`, `Thread`, `Process`, `Machine`, or `LogContext`. The profile registers them, plus `Application`, `Version`, and `CommitHash`.
- To keep personal data and secrets out of logs, mark members with `[SecretData]`, `[PrivateData]`, or `[IdentifierData]`. Do not write a Serilog enricher or destructuring policy that masks by property name. For a value logged through a plain `{Email}` hole or by a library, use `ClassifyProperty("Email", ...)`.
- A plain `{Value}` hole renders `ToString()` and is not redacted by member attributes. Destructure with `{@Value}`, or override `ToString()` on a type that holds a secret.
- Serilog's static `Log`, the bootstrap logger, and properties pushed with `LogContext.PushProperty` are never masked. Log secrets-adjacent data through an injected `ILogger` or `Serilog.ILogger`.

---

## Headless.Logging.Redaction

The data-classification taxonomy and the redaction applied to every `ILogger` entry, under any logger factory.

### Setup

```bash
dotnet add package Headless.Logging.Redaction
```

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

**Verify:** the host starts without a `LogRedactionFactoryReplaced` warning, and logging a `Customer` with `{@Customer}` shows `ApiKey` as `""` and `Email` and `AccountNumber` as pseudonyms (or `""` without a key). In a test, resolve `LogTextMasker` and assert `Mask("id 29001011234567")` returns the masked text.

### Configuration

`services.AddHeadlessRedaction(setup?)` is the one entry point. It registers Microsoft's redaction (`IRedactorProvider`) with all three classifications erased, and wraps the registered `ILoggerFactory`, so providers receive every entry already redacted. Call it from the app and from libraries, in any order: each call adds only what its own `setup` asks for, and a factory is never wrapped twice. `AddHeadless()` and `AddHeadlessSerilog()` call it.

`HeadlessRedactionSetupBuilder` (the `setup` argument):

| Member | Effect |
| --- | --- |
| `UseHmac(hmacSection)`, `UseHmac(configureHmac)` | `Private` and `Identifier` become HMAC pseudonyms keyed by Microsoft's `HmacRedactorOptions`: a Base64 `Key` of at least 44 characters, and a `KeyId`. `Secret` stays erased. |
| `MaskText(pattern, replacement = "***", scope?)` | Masks value shapes no classification reaches, such as a 14-digit national ID or a card number. The `string` overload compiles the pattern culture-invariant with a 100 ms match timeout. The `Regex` overload uses your options. `MaskText(ILogTextRedactor, scope?)` takes your own redactor. |
| `scope` (`LogTextRedactionScope`) | Limits a pattern to `Categories` (prefixes, ordinal, ignoring case, as logging filters match them) and a `MinimumLevel` (default `Trace`). |
| `ConfigureRedaction(configure)` | Microsoft's `IRedactionBuilder`, applied after the defaults. Map your own classifications or replace a default. |
| `ClassifyProperty(name, classification)` | Redacts a structured log property by name, ignoring case, as if its value carried `classification`. Applies to every entry and scope, element by element for a collection, and to events written through Serilog's own injected `ILogger`, with the same pseudonym. The rendered message carries the redacted value. |

Other public types:

- `HeadlessDataClassifications`: the taxonomy (`TaxonomyName` = `"Headless"`). `Secret` is credentials and keys. `Private` is personal data about a person. `Identifier` is IDs operators correlate on that must not appear raw.
- `[SecretData]`, `[PrivateData]`, `[IdentifierData]`: `DataClassificationAttribute`s for properties, fields, parameters, and types.
- `PrefixMaskRedactor`: keeps the first characters (8 by default) and appends `***`, for device tokens and similar. Map it to a classification with `ConfigureRedaction`.
- `ILogTextRedactor`: a text redactor of your own. A library can register one directly as an `ILogTextRedactor` singleton, without the builder; it then scans every entry.
- `LogTextMasker`: applies every registered text redactor outside the logger factory. `Mask(string)`, `MaskValue(object?)`, and `Mask(Exception)` each return the argument unchanged when nothing matched. `IsEnabled` is `true` when any text redactor is registered. Resolve it to mask a channel the framework does not write, such as an error tracker's before-send hook (Sentry's `SetBeforeSend`, an Application Insights telemetry processor). Every redactor applies through it, whatever its scope, because these channels have no category or level.
- `ILogPathRedactor`: declares request paths that carry a credential (`bool TryRedact(string path, out string? redacted)`). Register implementations as `ILogPathRedactor` singletons. `UseSignedUrlEndpoint` registers one for signed blob URLs. `ILogPathRedactor.TryRedactAny(redactors, path, out redacted)` and `ILogPathRedactor.RedactAny(redactors, path)` apply the first matching redactor, for paths your own code logs.
- `RedactedException`: see exception masking below.

### Design and runtime behavior

Classification:

- **Secrets are always erased.** HMAC applies only to private data and identifiers. Every `AddHeadlessRedaction` call maps `Secret` to erasing again.
- **Without a key, everything classified is erased.** An erased member keeps its name with an empty value. Add a key when operators need "same person" or "same ID" across events.
- **A library's bare call never undoes the app's key.** Only `UseHmac` maps `Private` and `Identifier`, so `AddHeadlessRedaction()` from a library, before or after the app's keyed call, leaves the pseudonyms in place.
- **Unmapped classifications fail closed.** A classification with no redactor, or a combination such as `[PrivateData][IdentifierData]` on one member, uses Microsoft's erasing fallback.
- **A pseudonym is still personal data** when it identifies a person. It is not anonymization. Rotating the key breaks correlation across the rotation.
- **A name-classified property is redacted like an attribute-classified one:** same redactor, same pseudonym, same element-by-element collections. A value printed with a format that changes its text (`{Amount:N2}`) is redacted in the property but can keep its formatted text in the message: classify it, or add a `MaskText` pattern. Members of a destructured `{@Value}` are not matched by name: classify them.
- **Attribute names avoid ASP.NET Core Identity's `[PersonalData]`.** `Microsoft.Extensions.Compliance.Testing` declares its own `PrivateDataAttribute`, so a test file that imports it and `Headless.Logging` must qualify one.

Logger factory:

- **A later logger factory replaces the wrapper.** `AddHeadlessSerilog` wraps its own. After a plain `UseSerilog`, or any other factory registered after `AddHeadlessRedaction`, call `AddHeadlessRedaction()` again. Until then the host logs `LogRedactionFactoryReplaced` (Warning) at startup.
- **Raw classified values never reach a provider.** A level no provider listens to costs nothing.
- **Only ASP.NET Core's hosting category is path-rewritten**, in the request scope (`RequestPath`) and request lines (`Path`). A logger with nothing to redact is returned unwrapped.

Text masking:

- **Text redaction is a net, not the control.** It runs on every string of every entry in its scope, and a pattern can match an unrelated value of the same shape. Classify first. Add a pattern only for shapes that reach logs as free text, such as an interpolated message, and scope it to the categories and levels where they appear.
- **A timed-out match replaces the whole text**, so a slow match never lets a value through. Text masking runs after classification and path redaction.
- **What is scanned.** Each entry's rendered text, property values, scope values, and exception messages. Integers are scanned, because a 14-digit ID held as a `long` renders as that ID. Booleans, GUIDs, enums, and date and time values are not. A non-string value that matches is replaced by its masked text, so template-rendering providers such as Serilog print the masked form. Members of a destructured `{@Value}` are not scanned: classify them. An entry with nothing to mask reaches providers unchanged.

Exception masking:

- **The logged exception is never changed.** A text redactor in scope scans the `Message` of the logged exception and of each inner exception, every `AggregateException` member included. With no match, providers receive the same object. With a match, they receive a copy of the same type with masked messages and masked inner exceptions. The copy keeps the original's stack frames, `TargetSite`, `Data`, `HResult`, and `GetType()`, so error trackers group and symbolicate it as before.
- **`RedactedException` is the fallback.** When a matching exception cannot be masked in a copy of its own type (a subclass that builds `Message` from its own fields, for example), providers receive a `RedactedException`. Its `ToString()` is the original's full text, masked. `Message` and `StackTrace` are the original's, masked. `OriginalTypeName` names the original type. `InnerException` and `InnerExceptions` hold the original's inner exceptions. `Data` is the original's dictionary, unscanned. `TargetSite` is `null`. A provider that reports `GetType()` reports `RedactedException`: read `OriginalTypeName`.
- **Not masked:** text only in a stack trace or a `ToString()` override, `Exception.Data` values (enrichers such as Serilog.Exceptions write them as they are), and exception text returned in an HTTP response. Classification does not apply to exception text.
- **Span exception events.** An event another library or your code records on a span (`Activity.AddException`, an instrumentation's `RecordException`) cannot be rewritten afterwards. Record it from `LogTextMasker.Mask(exception)` instead.
- **Every channel masks the same way.** Logs, the spans `AddHeadless` records, span failure text, and Serilog's own `ILogger` use the same redactors and the same exception copy, so a value is masked identically wherever it appears and nothing else in the record changes.

### Channel coverage

| Channel | What redacts it |
| --- | --- |
| A destructured value (`{@Value}`) under Serilog | `DataClassificationDestructuringPolicy`, which the reloadable Serilog profile adds when `AddHeadlessRedaction` or `AddRedaction` is registered |
| A classified `[LoggerMessage]` or `[LogProperties]` parameter | `AddHeadlessRedaction()` under any logger factory |
| ASP.NET Core's request scope and request lines | `AddHeadlessRedaction()` with an `ILogPathRedactor` (the signed-URL endpoint registers one) |
| A plain `{Value}` hole, an interpolated message | `ClassifyProperty(name, classification)` for a known property name, the value's `ToString()`, and `MaskText` for known shapes |
| HTTP logging (`UseHttpLogging`) | `AddHeadlessHttpLoggingRedaction()` (credential headers erased, route parameters you classify), and `AddHeadlessRequestLogRedaction()` for a declared credential path |
| Span tags (`url.path`, `http.target`, `url.full`) | `AddHeadlessRequestLogRedaction()` for the request span; `AddHeadlessLogPathRedaction()` on the tracer provider for every span, outgoing calls included (`AddHeadless()` adds it) |
| Outgoing `HttpClient` logging | Microsoft's `AddExtendedHttpClientLogging` (package `Microsoft.Extensions.Http.Diagnostics`), with the same classifications. It replaces the default `HttpClient` logger rather than adding beside it. |
| Exported OTLP logs and spans | everything above, plus the [collector backstop](#collector-backstop) |
| Exception messages and inner-exception messages logged through `ILogger` | `MaskText` and any `ILogTextRedactor`, through a masked copy of the same type, or a `RedactedException` |
| The request span's exception event | `AddHeadless()` records it from the masked copy when `OpenTelemetry.RecordException` is on; events other code records are not rewritten |
| A span's status description and `exception.message` and `exception.stacktrace` tags | `AddHeadlessTextRedaction()` on the tracer provider (`AddHeadless()` adds it) |
| Events written through an injected Serilog `ILogger` | `AddHeadlessSerilog()`: classified property names (`ClassifyProperty`), the template text, scalar properties, `ForContext` values, and the exception |

### Collector backstop

Redact in the app first: a value that reaches the collector has already crossed the network, and console and file logs never reach it. For exported telemetry, add the OpenTelemetry Collector's `redaction` processor as a second line:

- List the attribute keys the backend may receive in `allowed_keys`, so anything else is dropped.
- Add `blocked_values` regexes for shapes that must never pass. They check the values of allowed attribute keys and map-shaped log bodies, not a plain-text body.
- Set `hash_function` so blocked values are hashed rather than erased, which keeps "same value" searchable. Use a keyed hash (`hmac-sha256` with `hmac_key`): guesses cannot be hashed to reverse it. The processor also accepts `md5`, `sha1`, `sha3`, and `hmac-sha512`.

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

---

## Headless.Logging.Redaction.AspNetCore

Applies redaction to ASP.NET Core HTTP logging and to OpenTelemetry span tags.

### Setup

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
    .AddHeadlessTextRedaction()
    .AddOtlpExporter());

var app = builder.Build();
app.UseHttpLogging();
```

With `AddHeadless()`, skip the tracing block: it already adds both processors ahead of its exporter.

**Verify:** request a route with a `userId` and an `Authorization` header. The HTTP logging record shows `Authorization` with an empty value and `userId` as a pseudonym. A request to a signed URL shows the redacted path in the request log's `Path` and in the exported span's `url.path`.

### Configuration

- `services.AddHeadlessHttpLoggingRedaction(configure?)` adds Microsoft's classification-based HTTP logging redaction plus `AddHeadlessRequestLogRedaction()`. It registers HTTP logging itself; the pipeline still needs `app.UseHttpLogging()`. A second call adds only its `configure`, after the earlier ones. Microsoft marks its own API experimental (`EXTEXP0013`); this method raises no diagnostic for callers.
- `HttpLoggingRedactionOptions`: `RequestHeaders`, `ResponseHeaders`, and `RouteParameters` (name to `DataClassification`), `ExcludePathStartsWith`, and `IncludeUnmatchedRoutes`. `RequestHeaders` starts with `Authorization`, `Proxy-Authorization`, and `Cookie`, and `ResponseHeaders` with `Set-Cookie`, all `HeadlessDataClassifications.Secret` (logged by name, value erased).
- `services.AddHeadlessRequestLogRedaction()` applies every registered `ILogPathRedactor` wherever ASP.NET Core writes the request path: the request scope and request lines, the `Path` field of the HTTP logging record, and the request span's `url.path`, `http.target`, and `url.full` tags. It is idempotent and safe to call from libraries. `AddHeadless()` and `UseSignedUrlEndpoint` call it.
- `tracing.AddHeadlessLogPathRedaction()` rewrites the same three tags on every span when it ends, server and client alike, so an outgoing `HttpClient` call to a credential URL is covered too. A matching value loses its query and fragment, because a credential URL can carry the same secret there.
- `tracing.AddHeadlessTextRedaction()` masks every span's status description and `exception.message` and `exception.stacktrace` tags with the app's text redactors when the span ends. Only the matching shape changes. It does nothing without a text redactor. `AddHeadless()` adds it after `AddHeadlessLogPathRedaction()`.

### Design and runtime behavior

- **Add both span processors before any exporter.** Processors run in the order they are added, and an exporter added first exports the raw value.
- **The request span is redacted when the first middleware runs.** Exporters receive the redacted value. A sampler, or a processor's `OnStart`, runs earlier and still sees the raw path.
- **HTTP logging writes no header you do not list.** A listed header is logged by name with its value through its classification's redactor. An unlisted header is left out entirely.
- **Recorded exception events are not rewritten.** Record span exception events from `LogTextMasker.Mask(exception)`, as `AddHeadless()` does for the request span.

---

## Headless.Logging.Serilog

The Headless Serilog profile: a bootstrap logger, and a reloadable host logger forwarded to OpenTelemetry with redaction applied.

### Setup

```bash
dotnet add package Headless.Logging.Serilog
```

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

    // 3. Give the host its own reloadable production logger, forwarded to the OpenTelemetry provider and redacted
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

To suppress file output (container, stdout only):

```csharp
var serilogOptions = new SerilogOptions { WriteToFiles = false };

Log.Logger = SerilogFactory.CreateBootstrapLoggerConfiguration(serilogOptions).CreateLogger();

builder.AddHeadlessSerilog(serilogOptions);
```

**Verify:** run the host outside Development. Each console line is one compact JSON object (`@t`, `@mt`, `@l`, and the `Application` property), each event appears once, and no `LogRedactionFactoryReplaced` warning is logged. A forced startup failure lands in `Logs/bootstrap-*.log` (unless `WriteToFiles` is `false`).

### Configuration

Set levels in `appsettings.json`. Changes take effect at run time without a restart:

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

`SerilogOptions` controls the console format and the file sinks. Invalid values throw when the options are created: `LogDirectory` must not be whitespace, and `RetainedFileCountLimit` and `MaxHeaderLength` must be positive when set.

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConsoleFormat` | `SerilogConsoleFormat?` | `null` | `null` writes `Text` in Development and `Json` elsewhere. |
| `WriteToFiles` | `bool` | `true` | `false` skips every file sink; the console and Debug sinks remain. |
| `LogDirectory` | `string` | `"Logs"` | Relative or absolute directory for log files. |
| `Buffered` | `bool` | `true` | Buffered file writes. |
| `FlushToDiskInterval` | `TimeSpan` | 1 second | How often buffered events flush to disk. |
| `RollingInterval` | `RollingInterval` | `Day` | Serilog rolling interval. |
| `RetainedFileCountLimit` | `int?` | `5` | Rolled files kept per level file; `null` keeps all. |
| `MaxHeaderLength` | `int` | `512` | Truncation length for the request headers the API Serilog profile (`Headless.Api.Logging.Serilog`) adds; this package's sinks do not use it. |

`SerilogConsoleFormat` is `Text` (rendered with `SerilogFactory.OutputTemplate`) or `Json` (compact JSON from `Serilog.Formatting.Compact`).

`AddHeadlessSerilog(options, configure, preserveStaticLogger)`:

- `configure` runs after the Headless profile and receives the built `IServiceProvider`, so it can call `ReadFrom.Services(services)` or add sinks.
- `preserveStaticLogger` defaults to `true`: the host gets its own logger and `Log.Logger` stays the bootstrap logger. Several hosts in one process, such as `WebApplicationFactory` test hosts side by side, never write to each other's sinks, and disposing one does not silence the rest. Static `Log.*` calls keep writing to the bootstrap sinks, so the `Log.Fatal` above reaches the bootstrap logger. Pass `false` only to make the host's logger the static one, as `UseSerilog` does. Each `ILogger` then binds to whichever logger `Log.Logger` holds when it is created, and disposing the host calls `Log.CloseAndFlush()`, which replaces the static logger with a no-op. Do not combine `false` with several hosts per process.

`SerilogFactory` also exposes `CreateReloadableLoggerConfiguration()` and the `ConfigureBootstrapLoggerConfiguration()` and `ConfigureReloadableLoggerConfiguration()` extensions on `LoggerConfiguration`, for a `UseSerilog(...)` host. `loggerConfiguration.Destructure.WithRedaction(provider)` adds the classification policy to any other Serilog logger.

### Design and runtime behavior

Sinks:

- **Bootstrap logger:** console, Debug, and an async file sink at `{LogDirectory}/bootstrap-.log` for Fatal, Error, and Warning. It does not read configuration, and it reads the environment from `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT` (unset means Production). An environment given only on the command line (`--environment`) does not reach it: pin `ConsoleFormat` in that case.
- **Host logger:** reads the `Serilog` configuration section, an environment-aware console, and three async level files: `fatal-.log`, `error-.log`, and `warning-.log`. Each file receives only its exact level; Fatal does not also go to `error-.log`.
- **Console:** themed text (`AnsiConsoleTheme.Code`) in Development, compact JSON elsewhere: one CLEF object per line (`@t`, `@mt`, `@l`, `@x`, and every property as a field), which keeps a multi-line exception inside one record for log aggregators. Text outside Development uses no theme. File and Debug sinks always write text with `OutputTemplate`.
- **Debug sink:** active only in `DEBUG` builds. Do not guard it yourself.
- **`SerilogFactory.OutputTemplate`** (`public const string`): `[{UtcTimestamp:yyyy-MM-dd HH:mm:ss.fff}Z {Level:u3}] {RequestPath} {SourceContext} {Message:lj}{NewLine}{Exception}`. Reference it when a custom sink must match the standard format.

Event content:

- **Enrichers:** `LogContext`, `EnvironmentName`, `ThreadId`, `ProcessId`, `ProcessName`, `MachineName`, `Application`, `Version`, `CommitHash`.
- **Trace correlation** comes from Serilog's built-in `LogEvent.TraceId` and `LogEvent.SpanId`: compact JSON writes `@tr` and `@sp`, and text templates read `{TraceId}` and `{SpanId}`. No `TraceId`, `SpanId`, or `ParentId` event properties are added, so a sink or query must read the built-in fields.
- **`IPAddress`** values destructure to their string form.
- **Forwarding:** `AddHeadlessSerilog` passes `writeToProviders: true`, so every event also reaches each registered `ILoggerProvider`, which is how OpenTelemetry log export receives it, with structured attributes kept.
- **Double rendering in Development.** `AddHeadless()` sets the OpenTelemetry `IncludeFormattedMessage = true`, so the OpenTelemetry provider renders each forwarded message. A sink that also renders the message renders it a second time: the text console (the Development default) on every event, and the level files for warnings and above. The JSON console writes the template (`@mt`), so a production host renders once. To render once in Development too, pin `ConsoleFormat = SerilogConsoleFormat.Json`, or set `IncludeFormattedMessage = false` through `HeadlessServiceDefaultsOptions.OpenTelemetry.ConfigureLogging` when the exported record may carry the template without a rendered body.

Redaction of destructured values (`DataClassificationDestructuringPolicy`):

- The host profile adds the policy first, ahead of configuration- and DI-registered policies, whenever the services resolve an `IRedactorProvider` (`AddHeadlessRedaction` or Microsoft's `AddRedaction`). Without a provider, classified members log unchanged.
- A classified property keeps its name and holds the redactor's output: an erasing redactor yields `""`, so the log still shows the property was set. A `null` value stays `null`.
- A property with several classifications is redacted as one combined set; with no redactor mapped for that set, the provider's fallback redactor applies.
- A classified collection property is redacted element by element, which keeps the count and, with HMAC, shows which elements repeat. A `string`, `char[]`, or `byte[]` is one value, redacted whole (a `byte[]` as Base64).
- A classification on a type redacts the whole value to one string, its `ToString()` passed through the redactor, even when the type implements `IEnumerable`.
- `DataClassification.None` (`[NoDataClassification]`) counts as unclassified.
- Inherited properties are included, overrides inherit the base declaration's classification, and indexers and non-public getters are skipped. A throwing getter logs a message naming the exception type.
- Values are formatted with the invariant culture, so a pseudonym is the same on every host.

Serilog's own `ILogger`:

- When a text redactor or a `ClassifyProperty` name is registered, `AddHeadlessSerilog` wraps the injected `Serilog.ILogger`. A property whose name is classified, as a template hole or a `ForContext` value, goes through the classification's redactor (element by element for a sequence; a destructured `{@Value}` under that name is redacted whole). Everything else gets the same masking: the template's literal text, scalar property values, `ForContext` values, and the exception. Members of a destructured value are not matched by name: classify them. Events from `Microsoft.Extensions.Logging` arrive already masked and are not scanned twice.
- To mask `UseSerilogRequestLogging`, set its `Logger` option to the injected `Serilog.ILogger`.
