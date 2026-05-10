# Headless.Messaging.Abstractions

Core abstractions for type-safe, high-performance message consumption and publishing with outbox pattern support.

## Problem Solved

Provides standardized interfaces for building reliable distributed messaging systems with compile-time type safety, avoiding reflection overhead and enabling deterministic publish and consume behavior.

## Key Features

- **Type-Safe Consumption**: `IConsume<TMessage>` interface with `ConsumeContext<TMessage>` for compile-time verification (5-8x faster than reflection)
- **Outbox Publishing**: `IOutboxPublisher` for transactional message publishing with database consistency
- **Scheduled Publishing**: `IScheduledPublisher` for delayed message delivery
- **Direct Publishing**: `IDirectPublisher` for fire-and-forget, low-latency message delivery
- **Runtime Subscriptions**: `IRuntimeSubscriber` for ephemeral broker-attached delegates with scoped DI
- **Per-Dispatch Lifecycle Hooks**: `IConsumerLifecycle` runs around each scoped message delivery
- **Rich Metadata**: Message ID, correlation ID, timestamps, headers, and topic routing

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

// Register consumers with Headless.Messaging.Core
builder.Services.AddHeadlessMessaging(options =>
{
    options.SubscribeFromAssemblyContaining<Program>();
    options.WithTopicMapping<OrderPlacedEvent>("orders.placed");
});

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
            new PublishOptions
            {
                Topic = "orders.placed",
                MessageId = $"order:{order.Id}"
            },
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

// Schedule a message for later delivery
public sealed class ReminderService(IScheduledPublisher publisher)
{
    public async Task ScheduleAsync(ReminderEvent reminder, CancellationToken ct)
    {
        await publisher.PublishDelayAsync(TimeSpan.FromMinutes(5), reminder, ct);
    }
}
```

## Runtime Subscriptions

Runtime delegates use the same scoped consume pipeline as `IConsume<T>` handlers. The default policy is deterministic and fail-fast:

- topic resolves from mappings or conventions
- group resolves from application id + handler id + version
- duplicate registrations are rejected unless you explicitly opt into `Ignore` or `Replace`
- anonymous delegates must provide `HandlerId`

`PublishOptions.MessageId` is a logical transport-level identifier. Durable outbox providers keep their own numeric storage ID for retries, monitoring, requeue, and delete operations. When a message is published durably, `MessageId` is capped at `PublishOptions.MessageIdMaxLength` characters.

`PublishOptions.TenantId` is the typed surface for multi-tenancy on the envelope. Use it instead of writing the wire header directly. The publish pipeline enforces a strict 4-case integrity policy: a raw write to `Headers["headless-tenant-id"]` without a typed property is rejected with `InvalidOperationException`; a raw write that disagrees with the typed property is also rejected; a raw write that matches the typed property is accepted (no-op reconciliation). The corresponding `ConsumeContext.TenantId` is populated from the wire header (`Headers.TenantId`) and is `null` when absent, empty, whitespace, or longer than `PublishOptions.TenantIdMaxLength` (lenient consume-side handling). Consume-side values are untrusted wire data — validate the charset before downstream use in URLs, SQL, or log lines.

> **API note.** `PublishOptions` and `ConsumeContext<TMessage>` are `sealed record class`. They have value equality across all properties and support `with` expressions for non-destructive mutation — for example, a publish-side filter can stamp a tenant via `context.Options = (context.Options ?? new PublishOptions()) with { TenantId = "acme" }` without manually copying every other field. Note that `Headers` carries an `IDictionary<string,string?>`, whose default reference-equality may surprise callers comparing two instances that differ only in header contents.

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

- none beyond the .NET runtime surface

## Side Effects

None. This is an abstractions package.
