---
domain: Core
packages: Checks, Domain, Domain.EventDispatcher
---

# Core

> Guard clauses, DDD building blocks (entities, aggregates, value objects, auditing), and in-process domain events.

## Orientation

- **`Headless.Checks`**: guard clauses. `Argument` validates method arguments and throws the BCL argument exceptions. `Ensure` asserts object state and throws `InvalidOperationException` or `ObjectDisposedException`.
- **`Headless.Domain`**: DDD base types and contracts: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject<TSelf>`, the audit interfaces and audited bases, `IHasConcurrencyStamp`, `IMultiTenant`, and the event contracts. An aggregate raises in-process domain events through `AddDomainEvent` and distributed integration events through `AddIntegrationEvent`. The package registers nothing and dispatches nothing.
- **`Headless.Domain.EventDispatcher`**: the DI-backed `IDomainEventDispatcher`. Register it with `AddHeadlessDomainEventDispatcher()` and implement `IDomainEventHandler<TPayload>`.
- **Who dispatches events.** With `HeadlessDbContext`, `.AddDomainEvents()` dispatches domain events inside the save transaction, and `.AddIntegrationEventOutbox()` (`Headless.EntityFramework.Messaging`) writes integration events to the outbox. See [ORM](orm.md#headlessentityframework).
- Strongly typed IDs (`UserId`, `AccountId`) and result types (`ApiResult`, `Result<TValue, TError>`) ship in `Headless.Primitives`; see [extensions.md](extensions.md#headlessprimitives). The ambient-context contracts (`ICurrentUser`, `ICurrentLocale`, host identity) ship in `Headless.Context.Abstractions`; see [context.md](context.md).

## Agent Rules

- Inherit `Entity<TId>` for entities, `AggregateRoot<TId>` for aggregate roots, and `ValueObject<TSelf>` for value objects.
- Raise events from the aggregate's own methods: `AddDomainEvent(payload)` for in-process handlers, `AddIntegrationEvent(payload)` for other services. Both are `protected`, so code outside the aggregate cannot raise them.
- Treat an event payload as immutable after you raise it. The framework does not copy it.
- To dispatch a domain event yourself, capture it first: `dispatcher.DispatchAsync(EventContext.Capture(payload), ct)`. `IDomainEventDispatcher` has no synchronous `Publish` and no overload that takes a bare payload.
- Inherit an audited base (`AuditedEntity`, `AuditedAggregateRoot`, `SoftDeletableEntity`, `SoftDeletableAggregateRoot`) instead of re-declaring the audit properties. When you implement the audit interfaces by hand, give each property a `private` setter on the entity class itself, or a `protected` setter on a base class you write: the save pipeline cannot see a `private` setter declared on a base class.
- Soft delete is the only lifecycle state the framework audits and filters. Model other states, such as suspension or blocking, as your own properties and filter them with explicit query predicates.
- Read `IsDeleted` for the current deletion state, not `DeletedAt` or `RestoredAt`: each pair keeps the most recent transition of its kind.

---

## Headless.Checks

Guard clauses for argument validation and state assertions in application code.

### Setup

```bash
dotnet add package Headless.Checks
```

```csharp
using Headless.Checks;

public sealed class UserService
{
    private bool _initialized;
    private bool _disposed;

