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

### Auditing

Implement audit interfaces for automatic tracking:

```csharp
public sealed class Product : Entity<int>, ICreateAudit, IUpdateAudit
{
    public required string Name { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset? DateUpdated { get; set; }
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

None.

## Side Effects

None.
