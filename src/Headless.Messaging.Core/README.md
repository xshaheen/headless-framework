# Headless.Messaging.Core

Core implementation of the type-safe messaging system with outbox pattern, message processing, and consumer lifecycle management.

## Problem Solved

Provides the foundational runtime for reliable distributed messaging with transactional outbox, automatic retries, delayed delivery, and type-safe consumer orchestration across multiple transport providers.

## Key Features

- **Outbox Publisher**: Transactional message publishing with database consistency
- **Consumer Management**: Automatic registration, invocation, and lifecycle handling
- **Message Processing**: Retry processor, delayed message scheduler, transport health checks
- **Type-Safe Dispatch**: Reflection-free consumer invocation via compile-time generated code
- **Extension System**: Pluggable storage and transport providers
- **Bootstrapper**: Hosted service for startup and shutdown coordination
- **Job Scheduling**: Cron-based recurring jobs with retry, distributed locking, and execution tracking

## Installation

```bash
dotnet add package Headless.Messaging.Core
```

## Quick Start

```csharp
// Register messaging with storage and transport
builder.Services.AddMessages(options =>
{
    // Core configuration
    options.SucceedMessageExpiredAfter = 24 * 3600;
    options.FailedRetryCount = 50;

    // Add storage (required)
    options.UsePostgreSql("connection_string");

    // Add transport (required)
    options.UseRabbitMQ(rmq =>
    {
        rmq.HostName = "localhost";
        rmq.Port = 5672;
    });

    // Register consumers
    options.ScanConsumers(typeof(Program).Assembly);
});

// Publish messages with outbox (reliable delivery)
public sealed class OrderService(IOutboxPublisher publisher, IOutboxTransaction transaction)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        using (transaction.Begin())
        {
            // Database changes and message publish are atomic
            await publisher.PublishAsync("orders.placed", order, cancellationToken: ct);
            await transaction.CommitAsync(ct);
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

        // With custom headers (using Headers constants)
        var headers = new Dictionary<string, string?>
        {
            [Headers.CorrelationId] = Guid.NewGuid().ToString(),
        };
        await publisher.PublishAsync(metric, headers, ct);
    }
}
```

**Characteristics:**

- **No persistence**: Messages bypass outbox storage
- **Lower latency**: Direct transport send without database round-trip
- **No retries**: Transport failures throw immediately
- **Topic from type**: Topic resolved from `WithTopicMapping<T>()` or conventions

**Use cases:** Metrics, telemetry, cache invalidation, real-time notifications

## Configuration

Register in `Program.cs`:

```csharp
builder.Services.AddMessages(options =>
{
    options.FailedRetryCount = 50;
    options.SucceedMessageExpiredAfter = 24 * 3600;
    options.ConsumerThreadCount = 1;
    options.DefaultGroupName = "myapp";
});
```

## Job Scheduling

Built-in cron-based job scheduling routed through the messaging consumer infrastructure.

### Defining Scheduled Jobs

Use the `[Recurring]` attribute on an `IConsume<ScheduledTrigger>` consumer:

```csharp
[Recurring("0 */5 * * * *", Name = "CleanupExpiredTokens")]
public sealed class TokenCleanupJob : IConsume<ScheduledTrigger>
{
    public async ValueTask Consume(
        ConsumeContext<ScheduledTrigger> context,
        CancellationToken cancellationToken)
    {
        // cleanup logic
    }
}
```

Jobs are automatically discovered when using `ScanConsumers()`.

### Fluent API

Register jobs programmatically via the consumer builder:

```csharp
builder.Services.AddMessages(options =>
{
    options.Consumer<TokenCleanupJob>()
        .WithSchedule("0 */5 * * * *")
        .WithTimeZone("America/New_York")
        .Build();
});
```

### SchedulerOptions

Configure scheduler behavior:

```csharp
builder.Services.Configure<SchedulerOptions>(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(1);     // default
    options.MaxPollingInterval = TimeSpan.FromSeconds(60); // default
    options.BatchSize = 10;                                // default
    options.LockHolder = Environment.MachineName;          // default
    options.LockTimeout = TimeSpan.FromMinutes(5);         // default
});
```

- `PollingInterval` -- base polling interval; the scheduler backs off from this during idle periods
- `MaxPollingInterval` -- upper bound for adaptive backoff (doubles each idle cycle, capped here)
- `BatchSize` -- max jobs acquired per poll cycle
- `LockHolder` -- instance identifier for atomic job acquisition
- `LockTimeout` -- distributed lock timeout for `SkipIfRunning` jobs

### Retry Behavior

Set `RetryIntervals` on `[Recurring]` to define retry delays (in seconds) after failures. The last interval is reused for subsequent retries. Without `RetryIntervals`, recurring jobs skip to the next cron occurrence on failure.

### Job Reconciliation

On startup, the scheduler reconciles `[Recurring]`-annotated consumers with persisted `ScheduledJob` records via upsert, ensuring new jobs are registered and existing jobs have updated cron expressions.

### Distributed Locking

When `IDistributedLockProvider` is registered and a job has `SkipIfRunning = true`, the scheduler acquires a cross-instance lock before dispatching. If the lock is held by another node, the occurrence is skipped.

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
- Starts scheduler background service for job polling and dispatch
- Creates database tables for outbox and scheduling storage (via storage provider)
- Establishes transport connections (via transport provider)