    public void CreateUser(string name, int age, List<string> roles)
    {
        Argument.IsNotNullOrEmpty(name);
        Argument.IsPositive(age);
        Argument.IsNotNullOrEmpty(roles);
        Argument.HasNoNulls(roles);

        Ensure.True(_initialized, "Service must be initialized.");
        Ensure.NotDisposed(_disposed, this);
    }
}
```

**Verify:** no startup signal. In a unit test, assert that `CreateUser(null!, 1, [])` throws `ArgumentNullException` whose `ParamName` is `name`.

### API

Every `Argument` method captures the argument expression as `paramName` through `[CallerArgumentExpression]`, and most return the validated value, so `_name = Argument.IsNotNull(name);` works. A null argument throws `ArgumentNullException`, an out-of-range number, length, or count throws `ArgumentOutOfRangeException`, and any other failed check throws `ArgumentException`.

| Method | Checks |
| --- | --- |
| `IsNotNull`, `IsNotNullOrEmpty` (string or collection), `IsNotNullOrWhiteSpace` | Presence. |
| `IsNotEmpty(Guid)` | Rejects `Guid.Empty`. The `Guid?` overload lets `null` pass. |
| `IsPositive`, `IsNegative`, `IsPositiveOrZero`, `IsNegativeOrZero`, `IsZero`, `IsNotZero` | Sign. `INumber<T>`, nullable, and `TimeSpan` overloads. |
| `IsEqualTo`, `IsNotEqualTo` | Value equality, with an optional `IEqualityComparer<T>`. Use `IsReferenceEqualTo` and `IsReferenceNotEqualTo` for identity. |
| `IsCloseTo`, `IsNotCloseTo` | Tolerance comparison for `INumber<T>`. Use it instead of `IsEqualTo` for `float` and `double`. `int`, `long`, and `nint` also take an unsigned `delta`, which cannot overflow. |
| `IsBitwiseEqualTo` | Raw-byte equality for `unmanaged` types. It tells `+0.0` from `-0.0` and matches identical NaN payloads. |
| `IsOneOf`, `IsInEnum` | Membership in a set or a defined enum value. |
| `HasNoNulls`, `HasNoDuplicates` | Collection elements. `HasNoDuplicates` takes an optional `IEqualityComparer<T>`. |
| `HasLength`, `HasMinLength`, `HasMaxLength`, `HasLengthBetween`, `HasLengthGreaterThan`, `HasLengthLessThan`, `HasLengthNotEqualTo` | String length. |
| `HasCount`, `HasMinCount`, `HasMaxCount`, `HasCountBetween` | Item count, on `IReadOnlyCollection<T>` and `IEnumerable<T>`. |
| `StartsWith`, `EndsWith`, `Contains` | String content. The default comparison is `StringComparison.Ordinal`. |
| `IsPortableKey` | Rejects key text some storage provider would not keep unchanged: surrounding white space, a NUL character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (Npgsql refuses it, and SQL Server stores it as U+FFFD, merging distinct keys). Case, accents, other control characters, and surrogate pairs pass. `null` and empty pass. Use it for key text stored on more than one provider. |
| `HasNoSurroundingWhiteSpace` | Rejects a value that starts or ends with white space. `null` and empty pass. Use it on strings stored as keys: SQL Server ignores trailing spaces in comparisons and unique indexes under every collation, so `"acme"` and `"acme "` are one key there and two on PostgreSQL. |
| `IsInRangeFor` | An index against a count, collection, or span. |
| `FileExists`, `DirectoryExists` | A path on disk. |
| `Matches` | A string against a regex. |
| `IsTrue`, `IsFalse` | A custom precondition. |

`Ensure` asserts state, not arguments:

| Method | Throws |
| --- | --- |
| `Ensure.True(condition, message)`, `Ensure.False(...)` | `InvalidOperationException` |
| `Ensure.NotNull(value)` | `InvalidOperationException`. Returns the value. |
| `Ensure.NotDisposed(disposed, this)` | `ObjectDisposedException` |

### Configuration

None.

### Design and runtime behavior

- `Ensure.True` and `Argument.IsTrue` accept an interpolated-string message (`$"At most {_max} pending orders."`) and format it only when the check fails, so a passing check allocates no string. The message uses the invariant culture. A string literal or `string` variable binds to the plain `string?` overload.
- The methods support nullable values, `Span<T>`, `ReadOnlySpan<T>`, collections, and strings.

## Headless.Domain

DDD base types and contracts: entities, aggregate roots, value objects, auditing, concurrency, tenancy, and event emission.

### Setup

```bash
dotnet add package Headless.Domain
```

```csharp
using Headless.Domain;

