---
domain: Core
packages: Base, BuildingBlocks, Checks, Domain, Domain.LocalEventBus, Security.Abstractions, Security
---

# Core

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Extensions](#headlessextensions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Core](#headlesscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Checks](#headlesschecks)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Domain](#headlessdomain)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Domain.LocalEventBus](#headlessdomainlocaleventbus)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Security.Abstractions](#headlesssecurityabstractions)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Security](#headlesssecurity)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)

> Foundational utilities, DDD building blocks, guard clauses, multi-tenancy, and domain messaging for the Headless framework.

## Quick Orientation

- **`Headless.Extensions`** — utility extensions, domain primitives (`UserId`, `AccountId`, `Money`, `PhoneNumber`), result pattern (`ApiResult<T>`, `Result<TValue, TError>`), error hierarchy (`ResultError`, `NotFoundError`, `ValidationError`), GUID generation (`SequentialGuid`, `IGuidGenerator`), collection helpers, pagination, constants (`JwtClaimTypes`, `RegexPatterns`, `HttpHeaderNames`), and validators.
- **`Headless.Core`** — cross-cutting abstractions: `IClock`, `ICurrentUser`, `ICurrentTenant`, `ICurrentLocale`, `ICurrentTimeZone`, `ITimezoneProvider`, `ICurrentPrincipalAccessor`, plus utilities (`SnappyCompressor`, `LogState` structured logging) and `AddHeadlessGuidGenerator()` for keyed GUID strategy registration.
- **`Headless.Security.Abstractions`** — security contracts and options: `IStringEncryptionService`, `IStringHashService`, `StringEncryptionOptions`, `StringHashOptions`, and their validators. `IStringHashService.Create(...)` supports an optional salt and can fall back to `StringHashOptions.DefaultSalt` or an empty salt when no default is configured.
- **`Headless.Security`** — default implementations and DI helpers for string encryption and hashing. `AddStringEncryptionService(...)` and `AddStringHashService(...)` are idempotent: the first registration wins.
- **`Headless.Checks`** — guard clause library with `Argument` (preconditions) and `Ensure` (runtime assertions).
- **`Headless.Domain`** — DDD abstractions: `Entity`, `AggregateRoot`, `ValueObject`, auditing interfaces, concurrency stamps, and event contracts. Domain (in-process) events use `IDomainEvent` + `IDomainEventEmitter`; integration (distributed) events use `IIntegrationEvent` + `IIntegrationEventEmitter`. `AggregateRoot` implements both emitters; integration events are dispatched by the ORM/messaging layer, not from this package (see [orm.md](orm.md)).
- **`Headless.Domain.LocalEventBus`** — DI-based `ILocalEventBus` for in-process domain event dispatch. Register with `AddHeadlessLocalEventBus()` and implement `IDomainEventHandler<T>`. Namespace stays `Headless.Domain`; only the package/assembly name changed from `Headless.Domain.LocalPublisher`.

## Agent Instructions

- Use `Headless.Checks` (`Argument.IsNotNull`, `Argument.IsNotNullOrEmpty`, `Argument.IsPositive`, etc.) for argument validation instead of raw `ArgumentNullException` or `ArgumentOutOfRangeException`. Use `Ensure` for internal state assertions.
- Use `Headless.Domain` base classes for DDD: inherit `Entity<T>` for entities, `AggregateRoot<T>` for aggregate roots, `ValueObject` for value objects. Emit in-process events via `AddDomainEvent()` and distributed events via `AddIntegrationEvent()` on aggregate roots.
- Use `Headless.Core` for `ICurrentUser`, `ICurrentTenant`, and `IClock` — never use `DateTime.UtcNow` directly; inject `IClock` instead.
- Use `ApiResult<T>` / `ApiResult` from `Headless.Extensions` for service return types instead of throwing exceptions for expected failures. Use `Result<TValue, TError>` when you need custom error types.
- For local (in-process) domain events, register `AddHeadlessLocalEventBus()` and implement `IDomainEventHandler<T>`. Use `DomainEventHandlerOrderAttribute` to control handler execution order. For integration (distributed) events, emit `IIntegrationEvent` via `AddIntegrationEvent()` on the aggregate; dispatch is handled by the ORM/messaging layer (see [orm.md](orm.md)), not by this package.
- For strongly-typed IDs, use the primitives from `Headless.Extensions` (`UserId`, `AccountId`) — they have source-generated JSON and TypeConverter support.
- Auditing interfaces (`ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`) are marker interfaces — the ORM layer fills the properties automatically.
- `Headless.Extensions` has no configuration. Register GUID generation through `AddHeadlessGuidGenerator()` only from host/package setup; persisted backends should resolve `SequentialGuidType.Version7` or `SequentialGuidType.SqlServer` by key instead of depending on the unkeyed default.
- `Headless.Settings.Core` requires `IStringEncryptionService` to be registered before `AddHeadlessSettings(...)`. Recommended: bind `Headless:StringEncryption` with `AddStringEncryptionService(...)`.
- Use `Polly.Core`'s `ResiliencePipelineBuilder().AddRetry(...)` for retry logic with exponential backoff and jitter. Build the pipeline once per operation class (e.g. one for transient-Redis-error retries, one for status-check retries) and reuse it. `Polly.Core` has zero transitive dependencies on `net10.0`.
- Use `LogState` with `HeadlessLoggerExtensions` for structured logging with tags and properties.
- For string encryption, inject `IStringEncryptionService` (from `Headless.Security.Abstractions`) and register the implementation once with `AddStringEncryptionService(...)` (from `Headless.Security`). The first registration wins — do not call it twice.
- `IStringHashService` is a deterministic keyed lookup digest (PBKDF2), **not** a password hasher. Use it for blind indexes over encrypted columns. For password storage use ASP.NET Core's `PasswordHasher<T>`.
- `StringEncryptionOptions.DefaultPassPhrase` and `DefaultSalt` are required; both are validated at startup. A missing or empty value is a startup error.

---

## Headless.Extensions

Foundational utility library providing extension methods, primitives, helpers, and common abstractions used throughout the framework.

### Problem Solved

Eliminates repetitive utility code across projects by providing a comprehensive set of battle-tested extensions, helpers, and primitives for common operations (strings, collections, dates, IO, reflection, etc.).

### Key Features

- **Result Pattern**:
    - `ApiResult` / `ApiResult<T>` - Success/failure with built-in error factories (`NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `ValidationFailed`)
    - `Result<TValue, TError>` / `Result<TError>` - Flexible result types with custom error types
    - `ResultError` hierarchy - `NotFoundError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, `AggregateError`
    - `ErrorDescriptor` - Standardized error reporting with code, description, severity, and params

- **Primitives** (Source-generated with JSON/TypeConverter support):
    - `UserId` / `AccountId` - Strongly-typed identifiers
    - `Money` - Currency-aware decimal with arithmetic operators
    - `Month` - Month representation
    - `PhoneNumber` - E.164 phone number
    - `Image` / `File` - Media metadata
    - `PageMetadata` - SEO metadata
    - `TenantInformation` - Tenant data

- **Domain Value Objects**: `Currency`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`, `OrderBy`, `NameValue<T>`, `ExtraProperties`, `Locales`, `TimeUnit`
- **ID Generation**: `IGuidGenerator` (`SequentialGuidGenerator` supporting time-ordered `Version7` and SQL Server comb `SqlServer` strategies)
- **Pagination**: `IndexPageRequest`/`IndexPage<T>` and `ContinuationPageRequest`/`ContinuationPage<T>`
- **Collections**: `ParallelForEachAsync`, `DetectChanges`, `EquatableArray<T>`, `ComparerFactory`, `TypeList`
- **Threading**: `KeyedAsyncLock` — per-key async mutual exclusion with an optional `TimeProvider`-driven acquisition timeout (returns `null` instead of blocking when the wait elapses)
- **LINQ**: `PredicateBuilder` for composing EF Core expressions (`And`, `Or`, `Not`)
- **Dates & Time**: Fluent date manipulation, `TimeProvider` extensions, timezone conversion, `ChangeableTimezoneTimeProvider`
- **Strings**: Humanize integration, truncation, manipulation helpers
- **Constants**: `RegexPatterns` (email, URL, IP, etc.), `ContentTypes`, `HttpHeaderNames`, `JwtClaimTypes`, `UserClaimTypes`, `LanguageCodes`
- **Reflection**: Fast property accessors, type scanning, IL emit helpers, `AssemblyInformation`
- **HTTP**: `BasicAuthenticationValue`, `HttpStatusCodeExtensions`
- **Exceptions**: `EntityNotFoundException`, `ConflictException`
- **Validation**: `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator`, `EgyptianNationalIdValidator`
- **Helpers**: `OsHelper`, `DisposableFactory`, `IpAddressHelper`

### Installation

```bash
dotnet add package Headless.Extensions
```

### Quick Start

#### Result Pattern

```csharp
public async Task<ApiResult<User>> GetUserAsync(Guid id)
{
    var user = await _repo.GetByIdAsync(id);
    if (user is null)
        return ApiResult<User>.NotFound();

    return ApiResult<User>.Ok(user);
}
```

#### Collection Extensions

```csharp
await users.ParallelForEachAsync(async user => await ProcessUserAsync(user), maxDegreeOfParallelism: 5);
```

#### Egyptian National ID Validator

```csharp
if (EgyptianNationalIdValidator.IsValid("29901011234567"))
{
    var info = EgyptianNationalIdValidator.Analyze("29901011234567");
    var birthDate = info.BirthDate;
    var governorate = info.Governorate;
}
```

### Configuration

No configuration required.

### Dependencies

- `Headless.Checks`
- `Headless.Generator.Primitives` (source generator)
- `Headless.Generator.Primitives.Abstractions`
- `CommunityToolkit.HighPerformance`
- `Humanizer.Core`
- `libphonenumber-csharp`
- `Microsoft.Bcl.TimeProvider`
- `MimeTypes`
- `morelinq`
- `Nito.AsyncEx`
- `Nito.Disposables`
- `Polly.Core`
- `System.Reactive`
- `TimeZoneConverter`

### Side Effects

None.

## Headless.Core

Core abstractions for building applications with multi-tenancy, user context, and cross-cutting concerns.

### Problem Solved

Provides standardized interfaces for common cross-cutting concerns (clock, user, tenant, locale, timezone conversion) and utilities (compression, structured logging) enabling consistent patterns across all application layers.

### Key Features

- **Abstractions**:
    - `IClock` - Testable time abstraction (wraps `TimeProvider`)
    - `ICurrentUser` - Current authenticated user context with roles and claims
    - `ICurrentTenant` - Multi-tenancy support with scoped tenant switching
    - `ITenantWriteGuardBypass` - Explicit bypass scope for audited host/admin tenant writes
    - `CrossTenantWriteException` / `MissingTenantContextException` - tenant write-guard exceptions (non-transient; exclude from retry)
    - `ICorrelationIdProvider` / `ActivityCorrelationIdProvider` - correlation ID for tracing, audit, and structured logging
    - `ICurrentLocale` - Localization context (language, locale, culture)
    - `ICurrentTimeZone` - Timezone handling
    - `ICurrentPrincipalAccessor` - Scoped `ClaimsPrincipal` access with temporary switching
    - `IPasswordGenerator` - Configurable secure password generation
    - `ICancellationTokenProvider` - Cancellation token access with fallback logic
    - `ITimezoneProvider` - Windows/IANA timezone conversion and listing
    - `IApplicationInformationAccessor` / `IBuildInformationAccessor` - Application metadata and build info
    - `IEnumLocaleAccessor` - Localized enum display values

- **Utilities**:
    - `SnappyCompressor` - Snappy compression/decompression with JSON serialization (AOT-compatible)
    - `LogState` / `HeadlessLoggerExtensions` - Structured logging with fluent state builder, tags, and scoped properties
    - `AddHeadlessGuidGenerator()` - registers keyed `IGuidGenerator` strategies for Version7 and SQL Server GUID ordering, plus an unkeyed backend-agnostic default

### Installation

```bash
dotnet add package Headless.Core
```

### Quick Start

```csharp
public sealed class OrderService(IClock clock, ICurrentUser user, ICurrentTenant tenant)
{
    public Order CreateOrder(CreateOrderRequest request)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId!.Value,
            TenantId = tenant.Id,
            CreatedAt = clock.UtcNow,
            Total = new Money(request.Amount, request.Currency),
        };
    }
}
```

#### Structured Logging

```csharp
logger.LogInformation(s => s.Tag("orders").Property("orderId", orderId), "Order {OrderId} created", orderId);
```

#### Retry and Deferred Execution

For retries and delayed execution, use `Polly.Core` directly — it ships zero transitive dependencies on `net10.0`:

```csharp
private static readonly ResiliencePipeline _RetryPipeline = new ResiliencePipelineBuilder()
    .AddRetry(
        new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(1),
            UseJitter = true,
        }
    )
    .Build();

