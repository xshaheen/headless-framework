# Headless.Domain.LocalEventBus

DI-based implementation of `IDomainEventDispatcher` for in-process domain event handling.

## Problem Solved

Provides in-memory domain event dispatch that resolves handlers from the DI container, enabling decoupled event-driven architecture within a single process and unit of work.

## Key Features

- `IDomainEventDispatcher` implementation (`ServiceProviderDomainEventDispatcher`) backed by DI
- One envelope-only async contract: `DispatchAsync<TPayload>(EventContext<TPayload>, CancellationToken)`
- Handler resolution per publish from the active scope
- Handler ordering via `DomainEventHandlerOrderAttribute`
- Handler exception aggregation and cooperative cancellation

## Design Notes

- **Async-only contract.** `IDomainEventDispatcher` deliberately exposes no synchronous `Publish`: a public sync member would dispatch the async handlers sync-over-async, which can deadlock on threads that carry a synchronization context (classic ASP.NET, Blazor Server, WPF). Infrastructure that must publish from a synchronous code path (for example the EF sync `SaveChanges` pipeline) owns and contains that bridge internally.
- **Exact-runtime dispatch.** `DispatchAsync(context)` resolves handlers for the exact runtime payload type, with no base/interface traversal. Cached compiled invokers support heterogeneous emitter batches without repeated reflection. Dispatch preserves captured identity and lineage. Each handler receives one immutable `EventContext<TPayload>`; nested emissions use that event as their immediate cause.
- **Scoped lifetime.** `AddHeadlessDomainEventDispatcher()` registers `IDomainEventDispatcher` as scoped (`TryAddScoped`). Handlers are resolved from the caller's scope, so they share the same scoped services — notably the `DbContext` — when published inside a unit of work.
- **Exception aggregation and cancellation.** Handlers are resolved and invoked per publish. A single handler exception is rethrown as-is; multiple handler exceptions are wrapped in an `AggregateException`. Cancellation is observed between handlers; if the token is cancelled, already-accumulated handler exceptions are preserved rather than discarded.

## Installation

```bash
dotnet add package Headless.Domain.LocalEventBus
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the in-process domain event dispatcher
builder.Services.AddHeadlessDomainEventDispatcher();

// Register handlers
builder.Services.AddScoped<IDomainEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

### Dispatching Events

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

### Handling Events

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

## Configuration

No configuration required.

## Dependencies

- `Headless.Domain`
- `Headless.Hosting`

## Side Effects

- Registers `IDomainEventDispatcher` (`ServiceProviderDomainEventDispatcher`) as scoped.
