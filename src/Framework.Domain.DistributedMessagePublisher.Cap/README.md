# Framework.Domains.DistributedMessagePublisher.Cap

CAP-based implementation of `IDistributedMessagePublisher` for reliable distributed messaging with transactional outbox pattern.

## Problem Solved

Provides reliable distributed message publishing using CAP (eventually consistent processing) with support for multiple message brokers (RabbitMQ, Kafka, etc.) and transactional outbox.

## Key Features

- `IDistributedMessagePublisher` implementation using DotNetCore.CAP
- Transactional outbox pattern support
- Multiple broker support (RabbitMQ, Kafka, Azure Service Bus, etc.)
- Dashboard for monitoring
- Automatic retry and dead-letter handling
- JSON serialization integration

## Installation

```bash
dotnet add package Framework.Domains.DistributedMessagePublisher.Cap
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCap(options =>
{
    options.UseEntityFramework<AppDbContext>();
    options.UseRabbitMQ("localhost");
    options.UseDashboard();
});

builder.Services.AddCapDistributedMessagePublisher();
```

### Publishing Messages

```csharp
public sealed class OrderService(IDistributedMessagePublisher publisher)
{
    public async Task CompleteOrderAsync(Order order, CancellationToken ct)
    {
        order.Complete();

        // Publish distributed message
        await publisher.PublishAsync(
            new OrderCompletedMessage(order.Id, order.Total),
            ct
        ).AnyContext();
    }
}
```

### Handling Messages

```csharp
[Message("orders.completed")]
public sealed class OrderCompletedHandler : IDistributedMessageHandler<OrderCompletedMessage>
{
    public async Task HandleAsync(OrderCompletedMessage message, CancellationToken ct)
    {
        // Handle the message
    }
}
```

## Configuration

Configured via CAP options. See CAP documentation for broker-specific settings.

## Dependencies

- `Framework.Domains`
- `Framework.Base`
- `Framework.Serializer.Json`
- `DotNetCore.CAP`
- `DotNetCore.CAP.Dashboard`

## Side Effects

- Registers `IDistributedMessagePublisher`
- CAP registers background services for message processing