var result = await _RetryPipeline.ExecuteAsync(
    async ct => await httpClient.GetAsync(url, ct).ConfigureAwait(false),
    cancellationToken
);
```

### Configuration

No configuration required for the abstractions. Host/package setup can call `AddHeadlessGuidGenerator()` when it needs the framework GUID generator defaults.

### Dependencies

- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Serializer.Json`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Snappier`

### Side Effects

- `AddHeadlessGuidGenerator()` registers keyed singleton `IGuidGenerator` strategies for `SequentialGuidType.Version7` and `SequentialGuidType.SqlServer`
- `AddHeadlessGuidGenerator()` also registers an unkeyed singleton `IGuidGenerator` using `Version7` unless a caller supplies another default strategy

## Headless.Checks

Guard clause library for argument validation and defensive programming.

### Problem Solved

Provides a fluent, expressive API for validating method arguments and ensuring preconditions, eliminating boilerplate validation code and standardizing error messages.

### Key Features

- **Argument Validation**: Extensive static methods on `Argument` class
- **Runtime Assertions**: `Ensure` class for internal state validation
- **Performance Optimized**: `AggressiveInlining` and `DebuggerStepThrough`
- **Caller Expression Support**: Automatic parameter name capture
- **Type Support**: Nullable, `Span<T>`, `ReadOnlySpan<T>`, collections, strings

### Installation

```bash
dotnet add package Headless.Checks
```

### Quick Start

#### Argument Validation

```csharp
using Headless.Checks;

