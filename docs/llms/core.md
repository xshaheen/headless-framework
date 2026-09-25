---
domain: Core
packages: Core, Checks, Domain, Domain.LocalEventBus
---

# Core

> Foundational utilities, DDD building blocks, guard clauses, multi-tenancy, and domain messaging for the Headless framework.

## Orientation

- **`Headless.Extensions`** — the framework's base utility library (result pattern, domain primitives, value objects, collections, IO, threading, reflection helpers, constants, validators). Almost every other `Headless.*` package depends on it. Documented separately — see [extensions.md](extensions.md).
- **`Headless.Core`** — cross-cutting abstractions: `ICurrentUser`, `ICurrentLocale`, `ICurrentTimeZone`, `ITimezoneProvider`, `ICurrentPrincipalAccessor`, plus utilities (`SnappyCompressor`, `LogState` structured logging) and `AddHeadlessGuidGenerator()` for keyed GUID strategy registration. It also supplies the default `AsyncLocal`-backed implementations of the tenant-context contracts (`CurrentTenant`, `AsyncLocalCurrentTenantAccessor`, `NullCurrentTenant`, `TenantWriteGuardBypass`) — the contracts themselves (`ICurrentTenant`, `ICurrentTenantAccessor`, `ITenantWriteGuardBypass`, `CrossTenantWriteException`, `MissingTenantContextException`) live in `Headless.MultiTenancy.Abstractions` under the `Headless.MultiTenancy` namespace, which `Headless.Core` references. See [multi-tenancy.md](multi-tenancy.md) for the full tenancy surface, including the opt-in tenant catalog.
- **Security** — string encryption, lookup hashes, and secret hashing (`Headless.Security.Abstractions`, `Headless.Security`, `Headless.Security.Argon2`) are documented separately — see [security.md](security.md).
- **`Headless.Checks`** — guard clause library with `Argument` (preconditions) and `Ensure` (runtime assertions).
- **`Headless.Domain`** — DDD abstractions: `Entity`, `AggregateRoot`, `ValueObject`, auditing interfaces, concurrency stamps, and event contracts. Domain (in-process) events use plain payloads through `IDomainEventEmitter`; integration (distributed) events use plain payloads through `IIntegrationEventEmitter`. `AggregateRoot` implements both emitters; integration events are dispatched by the ORM/messaging layer, not from this package (see [orm.md](orm.md)).
- **`Headless.Domain.LocalEventBus`** — DI-based `IDomainEventDispatcher` for in-process domain event dispatch. Register with `AddHeadlessDomainEventDispatcher()` and implement `IDomainEventHandler<T>`. Namespace: `Headless.Domain`.

## Agent Rules

