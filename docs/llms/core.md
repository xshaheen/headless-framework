---
domain: Core
packages: Base, BuildingBlocks, Checks, Domain, Domain.LocalPublisher, Security.Abstractions, Security
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
    - [Result Pattern](#result-pattern)
    - [Collection Extensions](#collection-extensions)
    - [Egyptian National ID Validator](#egyptian-national-id-validator)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Core](#headlesscore)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start-1)
    - [Structured Logging](#structured-logging)
    - [Retry with Backoff](#retry-with-backoff)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Checks](#headlesschecks)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-2)
    - [Argument Validation](#argument-validation)
    - [Common Checks](#common-checks)
    - [Runtime Assertions](#runtime-assertions)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Domain](#headlessdomain)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-3)
    - [Auditing](#auditing)
    - [Value Objects](#value-objects)
  - [Configuration](#configuration-3)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)
- [Headless.Domain.LocalPublisher](#headlessdomainlocalpublisher)
  - [Problem Solved](#problem-solved-4)
  - [Key Features](#key-features-4)
  - [Installation](#installation-4)
  - [Quick Start](#quick-start-4)
    - [Publishing Events](#publishing-events)
    - [Handling Events](#handling-events)
  - [Configuration](#configuration-4)
  - [Dependencies](#dependencies-4)
  - [Side Effects](#side-effects-4)

> Foundational utilities, DDD building blocks, guard clauses, multi-tenancy, and domain messaging for the Headless framework.

## Quick Orientation

- **`Headless.Extensions`** — utility extensions, domain primitives (`UserId`, `AccountId`, `Money`, `PhoneNumber`), result pattern (`ApiResult<T>`, `Result<TValue, TError>`), error hierarchy (`ResultError`, `NotFoundError`, `ValidationError`), ID generation (`SnowflakeId`, `SequentialGuid`, `IGuidGenerator`, `ILongIdGenerator`), collection helpers, pagination, constants (`JwtClaimTypes`, `RegexPatterns`, `HttpHeaderNames`), and validators.
- **`Headless.Core`** — cross-cutting abstractions: `IClock`, `ICurrentUser`, `ICurrentTenant`, `ICurrentLocale`, `ICurrentTimeZone`, `ITimezoneProvider`, `ICurrentPrincipalAccessor`, plus utilities (`Run` retry helper, `SnappyCompressor`, `LogState` structured logging).
- **`Headless.Security.Abstractions`** — security contracts and options: `IStringEncryptionService`, `IStringHashService`, `StringEncryptionOptions`, `StringHashOptions`, and their validators. `IStringHashService.Create(...)` supports an optional salt and can fall back to `StringHashOptions.DefaultSalt` or an empty salt when no default is configured.
- **`Headless.Security`** — default implementations and DI helpers for string encryption and hashing. `AddStringEncryptionService(...)` and `AddStringHashService(...)` are idempotent: the first registration wins.
- **`Headless.Checks`** — guard clause library with `Argument` (preconditions) and `Ensure` (runtime assertions).
- **`Headless.Domain`** — DDD abstractions: `Entity`, `AggregateRoot`, `ValueObject`, auditing interfaces, concurrency stamps, and local/distributed messaging contracts (`ILocalMessage`, `IDistributedMessage`).
- **`Headless.Domain.LocalPublisher`** — DI-based `ILocalMessagePublisher` for in-process event handling. Register with `AddLocalMessagePublisher()` and implement `ILocalMessageHandler<T>`.

## Agent Instructions

- Use `Headless.Checks` (`Argument.IsNotNull`, `Argument.IsNotNullOrEmpty`, `Argument.IsPositive`, etc.) for argument validation instead of raw `ArgumentNullException` or `ArgumentOutOfRangeException`. Use `Ensure` for internal state assertions.
- Use `Headless.Domain` base classes for DDD: inherit `Entity<T>` for entities, `AggregateRoot<T>` for aggregate roots, `ValueObject` for value objects. Emit domain events via `AddMessage()` on aggregate roots.
- Use `Headless.Core` for `ICurrentUser`, `ICurrentTenant`, and `IClock` — never use `DateTime.UtcNow` directly; inject `IClock` instead.
- Use `ApiResult<T>` / `ApiResult` from `Headless.Extensions` for service return types instead of throwing exceptions for expected failures. Use `Result<TValue, TError>` when you need custom error types.
- For local (in-process) domain events, register `AddLocalMessagePublisher()` and implement `ILocalMessageHandler<T>`. Use `LocalEventHandlerOrderAttribute` to control handler execution order.
- For strongly-typed IDs, use the primitives from `Headless.Extensions` (`UserId`, `AccountId`) — they have source-generated JSON and TypeConverter support.
- Auditing interfaces (`ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`) are marker interfaces — the ORM layer fills the properties automatically.
- `Headless.Extensions` has no configuration. `Headless.Core` implementations are registered by `Headless.Api` or other host packages — do not register them manually.
- `Headless.Settings.Core` requires `IStringEncryptionService` to be registered before `AddSettingsManagementCore(...)`. Recommended: bind `Headless:StringEncryption` with `AddStringEncryptionService(...)`.
- Use `Run.WithRetriesAsync()` from `Headless.Core` for retry logic with exponential backoff instead of manual retry loops.
- Use `LogState` with `LoggerExtensions` for structured logging with tags and properties.

---
# Headless.Extensions

Foundational utility library providing extension methods, primitives, helpers, and common abstractions used throughout the framework.

## Problem Solved

Eliminates repetitive utility code across projects by providing a comprehensive set of battle-tested extensions, helpers, and primitives for common operations (strings, collections, dates, IO, reflection, etc.).

## Key Features

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
- **ID Generation**: `IGuidGenerator` (sequential GUIDs for SQL Server/MySQL/Oracle), `ILongIdGenerator` (`SnowflakeId`)
- **Pagination**: `IndexPageRequest`/`IndexPage<T>` and `ContinuationPageRequest`/`ContinuationPage<T>`
- **Collections**: `ParallelForEachAsync`, `DetectChanges`, `EquatableArray<T>`, `ComparerFactory`, `TypeList`
- **LINQ**: `PredicateBuilder` for composing EF Core expressions (`And`, `Or`, `Not`)
- **Dates & Time**: Fluent date manipulation, `TimeProvider` extensions, timezone conversion, `ChangeableTimezoneTimeProvider`
- **Strings**: Humanize integration, truncation, manipulation helpers
- **Constants**: `RegexPatterns` (email, URL, IP, etc.), `ContentTypes`, `HttpHeaderNames`, `JwtClaimTypes`, `UserClaimTypes`, `LanguageCodes`
- **Reflection**: Fast property accessors, type scanning, IL emit helpers, `AssemblyInformation`
- **HTTP**: `BasicAuthenticationValue`, `HttpStatusCodeExtensions`
- **Exceptions**: `EntityNotFoundException`, `ConflictException`
- **Validation**: `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator`, `EgyptianNationalIdValidator`
- **Helpers**: `OsHelper`, `DisposableFactory`, `IpAddressHelper`

## Installation

```bash
dotnet add package Headless.Extensions
```

## Quick Start

### Result Pattern

```csharp
public async Task<ApiResult<User>> GetUserAsync(Guid id)
{
    var user = await _repo.GetByIdAsync(id);
    if (user is null)
        return ApiResult<User>.NotFound();

    return ApiResult<User>.Ok(user);
}
```

### Collection Extensions

```csharp
await users.ParallelForEachAsync(
    async user => await ProcessUserAsync(user),
    maxDegreeOfParallelism: 5
);
```

### Egyptian National ID Validator

```csharp
if (EgyptianNationalIdValidator.IsValid("29901011234567"))
{
    var info = EgyptianNationalIdValidator.Analyze("29901011234567");
    var birthDate = info.BirthDate;
    var governorate = info.Governorate;
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Checks`
- `Headless.Generator.Primitives` (source generator)
- `Headless.Generator.Primitives.Abstractions`
- `CommunityToolkit.HighPerformance`
- `Humanizer.Core`
- `IdGen`
- `libphonenumber-csharp`
- `Microsoft.Bcl.TimeProvider`
- `MimeTypes`
- `morelinq`
- `Nito.AsyncEx`
- `Nito.Disposables`
- `Polly.Core`
- `System.Reactive`
- `TimeZoneConverter`

## Side Effects

None.
---
# Headless.Core

Core abstractions for building applications with multi-tenancy, user context, and cross-cutting concerns.

## Problem Solved

Provides standardized interfaces for common cross-cutting concerns (clock, user, tenant, locale, timezone conversion) and utilities (retry logic, compression, structured logging) enabling consistent patterns across all application layers.

## Key Features

- **Abstractions**:
  - `IClock` - Testable time abstraction (wraps `TimeProvider`)
  - `ICurrentUser` - Current authenticated user context with roles and claims
  - `ICurrentTenant` - Multi-tenancy support with scoped tenant switching
  - `ICurrentLocale` - Localization context (language, locale, culture)
  - `ICurrentTimeZone` - Timezone handling
  - `ICurrentPrincipalAccessor` - Scoped `ClaimsPrincipal` access with temporary switching
  - `IPasswordGenerator` - Configurable secure password generation
  - `ICancellationTokenProvider` - Cancellation token access with fallback logic
  - `ITimezoneProvider` - Windows/IANA timezone conversion and listing
  - `IApplicationInformationAccessor` / `IBuildInformationAccessor` - Application metadata and build info
  - `IEnumLocaleAccessor` - Localized enum display values
  - `IHaveLogger` / `IHaveTimeProvider` - Mixin interfaces for logger and time provider access

- **Utilities**:
  - `Run` - Retry helper with exponential backoff (`WithRetriesAsync`, `DelayedAsync`)
  - `SnappyCompressor` - Snappy compression/decompression with JSON serialization (AOT-compatible)
  - `LogState` / `LoggerExtensions` - Structured logging with fluent state builder, tags, and scoped properties

## Installation

```bash
dotnet add package Headless.Core
```

## Quick Start

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
            Total = new Money(request.Amount, request.Currency)
        };
    }
}
```

### Structured Logging

```csharp
logger.LogInformation(
    s => s.Tag("orders").Property("orderId", orderId),
    "Order {OrderId} created",
    orderId
);
```

### Retry with Backoff

```csharp
var result = await Run.WithRetriesAsync(
    async ct => await httpClient.GetAsync(url, ct),
    maxAttempts: 3,
    logger: logger
);
```

## Configuration

No configuration required. Implementations are registered by `Headless.Api` or other host packages.

## Dependencies

- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Serializer.Json`
- `FluentValidation`
- `Microsoft.Extensions.Logging.Abstractions`
- `Snappier`

## Side Effects

None. This is an abstractions package.
---
# Headless.Checks

Guard clause library for argument validation and defensive programming.

## Problem Solved

Provides a fluent, expressive API for validating method arguments and ensuring preconditions, eliminating boilerplate validation code and standardizing error messages.

## Key Features

- **Argument Validation**: Extensive static methods on `Argument` class
- **Runtime Assertions**: `Ensure` class for internal state validation
- **Performance Optimized**: `AggressiveInlining` and `DebuggerStepThrough`
- **Caller Expression Support**: Automatic parameter name capture
- **Type Support**: Nullable, `Span<T>`, `ReadOnlySpan<T>`, collections, strings

## Installation

```bash
dotnet add package Headless.Checks
```

## Quick Start

### Argument Validation

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

### Common Checks

- `Argument.IsNotNull(value)`
- `Argument.IsNotNullOrEmpty(string|collection)`
- `Argument.IsNotNullOrWhiteSpace(string)`
- `Argument.IsPositive(number)` / `IsNegative` / `IsPositiveOrZero` / `IsNegativeOrZero`
- `Argument.IsOneOf(value, allowedValues)`
- `Argument.IsInEnum(enumValue)`
- `Argument.HasNoNulls(collection)`
- `Argument.FileExists(path)` / `DirectoryExists(path)`

### Runtime Assertions

```csharp
using Headless.Checks;

public void ProcessOrder()
{
    Ensure.True(_initialized, "Service must be initialized.");
    Ensure.NotDisposed(_disposed, this);
    Ensure.False(_queue.IsEmpty, "Queue should not be empty.");
}
```

## Configuration

No configuration required.

## Dependencies

None.

## Side Effects

None.
---
# Headless.Domain

Core domain-driven design abstractions including entities, aggregate roots, value objects, auditing, and messaging interfaces.

## Problem Solved

Provides building blocks for implementing DDD patterns: entities with identity, aggregate roots with domain events, value objects, auditing interfaces, and messaging contracts.

## Key Features

- **Entity Abstractions**: `IEntity`, `IEntity<T>`, base `Entity` class
- **Aggregate Roots**: `IAggregateRoot`, `AggregateRoot` with built-in message emission
- **Value Objects**: `ValueObject` base class with equality
- **Auditing**: `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`
- **Concurrency**: `IHasConcurrencyStamp`, `IHasETag`
- **Multi-tenancy**: `IMultiTenant`
- **Local Messaging**: `ILocalMessage`, `ILocalMessagePublisher`, `ILocalMessageHandler`
- **Distributed Messaging**: `IDistributedMessage`, `IDistributedMessagePublisher`, `IDistributedMessageHandler`
- **Entity Events**: `EntityCreatedEventData`, `EntityUpdatedEventData`, `EntityDeletedEventData`

## Installation

```bash
dotnet add package Headless.Domain
```

## Quick Start

```csharp
public sealed class Order : AggregateRoot<Guid>, ICreateAudit
{
    public required string CustomerName { get; init; }
    public decimal Total { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public void Complete()
    {
        Status = OrderStatus.Completed;
        AddMessage(new OrderCompletedEvent(Id));
    }
}

public sealed record OrderCompletedEvent(Guid OrderId) : ILocalMessage;
```

### Auditing

Implement audit interfaces for automatic tracking:

```csharp
public sealed class Product : Entity<int>, ICreateAudit, IUpdateAudit
{
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### Value Objects

```csharp
public sealed class Address : ValueObject
{
    public required string Street { get; init; }
    public required string City { get; init; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
    }
}
```

## Configuration

No configuration required. This is an abstractions package.

## Dependencies

None.

## Side Effects

None.
---
# Headless.Domain.LocalPublisher

DI-based implementation of `ILocalMessagePublisher` for in-process domain event handling.

## Problem Solved

Provides in-memory local message publishing that resolves handlers from the DI container, enabling decoupled event-driven architecture within a single process.

## Key Features

- `ILocalMessagePublisher` implementation using DI
- Automatic handler discovery and resolution
- Handler ordering via `LocalEventHandlerOrderAttribute`
- Sync and async publishing support
- Scoped handler resolution

## Installation

```bash
dotnet add package Headless.Domain.LocalPublisher
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register local message publisher
builder.Services.AddLocalMessagePublisher();

// Register handlers (automatically discovered or explicit)
builder.Services.AddScoped<ILocalMessageHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

### Publishing Events

```csharp
public sealed class OrderService(ILocalMessagePublisher publisher)
{
    public async Task CreateOrderAsync(Order order, CancellationToken ct)
    {
        await _repository.AddAsync(order, ct).ConfigureAwait(false);

        await publisher.PublishAsync(new OrderCreatedEvent(order.Id), ct).ConfigureAwait(false);
    }
}
```

### Handling Events

```csharp
public sealed class OrderCreatedHandler : ILocalMessageHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent message, CancellationToken ct)
    {
        // Send email, update read model, etc.
    }
}

[LocalEventHandlerOrder(1)] // Execute first
public sealed class AuditHandler : ILocalMessageHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken ct)
    {
        // Audit logging
        return Task.CompletedTask;
    }
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Domain`
- `Headless.Hosting`

## Side Effects

- Registers `ILocalMessagePublisher` as scoped