public void CreateUser(string name, int age, List<string> roles)
{
    Argument.IsNotNullOrEmpty(name);
    Argument.IsPositive(age);
    Argument.IsNotNullOrEmpty(roles);
    Argument.HasNoNulls(roles);
}
```

#### Common Checks

- `Argument.IsNotNull(value)`
- `Argument.IsNotNullOrEmpty(string|collection)`
- `Argument.IsNotNullOrWhiteSpace(string)`
- `Argument.IsNotEmpty(guid)` — rejects `Guid.Empty` (also `Guid?`; null passes through)
- `Argument.IsPositive(number)` / `IsNegative` / `IsPositiveOrZero` / `IsNegativeOrZero`
- `Argument.IsZero(number)` / `IsNotZero(number)` — `INumber<T>`, nullable, and `TimeSpan` overloads
- `Argument.IsEqualTo(value, expected)` / `IsNotEqualTo(value, other)` — value equality (optional `IEqualityComparer<T>` overload); contrast `IsReferenceEqualTo`/`IsReferenceNotEqualTo` for identity
- `Argument.IsCloseTo(value, target, delta)` / `IsNotCloseTo(…)` — tolerance comparison for `INumber<T>`; prefer over `IsEqualTo` for `float`/`double`
- `Argument.IsBitwiseEqualTo(value, target)` — raw-byte equality for `unmanaged` types (distinguishes `+0.0`/`-0.0`, matches identical NaN payloads)
- `Argument.IsOneOf(value, allowedValues)`
- `Argument.IsInEnum(enumValue)`
- `Argument.HasNoNulls(collection)` / `HasNoDuplicates(collection)` — `HasNoDuplicates` takes an optional `IEqualityComparer<T>`
- `Argument.HasLength` / `HasMinLength` / `HasMaxLength` / `HasLengthBetween(string, …)` — string length bounds (throw `ArgumentOutOfRangeException`)
- `Argument.HasCount` / `HasMinCount` / `HasMaxCount` / `HasCountBetween(collection, …)` — item-count bounds (`IReadOnlyCollection<T>` fast-path + `IEnumerable<T>`)
- `Argument.StartsWith` / `EndsWith` / `Contains(string, value, comparison)` — string content (`StringComparison.Ordinal` by default)
- `Argument.IsInRangeFor(index, count | collection | span)` — bounds-checks an index against a length/collection/span
- `Argument.FileExists(path)` / `DirectoryExists(path)`
- `Argument.Matches(string, regex)` — throws `ArgumentException` when the string does not match the pattern
- `Argument.Is(condition, message, nameof(arg))` / `IsFalse(condition, …)` — custom argument precondition that must hold / must not hold; throws `ArgumentException`

#### Runtime Assertions

```csharp
using Headless.Checks;

