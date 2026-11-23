---
domain: Logging
packages: Logging, Logging.Redaction, Logging.Redaction.AspNetCore
---

# Logging

> Host logging on Microsoft.Extensions.Logging with Headless defaults and enrichment, and classification-based redaction of logs, enrichment, HTTP logging, OpenTelemetry spans, and propagated baggage.

## Orientation

Three packages:

- **`Headless.Logging`** gives a host its logging profile in one call. `builder.AddHeadlessLogging()` adds an environment-aware console, Microsoft's log enrichment with the process, application, and host facts on every entry, trace correlation, levels from `Logging:LogLevel` that reload at run time, and the Headless redaction. `HeadlessBootstrapLogger.Create()` logs the crash that happens before the host exists.
- **`Headless.Logging.Redaction`** holds the data-classification taxonomy (`[SecretData]`, `[PrivateData]`, `[IdentifierData]`) and `AddHeadlessRedaction`, which decides what any log entry may carry, whichever logger factory and provider the host uses.
- **`Headless.Logging.Redaction.AspNetCore`** applies the same rules to ASP.NET Core HTTP logging and to OpenTelemetry span tags.

The default path for an API host:

```csharp
// Program.cs
using var bootstrap = HeadlessBootstrapLogger.Create(); // logs a crash before the host exists

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddHeadless(); // AddHeadlessLogging, request enrichment, redaction, OpenTelemetry
    await builder.Build().RunAsync();
}
catch (Exception exception)
{
    bootstrap.CreateLogger("Startup").LogCritical(exception, "The host terminated unexpectedly");
    throw;
}
```

`AddHeadless()` (`Headless.Api.ServiceDefaults`) calls `AddHeadlessLogging()`, adds the request fields to every entry written during a request, and calls `AddHeadlessRedaction()` and `AddHeadlessRequestLogRedaction()`. It also adds both span processors. A worker host calls `builder.AddHeadlessLogging()` itself. Call `AddHeadlessRedaction(setup => ...)` yourself only to add an HMAC key, text patterns, or name classifications.

## Agent Rules

- Give a worker host its profile with `builder.AddHeadlessLogging()`. On an API host `AddHeadless()` already calls it: do not call it again. Pass options through `HeadlessServiceDefaultsOptions.Logging` instead.
- Do not add `AddJsonConsole()` or `AddSimpleConsole()` yourself. `AddHeadlessLogging` configures the console the default host already registered. Pin the format with `HeadlessLoggingOptions.ConsoleFormat`, or `HeadlessServiceDefaultsOptions.Logging` under `AddHeadless()`.
- Set log levels in the `Logging:LogLevel` configuration section. They reload at run time. `Serilog:MinimumLevel` is not read.
- Add a field to every entry with your own `ILogEnricher` (`services.AddLogEnricher<T>()`), or `IStaticLogEnricher` (`services.AddStaticLogEnricher<T>()`) for a value fixed per process. Do not push it with `BeginScope` in middleware. Enricher values pass through `ClassifyProperty` names like any property.
- Wrap `Build()` and `RunAsync()` in a `try` with `HeadlessBootstrapLogger.Create()` in a `using`, so a configuration or DI failure is logged. Dispose it before the process exits: disposing flushes the console.
- To keep personal data and secrets out of logs, mark members with `[SecretData]`, `[PrivateData]`, or `[IdentifierData]`. For a value logged through a plain `{Email}` hole, set by an enricher, or written by a library, use `ClassifyProperty("Email", ...)`.
- Log an object whose members you want to read as a `record`, or capture it with `{@Value}`. Classified members show redacted. An object of any other type that has a classified member prints only its type name, so a `ToString()` override cannot print a member.
- A span tag is redacted only by name: classify the tag key (`ClassifyProperty("enduser.email", ...)`). On a tracer you build yourself, such as a worker's, add `.AddHeadlessLogPathRedaction().AddHeadlessTextRedaction()` before the exporter.
- Register `AddHeadlessRedaction()` with your classified names before anything builds an `HttpClient` handler, and assign a custom propagator before `AddHeadlessRedaction()` (runtime) or before the tracer is built (OpenTelemetry). Otherwise classified baggage keys leave the process.
- To keep a Serilog sink, add it as a provider (`builder.Logging.AddSerilog(logger)` from `Serilog.Extensions.Logging`), never as the logger factory (`UseSerilog`, or `services.AddSerilog` from `Serilog.Extensions.Hosting`). As a provider it receives entries already redacted. As the factory it replaces the redacting one, and the host logs `LogRedactionFactoryReplaced` (Warning). Properties pushed with Serilog's `LogContext.PushProperty` bypass redaction either way.

