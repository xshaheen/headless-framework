# Headless.Messaging.Abstractions

Core abstractions for type-safe, high-performance message consumption and publishing with outbox pattern support.

## Problem Solved

Provides standardized interfaces for building reliable distributed messaging systems with compile-time type safety, avoiding reflection overhead and enabling transactional outbox patterns for guaranteed message delivery.

## Key Features

- **Type-Safe Consumption**: `IConsume<TMessage>` interface with `ConsumeContext<TMessage>` for compile-time verification (5-8x faster than reflection)
- **Outbox Publishing**: `IOutboxPublisher` for transactional message publishing with database consistency
- **Rich Metadata**: Message ID, correlation ID, timestamps, headers, and topic routing
- **Consumer Configuration**: `IMessagingBuilder` for assembly scanning and manual consumer registration
- **Delayed Publishing**: Schedule messages for future delivery
- **Multi-Type Consumers**: Single consumer can handle multiple message types

## Installation

```bash
dotnet add package Headless.Messaging.Abstractions
```

## Quick Start

```csharp
// Define message consumer
public sealed class OrderPlacedHandler(
    IOrderRepository orders,
    ILogger<OrderPlacedHandler> logger) : IConsume<OrderPlacedEvent>
{
    public async ValueTask Consume(
        ConsumeContext<OrderPlacedEvent> context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing order {OrderId} at {Timestamp}",
            context.Message.OrderId,
            context.Timestamp);

        await orders.CreateAsync(context.Message, cancellationToken);
    }
}

// Register consumers
builder.Services.AddMessages(options =>
{
    options.ScanConsumers(typeof(Program).Assembly);
    options.WithTopicMapping<OrderPlacedEvent>("orders.placed");
});

// Publish with outbox
public sealed class OrderService(IOutboxPublisher publisher)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        // Publish transactionally with database changes
        await publisher.PublishAsync("orders.placed", new OrderPlacedEvent
        {
            OrderId = order.Id,
            Total = order.Total
        }, cancellationToken: ct);
    }
}
```

## Configuration

No configuration required. This is an abstractions package. Implementations are provided by:
- `Headless.Messaging.Core` (base implementation)
- Transport packages: `Headless.Messaging.RabbitMQ`, `Headless.Messaging.Kafka`, etc.
- Storage packages: `Headless.Messaging.PostgreSql`, `Headless.Messaging.SqlServer`, etc.

## Dependencies

- `Headless.Base`
- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

None. This is an abstractions package.
