---
domain: Utilities
packages: FluentValidation, Generator.Primitives, Generator.Primitives.Abstractions, Hosting, Http.Resilience, NetTopologySuite, PhoneNumbers, Redis, Sitemaps, Slugs
---

# Utilities

> Independent utility packages: validation rules, strongly-typed primitives, host verbs and deploy steps, outbound HTTP resilience, geometry, phone numbers, Redis Lua scripts, sitemaps, and slugs.

## Orientation

Install each package only when the task needs it. They do not depend on each other, except that `Headless.Generator.Primitives` needs `Headless.Generator.Primitives.Abstractions`.

| Task | Package | Entry point |
| --- | --- | --- |
| Validate request models with ready-made rules and stable error codes | `Headless.FluentValidation` | Rules on `IRuleBuilder` (`UniqueElements()`, `Latitude()`, `HttpsOrLoopbackHttpUrl()`, ...), `WithErrorDescriptor`, `ToErrorDescriptors` |
| Declare a strongly-typed ID or value type | `Headless.Generator.Primitives` + `Headless.Generator.Primitives.Abstractions` | A `partial` type that implements `IPrimitive<T>` |
| Run a host with deploy verbs, health checks, seeders, and warm-ups | `Headless.Hosting` | `RunHeadlessAsync(args)`, `AddSeeder<T>()`, `AddWarmup<T>()`, `AddHeadlessHealthCheck`, `IResourceProvisioner` |
| Give an outbound `HttpClient` a retry policy that matches its side effects | `Headless.Http.Resilience` | `AddEffectResilienceHandler(OutboundEffect)` |
| Work with geometry for SQL Server geography | `Headless.NetTopologySuite` | `SanitizeForSqlGeography()`, `AddHeadlessNetTopologySuite()` |
| Format, normalize, and validate phone numbers | `Headless.PhoneNumbers` | `GetInternationalFormat()`, `Normalize()`, `InternationalPhoneNumber()` |
| Run Lua scripts on Redis | `Headless.Redis` | `HeadlessRedisScriptsLoader.EvaluateAsync` |
| Write XML sitemaps | `Headless.Sitemaps` | `WriteToAsync`, `WriteAsync(onShard)` |
| Make URL slugs | `Headless.Slugs` | `Slug.Create(text, options)` |

CAPTCHA verification is its own domain: see [captcha.md](captcha.md).

## Agent Rules

- To declare a strongly-typed primitive, install both `Headless.Generator.Primitives` and `Headless.Generator.Primitives.Abstractions`, and declare a `partial` type directly in a namespace that implements `IPrimitive<T>` with a static `Validate` method. Do not hand-write equality, conversions, or converters.
- Set the `Primitive*` MSBuild properties to `true` for every converter kind you need. Each one defaults to `false`, so a primitive without them serializes to JSON as an empty object and has no EF Core converter.
- Use the `Headless.FluentValidation` rules (`UniqueElements()`, `Latitude()`, `PageSize()`, `Id()`, ...) instead of writing custom predicates for the same checks. Branch on the error code (`FluentValidatorErrorCodes`), never on the localized message.
- Pass `WithErrorDescriptor` a factory, such as a describer method group, not a descriptor captured in the validator's constructor. A captured description answers every request in the language of the first one.
- Use `Headless.PhoneNumbers` for phone validation and formatting. `PhoneNumber.ToString()` returns the raw `+{CountryCode} {Number}`. Call `GetInternationalFormat()` for display.
- End `Program.cs` with `return await app.RunHeadlessAsync(args);` instead of `app.Run()`. It adds the `describe`, `check`, `resources`, `seed`, and `deploy` verbs that deployments and verification steps run.
- Run `dotnet <App>.dll deploy` (or `resources provision`, then `seed`) on every release. A starting host never creates tables, queues, or containers, and never runs seeders.
- Use `AddDeployStepsOnStartup()` only in development and test hosts. Do not use it in production.
- Put a misconfiguration that must stop the host in an `IHeadlessStartupValidator`, not in a hosted service. Keep it to in-memory checks.
- Put first-request preparation (cache loads, script loads) in an `IWarmup`, not in a startup validator or a hosted service's `StartAsync`. Make `WarmUpAsync` safe to repeat: a failed warm-up retries.
- Register options with `Configure<TOptions, TValidator>(...)` or `AddOptions<TOptions, TValidator>()` from `Headless.Hosting`. Both validate with FluentValidation when the host starts.
- Use `HeadlessRedisScriptsLoader` only for Lua scripts, not for general Redis calls. Reuse each script definition's single `Instance`.
- Use `GeoConstants.GoogleMapsSrid` (4326) as the SRID for geography data. Call `SanitizeForSqlGeography()` before you save a polygon to a SQL Server `geography` column.
- For a sitemap of more than 50,000 URLs, call `WriteAsync(onShard)` and write each shard inside the callback. A shard is invalid after the callback returns.

---

## Headless.FluentValidation

Extra FluentValidation rules with stable `g:` error codes, `ErrorDescriptor` integration, and English, formal Arabic, and Egyptian Arabic messages.

### Setup

```bash
dotnet add package Headless.FluentValidation
```

The rules and extension methods are in the `FluentValidation` namespace, so `using FluentValidation;` is enough.

```csharp
using FluentValidation;

public sealed class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Roles).MinimumElements(1).UniqueElements();
        RuleFor(x => x.ApiEndpoint).HttpsOrLoopbackHttpUrl(); // HTTPS externally; HTTP only on loopback; no userinfo
    }
}
```

**Verify:** no startup signal. In a unit test, validate a model with duplicate roles and assert that `result.Errors` contains an error whose `ErrorCode` is `FluentValidatorErrorCodes.UniqueElements` (`g:unique_elements`, namespace `Headless.FluentValidation.Resources`).

### Validators

| Category | Rules |
| --- | --- |
| National ID | `EgyptianNationalId` (with checksum) |
| Collection | `MaximumElements`, `MinimumElements`, `UniqueElements` |
| Geo | `Latitude`, `Longitude` |
| Pagination | `PageIndex`, `PageSize`, `SearchQuery` |
| URL | `Url`, `HttpUrl`, `HttpsOnlyUrl`, `HttpsOrLoopbackHttpUrl`, `FileUrl`, `FtpUrl`, `MailtoUrl`, `CorsOrigin` |
| Network | `Ipv4`, `Ipv6`, `IpAddress` |
| String | `OnlyIntegers`, `OnlyDecimals`, `Slug`, `Username`, `ZipCode`, `HexColor`, `Base64`, `Trimmed`, `Culture` |
| Date and time | `InThePast`, `InTheFuture`, `NotInThePast`, `NotInTheFuture`, `MinimumAge`, for `DateTime`, `DateTimeOffset`, `DateOnly`, and their nullable forms. "Now" comes from an injectable `TimeProvider`. |
| Enum | `EnumName(typeof(TEnum))` |
| Safe text | `NoScripts` (rejects markup) |
| Storage identifier | `IsValidIdentifierFor(StorageProvider)` for a known dialect, `IsValidCrossProviderIdentifier` when the dialect is known only at run time |
| ID | `Id` (non-empty `Guid`, positive `int` or `long`) |

