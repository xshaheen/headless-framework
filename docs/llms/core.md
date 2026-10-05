---
domain: Core
packages: Checks, Domain, Domain.EventDispatcher
---

# Core

> Guard clauses, DDD building blocks, and domain messaging for the Headless framework.

## Orientation

- **`Headless.Checks`** — guard clause library with `Argument` (preconditions) and `Ensure` (runtime assertions).
- **`Headless.Domain`** — DDD abstractions: `Entity`, `AggregateRoot`, `ValueObject`, auditing interfaces, concurrency stamps, and event contracts. Domain (in-process) events use plain payloads through `IDomainEventEmitter`; integration (distributed) events use plain payloads through `IIntegrationEventEmitter`. `AggregateRoot` implements both emitters; integration events are dispatched by the ORM/messaging layer, not from this package (see [orm.md](orm.md)).
- **`Headless.Domain.EventDispatcher`** — DI-based `IDomainEventDispatcher` for in-process domain event dispatch. Register with `AddHeadlessDomainEventDispatcher()` and implement `IDomainEventHandler<T>`. Namespace: `Headless.Domain`.
- The ambient-context contracts and services (`ICurrentUser`, `ICurrentLocale`, `ICurrentPrincipalAccessor`, host identity, ...) ship as `Headless.Context.Abstractions` / `Headless.Context`; see [context.md](context.md).

## Agent Rules

- Use `Headless.Checks` (`Argument.IsNotNull`, `Argument.IsNotNullOrEmpty`, `Argument.IsPositive`, etc.) for argument validation instead of raw `ArgumentNullException` or `ArgumentOutOfRangeException`. Use `Ensure` for internal state assertions.
- Use `Headless.Domain` base classes for DDD: inherit `Entity<T>` for entities, `AggregateRoot<T>` for aggregate roots, `ValueObject<TSelf>` for value objects. Emit in-process events via `AddDomainEvent()` and distributed events via `AddIntegrationEvent()` on aggregate roots.
- Name framework-owned event timestamps with an `At` suffix (`CreatedAt`, `UpdatedAt`, `DeletedAt`, `PublishedAt`) and use an `On` suffix only for `DateOnly` values (`EffectiveOn`). Avoid `DateCreated`-style prefixes; persisted instants and public timestamp contracts use `DateTimeOffset`. Preserve provider-owned CLR members, JSON fields, and protocol keys exactly as defined by the third party; the framework convention does not rename contracts it does not own.
- When a value arrives from an external SDK with an untrustworthy `DateTime.Kind` (AWS S3 returns `Unspecified`), normalize with `NormalizeToUtc()` from `Headless.Extensions` before converting to `DateTimeOffset` — `new DateTimeOffset(DateTime)` applies the *host's* offset to an `Unspecified` value.
- Use `ApiResult<T>` / `ApiResult` from `Headless.Extensions` for service return types instead of throwing exceptions for expected failures. Use `Result<TValue, TError>` when you need custom error types.
- For local (in-process) domain events, register `AddHeadlessDomainEventDispatcher()` and implement `IDomainEventHandler<T>`. Use `DomainEventHandlerOrderAttribute` to control handler execution order. For integration (distributed) events, emit integration payloads via `AddIntegrationEvent()` on the aggregate; dispatch is handled by the ORM/messaging layer (see [orm.md](orm.md)), not by this package.
- For strongly-typed IDs, use the primitives from `Headless.Extensions` (`UserId`, `AccountId`) — they have source-generated JSON and TypeConverter support.
- Auditing interfaces (`ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`) are marker interfaces — the ORM layer fills the properties automatically. Inherit an audited base instead of re-declaring the properties: `Audited*` for create and update, `Suspendable*` to add suspension, `SoftDeletable*` to add soft delete. An entity that needs both suspension and soft delete inherits one of the latter and implements the other interface by hand. When implementing the interfaces by hand, give each property a `private` or `protected` setter: the ORM writes non-public setters, but a `private` setter declared on a base class is invisible to it, so a hand-written base class needs `protected`.

---

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
- `Argument.IsPortableKey(string?)` — rejects text some storage provider would not keep unchanged as a key (null and empty pass through): surrounding white space, a NUL character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (Npgsql refuses it; SqlClient sends it as U+FFFD, merging distinct keys on SQL Server). Case, accents, other control characters, and surrogate pairs pass and stay distinct. Use it for key text a feature stores on more than one provider
- `Argument.HasNoSurroundingWhiteSpace(string?)` — rejects a value that starts or ends with white space (null and empty pass through). Use it on strings stored as keys: SQL Server ignores trailing spaces when it compares strings or enforces a unique index, under every collation, so `"acme"` and `"acme "` are one key there and two on PostgreSQL
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
- **Value Objects**: `ValueObject<TSelf>` base class with equality over the components it compares and hashes