---

## Headless.Logging

The Headless logging profile for API and worker hosts, on Microsoft.Extensions.Logging.

### Setup

```bash
dotnet add package Headless.Logging
```

```csharp
// Program.cs (worker host)
using var bootstrap = HeadlessBootstrapLogger.Create();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.AddHeadlessLogging();
    builder.Services.AddHeadlessRedaction(setup => setup
        .ClassifyProperty("enduser.email", HeadlessDataClassifications.Private));

    builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());
    builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
        .AddSource("MyApp.*")
        .AddHeadlessLogPathRedaction() // before the exporter
        .AddHeadlessTextRedaction()    // classified and pattern-shaped span tags, and baggage
        .AddOtlpExporter());

    builder.Services.AddHostedService<Worker>();
    await builder.Build().RunAsync();
}
catch (Exception exception)
{
    bootstrap.CreateLogger("Startup").LogCritical(exception, "The host terminated unexpectedly");
    throw;
}
```

**Verify:** run the host outside Development. Each console line is one JSON object with a `Timestamp` ending in `Z` and a `State` that carries `service.name`, `deployment.environment`, `process.pid`, and `MachineName`. An entry written inside a span carries `TraceId` and `SpanId` in `Scopes`. A span tagged `enduser.email` exports the tag with an empty value.

### Configuration

`builder.AddHeadlessLogging(options?)` on any `IHostApplicationBuilder`. `HeadlessLoggingOptions`:

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `ConsoleFormat` | `HeadlessConsoleFormat?` | `null` | `null` writes `Text` in Development and `Json` everywhere else. |
| `MaxHeaderLength` | `int` | `512` | The length request headers are cut to when an API host adds them to entries. Zero or negative throws `ArgumentOutOfRangeException`. |

`HeadlessConsoleFormat` is `Text` (Microsoft's simple console, one line per entry) or `Json` (Microsoft's JSON console). Both write UTC timestamps (`yyyy-MM-ddTHH:mm:ss.fffZ`) and scopes.

Levels come from configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

`HeadlessBootstrapLogger.Create(options?)` returns an `ILoggerFactory` the caller owns. It writes the same console format, chosen from `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT` (unset means Production), logs `Information` and above, and reads no configuration. An environment given only on the command line (`--environment`) does not reach it: pin `ConsoleFormat` in that case.

### Design and runtime behavior

Fields on every entry:

| Field | Source |
| --- | --- |
| `service.name` | `IHostEnvironment.ApplicationName`, unless the app sets Microsoft's `ApplicationMetadata.ApplicationName` |
| `deployment.environment` | `IHostEnvironment.EnvironmentName`, unless the app sets `ApplicationMetadata.EnvironmentName` |
| `service.version` | The entry assembly's informational version, unless the app sets `ApplicationMetadata.BuildVersion` |
| `process.pid`, `thread.id` | The current process and thread |
| `MachineName`, `ProcessName`, `CommitHash` | The host, and the entry assembly's commit |
| `TraceId`, `SpanId` | The current `Activity`, as a scope |