- The phone rules ship in [`Headless.PhoneNumbers`](#headlessphonenumbers). Their error codes (`g:invalid_phone_number`, `g:invalid_country_code`, `g:local_phone_number`, `g:invalid_mobile_number`) are in `FluentValidatorErrorCodes` and `FluentValidatorErrorDescriber.PhoneNumbers`.
- The `IFormFile` rules (`FileNotEmpty`, `GreaterThanOrEqualTo`, `LessThanOrEqualTo`, `ContentTypes`, `HaveSignatures`) ship in `Headless.Api.FluentValidation`. Their codes and messages live here (`FluentValidatorErrorCodes.File*`, `FluentValidatorErrorDescriber.Files`). A content-type failure reports `g:file_unexpected_content_type` with the accepted types in `{ContentTypes}`. A signature failure reports `g:file_unexpected_signature`. A null file passes every file rule. The size and content-type rules implement `IFileSizeValidator` and `IFileContentTypeValidator`, which Headless.OpenApi reads to document the limits (see [api.md](api.md#headlessapifluentvalidation)).
- Property paths in the results are camelCase.

### Error descriptors

```csharp
RuleFor(x => x.Total)
    .GreaterThan(0)
    .WithErrorDescriptor(OrderErrorDescriber.TotalInvalid); // static ErrorDescriptor TotalInvalid() => new("order:total_invalid", OrderMessages.total_invalid);
```

- `WithErrorDescriptor` reads the code and severity once, when the rule is built. It calls the factory again for every failure, so a description read from a localized resource follows the culture of the request. For a fixed text, pass a lambda such as `() => new ErrorDescriptor("order:total_invalid", "Total must be positive.")`.
- `ErrorDescriptor` defaults to error severity. Pass `ValidationSeverity.Warning` or `ValidationSeverity.Information` for a non-error rule. `WithErrorDescriptor` and `ToErrorDescriptors` both keep the severity.

To turn a result into structured API errors, call `ToErrorDescriptors()`:

```csharp
var errors = result.Errors.ToErrorDescriptors(); // IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>
```

### Regional Arabic

Every package that ships Arabic messages ships them twice: formal Arabic under `ar` and Egyptian Arabic under `ar-EG`. .NET resource fallback runs from the region to `ar`, then to English. So an `ar-EG` request reads Egyptian Arabic, and `ar` or any other `ar-*` region (`ar-SA`, `ar-AE`) reads formal Arabic.

| Culture | Register | Example (`g:rate_limit_exceeded`) |
| --- | --- | --- |
| `ar` | Formal Arabic, in the plain register regional products use | `عدد الطلبات كبير جداً. حاول مرة أخرى بعد قليل.` |
| `ar-EG` | Professional Egyptian Arabic | `عدد الطلبات كتير. جرّب تاني بعد شوية.` |

Formal Arabic uses vocabulary every region reads (رقم الهاتف، رقم الهوية الوطنية، البريد الإلكتروني، كلمة المرور). Egypt-only services, such as the Paymob wallet messages, keep Egyptian terms (الرقم القومي) under `ar` too.

FluentValidation's built-in messages (`NotEmpty`, `EmailAddress`, `Length`, and the rest) come from its `LanguageManager`. `AddHeadless()` from `Headless.Api.ServiceDefaults` replaces the library's `ar` wording with the Headless formal Arabic and adds `ar-EG` wording. An application without `Headless.Api.ServiceDefaults` calls the two methods once at startup:

```csharp
using FluentValidation;
using FluentValidation.Resources;

if (ValidatorOptions.Global.LanguageManager is LanguageManager languageManager)
{
    languageManager.AddHeadlessArabicTranslations();
    languageManager.AddHeadlessEgyptianArabicTranslations();
}
```

- The Arabic built-in messages leave out `{PropertyName}`, because it resolves to the property's English name. Show the message next to its field. English keeps the library's text, property name included.
- Each call overwrites the text for its culture and key. Add your own `ar` or `ar-EG` translations after `AddHeadless()` or after the calls above. A translation added before is replaced.
- An application that assigns its own `ILanguageManager` that does not derive from `LanguageManager` gets no Headless built-in translations.
- List `ar-EG` in the request localization's `SupportedUICultures`. With only `ar` listed, an `ar-EG` request resolves to `ar` and reads formal Arabic.
- If the application sets `SatelliteResourceLanguages`, list `ar-EG` there too. Otherwise the build drops the `ar-EG` satellite assemblies.

---

## Headless.Generator.Primitives

A Roslyn source generator that turns a `partial` type implementing `IPrimitive<T>` into a validated value type with equality, conversions, and opt-in converters.

### Setup

```bash
dotnet add package Headless.Generator.Primitives
dotnet add package Headless.Generator.Primitives.Abstractions
```

Turn on each converter kind you need in the project that declares the primitives:

```xml
<PropertyGroup>
  <PrimitiveJsonConverters>true</PrimitiveJsonConverters>
  <PrimitiveTypeConverters>true</PrimitiveTypeConverters>
  <PrimitiveEntityFrameworkValueConverters>true</PrimitiveEntityFrameworkValueConverters>
</PropertyGroup>
```

```csharp
using System.Text.Json;
using Headless.Generator.Primitives;

[StringLength(1, 50)]
public sealed partial class Email : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        if (!value.Contains('@'))
            return PrimitiveValidationResult.Error("Invalid email format");

        return PrimitiveValidationResult.Ok;
    }
}

var email = new Email("user@example.com"); // throws InvalidPrimitiveValueException when Validate fails
string value = email; // implicit conversion to the underlying value
var json = JsonSerializer.Serialize(email); // "user@example.com"
```

To map every primitive of the assembly in EF Core, call the generated helper from `ConfigureConventions`. It lives in `<AssemblyName>.Converters.Extensions`:

```csharp no-compile
using MyApp.Converters.Extensions; // the declaring assembly's name

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.AddPrimitivePropertyConversions();
}
```

To map one property, use the generated converter instead: `modelBuilder.Entity<User>().Property(u => u.Email).HasConversion<EmailValueConverter>();`.

**Verify:** the build reports no `HF1xxx` diagnostic. In a unit test, assert that `new Email("bad")` throws `InvalidPrimitiveValueException`, and that `JsonSerializer.Serialize(email)` returns the underlying value as a JSON string, not `{}`.

### Configuration

Set these MSBuild properties in the project that declares the primitives. Each defaults to `false`.

| Property | Generates |
| --- | --- |
| `PrimitiveJsonConverters` | A System.Text.Json converter that reads and writes the underlying value |
| `PrimitiveTypeConverters` | A `TypeConverter`, for model binding from route and query values |
| `PrimitiveEntityFrameworkValueConverters` | `{Name}ValueConverter` and the assembly's `AddPrimitivePropertyConversions()` helper |
| `PrimitiveDapperConverters` | A Dapper type handler per primitive and the assembly's `AddPrimitivesDapperTypeHandlers()` helper |
| `PrimitiveXmlConverters` | XML serialization support |
| `PrimitiveMessagePackFormatters` | `{Name}MessagePackFormatter` and a `[MessagePackFormatter]` attribute on the primitive. The project must reference `MessagePack`. |

### Design and runtime behavior

- The generator handles only a non-generic type declared directly in a namespace (the global namespace included). A nested or generic primitive gets nothing generated (`HF1004`, `HF1005`).
- Converters go in `<Namespace>.Converters` beside the primitive, or `Converters` for a primitive in the global namespace. The EF Core and Dapper registration helpers go in `<AssemblyName>.Converters.Extensions`, with the assembly name turned into a valid namespace (`My-App` gives `My_App.Converters.Extensions`). Two primitives may share a simple name in different namespaces.
- Generated members include the validating constructor, `TryCreate(...)` (which does not throw), `IEquatable<T>`, `IComparable<T>`, and the implicit conversion to the underlying type.
- A primitive may wrap another primitive (`Price : IPrimitive<Amount>` where `Amount : IPrimitive<decimal>`), to any depth. It stores and serializes the innermost value: JSON, TypeConverter, XML, EF Core (`ValueConverter<Price, decimal>`), Dapper, and MessagePack read and write a `decimal`, and `GetUnderlyingPrimitiveType()` returns `typeof(decimal)`. `Validate`, `TryCreate`, `Parse`, and `GetUnderlyingPrimitiveValue()` take or return the wrapped `Amount`. Every value is validated as `Amount`, then as `Price`, so a value either one rejects fails the read. Implicit conversions go both ways to the innermost type, and from `Amount` to `Price`.
- Reading the value of a `default` struct primitive throws `InvalidPrimitiveValueException` ("The domain value has not been initialized").
- Declaring your own parameterless or value constructor conflicts with the generated one and fails with `CS0111`.
- A primitive implements `IConvertible` only when its underlying type does, so `Guid`, `DateTimeOffset`, and `TimeSpan` primitives do not.
- The Dapper handler binds the underlying value. Its `Parse` accepts a wider integer or floating-point type that a provider returns (for example `short` for a `byte` primitive on PostgreSQL) when the value fits, and throws `InvalidCastException` otherwise.
- The MessagePack formatter writes the underlying value and reads it back through the validating constructor, so an invalid payload fails the read. A `class` primitive writes and reads nil as `null`. MessagePack's standard and contractless resolvers honor `[MessagePackFormatter]`, so `MessagePackBinarySerializer` picks the formatter up with no resolver to compose. With the property off, a primitive serializes as an empty map and reads back uninitialized.
- No OpenAPI mapping is generated. `Headless.OpenApi` documents every `IPrimitive<T>` as its underlying JSON type (the innermost type for a primitive that wraps another), with a string primitive's `[StringLength]` bounds as `minLength` and `maxLength`.
- The generator adds no runtime dependency.

### Diagnostics

Each rule is reported on the primitive's type name, or on the offending attribute for `HF1012` and `HF1013`. `#pragma warning disable` and `.editorconfig` severity settings apply to them.

| ID | Severity | Meaning | Fix |
| --- | --- | --- | --- |
| <a id="hf1000"></a>`HF1000` | Error | The generator threw an exception. The message carries it. | Report it as a generator bug with the message. |
| <a id="hf1001"></a>`HF1001` | Error | The primitive wraps a type the generator does not support, such as a custom class or a nullable value type. Nothing is generated for it. | Wrap a string, `Guid`, `bool`, `char`, numeric, or date and time type, or another primitive. |
| <a id="hf1002"></a>`HF1002` | Error | The primitive is not declared `partial`. | Add `partial` to the declaration. |
| <a id="hf1004"></a>`HF1004` | Error | The primitive is declared inside another type. Nothing is generated for it. | Move the declaration directly into a namespace. |
| <a id="hf1005"></a>`HF1005` | Error | The primitive declares type parameters. Nothing is generated for it. | Remove the type parameters, or declare one primitive per closed type. |
| <a id="hf1012"></a>`HF1012` | Error | `[SerializationFormat]` is on a primitive that does not wrap `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan`. The format is ignored. | Remove the attribute, or wrap a date or time type. |
| <a id="hf1013"></a>`HF1013` | Error | `[SupportedOperations]` is on a primitive that does not wrap a numeric type. The attribute is ignored. | Remove the attribute. |
| <a id="hf1015"></a>`HF1015` | Warning | A class primitive wraps a value type. | Declare the primitive as a `struct`. |
| <a id="hf1016"></a>`HF1016` | Warning | A struct primitive wraps a reference type. | Declare the primitive as a `class`. |

---

## Headless.Generator.Primitives.Abstractions

The contracts and attributes the primitives generator reads: `IPrimitive<T>`, `PrimitiveValidationResult`, `InvalidPrimitiveValueException`, and the configuration attributes.

### Setup

Install it with `Headless.Generator.Primitives` (see [its setup](#headlessgeneratorprimitives)). The types are in the `Headless.Generator.Primitives` namespace.

| Attribute | Effect |
| --- | --- |
| `StringLengthAttribute` | Minimum and maximum length of a string primitive. A value out of range throws `InvalidPrimitiveValueException`. |
| `SupportedOperationsAttribute` | Turns on arithmetic operators for a numeric primitive (`Addition`, `Subtraction`, ...) |
| `SerializationFormatAttribute` | The format string a date or time primitive serializes with |
| `UnderlyingPrimitiveTypeAttribute` | Names the underlying type explicitly |

```csharp
using Headless.Generator.Primitives;

[SupportedOperations(Addition = true, Subtraction = true)]
public readonly partial struct Quantity : IPrimitive<int>
{
    public static PrimitiveValidationResult Validate(int value)
    {
        return value >= 0
            ? PrimitiveValidationResult.Ok
            : PrimitiveValidationResult.Error("Quantity cannot be negative");
    }
}
```

**Verify:** the build reports no `HF1xxx` diagnostic, and a unit test asserts that `new Quantity(2) + new Quantity(3)` equals `new Quantity(5)` and that `new Quantity(-1)` throws `InvalidPrimitiveValueException`.

The package also ships `DateOnly` and XML helper extensions. It has no configuration and no runtime registration.

---

## Headless.Hosting

The host's deploy verbs, health checks, startup validation, seeders, resource provisioning, warm-ups, and service-registration helpers.

| Capability | Entry point | Section |
| --- | --- | --- |
| Host verbs for deploy and verification | `RunHeadlessAsync(args)` | [Host verbs](#host-verbs) |
| Environments that share one Redis | `Headless:Prefix`, `ConfigureHeadlessPrefix` | [Isolation prefix](#isolation-prefix) |
| Readiness checks for dependencies | `AddHeadlessHealthCheck`, `ConfigureHeadlessHealthCheck`, `RemoveHealthChecks` | [Health checks](#health-checks) |
| Pre-rollout dependency check | the `check` verb | [Health check command](#health-check-command) |
| What the host registered, without starting it | the `describe` verb | [Host manifest](#host-manifest) |
| Tables, queues, and containers a feature needs | the `resources` verb, `IResourceProvisioner` | [Resource provisioning](#resource-provisioning) |
| Data the code declares | the `seed` verb, `ISeeder` | [Database seeders](#database-seeders) |
| Work that prepares the first requests | `IWarmup` | [Warm-up](#warm-up) |
| Refuse to start on a misconfiguration | `IHeadlessStartupValidator` | [Startup validators](#startup-validators) |
| Fail at start when a provider package is missing | `RequireRegisteredService<T>` | [Required services](#required-services) |
| Validated options | `Configure<TOptions, TValidator>` | [Options with validation](#options-with-validation) |
| Conditional, replacing, and decorating registrations | `AddIf`, `AddOrReplace*`, `TryDecorateWith` | [Service registration helpers](#service-registration-helpers) |
| Sequential GUIDs | `AddHeadlessGuidGenerator()` | Registers keyed `IGuidGenerator` strategies for `SequentialGuidType.Version7` and `SequentialGuidType.SqlServer`, plus an unkeyed default. The contracts are in [extensions.md](extensions.md). |
| Structured logging scopes | `LogState`, `HeadlessLoggerExtensions` | A scope builder with fluent property and tag methods, and level-gated `Log*` overloads that build the scope inline |
| Relational schema steps | `AddHeadlessSchemaContribution` | [SQL](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), including [its observability](sql.md#schema-runner-observability) |

### Setup

```bash
dotnet add package Headless.Hosting
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MyOptions, MyOptionsValidator>(builder.Configuration.GetSection("MySection"));
builder.Services.AddSeeder<UserSeeder>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDeployStepsOnStartup(); // provision resources and seed as the host starts
}

var app = builder.Build();
// ... middleware and endpoints ...

return await app.RunHeadlessAsync(args);
```

**Verify:** `dotnet MyApp.dll describe` exits 0 and prints a JSON manifest, which proves the host builds and passes startup validation without reaching any dependency. `dotnet MyApp.dll check` exits 0 when every dependency the host registered answers.

### Host verbs

`RunHeadlessAsync(args)` (namespace `Microsoft.Extensions.Hosting`) reads the first argument:

| First argument | What it does | Starts the host |
| --- | --- | --- |
| `describe` | Prints the [host manifest](#host-manifest) as JSON | No |
| `check` | Runs the health checks once ([Health check command](#health-check-command)) | No |
| `resources <subcommand>` | Provisions, verifies, purges, deletes, lists, or reports resources ([Resource provisioning](#resource-provisioning)) | No |
| `seed [verify\|list]` | Runs or verifies the seeders ([Database seeders](#database-seeders)) | No |
| `deploy` | Runs `resources provision`, then `seed`, and stops at the first that fails. Takes no arguments; run the two verbs separately for filters or a timeout. | No |
| Anything else | Runs the host as `RunAsync` does | Yes |

- **Every verb validates like a start.** It runs options marked `ValidateOnStart`, then every `IHeadlessStartupValidator`, in the order a real start does. A verb never starts a hosted service, so it opens no broker connection and claims no job. The resource [startup checks](#what-a-starting-host-checks) are skipped, so `resources provision` can create what a start would reject.
- **Run verbs from the host's own entry point.** Only the entry point sees the host's real configuration sources.
- **Console logging goes to standard error** during a verb, for hosts built with `HostApplicationBuilder` or `WebApplicationBuilder`. A host built with the legacy `Host.CreateDefaultBuilder` can still write Microsoft console logger lines to standard output. `describe` then writes a warning to standard error first. Filter the log lines out, or turn console logging off for the command.
- **A normal run exits 1 after a background fault.** When the host runs normally, `RunHeadlessAsync` returns 0, or 1 when a `BackgroundService` had faulted by the time the host began stopping. It then writes `Host stopped: {service} failed: {message}` to standard error. A fault the stop causes, such as a disposed client during shutdown, does not count. Orchestrators see the exit code.
- The host is disposed in every mode.

### Isolation prefix

Give each host its own prefix when several environments share one Redis, such as worktrees, preview stacks, or sandboxes. Set it once, with no code:

```json
{ "Headless": { "Prefix": "wt1" } }
```

You can also set the environment variable `Headless__Prefix=wt1`, or call `services.ConfigureHeadlessPrefix("wt1")`. The code call wins over configuration, whether you call it before or after the features are registered.

- **Shape.** Empty (the default), or 1 to 20 lowercase letters, digits, and `-`, not starting or ending with `-`. Any other value fails the start with `OptionsValidationException` naming `Headless:Prefix`. The allowed characters include no store separator (`:`, `.`, `_`, `/`) and no pattern character, so a prefix cannot reach another environment's names or widen a pattern delete.
- **Position.** The prefix goes in front of each feature's own key prefix, joined with `:`. A lock on `orders` uses `wt1:distributed-lock:orders`, and a cache with `KeyPrefix = "app:"` writes `wt1:app:{key}`. An empty prefix leaves every name unchanged.
- **What it isolates.** Redis and in-memory cache keys and the Redis hybrid invalidation channel ([Caching](caching.md)); mutex, semaphore, and reader-writer lock resources and the Redis lock release channel ([Distributed Locks](distributed-locks.md)); Redis coordination keys ([Coordination](coordination.md)). Features that write through `ICache` or `IDistributedLock` inherit it: idempotency, rate limiting, the cache-factory lock, TUS file locks, tenant-scoped caching, Features, Settings, Permissions, and Jobs.
- **What it does not isolate.** Database-backed locks (PostgreSQL advisory locks and SQL Server app locks, including the static `SqlServerDistributedLock` helpers), relational coordination (separated by cluster name), relational storage tables, and blob containers, `Headless.Blobs.Redis` included. Environments that share one Redis must give that blob provider distinct container names. Use a separate database, schema, or bucket for the rest.
- **One prefix per shared relational store.** Hosts that share one Jobs or other relational store are one environment. Give them the same prefix, or distinct `CoordinationOptions.ClusterName` values. Otherwise a host treats the other prefix's live nodes as dead and reclaims their in-flight rows. For the same reason, adding or changing the prefix on a running fleet that shares such a store needs a full stop and restart, not a rolling deploy.
- **Redis lock keys.** Lock storage hex-encodes the resource inside a hash tag, so a lock key does not start with the prefix and `SCAN wt1:*` does not find it. Cache and coordination keys do start with it.
- **Not a security boundary.** The prefix separates names. Credentials still decide who can reach a store.

A provider package applies the prefix to its own options with `services.ApplyHeadlessPrefix<TOptions>((options, prefix) => ...)`. The options factory applies it after every `Configure`, `PostConfigure`, and validation step, once per named instance, so a later consumer `PostConfigure` cannot remove it.

**Verify:** at start the host logs `Headless names in shared stores start with the prefix '{Prefix}'`, and `dotnet MyApp.dll describe` shows the prefix as `host.prefix`.

### Health checks

A provider package that talks to an external dependency contributes a readiness health check when you register it. Nothing connects at registration: each check probes its dependency only when a health check runs. Messaging and Jobs also contribute checks that read their own host state without I/O. A `Degraded` status in the Probe column is that check's default failure status.

| Registered by | Check name | Dependency tag | Probe |
| --- | --- | --- | --- |
| `AddPostgreSqlSql` | `sql-postgresql` | `database` | `SELECT 1`, or the configured `TestCommand`, on a new connection |
| `AddSqlServerSql` | `sql-sqlserver` | `database` | `SELECT 1`, or the configured `TestCommand`, on a new connection |
| `AddHeadlessDbContext<T>`, `AddHeadlessDbContextPool<T>` | `dbcontext-{T full name}` | `database` | Microsoft's `AddDbContextCheck<T>`: `Database.CanConnectAsync`, or the configured `TestCommand` |
| Caching `UseRedis` / `AddRedisTier` | `cache-redis` (named cache: `cache-redis-{name}`) | `redis` | `PING` to every configured endpoint, and `CLUSTER INFO` reporting `cluster_state:ok` on cluster nodes |
| Distributed locks `UseRedis` | `distributed-locks-redis` | `redis` | The same endpoint probe through the registered `IConnectionMultiplexer` |
| Messaging `UseNats` | `messaging-nats` | `messaging` | NATS `PING` on a pooled connection, with a 5-second default timeout |
| Blobs `UseAzure` | `blobs-azure` (named store: `blobs-azure-{name}`) | `blobs` | Reads one page of the account's container list, or the properties of `AzureStorageOptions.HealthCheckContainerName` when set |
| `AddHeadlessMessaging` | `messaging` | `messaging`, `host-state` | `Unhealthy` before the Messaging host starts and once shutdown begins |
| `AddHeadlessMessaging` | `messaging-broker` | `messaging`, `host-state` | `Unhealthy` while the broker is unreachable, judged from receive and send outcomes ([Messaging](messaging/operations.md#broker-reachability)) |
| `AddHeadlessMessaging` | `messaging-consumers` | `messaging`, `host-state` | `Degraded` while any consumer circuit is `Open`, including an operator pause |
| `AddHeadlessMessaging` with `AddOutbox(name)` | `messaging-outboxes` | `messaging`, `host-state` | `Degraded` while an additional outbox is not initialized |
| Messaging PostgreSQL or SQL Server storage | `messaging-storage-postgresql-default`, `messaging-storage-sqlserver-default`; per additional outbox `messaging-storage-postgresql-outbox-{name}`, `messaging-storage-sqlserver-outbox-{name}` | `messaging`, `database` | `SELECT 1` on a new connection from that store's options |
| `AddHeadlessJobs` with background services | `jobs` | `jobs`, `host-state` | `Unhealthy` until Jobs activation completes and once shutdown begins |
| `AddHeadlessJobs` with background services | `jobs-scheduler` | `jobs`, `host-state` | `Degraded` while the scheduler is stopped by an operator or by manual start mode |
| Jobs `UseEntityFramework` | `jobs-storage-{DbContext full name}` | `jobs`, `database` | `Database.CanConnectAsync` on a context from the store's factory |
| The first `AddWarmup` call | `warmup` | `host-state` | `Unhealthy` until every gating warm-up finishes ([Warm-up](#warm-up)) |

**Tags.** Every contributed check carries `ready` (`HeadlessHealthCheckTags.Ready`), `headless` (`HeadlessHealthCheckTags.Headless`), and its dependency tag. A check that reads a running host's own state also carries `host-state` (`HeadlessHealthCheckTags.HostState`), and the [`check` verb](#health-check-command) skips it. No contributed check carries the liveness tag, so `MapHeadlessEndpoints()` shows them on `/health` and keeps them off `/alive`: a database outage never makes an orchestrator restart a healthy process. To serve readiness on its own path, map it by tag:

```csharp
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(HeadlessHealthCheckTags.Ready),
});
```

**Turn one off.** Set `Headless:HealthChecks:{name}:Enabled` to `false` in configuration, or `options.Enabled = false` in `ConfigureHeadlessHealthCheck(name, ...)`. The check is then registered on no endpoint. Configuration drops a check for one deployment without a code change:

```json
{ "Headless": { "HealthChecks": { "cache-redis": { "Enabled": false } } } }
```

**Remove checks in code.** `RemoveHealthChecks(predicate)` removes matching registrations after every other configuration runs, so its position in `Program.cs` does not matter. Use it for a check Headless did not contribute, or to remove a group by tag:

```csharp
// Drop one contributed check.
builder.Services.RemoveHealthChecks(registration => registration.Name == "messaging-nats");

// Drop every check Headless contributed, for example to use your own checks instead.
builder.Services.RemoveHealthChecks(registration => registration.Tags.Contains(HeadlessHealthCheckTags.Headless));
```

**Configure one.** `ConfigureHeadlessHealthCheck(name, options => ...)` changes a contributed check through `HeadlessHealthCheckOptions`. Its position relative to the provider registration does not matter. The same settings bind from `Headless:HealthChecks:{name}`. Code wins over configuration. A value that does not parse fails at startup with the key named.

```csharp
builder.Services.ConfigureHeadlessHealthCheck("cache-redis", options =>
{
    options.FailureStatus = HealthStatus.Degraded; // a cache outage degrades, it does not fail readiness
    options.Timeout = TimeSpan.FromSeconds(2);
    options.Tags.Add("cache");
});
builder.Services.ConfigureHeadlessHealthCheck("sql-postgresql", options =>
    options.TestCommand = "SELECT 1 FROM orders LIMIT 1"); // prove the schema, not just the server
```

| Option | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | `false` registers the check on no endpoint |
| `FailureStatus` | `Unhealthy` | The status of a failed or timed-out probe. `Healthy` is rejected. |
| `Timeout` | none | Bounds the probe. On timeout the check reports `FailureStatus` with `A timeout occurred while running check.`. Without one, a probe runs until its driver gives up or the request is cancelled. Configuration format: `hh:mm:ss`. |
| `Tags` | none | Adds tags |
| `TestCommand` | none | Replaces the query of the SQL and DbContext checks. Other checks ignore it. The Azure blob check takes a container through `AzureStorageOptions.HealthCheckContainerName` instead. |

For anything else, change the registration in `services.PostConfigure<HealthCheckServiceOptions>(...)`.

**Observed behavior.**

- A probe that throws reports the failure status with the fixed description `The '{name}' dependency probe failed.` and the exception attached for the health-check log. The driver's message stays out of the description, because health endpoints are usually anonymous. The entry's `Duration` is the probe latency. Checks add no result data.
- A cancelled health request cancels the probe and reports nothing, instead of reporting the dependency as down.
- One name gives one check. A second contribution with the same name adds its tags to the first one.
- One dependency gives one check. Checks that reach the same database (same provider, host, port, and database) or the same Redis deployment (same endpoints) collapse into one. The owner's check (`sql-*`, `dbcontext-*`) keeps its name, failure status, and timeout and takes the others' tags. So `jobs-storage-{T}` folds into `dbcontext-{T}`, a Messaging store on the application's database folds into `sql-postgresql`, and `distributed-locks-redis` folds into `cache-redis` when both use one deployment. A store on its own database keeps its own check. Turning the owner off leaves the next check probing the dependency.
- Two connection pools to one database with different users share one probe. A credential that breaks for one of them shows up in its queries, not in readiness.

**Not covered.** A storage feature configured with its own connection string, instead of the shared `AddPostgreSqlSql` or `AddSqlServerSql` connection or a `HeadlessDbContext`, contributes no check. Add one with `AddHeadlessHealthCheck`. Messaging PostgreSQL and SQL Server storage is the exception: it probes each store's own connection. SQLite and in-memory providers contribute no check. The data-protection key-ring check is opt-in through `AddDataProtectionKeyRing()`, because its probe writes to the store (see [api.md](api.md)).

**Add your own check.** `AddHeadlessHealthCheck(name, probe, tags)` contributes a check with the same tags, naming, and failure reporting. The probe receives the check scope's services and a cancellation token. Completing means healthy. Throwing means unhealthy.

```csharp
builder.Services.AddHeadlessHealthCheck(
    "search-elastic",
    static async (provider, cancellationToken) =>
        await provider.GetRequiredService<ISearchClient>().PingAsync(cancellationToken),
    "search"
);
```

- To collapse your check with others on the same dependency, pass a resource: `AddHeadlessHealthCheck(name, HeadlessHealthCheckResource.Shared(sp => key), probe, tags)`.
- To wrap a check another package registers on `IHealthChecksBuilder`, call `AddHeadlessHealthCheck(name, (builder, name, tags) => builder.AddXxxCheck(name, failureStatus: null, tags), dependencyTag)`.
- To give a check a different default, such as `Degraded`, call `ConfigureHeadlessHealthCheckDefault(name, options => ...)`. Configuration and `ConfigureHeadlessHealthCheck` still override it.
- Tag a check with `host-state` when it reads state only a started host has.

**Choose a community check instead** (the Xabaril `AspNetCore.HealthChecks.*` packages) when you need health decided from a query's result (for example replication lag), a dependency no Headless provider owns, or a connection string the application does not otherwise use. Register it under another name next to the contributed checks, or drop the contributed one with `RemoveHealthChecks`.

### Health check command

`dotnet MyApp.dll check` runs the host's health checks once, prints one line per check, and exits non-zero when any check is `Unhealthy`. It does not start the host. Run it as a deploy step or an init container to stop a rollout whose database, Redis, or broker the new configuration cannot reach.

```bash
dotnet MyApp.dll check
```

```text
messaging-nats       Healthy     3ms
sql-postgresql       Unhealthy  41ms  The 'sql-postgresql' dependency probe failed. Connection refused
messaging            Skipped       -  Reports the state of a running host; not run before start.
messaging-broker     Skipped       -  Reports the state of a running host; not run before start.
messaging-consumers  Skipped       -  Reports the state of a running host; not run before start.
```

- **Which checks run.** Every registered check, with each registration's timeout and failure status, except those tagged `host-state`. Those print as `Skipped`. A check removed with `RemoveHealthChecks` or turned off in configuration is absent.
- **Output.** One line per check on standard output, ordered by name with skipped checks last: name, status (`Healthy`, `Degraded`, `Unhealthy`, or `Skipped`), duration in milliseconds, and description. A failed probe's line adds the exception message, which the health endpoints withhold. Treat the output as operator-only.
- **Exit code.** `1` when any check is `Unhealthy` or startup validation fails. Otherwise `0`. `Degraded` does not fail the command, which matches readiness.
- **Validation failure.** Standard error gets `Host check failed: {message}`, standard output stays empty, no check runs, and the exit code is `1`.
- **No checks.** Standard error gets `No health checks are registered.` and the exit code is `0`.

### Host manifest

The host manifest is a JSON document of what each feature registered in the host, such as Messaging consumers and Jobs functions with their effective settings. It answers "what does this deployed host consume and run?" without starting it.

```bash
dotnet MyApp.dll describe | jq '.sections.jobs.functions[].name'
```

```json
{
  "schemaVersion": 1,
  "sections": {
    "healthChecks": [
      { "name": "messaging", "tags": ["headless", "messaging", "ready"], "failureStatus": "unhealthy" }
    ],
    "host": {
      "applicationName": "Orders.Api",
      "environment": "Production",
      "applicationVersion": "2.4.0+9f2c1e7",
      "headlessVersion": "0.9.0+c56441b",
      "runtime": ".NET 10.0.13"
    },
    "jobs": { "functions": [] },
    "messaging": { "consumers": [], "messages": [], "outboxes": [], "transports": {} }
  }
}
```

- **Needs no infrastructure.** No section opens a connection, so `describe` succeeds when the broker and databases are unreachable, for example in CI. Configuration must still be valid: a missing RabbitMQ password, or two outboxes on one database, fails the command as it would fail a start.
- **Output.** On success, standard output carries only the JSON document and the exit code is `0`. On any failure, standard output stays empty, standard error gets `Host manifest failed: {message}`, and the exit code is `1`.
- `schemaVersion` is `HostManifest.SchemaVersion`. It changes only when a change would break a reader of the old shape.
- **`host`** is always written: the application and environment names, the entry assembly's and Headless's informational versions, and the .NET runtime, plus `prefix` when the host has an [isolation prefix](#isolation-prefix). It names no machine and no address.
- **`healthChecks`** is always written: every registered check ordered by name, with its sorted tags, its `failureStatus` (`unhealthy` or `degraded`), and `timeoutSeconds` when it has a timeout. No probe runs.
- The other sections are sorted by name, one per registered feature. The [Messaging](messaging/operations.md#host-manifest) and [Jobs](jobs/operations.md#host-manifest) guides document theirs.
- The manifest never carries connection strings, broker addresses, credentials, or runtime state.

`MapHeadlessHostManifest(pattern, authorizationPolicy)` in `Headless.Api.ServiceDefaults` serves the same document over HTTP, behind authorization by default (see [API & Web](api.md#host-manifest-endpoint)). `HostManifest.WriteAsync(services, stream, cancellationToken)` writes it to any stream.

**Add a section.** Implement `IHostManifestSection` and register it as a singleton with `TryAddEnumerable`:

```csharp
internal sealed class SearchHostManifestSection(SearchIndexRegistry registry) : IHostManifestSection
{
    public string Name => "search";

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("indexes");
        foreach (var index in registry.Indexes.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            writer.WriteStringValue(index.Name);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostManifestSection, SearchHostManifestSection>());
```

- `Write` writes exactly one complete JSON value, normally an object.
- Read only state that exists once the host is built. Never start a hosted service, open a connection, or call a broker.
- Write entries in a stable order, and omit a setting that does not apply instead of writing `null`, so two manifests of one configuration diff cleanly.
- Two sections with the same `Name`, or a section named `host` or `healthChecks`, fail the manifest. A section that throws fails it with a message that carries the section's error text, so never show that message to an untrusted caller.

### Resource provisioning

A provisioned resource is external state a feature needs before it runs: database tables, broker queues, a storage container. A deployment creates them with the `resources` verb, from the same entry point and configuration as the host:

```bash
dotnet MyApp.dll resources provision            # create what is missing
dotnet MyApp.dll resources verify -k Schema      # verify without writing
dotnet MyApp.dll resources list
dotnet MyApp.dll resources status -n PostgreSql/headless/Sequences
```

**Verify:** `resources provision` prints `Ok` on every line and exits `0`. Then `resources verify` exits `0`.

| Subcommand | Effect |
| --- | --- |
| `provision` | Creates whatever is missing. Idempotent: run it on every release. |
| `verify` | Checks each resource without writing |
| `purge` | Removes data and keeps the structure, in reverse dependency order. [Guarded](#destructive-subcommands). |
| `delete` | Deletes what `provision` created, in reverse dependency order. [Guarded](#destructive-subcommands). |
| `status` | Prints each resource's status values, indented under it |
| `list` | Lists the resources, noting any that cannot purge or delete. Runs nothing. |

- **A subcommand is required.** A bare `resources` exits `2` with the usage, so a deploy script never changes state by omission.
- **Filters.** `-k|--kind <kind>` and `-n|--name <name>` match exactly, ignoring case. Repeat a flag to match any of its values. When you pass both flags, a resource must match both.
- **Timeout.** `--timeout <seconds>` bounds the whole run. The resource running when it elapses, and every resource not yet reached, report `TimedOut`. Without it, each resource's own timeouts apply, such as the schema runner's 2-minute lock wait and 10-minute DDL command timeout.
- **Output.** One aligned line per resource on standard output: kind, name, status (`Ok`, `Failed`, `Unsupported`, or `TimedOut`), duration, and the failure message.
- **Every resource runs.** A failure does not stop the run, so the report names every resource. Resources run one at a time, in dependency order.

| Exit code | When |
| --- | --- |
| `0` | Every resource is `Ok` or `Unsupported`, or no resource is registered |
| `1` | Any resource fails or times out, or startup validation fails |
| `2` | A missing or unknown subcommand or option, a missing or invalid value, filters that match no resource, or `purge` or `delete` outside the allowed environments without `--force` |

#### Destructive subcommands

`purge` and `delete` run without a flag only in the environments listed in `ResourceProvisioningOptions.DestructiveEnvironments` (default `Development` and `Test`, compared ignoring case). Every other environment, whatever its name, refuses them and exits `2` unless you pass `--force`: `dotnet MyApp.dll resources delete --force`. To allow another environment, call `services.ConfigureResourceProvisioning(o => o.DestructiveEnvironments.Add("QA"))`.

#### What a starting host checks

The host never creates a resource. It verifies each one, as the provisioner's `StartupCheck` (`ResourceStartupCheck`) says:

| `StartupCheck` | When the resource is missing or changed | Default for |
| --- | --- | --- |
| `FailStart` | The host start fails before any hosted service starts, naming every such resource and `resources provision`. | `Schema` (every contribution that is not export-only), `EntityFramework` |
| `GateReadiness` | The host starts. The `warmup` readiness check stays `Unhealthy`, naming the resource, until it appears. | `RabbitMQ` and any transport topology, `AzureBlob`, `S3Bucket`, and a custom provisioner that does not override it |
| `None` | Nothing at startup. Only `resources verify` checks it. | `MaxMind`, export-only schema contributions |

To change a kind's check, call `services.ConfigureResourceProvisioning(o => o.StartupChecks["RabbitMQ"] = ResourceStartupCheck.None)`. The schema checks read each schema's history once per host. A history row this host does not register logs `SchemaRunnerUnknownStep`.

#### Resource kinds

| Kind | Registered by | Name | Provision | Verify | Purge | Delete |
| --- | --- | --- | --- | --- | --- | --- |
| `Schema` | Every relational storage feature, one per schema contribution | `{dialect}/{schema}/{feature}`. When one feature and schema live in two databases (an additional outbox on its own database), each name ends in `@` and a short hash of its database, so `-n` can pick one. | Applies the missing steps, export-only contributions included | Fails on a missing step or a changed checksum | Deletes the rows of its tables, keeping host-state tables, sequences, and history | Drops its tables and sequences and deletes its history rows, so the next `provision` recreates them. A contribution that declares no objects supports neither. |
| `EntityFramework` | `AddDbContextMigrations<TContext>()` | The context type name | Applies the pending migrations, after every `Schema` resource | Fails naming each pending migration | Unsupported | Unsupported |
| `RabbitMQ` (any transport with topology) | `UseRabbitMq` | `topology` | Declares the lane exchanges, one queue per competing consumer subscription and registered Queue-lane message with its `.dlq`, and the bindings | Passively declares each exchange and queue | Purges each queue this host consumes and its `.dlq`, never a queue it only publishes to | Deletes the queues this host consumes and their `.dlq`. Leaves the shared lane exchanges and their `.dlx` (see [RabbitMQ](messaging/rabbitmq.md#headlessmessagingrabbitmq)). |
| `AzureBlob` | `UseAzure` in `AddHeadlessTus` | The container name | Creates the container with `ContainerPublicAccessType` | Fails when the container does not exist | Deletes the blobs under `BlobPrefix`, every tenant's uploads included, keeping the container | Deletes the blobs under `BlobPrefix`, then the container only when nothing else is left in it (see [Tus](tus.md#headlesstusazure)) |
| `S3Bucket` | `UseS3` in `AddHeadlessTus` | The bucket name | Creates the bucket. A bucket the account already owns counts as created. | Fails when the bucket does not exist | Deletes the store's objects under `Prefix`, every tenant's uploads included, keeping the bucket and every other object | Deletes the same objects, then the bucket only when nothing else is left in it (see [Tus](tus.md#headlesstuss3)) |

#### Provision as a development or test host starts

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDeployStepsOnStartup();
}
```

- `AddDeployStepsOnStartup()` provisions every resource, then runs every seeder, before any other hosted service starts. A failure fails the host start, naming each failed resource or the failed seeder.
- `o.Resources = ResourceStartupAction.ProvisionAndPurge` also purges each resource's data after provisioning. Outside `DestructiveEnvironments` it fails the host start instead of purging.
- `o.Resources = ResourceStartupAction.None` keeps the seed step only. `o.Seed = false` keeps the resource step only.
- The resource step runs before every other hosted service, wherever the call sits. The seed step runs at the call's position.
- A second call configures the same options again. `TryAddDeployStepsOnStartup(...)` registers the steps only when nothing registered them yet. `HeadlessTestServer` uses it, so an application that registers `ProvisionAndPurge` keeps it under the test server.

Tests and tools call the same runner on a built host: `await host.Services.ProvisionResourcesAsync()`, `PurgeResourcesAsync()`, and `DeleteResourcesAsync()`. Each runs every resource, skips unsupported operations, and then throws one `ResourceProvisioningException` whose `Failures` holds each failed resource's error. `PurgeResourcesAsync` and `DeleteResourcesAsync` throw `InvalidOperationException` and touch nothing when the host's environment is outside `DestructiveEnvironments`. Services built without an `IHostEnvironment` are allowed.

#### Add a resource

Implement `IResourceProvisioner` (`Kind`, `Name`, `ProvisionAsync`, `VerifyAsync`, `PurgeAsync`, `DeleteAsync`, `GetStatusAsync`) and register it with `services.AddResourceProvisioner(sp => ...)`. When the set depends on other registrations, such as one queue per consumer, implement `IResourceProvisionerSource` and register it with `services.AddResourceProvisionerSource<TSource>()`.

- Make every operation idempotent: a deployment runs `provision` on every release.
- Throw `ResourceProvisioningException` from `VerifyAsync` with a message that tells the operator what to run.
- Return `false` from `CanPurge` or `CanDelete` when that operation does not apply. Callers then skip it and report `Unsupported`, which is not a failure. Any exception an operation throws, `NotSupportedException` included, is `Failed`.
- Override `StartupCheck` to choose `FailStart` or `None` instead of the `GateReadiness` default.
- Override `DependsOn` to name the provisioners this one must follow. Two resources that depend on each other fail every operation with the cycle in the message.
- Keep `Name` free of host names, connection strings, and credentials: deploy logs print it.

### Database seeders

A seeder writes data the code declares (definitions, cron jobs, reference rows) into the stores. It is not a [provisioned resource](#resource-provisioning): a resource is structure that `provision` creates once, and a seeder must run again whenever the declarations change. Framework packages register their own seeders: `FeatureDefinitions`, `PermissionDefinitions`, `SettingDefinitions`, and `Jobs`.

```csharp
public sealed class UserSeeder : ISeeder
{
    public Task SeedAsync(CancellationToken cancellationToken) { /* seed users */ return Task.CompletedTask; }
}

builder.Services.AddSeeder<UserSeeder>();
builder.Services.AddDbContextMigrations<AppDbContext>(); // Headless.EntityFramework; structure, applied by `resources provision`

// Development: seed as the host starts. Production: the deployment runs `dotnet MyApp.dll seed`.
if (builder.Environment.IsDevelopment()) builder.Services.AddDeployStepsOnStartup();

// A tool or test runs them directly, in dependency order, as the `seed` verb does.
await app.Services.SeedAsync();
```

**Verify:** `dotnet MyApp.dll seed` prints `Ok` for each seeder and exits `0`. Then `dotnet MyApp.dll seed verify` exits `0`.

- **Registration.** `AddSeeder<T>()` or `AddSeeder(sp => ...)`. `ISeeder.Name` defaults to the type name.
- **Order.** Seeders run one at a time, each after the seeders its `DependsOn` names, otherwise in registration order. Two that depend on each other fail the run with the cycle in the message.
- **The host start does not seed.** Run the `seed` verb on every release, next to `resources provision`, or run `deploy`, which does both. In development and test hosts, `AddDeployStepsOnStartup()` seeds as the host starts. `HeadlessTestServer` does this by default (`seed: false` turns it off).
- **A skipped `seed` step fails silently.** Definitions that other applications on the same database read go stale, configured `Deleted*` definitions are not removed, `DynamicPermissionDefinitionsChanged` is not published, and cron changes are not applied (see the [Jobs seeder](jobs.md#headlessjobs)). Add `seed verify` to a release check to catch it.
- **Migrations are not seeders.** Register EF Core migrations with `AddDbContextMigrations<TContext>()`. `resources provision` applies them.

The `seed` verb:

| Form | Effect |
| --- | --- |
| `seed` | Runs every seeder |
| `seed -n Jobs` | Runs the named seeder. Repeat `-n` for more. |
| `seed list` | Lists the seeders |
| `seed verify` | Asks each seeder whether its store already holds what it would write. Writes nothing. |
| `--timeout <seconds>` | Bounds the run. Default 300. |

- **Output.** One line per seeder: name, status, duration, and message. `seed` prints `Ok`, `Failed`, or `Skipped`. A failed seeder stops the run and the rest are `Skipped`, because a later seeder may need what an earlier one writes.
- **`seed verify` output.** `Current`, `Stale`, `Unknown`, or `Failed` per seeder. `Unknown` comes from a seeder that cannot tell (the `ISeeder.VerifyAsync` default) and does not fail the command. The Features, Permissions, and Settings seeders compare their store with the code-declared definitions.
- **Exit codes.** `0` on success. `1` on a failed seeder, a `Stale` or `Failed` verify result, or failed startup validation. `2` for invalid arguments or an unknown seeder name.

### Warm-up

A warm-up is work a feature does once after the host starts so its first requests do not pay for it: loading a cache, reading a key ring, downloading data, or checking that broker objects exist. Implement `IWarmup` (`Name`, `GatesReadiness`, `WarmUpAsync`) and register it with `services.AddWarmup<T>()` or `services.AddWarmup(sp => ...)`. Registering a type again adds nothing.

**Verify:** `/health` reports the `warmup` check `Healthy` once the warm-up finishes. In a test, `await host.Services.WaitForWarmupAsync(cancellationToken)` returns.

- **Never delays the start.** Every warm-up runs concurrently in the background after the host starts, each in its own scope.
- **Readiness waits.** The first `AddWarmup` call adds the `warmup` health check (`HeadlessHealthCheckNames.Warmup`), tagged `ready`, `headless`, and `host-state`. It reports `Unhealthy`, naming each pending warm-up and its last error, until every warm-up whose `GatesReadiness` is `true` (the default) has finished. Liveness is not affected, and the [`check` verb](#health-check-command) skips it.
- **Failures retry and never crash.** A failed warm-up logs `WarmupFailed` and retries with a jittered back-off from 1 to 30 seconds, in real time, until it succeeds or the host stops. A misconfiguration that must stop the start belongs in an [`IHeadlessStartupValidator`](#startup-validators) instead.
- **Tests wait for it.** `WaitForWarmupAsync(cancellationToken)` returns once every gating warm-up has finished. Its cancellation message names each pending one. `HeadlessTestServer` calls it before it returns.

### Startup validators

To refuse to start on a misconfiguration, implement `IHeadlessStartupValidator` and register it. Do not write a hosted service for it.

```csharp
internal sealed class OrdersModelValidator(IDbContextFactory<AppDbContext> factory) : IHeadlessStartupValidator
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        if (db.Model.FindEntityType(typeof(Order)) is null)
        {
            throw new InvalidOperationException("Call modelBuilder.AddOrders() in OnModelCreating.");
        }
    }
}

services.AddStartupValidator<OrdersModelValidator>();
```

**Verify:** with the misconfiguration in place, `dotnet MyApp.dll describe` exits `1` and prints `Host manifest failed:` followed by the validator's message.

- **Runs before every hosted service.** Validators run before any `StartAsync`, so a misconfigured host fails before background workers or consumers start. The host verbs run them too.
- **Reports every failure at once.** Each validator runs even after another fails. One failure surfaces as its own exception, unwrapped, so a typed exception such as `MissingRequiredServiceException` keeps its type. Two or more surface as a `StartupValidationException` (an `AggregateException`) whose inner exceptions are the originals. Cancelling startup stops the run and is not reported as a failure.
- **Registration.** `AddStartupValidator<T>()` and `AddStartupValidator(Type)` add a validator type once. `AddStartupValidator(Func<IServiceProvider, IHeadlessStartupValidator>)` adds one on every call, for one validator per named instance.
- **Cheap checks only.** Validators run on every instance on every start and cannot be turned off. Keep them to in-memory checks: options, EF model metadata, DI registrations, and wiring flags. Put network I/O in a [warm-up](#warm-up) or a [resource provisioner](#resource-provisioning).
- **Warnings.** A validator that only warns logs through an injected logger and returns normally.

### Required services

A Headless package can depend on a contract whose implementation ships in a provider package you choose. For example, `Headless.Settings` needs an `ICache<T>` from a caching provider. Without the provider, the host would start and fail on the first request that touches the feature. `RequireRegisteredService<T>` turns that into a start failure:

```csharp
services.RequireRegisteredService<ICache<SettingValueCacheItem>>(
    requiredBy: "Headless settings value caching",
    remedy: "Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."
);
```

- **Checked at startup.** The check runs as a startup validator, so registration order does not matter.
- **Never resolved.** The check asks the container whether the service is registered and never constructs it. A closed generic such as `ICache<Foo>` counts as registered when only the open generic `ICache<>` is, which is how the caching providers register.
- **One report.** Every missing service in the host is reported in one `MissingRequiredServiceException`, each line naming its `requiredBy` and `remedy`. Identical declarations collapse to one line.
- `Headless.MultiTenancy`, `Headless.Settings`, `Headless.Permissions`, and `Headless.Features` require a caching provider this way. `Headless.Idempotency.ResponseReplay` requires the durable `IIdempotentOperations` store.

`RequireSingletonService<T>(requiredBy, remedy)` (and its `Type` overload) also checks the lifetime of a service that a feature's singleton injects but the application registers:

```csharp
services.RequireSingletonService(
    typeof(IDbContextFactory<>).MakeGenericType(dbContextType),
    requiredBy: "Headless settings EF storage",
    remedy: "Register it at the default singleton lifetime: AddDbContextFactory<TContext>() or AddPooledDbContextFactory<TContext>() for a plain DbContext, or AddHeadlessDbContext<TContext>() or AddHeadlessDbContextPool<TContext>() for a HeadlessDbContext."
);
```

- **Refuses a captive dependency in every environment.** A scoped or transient registration would be captured by the singleton for the life of the host. The check reads the lifetime from the service collection and throws `InvalidServiceLifetimeException` listing every violation, including in production, where scope validation is off.
- **Judges the registration the container resolves.** The last unkeyed registration of the closed type wins. With none, the last unkeyed open-generic registration decides. Keyed registrations are ignored.
- **Also requires the registration.** A missing registration still fails with `MissingRequiredServiceException`.
- The EF storage providers of `Headless.MultiTenancy`, `Headless.Settings`, `Headless.Features`, `Headless.Permissions`, `Headless.AuditLog`, and `Headless.Jobs` require a singleton `IDbContextFactory<TContext>` this way. If one of those features fails to start with `InvalidServiceLifetimeException`, register the factory at singleton lifetime.

### Options with validation

`Configure<TOptions, TValidator>(...)` and `AddOptions<TOptions, TValidator>()` (namespace `Microsoft.Extensions.DependencyInjection`) register the FluentValidation validator, validate the options, and mark them `ValidateOnStart`. Overloads take an `IConfiguration` section, an `Action<TOptions>`, or an `Action<TOptions, IServiceProvider>`, plus an optional options name.

```csharp
services.Configure<AppOptions, AppOptionsValidator>(configuration.GetSection("App"));
```

**Verify:** with an invalid value, the host start and `dotnet MyApp.dll describe` both fail with an `OptionsValidationException` that names the failed rule.

In a trimmed or native AOT application, use the `Action<TOptions>` overloads. The overloads that bind an `IConfiguration` (`Configure*`, `GetOptions`, `GetRequired`) or validate with data annotations carry `[RequiresUnreferencedCode]`, and the binding ones also `[RequiresDynamicCode]`, so the compiler warns at the call site.

### Service registration helpers

```csharp
// Conditional registration
services.AddIf(builder.Environment.IsDevelopment(), s => s.AddHeadlessEmails(setup => setup.UseDevelopment("emails.txt")));
services.AddIfElse(condition, ifAction, elseAction);

// Replacement
services.AddOrReplaceScoped<IService, NewImpl>();
services.AddOrReplaceSingleton<IService>(sp => new Impl(sp.GetRequiredService<IDep>()));

// Replaces only a known TFallback registration and keeps one the application registered.
services.AddOrReplaceFallbackSingleton<IService, NullFallback, DefaultImpl>();

// Removal
services.Unregister<IService>();

// Decorators: wrap every unkeyed registration, or only the registrations under one key.
services.TryDecorateWith<IService>((inner, sp) => new LoggingService(inner));
services.TryDecorateKeyedWith<IService>("reports", (inner, sp) => new LoggingService(inner));
```

**Verify:** no startup signal. In a test, build the provider and assert that `GetRequiredService<IService>()` returns the expected type.

- Decorators keep each registration's lifetime.
- A decorator that returns a different instance owns disposing the inner one, because the container tracks only what the factory returns.
- Decorating turns every registration into a factory, so an instance the application registered and owned (`AddSingleton(instance)`) becomes container-disposed.
- `TryDecorateKeyedWith` does not decorate a `KeyedService.AnyKey` registration.

---

## Headless.Http.Resilience

Derives an outbound `HttpClient`'s retry policy from the side effects its calls have.

### Setup

```bash
dotnet add package Headless.Http.Resilience
```

Declare the client's effect once, where the named client is built:

```csharp
services
    .AddHttpClient<IPaymentsClient, PaymentsClient>()
    .AddEffectResilienceHandler(OutboundEffect.Unsafe);
```

**Verify:** no startup signal. In an integration test against a stub server that returns `503`, assert that an `Unsafe` POST reaches the server once and a GET is retried.

### Configuration

`AddEffectResilienceHandler(this IHttpClientBuilder, OutboundEffect effect, string? idempotencyHeader = null, Action<HttpStandardResilienceOptions>? configureResilience = null)`:

| `OutboundEffect` | Retries |
| --- | --- |
| `Safe` | Every method. Use it for read-only clients. |
| `Idempotent` | Every method, with one stable key per logical call in `idempotencyHeader`, reused across that call's retries. A header the caller set is kept. A null or empty `idempotencyHeader` throws `ArgumentException`. |
| `Unsafe` | No automatic retry for POST, PUT, PATCH, DELETE, or CONNECT. Reads stay retryable. |

`configureResilience` runs after the derived defaults. Setting `Retry.ShouldHandle` there is the explicit opt-in to retrying unsafe methods.

### Design and runtime behavior

- The call first removes every resilience handler already on the builder, so a host-wide handler added through `ConfigureHttpClientDefaults` (for example by `AddHeadless()`) cannot wrap the client and retry a declared-unsafe call.
- The effect applies per client, not per operation. A client whose reads also have side effects must not use `Unsafe`, because `Unsafe` keeps reads retryable.
- The SMS, Paymob, reCAPTCHA, Turnstile, and APNs packages declare their effects this way.

---

## Headless.NetTopologySuite

NetTopologySuite extensions for precision, permissive geometry operations, SQL Server geography compatibility, and simplification.

### Setup

```bash
dotnet add package Headless.NetTopologySuite
```

```csharp
using Headless.NetTopologySuite;
using NetTopologySuite.Geometries;

builder.Services.AddHeadlessNetTopologySuite();

var factory = new GeometryFactory(GeoConstants.HighPrecision, GeoConstants.GoogleMapsSrid);

var polygon = factory.CreatePolygon(
    new[]
    {
        new Coordinate(0, 0),
        new Coordinate(10, 0),
        new Coordinate(10, 10),
        new Coordinate(0, 10),
        new Coordinate(0, 0),
    }
);

var sanitized = polygon.SanitizeForSqlGeography(); // orientation and validity for SQL Server geography
var simplified = polygon.Simplify(GeoConstants.Around1MDegrees);
```

**Verify:** no startup signal. After `AddHeadlessNetTopologySuite()` runs, `NtsGeometryServices.Instance` is the registered `GeoServices.NtsGeometryServices`. In a test, assert that `sanitized.IsValid` is `true`.

### Configuration

`AddHeadlessNetTopologySuite()` sets `GeoServices.NtsGeometryServices` as the process-wide `NtsGeometryServices.Instance` and registers it and `GeoServices.GeometryFactory` as singletons. Call it once at startup: NetTopologySuite reads the static instance, not DI.

### Design and runtime behavior

- **Operations:** `PermissiveIntersection()`, `PermissiveUnion()`, `ComputeOverlap()`, `EnsureIsOrientedCounterClockwise()`, `Simplify()` (topology-preserving), `SanitizeForSqlGeography()`, plus point and polygon creation helpers, coordinate range validation, and feature collection conversion.
- **Constants:** `GeoConstants.GoogleMapsSrid` (4326), the latitude and longitude bounds, precision models (`UltraPrecision`, `HighPrecision`, `SqlServerGeographyPrecision`, `StreetLevelPrecision`), and simplification tolerances (`Around11CmDegrees`, `Around1MDegrees`, `Around111MDegrees`).
- The extensions live in `HeadlessGeometryExtensions` in the `NetTopologySuite.Geometries` namespace. For a static call, write `HeadlessGeometryExtensions.SanitizeForSqlGeography(polygon)`.

---

## Headless.PhoneNumbers

libphonenumber-backed formatting, normalization, region lookup, parsing, mobile-number validation, and FluentValidation rules for the `PhoneNumber` value object.

### Setup

```bash
dotnet add package Headless.PhoneNumbers
```

```csharp
using FluentValidation;
using Headless.PhoneNumbers;
using Headless.Primitives;

public sealed class ContactValidator : AbstractValidator<Contact>
{
    public ContactValidator()
    {
        RuleFor(x => x.Phone).InternationalPhoneNumber(); // international format
        RuleFor(x => x.Mobile).MobilePhoneNumber(x => x.CountryCode); // mobile only, by calling code
    }
}

var phone = PhoneNumber.FromInternationalFormat("+201018541323");
var display = phone.GetInternationalFormat(); // "+20 10 18541323"
var key = phone.Normalize(); // lookup-normalized form
```

**Verify:** no startup signal and no DI registration. In a unit test, assert that `PhoneNumber.FromInternationalFormat("+201018541323").GetInternationalFormat()` returns `"+20 10 18541323"`, and that the validator reports `g:invalid_phone_number` for a malformed number.

### API

- Extension members on `PhoneNumber` (namespace `Headless.PhoneNumbers`): `GetNationalFormat()`, `GetInternationalFormat()`, `GetRegionCodes()`, `Normalize()`, and `ToUtilsPhoneNumber()`.
- Static members on `PhoneNumber`: `FromInternationalFormat(string?)`, `FromPhoneNumber(PhoneNumbers.PhoneNumber?)`, `Normalize(int code, string number)`, `NormalizeInternationalNumber(string)`, `GetNationalFormat(string?)`, and `GetInternationalFormat(string?)`.
- `MobilePhoneNumberValidator.IsValid(...)` checks a possible mobile number by country calling code, by region code, or in international form, which returns its normalized E.164 form.
- FluentValidation rules (namespace `FluentValidation`): `BasicPhoneNumber()`, `PhoneNumber(countryCodeFunc)`, `InternationalPhoneNumber()`, `MobilePhoneNumber(countryCodeFunc | regionCodeFunc)`, `InternationalMobileNumber()`, and `PhoneCountryCode()`.

### Design and runtime behavior

- `PhoneNumber` lives in `Headless.Primitives` and holds only `CountryCode` and a digits-only `Number`, so EF mappings and API models use it without libphonenumber. Its `ToString()` returns the raw `+{CountryCode} {Number}`, not a formatted number.
- `Number` is libphonenumber's national significant number. It keeps a leading zero that a region dials as part of the subscriber number: `FromInternationalFormat("+39 06 1234 5678").Number` is `0612345678`, and `MobilePhoneNumberValidator.IsValid(..., out normalizedNumber)` returns `+2250701234567` for Côte d'Ivoire. A national trunk prefix such as Egypt's `0` is not part of it.
- The country and region overloads of `IsValid` require the parsed number to carry that calling code. A `+`-prefixed number from another country (`"+16502530000"` checked against `20`) is refused. Regions that share a calling code, such as `US` and `CA` under `+1`, are not told apart. The `MobilePhoneNumber(...)` rules behave the same way.
- Every formatting, normalization, and region member parses the number on each call. Cache the result when a hot path formats the same number repeatedly.
- Convert to and from libphonenumber's `PhoneNumber` with `ToUtilsPhoneNumber()` and `PhoneNumber.FromPhoneNumber(...)`. There is no conversion operator.

---

## Headless.Redis

Definition-first Lua script loading and execution, plus connection helpers, for StackExchange.Redis.

### Setup

```bash
dotnet add package Headless.Redis
```

Declare each script as a `RedisScriptDefinition` with one shared `Instance`:

```csharp
using Headless.Redis;
using StackExchange.Redis;

public sealed class IncrementWithExpireScript : RedisScriptDefinition
{
    public static IncrementWithExpireScript Instance { get; } = new();

    private IncrementWithExpireScript()
        : base(
            """
            redis.call('incrby', @key, @value)
            redis.call('pexpire', @key, @expires)
            return redis.call('get', @key)
            """
        ) { }
}
```

Run it through the loader:

```csharp
using Headless.Redis;
using StackExchange.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
var scriptsLoader = new HeadlessRedisScriptsLoader(redis);

var result = await scriptsLoader.EvaluateAsync(
    redis.GetDatabase(),
    IncrementWithExpireScript.Instance,
    new
    {
        key = (RedisKey)"counter",
        value = (RedisValue)1,
        expires = 60_000,
    }
);
```

**Verify:** no startup signal. The first `EvaluateAsync` call returns the script's result (the counter value above). A Redis error surfaces as an exception from that call.

### Design and runtime behavior

- **Loading.** `EvaluateAsync` loads a script onto every writable node (`SCRIPT LOAD`) on its first use, then runs it by SHA (`EVALSHA`). On `NOSCRIPT` (after a failover, a restart, or a flushed script cache) it runs that call with `EVAL` and reloads on the next call. Concurrent loads are serialized.
- **Load ahead of first use** by calling `LoadAsync` with the package's scripts from an `IWarmup` whose `GatesReadiness` is `false` (see [Warm-up](#warm-up)). Never load from a startup validator or a hosted service's start, which blocks the host start on a Redis round trip.
- **One instance per definition type.** The loader caches loaded scripts by definition type and rejects a second instance of the same type. Reuse `Instance`.
- The Headless Redis providers (caching, distributed locks, coordination) load their scripts this way, in a non-gating warm-up. A host with Redis down still starts, the warm-up retries, and the Redis readiness check reports the outage.
- **Connection helpers** (`HeadlessConnectionMultiplexerExtensions`):
  - `PingAsync` sends `PING` through the default database and returns the round-trip time. Its token abandons the wait. The command itself ends through the multiplexer timeout.
  - `ProbeEndpointsAsync` sends `PING` to every configured endpoint and checks that `CLUSTER INFO` reports `cluster_state:ok` on cluster nodes.
  - `GetEndpointsKey` names the deployment by its sorted configured endpoints, without credentials and without contacting the server.
  - `CountAllKeysAsync` checks its optional `CancellationToken` before endpoint discovery and between endpoints. It cannot interrupt an in-flight `DBSIZE`.

---

## Headless.Sitemaps

XML sitemap and sitemap index writing, with localized alternates, images, and sharding past 50,000 URLs.

### Setup

```bash
dotnet add package Headless.Sitemaps
```

```csharp
using Headless.Sitemaps;

var urls = new List<SitemapUrl>
{
    new(
        location: new Uri("https://example.com/"),
        options: new SitemapUrlOptions
        {
            LastModified = DateTime.UtcNow,
            ChangeFrequency = ChangeFrequency.Daily,
            Priority = 1.0f,
        }
    ),
    new(
        location: new Uri("https://example.com/about"),
        options: new SitemapUrlOptions { ChangeFrequency = ChangeFrequency.Monthly, Priority = 0.8f }
    ),
};

await using var stream = new MemoryStream();
await urls.WriteToAsync(stream);
```

**Verify:** no startup signal. In a unit test, parse the written stream as XML and assert that it holds one `<url>` element per `SitemapUrl`.

### Large sitemaps

A sitemap file holds at most `SitemapConstants.MaxSitemapUrls` (50,000) `<url>` elements. `WriteAsync` splits the input into shards of that size and calls `onShard` for each one, in order, as soon as it fills. It returns the shard count. Build the sitemap index from it.

```csharp
// sitemapUrls is an IEnumerable<SitemapUrl> or an IAsyncEnumerable<SitemapUrl>,
// such as a streamed database query.
var shardCount = await sitemapUrls.WriteAsync(
    async (shard, ct) =>
    {
        await using var file = File.Create($"wwwroot/sitemap-{shard.Index}.xml");
        await shard.WriteToAsync(file, ct);
    },
    cancellationToken
);

var references = Enumerable
    .Range(0, shardCount)
    .Select(i => new SitemapReference { Location = new Uri($"https://example.com/sitemap-{i}.xml") });

await using var index = File.Create("wwwroot/sitemap.xml");
await references.WriteToAsync(index, cancellationToken);
```

- **A shard is valid only during its callback.** Await `shard.WriteToAsync` inside `onShard`. A call after the callback returns throws `InvalidOperationException`.
- **A blob upload needs its own buffer.** Write the shard into a `MemoryStream` inside the callback, rewind it, and upload it before the callback returns.
- **Alternates count toward the limit.** A `SitemapUrl` with alternates writes one `<url>` per alternate it emits (those selected by `WriteAlternateLanguageCodes`, else all). Its entries never split across shards, so a shard closes early when the next entry would overflow it.
- **Memory** holds one shard's URL references at a time, and no shard XML unless the callback buffers it.
- **Each shard declares only the namespaces it uses.** `xmlns:xhtml` and `xmlns:image` appear only when one of its URLs has alternates or images. The bytes equal `WriteToAsync` on the same URLs.
- **Failure and cancellation stop enumeration.** An exception from `onShard` propagates and no more URLs are read. Cancellation is checked before each URL. An empty input calls `onShard` zero times and returns `0`.

### Localized URLs

Use `SitemapAlternateUrl` for hreflang alternates:

```csharp
var urls = new List<SitemapUrl>
{
    new(
        alternateLocations:
        [
            new() { Location = new Uri("https://example.com/en/page"), LanguageCode = "en" },
            new() { Location = new Uri("https://example.com/ar/page"), LanguageCode = "ar" },
        ],
        options: new SitemapUrlOptions { LastModified = DateTime.UtcNow }
    ),
};

await using var stream = new MemoryStream();
await urls.WriteToAsync(stream);
```

### Sitemap index

```csharp
var references = new List<SitemapReference>
{
    new() { Location = new Uri("https://example.com/sitemap-products.xml"), LastModified = DateTime.UtcNow },
    new() { Location = new Uri("https://example.com/sitemap-blog.xml"), LastModified = DateTime.UtcNow },
};

await using var stream = new MemoryStream();
await references.WriteToAsync(stream);
```

### Design and runtime behavior

- Every location (`SitemapUrl`, `SitemapAlternateUrl`, `SitemapImage`, `SitemapReference`) must be an absolute URI. A relative one throws `ArgumentException` when the object is created, not partway through writing a file.
- Pass optional metadata (`LastModified`, `ChangeFrequency`, `Priority`) through `SitemapUrlOptions` to either `SitemapUrl` constructor.
- `SitemapImage` adds image entries. `SitemapIndexBuilder` builds an index for large sites.

---

## Headless.Slugs

URL-friendly slug generation with Unicode normalization, Arabic support, and configurable options.

### Setup

```bash
dotnet add package Headless.Slugs
```

```csharp
using Headless.Slugs;

var slug = Slug.Create("Hello World!"); // "hello-world"
var arabic = Slug.Create("مرحبا بالعالم"); // "مرحبا-بالعالم"

var options = new SlugOptions
{
    Replacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["&"] = "and",
        ["@"] = "at",
        ["+"] = "plus",
    },
};

var custom = Slug.Create("Tom & Jerry @ Home", options); // "tom-and-jerry-at-home"
```

**Verify:** no startup signal. In a unit test, assert that `Slug.Create("Hello World!")` returns `"hello-world"`.

### Configuration

| `SlugOptions` property | Default | Effect |
| --- | --- | --- |
| `Separator` | `"-"` | Separator between words. A null or empty value throws `ArgumentException`. |
| `MaximumLength` | `80` | Maximum length in UTF-16 code units |
| `CasingTransformation` | `ToLowerCase` | Casing applied to the result |
| `CanEndWithSeparator` | `false` | When `false`, trailing separators are stripped |
| `Culture` | `null` | Culture for the casing transformation. `null` applies invariant casing. |
| `AllowedRanges` | A built-in set of Unicode ranges | Characters kept in the slug. An empty list admits every character. |
| `Replacements` | A built-in set | Text replacements applied before slugging |

### Design and runtime behavior

- `Replacements` replaces the default set entirely. Replacements run after the input is normalized to NFD, and each key is stored in NFD, so a key such as `ä` matches the composed and decomposed spellings alike (`["ä"] = "ae"` turns `Bär` into `baer`). Two keys that are the same text once normalized throw `ArgumentException` on assignment.
- When the `MaximumLength` cut would split a surrogate pair (possible only when `AllowedRanges` is empty), the slug is one unit shorter instead.
- A lone surrogate in the input is dropped, not thrown on, so malformed user text still yields a slug.
