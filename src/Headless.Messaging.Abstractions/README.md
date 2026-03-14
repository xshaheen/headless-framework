# Headless.Messaging.Abstractions

Core abstractions for type-safe, high-performance message consumption and publishing with outbox pattern support.

## Problem Solved

Provides standardized interfaces for building reliable distributed messaging systems with compile-time type safety, avoiding reflection overhead and enabling deterministic publish and consume behavior.

## Key Features

- **Type-Safe Consumption**: `IConsume<TMessage>` interface with `ConsumeContext<TMessage>` for compile-time verification (5-8x faster than reflection)
- **Outbox Publishing**: `IOutboxPublisher` for transactional message publishing with database consistency
- **Direct Publishing**: `IDirectPublisher` for fire-and-forget, low-latency message delivery
- **Runtime Subscriptions**: `IRuntimeSubscriber` for ephemeral broker-attached delegates with scoped DI
- **Per-Dispatch Lifecycle Hooks**: `IConsumerLifecycle` runs around each scoped message delivery
- **Rich Metadata**: Message ID, correlation ID, timestamps, headers, and topic routing
- **Consumer Configuration**: `IMessagingBuilder` for deterministic assembly scanning, conventions, and manual consumer registration
- **Delayed Publishing**: Schedule messages for future delivery
- **Multi-Type Consumers**: Assembly scanning registers every `IConsume<T>` interface on the same consumer

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
builder.Services.AddMessaging(options =>
{
    options.SubscribeFromAssemblyContaining<Program>();
    options.WithTopicMapping<OrderPlacedEvent>("orders.placed");
});

// Explicit Subscribe<TConsumer>() is for single-message consumers.
// Use SubscribeFromAssembly(...) when one consumer implements multiple IConsume<T> interfaces.

// Publish with outbox (reliable delivery)
public sealed class OrderService(IOutboxPublisher publisher)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        await publisher.PublishAsync(
            new OrderPlacedEvent
            {
                OrderId = order.Id,
                Total = order.Total
            },
            new PublishOptions { Topic = "orders.placed" },
            ct);
    }
}

// Publish directly (fire-and-forget)
public sealed class MetricsService(IDirectPublisher publisher)
{
    public async Task TrackAsync(MetricEvent metric, CancellationToken ct)
    {
        // Topic resolved from WithTopicMapping<MetricEvent>()
        await publisher.PublishAsync(metric, ct);
    }
}
```

## Runtime Subscriptions

Runtime delegates use the same scoped consume pipeline as `IConsume<T>` handlers. The default policy is deterministic and fail-fast:

- topic resolves from mappings or conventions
- group resolves from application id + handler id + version
- duplicate registrations are rejected unless you explicitly opt into `Ignore` or `Replace`
- anonymous delegates must provide `HandlerId`

```csharp
public sealed class ProjectionWarmup(IRuntimeSubscriber subscriber)
{
    public ValueTask<RuntimeSubscriptionHandle> AttachAsync(CancellationToken cancellationToken)
    {
        return subscriber.SubscribeAsync<OrderPlacedEvent>(
            async (context, services, ct) =>
            {
                var cache = services.GetRequiredService<IProjectionCache>();
                await cache.WarmAsync(context.Message.OrderId, ct);
            },
            new RuntimeSubscriptionOptions
            {
                HandlerId = "ProjectionWarmup.OrderPlaced",
            },
            cancellationToken
        );
    }
}
```

## Consumer Lifecycle Hooks

`IConsumerLifecycle` is a per-dispatch contract, not an app startup or shutdown contract. When a consumer implements it:

- `OnStartingAsync()` runs after the scoped consumer instance is resolved and before `Consume(...)`
- `OnStoppingAsync()` runs in a `finally` block after each delivery, even when `Consume(...)` throws
- cleanup exceptions are suppressed so they do not mask the original message failure

Use it for per-delivery setup and teardown. Do not rely on it for application-wide startup state.

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