- **Auditing**: `ICreateAudit`, `IUpdateAudit`, `IDeleteAudit`, `ISuspendAudit`, each in three arities: timestamps only, `<TAccountId>` adding actor ids, and `<TAccountId, TAccount>` adding actor navigations plus `Update`, `Suspend`/`Unsuspend`, and `Delete`/`Restore`
- **Audited bases**, split by capability, each over `Entity<TId>` or `AggregateRoot<TId>` with `protected` setters: `AuditedEntity` / `AuditedAggregateRoot` (create + update), `SuspendableEntity` / `SuspendableAggregateRoot` (create + update + suspend), `SoftDeletableEntity` / `SoftDeletableAggregateRoot` (create + update + soft delete). Each comes in `<TId>`, `<TId, TAccountId>`, and `<TId, TAccountId, TAccount>` forms matching the interface arity; only the last form exposes the public transition methods (`Update`, plus `Suspend`/`Unsuspend` or `Delete`/`Restore`)
- **Concurrency**: `IHasConcurrencyStamp`
- **Multi-tenancy**: `IMultiTenant`
- **Domain Events (in-process)**: `IDomainEventEmitter`, `IDomainEventHandler<T>`, `DomainEventHandlerOrderAttribute`. An aggregate raises its own events through the `protected AddDomainEvent`; the readers/clearers (`GetDomainEvents`, `ClearDomainEvents`) and the `IDomainEventEmitter` contract stay public for infrastructure that collects and dispatches them. Dispatch is provided by `Headless.Domain.EventDispatcher`.
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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
}
```

Or inherit the audited base that carries only the capabilities the entity needs:

| Base | Capabilities |
| --- | --- |
| `AuditedEntity<…>` / `AuditedAggregateRoot<…>` | create, update |
| `SuspendableEntity<…>` / `SuspendableAggregateRoot<…>` | create, update, suspend |
| `SoftDeletableEntity<…>` / `SoftDeletableAggregateRoot<…>` | create, update, soft delete |

An entity that needs both suspension and soft delete inherits one of the last two and implements the other interface by hand. A create-only entity implements `ICreateAudit<…>` directly.

The `HeadlessDbContext` save pipeline stamps the audit fields, and it stamps the matching `*ById` from `ICurrentUser` when `TAccountId` is `UserId` or `AccountId`:

- Every modified save stamps `UpdatedAt` and `UpdatedById` with the current time and actor.
- `IsDeleted` false → true stamps `DeletedAt`/`DeletedById`; true → false stamps `RestoredAt`/`RestoredById`.
- `IsSuspended` false → true stamps `SuspendedAt`/`SuspendedById`; true → false stamps `UnsuspendedAt`/`UnsuspendedById`.
- Each pair holds the most recent transition of its kind. A reversal keeps the opposite pair as history, so read `IsDeleted` / `IsSuspended`, not the timestamps, for the current state.
- A non-null value the save already set explicitly wins over the stamp.
- A delete, restore, suspend, or unsuspend with no actor records a null actor id, never the previous transition's actor. "No actor" means none was passed to the transition method and none resolved from `ICurrentUser`. The transition methods likewise write `byId` and `by` exactly as given, including `null`.
- A modified save with no resolved current user records a null `UpdatedById` and clears a loaded `UpdatedBy`. An anonymous flow that knows the actor passes it to `Update(now, byId, by)` in the same save, which wins.

The entity changes `IsSuspended` / `IsDeleted` through its own behavior:

```csharp
public sealed class Invoice : SoftDeletableAggregateRoot<Guid, UserId>
{
    public required string Number { get; init; }

    public void Void() => IsDeleted = true;
}
```

The `<TId, TAccountId, TAccount>` form also implements the public transition methods: `Update` on every base, `Suspend`/`Unsuspend` on the suspendable bases, and `Delete`/`Restore` on the soft-deletable bases. Each records the given time and actor. `Suspend`, `Unsuspend`, `Delete`, and `Restore` do nothing when the entity is already in the target state. Call `Update` only when the recorded time or actor must differ from the pipeline's clock and current user, for example an anonymous flow acting for a known account: the pipeline stamps every modified save by itself.

Suspension is a business state. Suspended rows stay visible to queries unless the entity type opts into the suspend filter with `HasNotSuspendedFilter()` (see [ORM](orm.md)). Soft-deleted rows are hidden by the default not-deleted filter; load one to restore it with `IgnoreNotDeletedFilter()`.

#### Value Objects

```csharp
public sealed class Address : ValueObject<Address>
{
    public required string Street { get; init; }
    public required string City { get; init; }

    protected override bool EqualityComponentsEqual(Address other) =>
        string.Equals(Street, other.Street, StringComparison.Ordinal)
        && string.Equals(City, other.City, StringComparison.Ordinal);

    protected override void BuildHashCode(ref HashCode hash)
    {
        hash.Add(Street, StringComparer.Ordinal);
        hash.Add(City, StringComparer.Ordinal);
    }
}
```

### Configuration

No configuration required. This is an abstractions package.

### Runtime behavior

`EventEmissionScope.Begin` temporarily establishes async-flow-local business lineage. Dispose its scope in reverse creation order to restore the parent; no services, persistence, or transport are registered.

## Headless.Domain.EventDispatcher

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
dotnet add package Headless.Domain.EventDispatcher
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
