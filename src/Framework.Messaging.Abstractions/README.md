# Framework.Messaging.Abstractions

Defines the unified interface for message bus operations (publish/subscribe).

## Problem Solved

Provides a provider-agnostic messaging API, enabling seamless switching between message bus implementations (in-memory, Redis, RabbitMQ) without changing application code.

## Key Features

- `IMessageBus` - Combined publish/subscribe interface
- `IMessagePublisher` - Message publishing interface
- `IMessageSubscriber` - Message subscription interface
- `PublishMessageOptions` - Options for message delivery
- Async-first API design

## Installation

```bash
dotnet add package Framework.Messaging.Abstractions
```

## Usage

```csharp
public sealed class OrderService(IMessagePublisher publisher)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        // Process order...

        await publisher.PublishAsync(new OrderPlacedEvent
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId
        }, ct).AnyContext();
    }
}

public sealed class NotificationHandler(IMessageSubscriber subscriber)
{
    public async Task StartAsync(CancellationToken ct)
    {
        await subscriber.SubscribeAsync<OrderPlacedEvent>(async (msg, token) =>
        {
            // Send notification...
        }, ct).AnyContext();
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
