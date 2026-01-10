# Framework.Domains.LocalMessagePublisher.DI

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
dotnet add package Framework.Domains.LocalMessagePublisher.DI
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
        await _repository.AddAsync(order, ct).AnyContext();

        await publisher.PublishAsync(new OrderCreatedEvent(order.Id), ct).AnyContext();
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

- `Framework.Domains`
- `Framework.Hosting`

## Side Effects

- Registers `ILocalMessagePublisher` as scoped