An API host under `AddHeadless()` also gets `UserId`, `AccountId`, `CorrelationId`, `ClientIp`, `UserAgent`, `XClientVersion`, and `ApiVersion` on every entry written during a request. See [api.md](api.md#headlessapiservicedefaults).

Field names an earlier Serilog profile wrote, and where each value is now:

| Serilog property | Field |
| --- | --- |
| `Application` | `service.name` |
| `Version` | `service.version` |
| `EnvironmentName` | `deployment.environment` |
| `ProcessId` | `process.pid` |
| `ThreadId` | `thread.id` |
| `UtcTimestamp` | the console's `Timestamp` |
| `@tr`, `@sp` | `TraceId`, `SpanId` in `Scopes` |
| `MachineName`, `ProcessName`, `CommitHash`, and the request fields | unchanged |

Behavior:

- **Enrichment runs after redaction, and is redacted anyway.** Every enricher, Microsoft's, the Headless ones, and yours, is wrapped so its values pass through the `ClassifyProperty` names, whenever it was registered. A classified field keeps its name with the redactor's output.
- **Microsoft's `EnableEnrichment()` and `EnableRedaction()` stay safe to call later.** The redacting factory still wraps the factory they register.
- **No log files.** The console and OpenTelemetry are the outputs. Ship container output to a log store, or export logs through OTLP.
- **One console provider.** `AddHeadlessLogging` sets the format of the console the default host registered. `ClearProviders()` before it is not needed, and after it removes the console and OpenTelemetry providers too.

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

**Verify:** the host starts without a `LogRedactionFactoryReplaced` warning, and logging a `Customer` with `{@Customer}` prints `Customer { Email = …, AccountNumber = …, ApiKey = , Tier = gold }` with `Email` and `AccountNumber` as pseudonyms (or empty without a key). In a test, resolve `LogTextMasker` and assert `Mask("id 29001011234567")` returns the masked text.

### Configuration

`services.AddHeadlessRedaction(setup?)` is the one entry point. It registers Microsoft's redaction (`IRedactorProvider`) with all three classifications erased, and wraps the registered `ILoggerFactory`, so providers receive every entry already redacted. Call it from the app and from libraries, in any order: each call adds only what its own `setup` asks for, and a factory is never wrapped twice. `AddHeadless()` and `AddHeadlessLogging()` call it.

`HeadlessRedactionSetupBuilder` (the `setup` argument):

| Member | Effect |
| --- | --- |
| `UseHmac(hmacSection)`, `UseHmac(configureHmac)` | `Private` and `Identifier` become HMAC pseudonyms keyed by Microsoft's `HmacRedactorOptions`: a Base64 `Key` of at least 44 characters, and a `KeyId`. `Secret` stays erased. |
| `MaskText(pattern, replacement = "***", scope?)` | Masks value shapes no classification reaches, such as a 14-digit national ID or a card number. The `string` overload compiles the pattern culture-invariant with a 100 ms match timeout. The `Regex` overload uses your options. `MaskText(ILogTextRedactor, scope?)` takes your own redactor. |
| `scope` (`LogTextRedactionScope`) | Limits a pattern to `Categories` (prefixes, ordinal, ignoring case, as logging filters match them) and a `MinimumLevel` (default `Trace`). |
| `ConfigureRedaction(configure)` | Microsoft's `IRedactionBuilder`, applied after the defaults. Map your own classifications or replace a default. |
| `ClassifyProperty(name, classification)` | Redacts a value by its name, ignoring case, as if it carried `classification`: a structured log property in every entry and scope, an enrichment value, and a span tag (on a tracer with `AddHeadlessTextRedaction`), element by element for a collection, with the same pseudonym. The rendered message carries the redacted value. The name is also dropped from outgoing baggage. |

Other public types:

- `HeadlessDataClassifications`: the taxonomy (`TaxonomyName` = `"Headless"`). `Secret` is credentials and keys. `Private` is personal data about a person. `Identifier` is IDs operators correlate on that must not appear raw.
- `[SecretData]`, `[PrivateData]`, `[IdentifierData]`: `DataClassificationAttribute`s for properties, fields, parameters, and types.
- `PrefixMaskRedactor`: keeps the first characters (8 by default) and appends `***`, for device tokens and similar. Map it to a classification with `ConfigureRedaction`.
- `ILogTextRedactor`: a text redactor of your own. A library can register one directly as an `ILogTextRedactor` singleton, without the builder; it then scans every entry.
- `LogTextMasker`: applies every registered text redactor outside the logger factory. `Mask(string)`, `MaskValue(object?)`, and `Mask(Exception)` each return the argument unchanged when nothing matched. `AddHeadlessRedaction` also registers it as `ILogTextMasker` (package `Headless.Hosting`), which a library resolves without referencing this package. `IsEnabled` is `true` when any text redactor is registered. Resolve it to mask a channel the framework does not write, such as an error tracker's before-send hook (Sentry's `SetBeforeSend`, an Application Insights telemetry processor). Every redactor applies through it, whatever its scope, because these channels have no category or level.
- `ILogPathRedactor`: declares request paths that carry a credential (`bool TryRedact(string path, out string? redacted)`). Register implementations as `ILogPathRedactor` singletons. `UseSignedUrlEndpoint` registers one for signed blob URLs. `ILogPathRedactor.TryRedactAny(redactors, path, out redacted)` and `ILogPathRedactor.RedactAny(redactors, path)` apply the first matching redactor, for paths your own code logs.
- `RedactedException`: see exception masking below.