public void ProcessOrder()
{
    Ensure.True(_initialized, "Service must be initialized.");
    Ensure.NotDisposed(_disposed, this);
    Ensure.False(_queue.IsEmpty, "Queue should not be empty.");
    var connection = Ensure.NotNull(_connection); // state must-be-present; throws InvalidOperationException
}
```

### Configuration

No configuration required.

### Dependencies

None.

### Side Effects

None.

## Headless.Domain

Core domain-driven design abstractions including entities, aggregate roots, value objects, auditing, and messaging interfaces.

### Problem Solved

Provides building blocks for implementing DDD patterns: entities with identity, aggregate roots with domain events, value objects, auditing interfaces, and messaging contracts.

### Key Features

- **Entity Abstractions**: `IEntity`, `IEntity<T>`, base `Entity` class
- **Aggregate Roots**: `IAggregateRoot`, `AggregateRoot` with built-in message emission
- **Value Objects**: `ValueObject` base class with equality
- **Auditing**: `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`
- **Concurrency**: `IHasConcurrencyStamp`, `IHasETag`
- **Multi-tenancy**: `IMultiTenant`
- **Domain Events (in-process)**: `IDomainEvent`, `IDomainEventEmitter`, `IDomainEventHandler<T>`, `DomainEventHandlerOrderAttribute`. `AggregateRoot` implements `IDomainEventEmitter` (`AddDomainEvent`, `ClearDomainEvents`, `GetDomainEvents`). Dispatch is provided by `Headless.Domain.LocalEventBus`.
- **Integration Events (distributed)**: `IIntegrationEvent`, `IIntegrationEventEmitter`. `AggregateRoot` implements `IIntegrationEventEmitter` (`AddIntegrationEvent`, `ClearIntegrationEvents`, `GetIntegrationEvents`). This package only defines the contract and the emitter — integration events are dispatched by the ORM/messaging layer (`Headless.Orm.EntityFramework.Messaging`), not from `Headless.Domain` (see [orm.md](orm.md)).
- **Entity Events**: `EntityCreatedEventData`, `EntityUpdatedEventData`, `EntityDeletedEventData`

### Installation

```bash
dotnet add package Headless.Domain
```

### Quick Start

```csharp
public sealed class Order : AggregateRoot<Guid>, ICreateAudit
{
    public required string CustomerName { get; init; }
    public decimal Total { get; private set; }
    public DateTimeOffset DateCreated { get; set; }