public sealed class Order : AggregateRoot<Guid>, ICreateAudit
{
    public required string CustomerName { get; init; }
    public decimal Total { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete()
    {
        AddDomainEvent(new OrderCompletedEvent(Id));
    }
}

public sealed record OrderCompletedEvent(Guid OrderId);
```

**Verify:** no startup signal. In a unit test, call `order.Complete()` and assert that `order.GetDomainEvents()` holds one `EventContext<object>` whose `Payload` is the `OrderCompletedEvent`.

### API

- **Entities.** `Entity<TId>` compares by type and ID. `AggregateRoot<TId>` adds the event buffers and implements both `IDomainEventEmitter` and `IIntegrationEventEmitter`.
- **Value objects.** `ValueObject<TSelf>` compares on the components you name. Override `EqualityComponentsEqual` and `BuildHashCode`.
- **Audit interfaces.** `ICreateAudit`, `IUpdateAudit`, and `IDeleteAudit`, each in three arities: timestamps only (`CreatedAt`, `UpdatedAt`, `DeletedAt`, `RestoredAt`, `IsDeleted`); `<TAccountId>`, which adds the actor IDs (`CreatedById` and the rest); and `<TAccountId, TAccount>`, which adds the actor navigations plus `Update` on `IUpdateAudit` and `Delete` and `Restore` on `IDeleteAudit`.
- **Audited bases.** Each base exists over `Entity<TId>` and over `AggregateRoot<TId>`, in `<TId>`, `<TId, TAccountId>`, and `<TId, TAccountId, TAccount>` forms, with `protected` setters. Only the last form has the public transition methods.

  | Base | Capabilities |
  | --- | --- |
  | `AuditedEntity<…>`, `AuditedAggregateRoot<…>` | create, update |
  | `SoftDeletableEntity<…>`, `SoftDeletableAggregateRoot<…>` | create, update, soft delete |

  A create-only entity implements `ICreateAudit<…>` directly.
- **Concurrency and tenancy.** `IHasConcurrencyStamp` marks an entity for optimistic concurrency. `IMultiTenant` marks a tenant-owned entity.
- **Events.** `EventContext<TPayload>`, `EventContext.Capture`, `EventEmissionScope`, `EventEmissionContext`, `EventBuffer`, `IDomainEventEmitter`, `IIntegrationEventEmitter`, `IDomainEventHandler<TPayload>`, `DomainEventHandlerOrderAttribute`, and the entity event payloads `EntityCreatedEventData`, `EntityUpdatedEventData`, and `EntityDeletedEventData`.

### Auditing

The `HeadlessDbContext` save pipeline stamps the audit fields. It also stamps the matching `*ById` from `ICurrentUser` when `TAccountId` is `UserId` or `AccountId`:

- Every modified save stamps `UpdatedAt` and `UpdatedById` with the current time and actor.
- `IsDeleted` changing from `false` to `true` stamps `DeletedAt` and `DeletedById`. Changing from `true` to `false` stamps `RestoredAt` and `RestoredById`.
- Each pair holds the most recent transition of its kind. A restore keeps the deletion pair as history, and a later delete keeps the restore pair.
- A non-null value that the save already set explicitly wins over the stamp.
- A delete or restore with no actor records a `null` actor ID, never the previous transition's actor. "No actor" means none was passed to the transition method and none resolved from `ICurrentUser`. The transition methods write `byId` and `by` exactly as given, `null` included.
- A modified save with no resolved current user records a `null` `UpdatedById` and clears a loaded `UpdatedBy`. To record a known actor in an anonymous flow, call `Update(now, byId, by)` in the same save; that value wins.

The entity changes `IsDeleted` through its own behavior:

```csharp
using Headless.Domain;
using Headless.Primitives;

public sealed class Invoice : SoftDeletableAggregateRoot<Guid, UserId>
{
    public required string Number { get; init; }

