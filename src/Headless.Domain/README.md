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
- **Domain Events (in-process)**: `IDomainEvent`, `IDomainEventEmitter`, `IDomainEventHandler<T>`, `DomainEventHandlerOrderAttribute`. An aggregate raises its own events through the `protected AddDomainEvent`; the readers/clearers (`GetDomainEvents`, `ClearDomainEvents`) and the `IDomainEventEmitter` contract stay public for infrastructure that collects and dispatches them. Dispatch is provided by `Headless.Domain.LocalEventBus`.
- **Integration Events (distributed)**: `IIntegrationEvent`, `IIntegrationEventEmitter`. An aggregate raises its own events through the `protected AddIntegrationEvent`; `GetIntegrationEvents`/`ClearIntegrationEvents` and the `IIntegrationEventEmitter` contract stay public for infrastructure. This package only defines the contract and the emitter — integration events are dispatched by the ORM/messaging layer (`Headless.EntityFramework.Messaging`), not from `Headless.Domain`.
- **Entity Events**: `EntityCreatedEventData`, `EntityUpdatedEventData`, `EntityDeletedEventData`

Event payload interfaces are pure markers. `AggregateRoot` captures an `EventOccurrence<TPayload>` for every raise, including repeated raises of the same payload object. Its immutable `Context` contains `EventId`, root `CorrelationId`, immediate `CausationId`, and `TenantId`. Payloads must be treated as immutable after emission; the framework does not deep-copy arbitrary business objects.

Use `EventEmissionScope.Begin(new EventEmissionContext(correlationId, parentId, tenantId))` at an application or subsystem boundary. The scope flows across awaits, nests with strict reverse-order disposal, and isolates parallel async flows. Without a scope, an occurrence roots correlation at its own new ID. `Activity` tracing never supplies business identity. Infrastructure forwards an existing occurrence explicitly; passing only its payload raises a new occurrence. Use `EventOccurrence.Capture(payload)` for an inferred concrete payload type, or `EventOccurrence.Capture<IDomainEvent>(payload)` when filling a Domain emitter buffer.

Source migration for custom implementations:

| Previous contract | New contract |
| --- | --- |
| Payload implements `UniqueId` | Remove the infrastructure ID from the payload; use `occurrence.Context.EventId` |
| Emitter returns payload lists | Return `IReadOnlyList<EventOccurrence<IDomainEvent>>` or the integration equivalent; capture once when raising |
| Clear the whole buffer after save | Implement `ClearDomainEvents(batch)` / `ClearIntegrationEvents(batch)` to remove only the saved occurrence IDs; parameterless clear remains explicit discard |
| Custom handler takes payload and token | Add `EventOccurrenceContext context` before the cancellation token |
| Custom local bus accepts only payload | Implement payload and occurrence overloads; occurrence publication preserves identity and dispatches exact runtime payload type |

This change adds no event store, stream version, replay, or durable Domain contract registry. Domain remains independent of Messaging, Jobs, persistence, and commit coordination.


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

    public void Complete()
    {
        AddDomainEvent(new OrderCompletedEvent(Id));
    }
}

public sealed record OrderCompletedEvent(Guid OrderId) : IDomainEvent;
```

### Auditing

Implement audit interfaces for automatic tracking:

```csharp
public sealed class Product : Entity<int>, ICreateAudit, IUpdateAudit
{
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

### Value Objects

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

## Configuration

No configuration required. This is an abstractions package.

## Dependencies

- `Headless.Checks` for argument validation.

## Side Effects

`EventEmissionScope.Begin` temporarily establishes async-flow-local business lineage. Dispose its scope in reverse creation order to restore the parent; no services, persistence, or transport are registered.
