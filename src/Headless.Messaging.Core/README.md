# Headless.Messaging.Core

Core implementation of the type-safe messaging system with outbox pattern, message processing, and per-dispatch consumer lifecycle management.

## Problem Solved

Provides the foundational runtime for reliable distributed messaging with transactional outbox, automatic retries, delayed delivery, and type-safe consumer orchestration across multiple transport providers.

## Key Features

- **Outbox Publisher**: Transactional message publishing with database consistency
- **Unified Publish Contract**: `IDirectPublisher` and `IOutboxPublisher` share the same `PublishAsync(message, options, ct)` surface
- **Consumer Management**: Automatic registration, invocation, and per-dispatch lifecycle handling
- **Runtime Delegate Support**: Broker-attached function handlers with scoped DI and the same consume pipeline as class handlers
- **Message Processing**: Retry processor, delayed message scheduler, transport health checks
- **Type-Safe Dispatch**: Reflection-free consumer invocation via compile-time generated code
- **Extension System**: Pluggable storage and transport providers
- **Bootstrapper**: Hosted service for startup and shutdown coordination

## Installation

```bash
dotnet add package Headless.Messaging.Core
```

## Quick Start

```csharp
// Register messaging with storage and transport
builder.Services.AddMessaging(options =>
{
    // Core configuration
    options.SucceedMessageExpiredAfter = 24 * 3600;
    options.FailedRetryCount = 50;
    options.UseConventions(c =>
    {
        c.UseKebabCaseTopics();
        c.UseApplicationId("ordering-api");
        c.UseVersion("v1");
    });

    // Add storage (required)
    options.UsePostgreSql("connection_string");

    // Add transport (required)
    options.UseRabbitMQ(rmq =>
    {
        rmq.HostName = "localhost";
        rmq.Port = 5672;
    });

    // Register consumers
    options.SubscribeFromAssemblyContaining<Program>();
});

// Publish messages with outbox (reliable delivery)
public sealed class OrderService(IOutboxPublisher publisher, IOutboxTransaction transaction)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        await using (var dbTransaction = await dbContext.Database.BeginTransactionAsync(transaction, autoCommit: false, ct))
        {
            await publisher.PublishAsync(order, new PublishOptions { Topic = "orders.placed" }, ct);
            await dbContext.SaveChangesAsync(ct);
            await dbTransaction.CommitAsync(ct);
        }
    }
}

// Publish messages directly (fire-and-forget)
public sealed class MetricsService(IDirectPublisher publisher)
{
    public async Task TrackEventAsync(MetricEvent metric, CancellationToken ct)
    {
        // Bypasses outbox - sent directly to transport
        await publisher.PublishAsync(metric, ct);
    }
}
```

## Defaults And Telemetry

- `AddMessaging(...)` is the primary DI entry point.
- `SubscribeFromAssemblyContaining<T>()` and `Subscribe<T>()` are the primary registration APIs.
- topic and group defaults are deterministic; duplicate registrations fail fast by default.
- direct publish, outbox publish, and runtime delegates preserve the existing diagnostic listener and metric names used by dashboards.
- runtime delegates execute through the same scoped consume pipeline as class handlers, so diagnostics, filters, and correlation behavior stay aligned.
- `IConsumerLifecycle` hooks run per delivery on the scoped consumer instance, not once for application startup or shutdown.

## Publisher Options

### IOutboxPublisher (Reliable Delivery)

Use `IOutboxPublisher` for messages that must not be lost:

- **Transactional**: Messages are stored in database before sending
- **At-least-once**: Automatic retries with configurable backoff
- **Delayed delivery**: Schedule messages for future delivery
- **Ordering**: Preserves publish order within transactions

### IDirectPublisher (Fire-and-Forget)

Use `IDirectPublisher` for high-throughput, low-latency scenarios where occasional message loss is acceptable:

```csharp
// Configure topic mapping
options.WithTopicMapping<MetricEvent>("metrics.events");

// Inject and publish
public sealed class MetricsPublisher(IDirectPublisher publisher)
{
    public async Task PublishMetric(MetricEvent metric, CancellationToken ct)
    {
        // Sent immediately to transport, no persistence
        await publisher.PublishAsync(metric, ct);

        var options = new PublishOptions
        {
            CorrelationId = Guid.NewGuid().ToString(),
            Headers = new Dictionary<string, string?> { ["tenant-id"] = "demo" },
        };
        await publisher.PublishAsync(metric, options, ct);
    }
}
```

**Characteristics:**

- **No persistence**: Messages bypass outbox storage
- **Lower latency**: Direct transport send without database round-trip
- **No retries**: Transport failures throw immediately
- **Topic from type**: Topic resolved from `WithTopicMapping<T>()` or conventions

**Use cases:** Metrics, telemetry, cache invalidation, real-time notifications

## Runtime Delegates

Use `IRuntimeSubscriber` for ephemeral broker-attached handlers that should share the normal consume pipeline:

```csharp
public sealed class ProjectionSubscriptions(IRuntimeSubscriber subscriber)
{
    public ValueTask<RuntimeSubscriptionHandle> AttachAsync(CancellationToken cancellationToken)
    {
        return subscriber.SubscribeAsync<OrderPlacedEvent>(
            async (context, services, ct) =>
            {
                var projector = services.GetRequiredService<IOrderProjector>();
                await projector.ProjectAsync(context.Message, ct);
            },
            new RuntimeSubscriptionOptions
            {
                HandlerId = "ProjectionSubscriptions.OrderPlaced",
            },
            cancellationToken
        );
    }
}
```

## Configuration

Register in `Program.cs`:

```csharp
builder.Services.AddMessaging(options =>
{
    options.FailedRetryCount = 50;
    options.SucceedMessageExpiredAfter = 24 * 3600;
    options.ConsumerThreadCount = 1;
    options.DefaultGroupName = "myapp";
});
```

## Message Ordering Guarantees

Message ordering guarantees depend on the transport provider and configuration:

### Transport-Specific Ordering

- **Kafka**: Messages with same partition key are strictly ordered within partitions
- **Azure Service Bus**: FIFO ordering when sessions are enabled (`EnableSessions = true`)
- **RabbitMQ**: No ordering guarantees by default; consumers may process messages concurrently
- **AWS SQS**: FIFO queues provide strict ordering; standard queues do not
- **Redis Streams**: Ordered within consumer group, but parallel consumers may process out of order
- **NATS**: Ordering preserved per subject, but concurrent consumers introduce variability
- **Pulsar**: Ordered within partitions when using partition key
- **InMemoryQueue**: FIFO ordering with single consumer thread

### Configuration Impact on Ordering

- **`ConsumerThreadCount > 1`**: Enables concurrent message consumption, messages may process out of order
- **`EnableSubscriberParallelExecute = true`**: Buffers messages in-memory queue for parallel processing, no ordering guarantee
- **Single consumer thread (`ConsumerThreadCount = 1`)**: Sequential processing, maintains transport order

### Recommendations

- For strict ordering: Use `ConsumerThreadCount = 1` with Kafka (partition key), Azure Service Bus (sessions), or AWS SQS (FIFO)
- For high throughput: Use parallel processing; design consumers to be order-independent
- Test ordering behavior with your specific transport and configuration

## Dependencies

- `Headless.Messaging.Abstractions`
- `Headless.Base`
- `Headless.Checks`
- Transport package (RabbitMQ, Kafka, etc.)
- Storage package (PostgreSql, SqlServer, etc.)

## Side Effects

- Starts background hosted service for message processing
- Creates database tables for outbox storage (via storage provider)
- Establishes transport connections (via transport provider)