    public void Void() => IsDeleted = true;
}
```

On the `<TId, TAccountId, TAccount>` form, `Update`, `Delete`, and `Restore` record the given time and actor. `Delete` and `Restore` do nothing when the entity is already in the target state. Call `Update` only when the recorded time or actor must differ from the pipeline's clock and current user, because the pipeline already stamps every modified save.

The default not-deleted query filter hides soft-deleted rows. To load one, for example to restore it, query with `IgnoreNotDeletedFilter()` ([ORM](orm.md#global-query-filters)).

### Value objects

```csharp
using Headless.Domain;

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

### Events

An event payload is any reference type; no marker interface is required. Each raise captures an immutable `EventContext<TPayload>` with `Payload`, `EventId`, `CorrelationId` (the root of the chain), `CausationId` (the immediate cause), and `TenantId`. Raising the same payload object twice records two occurrences with different `EventId` values.

- **Correlation scope.** At an application or subsystem boundary, call `using var scope = EventEmissionScope.Begin(new EventEmissionContext(correlationId, causationId, tenantId));`. Events raised inside the scope take its lineage. The scope flows across `await` and isolates parallel async flows. Without a scope, an event roots its correlation at its own new ID. `Activity` tracing never supplies the correlation.
- **Dispose scopes in reverse order.** Disposing a scope that is not the current one in its async flow throws `InvalidOperationException`.
- **Forward an existing occurrence, not its payload.** Passing an existing `EventContext<TPayload>` to `AddDomainEvent(context)` or `AddIntegrationEvent(context)` keeps its identity and lineage. Passing only its payload raises a new occurrence. Outside an aggregate, create an occurrence with `EventContext.Capture(payload)`.
- **Reading and clearing.** `GetDomainEvents()` and `GetIntegrationEvents()` return `IReadOnlyList<EventContext<object>>`, so one aggregate can hold several payload types. `ClearDomainEvents(savedBatch)` removes only the occurrences in that batch, by `EventId`. The parameterless `ClearDomainEvents()` discards the whole pending buffer. The integration methods behave the same.

To emit events from an entity that does not inherit `AggregateRoot`, implement the emitter interface over an `EventBuffer`. `EventBuffer` keeps events in memory with the same occurrence rules. The entity must still implement `IIntegrationEventEmitter` or `IDomainEventEmitter`, because the save pipeline collects events through those interfaces. Keep one buffer per entity and event kind. A buffer is not thread-safe.

```csharp
using Headless.Domain;

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

### Configuration

None. The package registers no services.

### Design and runtime behavior

- `Headless.Domain` has no event store, stream version, replay, or durable event registry, and it does not depend on Messaging, Jobs, or persistence.
- Integration events reach other services only through the outbox bridge in `Headless.EntityFramework.Messaging` ([ORM](orm.md#headlessentityframeworkmessaging)). Without it, `AddIntegrationEvent` only fills the in-memory buffer.

## Headless.Domain.EventDispatcher

The DI-backed `IDomainEventDispatcher` for in-process domain event handlers.

### Setup

```bash
dotnet add package Headless.Domain.EventDispatcher
```

```csharp
using Headless.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessDomainEventDispatcher();
builder.Services.AddScoped<IDomainEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
builder.Services.AddScoped<IDomainEventHandler<OrderCreatedEvent>, AuditHandler>();

public sealed record OrderCreatedEvent(Guid OrderId);

public sealed class OrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(EventContext<OrderCreatedEvent> context, CancellationToken cancellationToken = default)
    {
        // Change local state in the same transaction. Send external effects through an outbox.
        return ValueTask.CompletedTask;
    }
}

[DomainEventHandlerOrder(-1)] // runs before handlers with the default order 0
public sealed class AuditHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(EventContext<OrderCreatedEvent> context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
```

To dispatch outside the EF save pipeline, capture the event and pass the envelope:

```csharp
using Headless.Domain;

public sealed class OrderNotifier(IDomainEventDispatcher dispatcher)
{
    public async Task NotifyCreatedAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var context = EventContext.Capture(new OrderCreatedEvent(orderId));
        await dispatcher.DispatchAsync(context, cancellationToken);
    }
}
```

**Verify:** resolve `IDomainEventDispatcher` from a scope and dispatch a captured event in a test; assert the handler ran. With `HeadlessDbContext` and `.AddDomainEvents()`, assert the handler ran after `SaveChangesAsync`.

### Configuration

None. `HeadlessDbContext`'s `.AddDomainEvents()` calls `AddHeadlessDomainEventDispatcher()` for you.

### Design and runtime behavior

- **Scoped.** `AddHeadlessDomainEventDispatcher()` registers `IDomainEventDispatcher` as scoped, and only when none is registered. Handlers resolve from the caller's scope on every dispatch, so inside a unit of work they share its scoped services, the `DbContext` included.
- **Exact payload type.** Dispatch resolves `IDomainEventHandler<T>` for the exact runtime payload type. A handler for a base class or interface of the payload does not run. A value-type payload throws `ArgumentException`.
- **Order.** Handlers run one at a time, in ascending `DomainEventHandlerOrder` (default `0`). Handlers with equal order run in registration order.
- **Lineage.** Each handler receives the same immutable `EventContext<TPayload>`. Events a handler raises take that event as their `CausationId` and keep its `CorrelationId` and `TenantId`.
- **Exceptions.** A failing handler does not stop the others. After all handlers run, one failure is rethrown as the original exception, and two or more are thrown as one `AggregateException`.
- **Cancellation.** The dispatcher checks the token before each handler. When it is cancelled, the exceptions collected so far are thrown in place of `OperationCanceledException`; with none, it throws `OperationCanceledException`.
- **Async only.** There is no synchronous dispatch, because blocking on async handlers can deadlock under a synchronization context.