    public void Complete()
    {
        Status = OrderStatus.Completed;
        AddDomainEvent(new OrderCompletedEvent(Id));
    }
}

public sealed record OrderCompletedEvent(Guid OrderId) : IDomainEvent
{
    public string UniqueId { get; } = Guid.NewGuid().ToString();
}
```

#### Auditing

Implement audit interfaces for automatic tracking:

```csharp
public sealed class Product : Entity<int>, ICreateAudit, IUpdateAudit
{
    public required string Name { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset? DateUpdated { get; set; }
}
```

#### Value Objects

```csharp
public sealed class Address : ValueObject
{
    public required string Street { get; init; }
    public required string City { get; init; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Street;
        yield return City;
    }
}
```

### Configuration

No configuration required. This is an abstractions package.

### Dependencies

None.

### Side Effects

None.

## Headless.Domain.LocalEventBus

DI-based implementation of `ILocalEventBus` for in-process domain event handling.

### Problem Solved

Provides in-memory domain event dispatch that resolves handlers from the DI container, enabling decoupled event-driven architecture within a single process and unit of work.

### Key Features

- `ILocalEventBus` implementation (`ServiceProviderLocalEventBus`) backed by DI
- Generic and non-generic publish overloads (`Publish`, `PublishAsync`)
- Handler resolution per publish from the active scope
- Handler ordering via `DomainEventHandlerOrderAttribute`
- Handler exception aggregation and cooperative cancellation

#### Design Notes

- **Non-generic runtime-typed dispatch.** `Publish(IDomainEvent)` / `PublishAsync(IDomainEvent)` dispatch to handlers of the event's exact runtime type — there is no contravariant traversal to base types or implemented interfaces. The runtime type is mapped to a compiled invoker that is built once and cached, so repeated publishes of the same concrete type avoid reflection on the hot path. The generic overloads (`Publish<T>` / `PublishAsync<T>`) dispatch against the static type argument `T`.
- **Scoped lifetime.** `AddHeadlessLocalEventBus()` registers `ILocalEventBus` as scoped (`TryAddScoped`). Handlers are resolved from the caller's scope, so they share the same scoped services — notably the `DbContext` — when published inside a unit of work.
- **Exception aggregation and cancellation.** Handlers are resolved and invoked per publish. A single handler exception is rethrown as-is; multiple handler exceptions are wrapped in an `AggregateException`. Cancellation is observed between handlers; if the token is cancelled, already-accumulated handler exceptions are preserved rather than discarded.
- **Namespace unchanged.** The package/assembly was renamed from `Headless.Domain.LocalPublisher`, but the namespace stays `Headless.Domain` — no `using` changes are needed.

### Installation

```bash
dotnet add package Headless.Domain.LocalEventBus
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the in-process local event bus
builder.Services.AddHeadlessLocalEventBus();

// Register handlers
builder.Services.AddScoped<IDomainEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

#### Publishing Events

```csharp
public sealed class OrderService(ILocalEventBus eventBus)
{
    public async Task CreateOrderAsync(Order order, CancellationToken ct)
    {
        await _repository.AddAsync(order, ct).ConfigureAwait(false);

        await eventBus.PublishAsync(new OrderCreatedEvent(order.Id), ct).ConfigureAwait(false);
    }
}
```

#### Handling Events

```csharp
public sealed class OrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent domainEvent, CancellationToken ct = default)
    {
        // Send email, update read model, etc.
        return ValueTask.CompletedTask;
    }
}

[DomainEventHandlerOrder(1)] // Execute first
public sealed class AuditHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent domainEvent, CancellationToken ct = default)
    {
        // Audit logging
        return ValueTask.CompletedTask;
    }
}
```

### Configuration

No configuration required.

### Dependencies

- `Headless.Domain`
- `Headless.Hosting`

### Side Effects

- Registers `ILocalEventBus` (`ServiceProviderLocalEventBus`) as scoped

---

## Headless.Security.Abstractions

Security contracts and option models for string encryption and hashing — no implementation, no DI coupling.

### Problem Solved

Allows downstream packages and application layers to depend on encryption and hashing abstractions without referencing a concrete implementation. `Headless.Settings.Core` depends on `IStringEncryptionService` from this package; consuming code can swap the implementation independently.

### Key Features

- **`IStringEncryptionService`** — AES-GCM authenticated encryption contract:
    - `Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null) → string?` — encrypts using the configured default pass phrase / salt, or an explicit override. Returns `null` when `plainText` is `null`. Each call uses a fresh random nonce, so identical plaintexts never produce identical cipher text.
    - `Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null) → string?` — decrypts a Base64 value produced by `Encrypt`. Returns `null` when `cipherText` is `null` or empty. Throws `CryptographicException` when the cipher text is too short, has been tampered with, or the pass phrase / salt does not match.
- **`IStringHashService`** — deterministic PBKDF2 hashing contract:
    - `Create(string value, string? salt = null) → string` — returns a Base64 PBKDF2 hash. Uses `StringHashOptions.DefaultSalt` when `salt` is omitted; falls back to an empty salt when no default is configured. The hash is deterministic: same value + salt always yield the same output. **Not suitable for password storage** (no per-record random salt, no verification primitive — use ASP.NET Core's `PasswordHasher<T>` for passwords).
- **`StringEncryptionOptions`** — `DefaultPassPhrase` (required), `DefaultSalt` (required `byte[]`), `KeySize` (128/192/256 bits; default 256), `Iterations` (PBKDF2 rounds; default 600 000).
- **`StringHashOptions`** — `Algorithm` (SHA256/SHA384/SHA512; default SHA256), `SizeInBytes` (≥16; default 32), `Iterations` (default 600 000), `DefaultSalt` (optional string).

### Installation

```bash
dotnet add package Headless.Security.Abstractions
```

### Quick Start

```csharp
// Inject the contract; the implementation is registered by Headless.Security.
public sealed class SecureSettingService(IStringEncryptionService encryption, IStringHashService hashing)
{
    // Encrypt a sensitive value (e.g. before writing to the database).
    public string Protect(string value) => encryption.Encrypt(value)!;

    // Decrypt a value read from the database.
    public string Unprotect(string cipher) => encryption.Decrypt(cipher)!;

    // Produce a deterministic lookup hash (e.g. blind index over an encrypted column).
    public string BlindIndex(string value, string tenantSalt) => hashing.Create(value, tenantSalt);
}
```

### Configuration

No configuration required. This is an abstractions-only package; options are configured when registering the implementation via `Headless.Security`.

### Dependencies

None.

### Side Effects

None.

---

## Headless.Security

Default implementations of `IStringEncryptionService` and `IStringHashService`, plus idempotent DI registration helpers.

### Problem Solved

Ships the concrete AES-GCM encryption and PBKDF2 hashing implementations so application code depends only on the `Headless.Security.Abstractions` contracts. Keeps security concerns separate from `Headless.Core` and `Headless.Api`.

### Key Features

- **`StringEncryptionService`** — `IStringEncryptionService` implementation using AES-GCM with PBKDF2-SHA256 key derivation. Derives the default key once at construction; per-call key derivation only when pass phrase / salt overrides are supplied. Output format: `Base64(nonce[12] || tag[16] || cipherText)`.
- **`StringHashService`** — `IStringHashService` implementation using `Rfc2898DeriveBytes.Pbkdf2`. Output: `Base64(hash[SizeInBytes])`. The call-site salt falls back to `StringHashOptions.DefaultSalt ?? string.Empty`.
- **`AddStringEncryptionService(IConfiguration)`** / **`AddStringEncryptionService(Action<StringEncryptionOptions>)`** / **`AddStringEncryptionService(Action<StringEncryptionOptions, IServiceProvider>)`** — three overloads for binding `StringEncryptionOptions`. All are idempotent: the first registration wins.
- **`AddStringHashService(IConfiguration)`** / **`AddStringHashService(Action<StringHashOptions>)`** / **`AddStringHashService(Action<StringHashOptions, IServiceProvider>)`** — three overloads for binding `StringHashOptions`. All are idempotent.

### Design Notes

- **Idempotency.** Both `AddStringEncryptionService` and `AddStringHashService` use `TryAddSingleton` under a prior-registration guard — calling either more than once is safe and the second call is silently ignored. Configure each service exactly once.
- **AES-GCM nonce.** A fresh 12-byte random nonce is generated via `RandomNumberGenerator.Fill` for every `Encrypt` call. This guarantees ciphertext indistinguishability even when the same plaintext is encrypted multiple times with the same key.
- **PBKDF2 key caching.** The default encryption key (derived from `DefaultPassPhrase` + `DefaultSalt` at construction) is cached as a `byte[]` singleton on the service instance. Overriding the pass phrase or salt on a per-call basis re-derives the key inline and is therefore slower. Design for the common case: configure the default key and use overrides only for rare multi-key scenarios.
- **`StringHashService` is not a password hasher.** The hash has no embedded salt, no algorithm identifier, and no cost parameter — it is a fast keyed lookup digest. Do not use it for storing user passwords; use ASP.NET Core's `PasswordHasher<T>` instead.

### Installation

```bash
dotnet add package Headless.Security
```

### Quick Start

#### String Encryption

```csharp
// appsettings.json section: "Headless:StringEncryption"
builder.Services.AddStringEncryptionService(builder.Configuration.GetSection("Headless:StringEncryption"));

// Or configure inline (useful in tests / single-file apps).
builder.Services.AddStringEncryptionService(options =>
{
    options.DefaultPassPhrase = "your-secret-pass-phrase";
    options.DefaultSalt = "your-salt-bytes"u8.ToArray();
    // options.KeySize     = 256;   // default
    // options.Iterations  = 600_000; // default
});
```

#### String Hashing

```csharp
builder.Services.AddStringHashService(options =>
{
    options.DefaultSalt = "global-app-salt";
    // options.Algorithm    = HashAlgorithmName.SHA256; // default
    // options.SizeInBytes  = 32;     // default
    // options.Iterations   = 600_000; // default
});

// Usage: produce a blind index for searching an encrypted column.
public string GetSearchKey(string value, string tenantId)
    => _hashService.Create(value, tenantId); // tenant-scoped deterministic hash
```

### Configuration

`StringEncryptionOptions`:

| Property | Default | Constraint |
|---|---|---|
| `DefaultPassPhrase` | — (required) | Non-empty string |
| `DefaultSalt` | — (required) | Non-empty `byte[]` |
| `KeySize` | 256 | 128, 192, or 256 |
| `Iterations` | 600 000 | > 0 |

`StringHashOptions`:

| Property | Default | Constraint |
|---|---|---|
| `Algorithm` | `SHA256` | SHA256, SHA384, or SHA512 |
| `SizeInBytes` | 32 | ≥ 16 |
| `Iterations` | 600 000 | > 0 |
| `DefaultSalt` | `null` | Optional string |

Both option types are validated via FluentValidation at startup (`ValidateOnStart`). A misconfigured `KeySize` or unsupported `Algorithm` is a startup error, not a runtime error.

### Dependencies

- `Headless.Security.Abstractions`
- `Headless.Checks`
- `Headless.Hosting`
- `FluentValidation`

### Side Effects

- `AddStringEncryptionService(...)` registers `IStringEncryptionService` (`StringEncryptionService`) as a singleton and registers validated `StringEncryptionOptions`.
- `AddStringHashService(...)` registers `IStringHashService` (`StringHashService`) as a singleton and registers validated `StringHashOptions`.
