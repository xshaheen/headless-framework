# Headless.Domain.LocalEventBus

DI-based implementation of `ILocalEventBus` for in-process domain event handling.

## Problem Solved

Provides in-memory domain event dispatch that resolves handlers from the DI container, enabling decoupled event-driven architecture within a single process and unit of work.

## Key Features

- `ILocalEventBus` implementation (`ServiceProviderLocalEventBus`) backed by DI
- Generic and non-generic publish overloads (`Publish`, `PublishAsync`)
- Handler resolution per publish from the active scope
- Handler ordering via `DomainEventHandlerOrderAttribute`
- Handler exception aggregation and cooperative cancellation

## Design Notes

- **Non-generic runtime-typed dispatch.** `Publish(IDomainEvent)` / `PublishAsync(IDomainEvent)` dispatch to handlers of the event's exact runtime type — there is no contravariant traversal to base types or implemented interfaces. The runtime type is mapped to a compiled invoker that is built once and cached, so repeated publishes of the same concrete type avoid reflection on the hot path. The generic overloads (`Publish<T>` / `PublishAsync<T>`) dispatch against the static type argument `T`.
- **Scoped lifetime.** `AddHeadlessLocalEventBus()` registers `ILocalEventBus` as scoped (`TryAddScoped`). Handlers are resolved from the caller's scope, so they share the same scoped services — notably the `DbContext` — when published inside a unit of work.
- **Exception aggregation and cancellation.** Handlers are resolved and invoked per publish. A single handler exception is rethrown as-is; multiple handler exceptions are wrapped in an `AggregateException`. Cancellation is observed between handlers; if the token is cancelled, already-accumulated handler exceptions are preserved rather than discarded.

## Installation

```bash
dotnet add package Headless.Domain.LocalEventBus
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the in-process local event bus
builder.Services.AddHeadlessLocalEventBus();

// Register handlers
builder.Services.AddScoped<IDomainEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

### Publishing Events

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

### Handling Events

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

## Configuration

No configuration required.

## Dependencies

- `Headless.Domain`
- `Headless.Hosting`

## Side Effects

- Registers `ILocalEventBus` (`ServiceProviderLocalEventBus`) as scoped.