- Use `Headless.Checks` (`Argument.IsNotNull`, `Argument.IsNotNullOrEmpty`, `Argument.IsPositive`, etc.) for argument validation instead of raw `ArgumentNullException` or `ArgumentOutOfRangeException`. Use `Ensure` for internal state assertions.
- Use `Headless.Domain` base classes for DDD: inherit `Entity<T>` for entities, `AggregateRoot<T>` for aggregate roots, `ValueObject` for value objects. Emit in-process events via `AddDomainEvent()` and distributed events via `AddIntegrationEvent()` on aggregate roots.
- Use `Headless.Core` for `ICurrentUser` and `ICurrentTenant`. For time, use the BCL `TimeProvider` — the framework has no clock abstraction of its own. `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` / `DateTimeOffset.UtcNow` are banned at compile time by the Headless SDK (`RS0030`).
- Name framework-owned event timestamps with an `At` suffix (`CreatedAt`, `UpdatedAt`, `DeletedAt`, `PublishedAt`) and use an `On` suffix only for `DateOnly` values (`EffectiveOn`). Avoid `DateCreated`-style prefixes; persisted instants and public timestamp contracts use `DateTimeOffset`. Preserve provider-owned CLR members, JSON fields, and protocol keys exactly as defined by the third party; the framework convention does not rename contracts it does not own.
- Time semantics belong to whichever authority owns the decision, not to the ambient environment of the running process. Pick by the question the timestamp answers: **"who owns this, and until when?"** (leases, locks, liveness, visibility) → the **store's** clock, inlined into the atomic statement; **"how long has this taken?"** (timeouts, backoff, deadlines) → a **monotonic** clock, `TimeProvider.GetTimestamp()` / `GetElapsedTime()`; **"when should this fire in human terms?"** (cron, calendars) → the **tz database** via an explicit `TimeZoneInfo` (never `TimeZoneInfo.Local`); **"when did this happen?"** (audit, `CreatedAt`, logs) → the injected **`TimeProvider`** (`timeProvider.GetUtcNow()`). Full rationale: [temporal-authority-standard](../solutions/design-patterns/temporal-authority-standard.md).
- Only that last row is an app-clock concern. Never sample the app clock to compute a lease deadline — pass a duration and let the store apply its own clock.
- When a value arrives from an external SDK with an untrustworthy `DateTime.Kind` (AWS S3 returns `Unspecified`), normalize with `NormalizeToUtc()` from `Headless.Extensions` before converting to `DateTimeOffset` — `new DateTimeOffset(DateTime)` applies the *host's* offset to an `Unspecified` value.
- Use `ApiResult<T>` / `ApiResult` from `Headless.Extensions` for service return types instead of throwing exceptions for expected failures. Use `Result<TValue, TError>` when you need custom error types.
- For local (in-process) domain events, register `AddHeadlessDomainEventDispatcher()` and implement `IDomainEventHandler<T>`. Use `DomainEventHandlerOrderAttribute` to control handler execution order. For integration (distributed) events, emit integration payloads via `AddIntegrationEvent()` on the aggregate; dispatch is handled by the ORM/messaging layer (see [orm.md](orm.md)), not by this package.
- For strongly-typed IDs, use the primitives from `Headless.Extensions` (`UserId`, `AccountId`) — they have source-generated JSON and TypeConverter support.
- Auditing interfaces (`ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`) are marker interfaces — the ORM layer fills the properties automatically.
- Register GUID generation through `AddHeadlessGuidGenerator()` only from host/package setup; persisted backends should resolve `SequentialGuidType.Version7` or `SequentialGuidType.SqlServer` by key instead of depending on the unkeyed default. The `IGuidGenerator` / `SequentialGuidType` contracts live in `Headless.Extensions` (see [extensions.md](extensions.md)).
- Use `Polly.Core`'s `ResiliencePipelineBuilder().AddRetry(...)` for retry logic with exponential backoff and jitter. Build the pipeline once per operation class (e.g. one for transient-Redis-error retries, one for status-check retries) and reuse it. `Polly.Core` has zero transitive dependencies on `net10.0`.
- Use `LogState` with `HeadlessLoggerExtensions` for structured logging with tags and properties.

---

## Headless.Core

Core abstractions for building applications with multi-tenancy, user context, and cross-cutting concerns.

### API and behavior