### Design and runtime behavior

Classification:

- **Secrets are always erased.** HMAC applies only to private data and identifiers. Every `AddHeadlessRedaction` call maps `Secret` to erasing again.
- **Without a key, everything classified is erased.** An erased member keeps its name with an empty value. Add a key when operators need "same person" or "same ID" across events.
- **A library's bare call never undoes the app's key.** Only `UseHmac` maps `Private` and `Identifier`, so `AddHeadlessRedaction()` from a library, before or after the app's keyed call, leaves the pseudonyms in place.
- **Unmapped classifications fail closed.** A classification with no redactor, or a combination such as `[PrivateData][IdentifierData]` on one member, uses Microsoft's erasing fallback.
- **A pseudonym is still personal data** when it identifies a person. It is not anonymization. Rotating the key breaks correlation across the rotation.
- **A name-classified property is redacted like an attribute-classified one:** same redactor, same pseudonym, same element-by-element collections. A value printed with a format that changes its text (`{Amount:N2}`) is redacted in the property but can keep its formatted text in the message: classify it, or add a `MaskText` pattern. Members of a logged object are not matched by name: mark them with an attribute.
- **Attribute names avoid ASP.NET Core Identity's `[PersonalData]`.** `Microsoft.Extensions.Compliance.Testing` declares its own `PrivateDataAttribute`, so a test file that imports it and `Headless.Logging` must qualify one.

Logged objects:

- **An object with a classified member is replaced before any provider sees it**, whether it is logged as `{@Value}`, `{Value}`, a scope value, or a `[LoggerMessage]` parameter declared as a base type, and when the classified member sits in a nested object, a collection, or a member declared as `object` or an interface.
- **A record, or a value captured as `{@Value}`, prints its members:** `Customer { Email = , Tier = gold }`. Any other type prints its type name, which is what the default `ToString()` prints.
- **A type classified as a whole** (`[PrivateData]` on the class) prints as one redacted value.
- **An object with no classified member reaches providers as the same instance.** Its shape is cached per type, so it costs one lookup after the first time. A throwing getter prints a message naming the exception type and does not fail the log call.
- **`[LoggerMessage]` rejects a parameter whose declared type has a classified member** (`LOGGEN035`). Pass the classified members as their own classified parameters, or use `[LogProperties]`.

Logger factory:

- **A later logger factory replaces the wrapper.** After any factory registered after `AddHeadlessRedaction`, such as a third-party logging library's, call `AddHeadlessRedaction()` again. Until then the host logs `LogRedactionFactoryReplaced` (Warning) at startup.
- **Raw classified values never reach a provider.** A level no provider listens to costs nothing.
- **Only ASP.NET Core's hosting category is rewritten for the request.** Its request scope (`RequestPath`) and request lines (`Path`) get a declared credential path redacted. Each query value in the request lines (`QueryString`) becomes `Redacted`, keeping the parameter names: `?email=Redacted&page=Redacted`. A logger with nothing to redact is returned unwrapped.

Text masking:

- **Text redaction is a net, not the control.** It runs on every string of every entry in its scope, and a pattern can match an unrelated value of the same shape. Classify first. Add a pattern only for shapes that reach logs as free text, such as an interpolated message, and scope it to the categories and levels where they appear.
- **A timed-out match replaces the whole text**, so a slow match never lets a value through. Text masking runs after classification and path redaction.
- **What is scanned.** Each entry's rendered text, property values, scope values, and exception messages and `Exception.Data` values. Integers are scanned, because a 14-digit ID held as a `long` renders as that ID. Booleans, GUIDs, enums, and date and time values are not. A non-string value that matches is replaced by its masked text, so a template-rendering provider prints the masked form. Members of a logged object are not scanned: classify them. An entry with nothing to mask reaches providers unchanged.

Exception masking:

- **The logged exception is never changed.** A text redactor in scope scans the `Message` and each `Data` value of the logged exception and of each inner exception, every `AggregateException` member included. A `Data` value is scanned the way a property value is, so an ID held as a `long` is found. With no match, providers receive the same object. With a match, they receive a copy of the same type with masked messages, masked inner exceptions, and, when a `Data` value matched, a new `Data` dictionary with the same keys and masked values. The copy keeps the original's stack frames, `TargetSite`, `HResult`, and `GetType()`, so error trackers group and symbolicate it as before. The original's `Data` is not changed.
- **`RedactedException` is the fallback.** When a matching exception cannot be masked in a copy of its own type (a subclass that builds `Message` from its own fields, or one that overrides `Data` and has a matching value, for example), providers receive a `RedactedException`. Its `ToString()` is the original's full text, masked. `Message` and `StackTrace` are the original's, masked. `OriginalTypeName` names the original type. `InnerException` and `InnerExceptions` hold the original's inner exceptions. `Data` holds the original's entries with masked values. `TargetSite` is `null`. A provider that reports `GetType()` reports `RedactedException`: read `OriginalTypeName`.
- **Not masked:** text only in a stack trace or a `ToString()` override, and exception text returned in an HTTP response. Classification does not apply to exception text.
- **Span exception events.** An event another library or your code records on a span (`Activity.AddException`, an instrumentation's `RecordException`) cannot be rewritten afterwards. Record it with `masker.RecordException(activity, exception)` on `ILogTextMasker` instead of `activity.AddException(masker.Mask(exception))`. It masks the event's `exception.message`, and its `exception.stacktrace` from the exception's full `ToString()` text, so a value only in a stack frame or a `ToString()` override is masked too. On a span that is not recording (`Activity.IsAllDataRequested` is `false`), it records nothing and masks nothing. If you implement `ILogTextMasker` yourself, return from `Mask(Exception)` an exception whose `Message` is masked: `RecordException` records that message as `exception.message`. The framework records its own span exception events this way: the request span and every messaging span.
- **Every channel masks the same way.** Logs, the spans `AddHeadless` records, and span tags use the same redactors and the same exception copy, so a value is masked identically wherever it appears and nothing else in the record changes.

Baggage:

- **A classified name never leaves the process as baggage.** Every name classified with `ClassifyProperty`, by any host in the process, is dropped from the baggage the runtime propagator (`DistributedContextPropagator.Current`, which `HttpClient` uses) and OpenTelemetry's default propagator inject. Trace context and the other entries still travel. Incoming baggage is not changed.
- **The runtime propagator is filtered from the `AddHeadlessRedaction()` call on.** An `HttpClient` handler copies the propagator when it is built, so a handler built earlier keeps the unfiltered one.
- **OpenTelemetry's default propagator is filtered when a tracer with `AddHeadlessTextRedaction()` is built.** A propagator you assign afterwards (`Sdk.SetDefaultTextMapPropagator`) is not filtered, and the host logs `BaggageFilterReplaced` (Warning) on its next start. A propagator assigned before keeps its own fields, and is filtered.
- **A propagator that reads `Baggage.Current` instead of the context it is given is not filtered.**

### Channel coverage

| Channel | What redacts it |
| --- | --- |
| A logged object (`{@Value}`, `{Value}`, a scope value, a `[LoggerMessage]` object) with a classified member | `AddHeadlessRedaction()` under any logger factory |
| A classified `[LoggerMessage]` or `[LogProperties]` parameter | `AddHeadlessRedaction()` under any logger factory |
| ASP.NET Core's request scope and request lines | `AddHeadlessRedaction()`: query values always, a path through an `ILogPathRedactor` (the signed-URL endpoint registers one) |
| A plain `{Value}` hole, an interpolated message | `ClassifyProperty(name, classification)` for a known property name, the value's `ToString()`, and `MaskText` for known shapes |
| Enrichment values (Microsoft's, the Headless ones, yours) | `ClassifyProperty(name, classification)` |
| HTTP logging (`UseHttpLogging`) | `AddHeadlessHttpLoggingRedaction()` (credential headers erased, route parameters you classify), and `AddHeadlessRequestLogRedaction()` for a declared credential path |
| Span path tags (`url.path`, `http.target`, `url.full`) | `AddHeadlessRequestLogRedaction()` for the request span; `AddHeadlessLogPathRedaction()` on the tracer provider for every span, outgoing calls included (`AddHeadless()` adds it) |
| Any other span tag | `AddHeadlessTextRedaction()` on the tracer provider: a classified tag name, and `MaskText` patterns on string tags (`AddHeadless()` adds it). ASP.NET Core instrumentation writes the `User-Agent` as `user_agent.original`: classify that name to redact it |
| A span's status description | `AddHeadlessTextRedaction()` with a text redactor |
| Baggage on outgoing calls and messages | `ClassifyProperty(name, classification)`, through both propagators (see Baggage above) |
| Outgoing `HttpClient` logging | Microsoft's `AddExtendedHttpClientLogging` (package `Microsoft.Extensions.Http.Diagnostics`), with the same classifications. It replaces the default `HttpClient` logger rather than adding beside it. |
| Exported OTLP logs and spans | everything above, plus the [collector backstop](#collector-backstop) |
| Exception messages, inner-exception messages, and `Exception.Data` values logged through `ILogger` | `MaskText` and any `ILogTextRedactor`, through a masked copy of the same type, or a `RedactedException` |
| The request span's exception event | `AddHeadless()` records it through `ILogTextMasker.RecordException` when `OpenTelemetry.RecordException` is on; events other code records are not rewritten |
| Messaging span exception events (publish, persist, consume, subscriber invoke) | `AddHeadlessRedaction()` with a text redactor; Messaging resolves `ILogTextMasker` when it is registered and records the raw exception otherwise |
| A Serilog sink attached as a provider | everything above, because it receives entries already redacted; properties from Serilog's `LogContext` are not |

### Collector backstop

Redact in the app first: a value that reaches the collector has already crossed the network, and console logs never reach it. For exported telemetry, add the OpenTelemetry Collector's `redaction` processor as a second line:

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
builder.Services.AddHeadlessRedaction(setup => setup
    .UseHmac(builder.Configuration.GetSection("Redaction:Hmac"))
    .ClassifyProperty("enduser.email", HeadlessDataClassifications.Private));
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

**Verify:** request a route with a `userId` and an `Authorization` header. The HTTP logging record shows `Authorization` with an empty value and `userId` as a pseudonym. A request to a signed URL shows the redacted path in the request log's `Path` and in the exported span's `url.path`. A span tagged `enduser.email` exports the tag with an empty value.

### Configuration

- `services.AddHeadlessHttpLoggingRedaction(configure?)` adds Microsoft's classification-based HTTP logging redaction plus `AddHeadlessRequestLogRedaction()`. It registers HTTP logging itself; the pipeline still needs `app.UseHttpLogging()`. A second call adds only its `configure`, after the earlier ones. Microsoft marks its own API experimental (`EXTEXP0013`); this method raises no diagnostic for callers.
- `HttpLoggingRedactionOptions`: `RequestHeaders`, `ResponseHeaders`, and `RouteParameters` (name to `DataClassification`), `ExcludePathStartsWith`, and `IncludeUnmatchedRoutes`. `RequestHeaders` starts with `Authorization`, `Proxy-Authorization`, and `Cookie`, and `ResponseHeaders` with `Set-Cookie`, all `HeadlessDataClassifications.Secret` (logged by name, value erased).
- `services.AddHeadlessRequestLogRedaction()` applies every registered `ILogPathRedactor` wherever ASP.NET Core writes the request path: the request scope and request lines, the `Path` field of the HTTP logging record, and the request span's `url.path`, `http.target`, and `url.full` tags. It is idempotent and safe to call from libraries. `AddHeadless()` and `UseSignedUrlEndpoint` call it.
- `tracing.AddHeadlessLogPathRedaction()` rewrites the same three tags on every span when it ends, server and client alike, so an outgoing `HttpClient` call to a credential URL is covered too. A matching value loses its query and fragment, because a credential URL can carry the same secret there.
- `tracing.AddHeadlessTextRedaction()` redacts every span when it ends: each tag whose name is classified with `ClassifyProperty`, whatever its value's type, and the text of every string tag and the status description through the app's text redactors. A numeric tag keeps its type, and only the matching shape changes. It does nothing without a classified name or a text redactor. Building the tracer also filters classified keys out of OpenTelemetry's default propagator. `AddHeadless()` adds it after `AddHeadlessLogPathRedaction()`.

### Design and runtime behavior

- **Add both span processors before any exporter.** Processors run in the order they are added, and an exporter added first exports the raw value.
- **The request span is redacted when the first middleware runs.** Exporters receive the redacted value. A sampler, or a processor's `OnStart`, runs earlier and still sees the raw path.
- **Span events are not rewritten.** An event's tags are read-only once recorded. Record span exception events with `ILogTextMasker.RecordException(activity, exception)`, as `AddHeadless()` does for the request span.
- **HTTP logging writes no header you do not list.** A listed header is logged by name with its value through its classification's redactor. An unlisted header is left out entirely.