- **Abstractions**:
    - `ICurrentUser` - Current authenticated user context; `UserId` and `Roles` are exposed only for authenticated principals
    - `ICorrelationIdProvider` / `ActivityCorrelationIdProvider` - correlation ID for tracing, audit, and structured logging
    - `ICurrentLocale` - Localization context (language, locale, culture)
    - `ICurrentTimeZone` - Timezone handling
    - `ICurrentPrincipalAccessor` - Scoped `ClaimsPrincipal` access with temporary switching
    - `IPasswordGenerator` - Configurable secure password generation; remaining character pools are required only when filler or extra unique characters are needed
    - `ICancellationTokenProvider` - Cancellation token access with fallback logic
    - `ITimezoneProvider` - Windows/IANA timezone conversion and listing
    - `IHostIdentityAccessor` / `IBuildInformationAccessor` - Process identity and build info. `AddHeadlessHostIdentity()` registers the accessor (`TryAdd`, so feature packages call it too and the host's own call wins); `ApplicationName` defaults to the entry assembly title and `HostName` to `POD_NAMESPACE/POD_NAME`, then the machine name. There is no per-start instance id here on purpose: Coordination allocates the only one (`NodeIdentity`, `host@incarnation`) on top of this host name, so nothing can disagree with it. Coordination's node id, the settings/features/permissions definition-store locks, and the change-announcement origin all read from the accessor, so an override in `HostIdentityOptions` moves every subsystem at once.
    - `IEnumLocaleAccessor` - Localized enum display values

- **Multi-tenancy implementations** (contracts live in `Headless.MultiTenancy.Abstractions`, namespace `Headless.MultiTenancy` — see [multi-tenancy.md](multi-tenancy.md)):
    - `CurrentTenant` / `AsyncLocalCurrentTenantAccessor` - default `ICurrentTenant` / `ICurrentTenantAccessor` implementations, `AsyncLocal`-scoped
    - `NullCurrentTenant` - fallback `ICurrentTenant` registered until a real tenant source (HTTP claim resolution, `AddHeadlessDbContextServices()`, ...) replaces it
    - `TenantWriteGuardBypass` - default `ITenantWriteGuardBypass` implementation; explicit bypass scope for audited host/admin tenant writes
    - `CrossTenantWriteException` / `MissingTenantContextException` - tenant write-guard exception types (defined in `Headless.MultiTenancy.Abstractions`; non-transient, exclude from retry)

- **Utilities**:
    - `SnappyCompressor` - Snappy compression/decompression with JSON serialization (AOT-compatible)
    - `LogState` / `HeadlessLoggerExtensions` - Structured logging with fluent state builder, tags, and scoped properties
    - `AddHeadlessGuidGenerator()` - registers keyed `IGuidGenerator` strategies for Version7 and SQL Server GUID ordering, plus an unkeyed backend-agnostic default

### Install

```bash
dotnet add package Headless.Core
```

### Setup and use

```csharp
public sealed class OrderService(TimeProvider timeProvider, ICurrentUser user, ICurrentTenant tenant)
{
    public Order CreateOrder(CreateOrderRequest request)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId!.Value,
            TenantId = tenant.Id,
            // "When did this happen?" — an audit timestamp, so the injected app clock owns it.
            CreatedAt = timeProvider.GetUtcNow(),
            Total = new Money(request.Amount, request.Currency),
        };
    }
}
```

`TimeProvider` is registered as a singleton by the Headless setup extensions (`TryAddSingleton(TimeProvider.System)`), so it is injectable without extra wiring. In tests, swap it for `FakeTimeProvider` — see [testing.md](testing.md).

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

### Runtime behavior

- `AddHeadlessGuidGenerator()` registers keyed singleton `IGuidGenerator` strategies for `SequentialGuidType.Version7` and `SequentialGuidType.SqlServer`
- `AddHeadlessGuidGenerator()` also registers an unkeyed singleton `IGuidGenerator` using `Version7` unless a caller supplies another default strategy

## Headless.Checks

Guard clause library for argument validation and defensive programming.

### API and behavior

- **Argument Validation**: Extensive static methods on `Argument` class
- **Runtime Assertions**: `Ensure` class for internal state validation
- **Performance Optimized**: `AggressiveInlining` and `DebuggerStepThrough`
- **Caller Expression Support**: Automatic parameter name capture
- **Type Support**: Nullable, `Span<T>`, `ReadOnlySpan<T>`, collections, strings

### Install

```bash
dotnet add package Headless.Checks
```

### Setup and use

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
- `Argument.IsCloseTo(value, target, delta)` / `IsNotCloseTo(…)` — tolerance comparison for `INumber<T>`; prefer over `IsEqualTo` for `float`/`double`. `int`/`long`/`nint` also have unsigned-`delta` overloads (overflow-safe, full distance range)
- `Argument.IsBitwiseEqualTo(value, target)` — raw-byte equality for `unmanaged` types (distinguishes `+0.0`/`-0.0`, matches identical NaN payloads)
- `Argument.IsOneOf(value, allowedValues)`
- `Argument.IsInEnum(enumValue)`
- `Argument.HasNoNulls(collection)` / `HasNoDuplicates(collection)` — `HasNoDuplicates` takes an optional `IEqualityComparer<T>`
- `Argument.HasLength` / `HasMinLength` / `HasMaxLength` / `HasLengthBetween` / `HasLengthGreaterThan` / `HasLengthLessThan` / `HasLengthNotEqualTo(string, …)` — string length bounds (throw `ArgumentOutOfRangeException`)
- `Argument.HasCount` / `HasMinCount` / `HasMaxCount` / `HasCountBetween(collection, …)` — item-count bounds (`IReadOnlyCollection<T>` fast-path + `IEnumerable<T>`)
- `Argument.StartsWith` / `EndsWith` / `Contains(string, value, comparison)` — string content (`StringComparison.Ordinal` by default)
- `Argument.IsInRangeFor(index, count | collection | span)` — bounds-checks an index against a length/collection/span
- `Argument.FileExists(path)` / `DirectoryExists(path)`
- `Argument.Matches(string, regex)` — throws `ArgumentException` when the string does not match the pattern
- `Argument.IsTrue(condition, message, nameof(arg))` / `IsFalse(condition, …)` — custom argument precondition that must hold / must not hold; throws `ArgumentException`

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

### Runtime behavior

None.

## Headless.Domain

Core domain-driven design abstractions including entities, aggregate roots, value objects, auditing, and messaging interfaces.

### API and behavior

- **Entity Abstractions**: `IEntity`, `IEntity<T>`, base `Entity` class
- **Aggregate Roots**: `IAggregateRoot`, `AggregateRoot` with built-in message emission
- **Value Objects**: `ValueObject` base class with equality
- **Auditing**: `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`
- **Concurrency**: `IHasConcurrencyStamp`
- **Multi-tenancy**: `IMultiTenant`
- **Domain Events (in-process)**: `IDomainEventEmitter`, `IDomainEventHandler<T>`, `DomainEventHandlerOrderAttribute`. An aggregate raises its own events through the `protected AddDomainEvent`; the readers/clearers (`GetDomainEvents`, `ClearDomainEvents`) and the `IDomainEventEmitter` contract stay public for infrastructure that collects and dispatches them. Dispatch is provided by `Headless.Domain.LocalEventBus`.
- **Integration Events (distributed)**: `IIntegrationEventEmitter`. An aggregate raises its own events through the `protected AddIntegrationEvent`; `GetIntegrationEvents`/`ClearIntegrationEvents` and the `IIntegrationEventEmitter` contract stay public for infrastructure. This package only defines the contract and the emitter — integration events are dispatched by the ORM/messaging layer (`Headless.EntityFramework.Messaging`), not from `Headless.Domain` (see [orm.md](orm.md)).
- **Entity Events**: `EntityCreatedEventData`, `EntityUpdatedEventData`, `EntityDeletedEventData`

Event payloads are plain reference types with no required marker interface. `AggregateRoot` captures an immutable `EventContext<TPayload>` for every raise, including repeated raises of the same payload object. The envelope contains `Payload`, `EventId`, root `CorrelationId`, immediate `CausationId`, and `TenantId`. Payloads must be treated as immutable after emission; the framework does not deep-copy arbitrary business objects.

Use `EventEmissionScope.Begin(new EventEmissionContext(correlationId, parentId, tenantId))` at an application or subsystem boundary. The scope flows across awaits, nests with strict reverse-order disposal, and isolates parallel async flows. Without a scope, an occurrence roots correlation at its own new ID. `Activity` tracing never supplies business identity. Infrastructure forwards an existing occurrence explicitly; passing only its payload raises a new occurrence. Use `EventContext.Capture(payload)` when explicitly creating an event outside aggregate behavior.

Emitter buffers contain `IReadOnlyList<EventContext<object>>` so one aggregate can raise different payload types. The generic `AddDomainEvent(context)` / `AddIntegrationEvent(context)` overloads preserve an existing concrete envelope. Batch clear removes only saved event IDs; parameterless clear explicitly discards the pending buffer.

Use `EventBuffer` when an entity implements an emitter interface without inheriting `AggregateRoot`. It provides `Add(payload)`, `Add(context)`, `Snapshot()`, `Clear()`, and `Clear(savedBatch)` with the same occurrence semantics as aggregate roots. Keep one buffer per entity and event kind; instances are not thread-safe. The buffer only retains events in memory. The entity must still implement `IIntegrationEventEmitter` or `IDomainEventEmitter` so infrastructure can collect them. Choose application-specific tenant and correlation values before adding an explicit `EventContext`; the buffer preserves them.

Handlers receive `EventContext<TPayload>` and a cancellation token. `IDomainEventDispatcher.DispatchAsync(context, token)` accepts only a captured envelope and resolves the exact runtime payload type, preserving identity and lineage across retries.

This package adds no event store, stream version, replay, or durable Domain contract registry. Domain remains independent of Messaging, Jobs, persistence, and commit coordination.


### Install

```bash
dotnet add package Headless.Domain
```

### Setup and use

```csharp
public sealed class Order : AggregateRoot<Guid>, ICreateAudit
{
    public required string CustomerName { get; init; }
    public decimal Total { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }

    public void Complete()
    {
        AddDomainEvent(new OrderCompletedEvent(Id));
    }
}

public sealed record OrderCompletedEvent(Guid OrderId);
```

For an entity using composition:

```csharp
public sealed class Account : IIntegrationEventEmitter
{
    private readonly EventBuffer _events = new();

    public void AddIntegrationEvent(object payload) => _events.Add(payload);

    public void AddIntegrationEvent<TPayload>(EventContext<TPayload> context)
        where TPayload : class => _events.Add(context);

    public IReadOnlyList<EventContext<object>> GetIntegrationEvents() => _events.Snapshot();

    public void ClearIntegrationEvents() => _events.Clear();

    public void ClearIntegrationEvents(IReadOnlyList<EventContext<object>> occurrences) => _events.Clear(occurrences);
}
```

#### Auditing

Implement audit interfaces for automatic tracking:

```csharp
public sealed class Product : Entity<int>, ICreateAudit, IUpdateAudit
{
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
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

### Runtime behavior

`EventEmissionScope.Begin` temporarily establishes async-flow-local business lineage. Dispose its scope in reverse creation order to restore the parent; no services, persistence, or transport are registered.

## Headless.Domain.LocalEventBus

DI-based implementation of `IDomainEventDispatcher` for in-process domain event handling.

### API and behavior

- `IDomainEventDispatcher` implementation (`ServiceProviderDomainEventDispatcher`) backed by DI
- One envelope-only async contract: `DispatchAsync<TPayload>(EventContext<TPayload>, CancellationToken)`
- Handler resolution per publish from the active scope
- Handler ordering via `DomainEventHandlerOrderAttribute`
- Handler exception aggregation and cooperative cancellation

### Design constraints

- **Async-only contract.** `IDomainEventDispatcher` deliberately exposes no synchronous `Publish`: a public sync member would dispatch the async handlers sync-over-async, which can deadlock on threads that carry a synchronization context (classic ASP.NET, Blazor Server, WPF). Infrastructure that must publish from a synchronous code path (for example the EF sync `SaveChanges` pipeline) owns and contains that bridge internally.
- **Exact-runtime dispatch.** `DispatchAsync(context)` resolves handlers for the exact runtime payload type, with no base/interface traversal. Cached compiled invokers support heterogeneous emitter batches without repeated reflection. Dispatch preserves captured identity and lineage. Each handler receives one immutable `EventContext<TPayload>`; nested emissions use that event as their immediate cause.
- **Scoped lifetime.** `AddHeadlessDomainEventDispatcher()` registers `IDomainEventDispatcher` as scoped (`TryAddScoped`). Handlers are resolved from the caller's scope, so they share the same scoped services — notably the `DbContext` — when published inside a unit of work.
- **Exception aggregation and cancellation.** Handlers are resolved and invoked per publish. A single handler exception is rethrown as-is; multiple handler exceptions are wrapped in an `AggregateException`. Cancellation is observed between handlers; if the token is cancelled, already-accumulated handler exceptions are preserved rather than discarded.

### Install

```bash
dotnet add package Headless.Domain.LocalEventBus
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the in-process domain event dispatcher
builder.Services.AddHeadlessDomainEventDispatcher();

// Register handlers
builder.Services.AddScoped<IDomainEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

#### Dispatching Events

```csharp
public sealed class OrderService(IDomainEventDispatcher eventDispatcher)
{
    public async Task CreateOrderAsync(Order order, CancellationToken ct)
    {
        await _repository.AddAsync(order, ct).ConfigureAwait(false);

        var context = EventContext.Capture(new OrderCreatedEvent(order.Id));
        await eventDispatcher.DispatchAsync(context, ct).ConfigureAwait(false);
    }
}
```

#### Handling Events

```csharp
public sealed class OrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(EventContext<OrderCreatedEvent> context, CancellationToken ct = default)
    {
        // Apply local transactional state changes; external effects belong in an outbox.
        return ValueTask.CompletedTask;
    }
}

[DomainEventHandlerOrder(-1)] // Execute before handlers with the default order
public sealed class AuditHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(EventContext<OrderCreatedEvent> context, CancellationToken ct = default)
    {
        // Audit logging
        return ValueTask.CompletedTask;
    }
}
```

### Configuration

No configuration required.

### Runtime behavior

- Registers `IDomainEventDispatcher` (`ServiceProviderDomainEventDispatcher`) as scoped
