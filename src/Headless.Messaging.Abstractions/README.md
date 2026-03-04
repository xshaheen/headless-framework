# Headless.Messaging.Abstractions

Core abstractions for type-safe, high-performance message consumption and publishing with outbox pattern support.

## Problem Solved

Provides standardized interfaces for building reliable distributed messaging systems with compile-time type safety, avoiding reflection overhead and enabling transactional outbox patterns for guaranteed message delivery.

## Key Features

- **Type-Safe Consumption**: `IConsume<TMessage>` interface with `ConsumeContext<TMessage>` for compile-time verification (5-8x faster than reflection)
- **Outbox Publishing**: `IOutboxPublisher` for transactional message publishing with database consistency
- **Direct Publishing**: `IDirectPublisher` for fire-and-forget, low-latency message delivery
- **Rich Metadata**: Message ID, correlation ID, timestamps, headers, and topic routing
- **Consumer Configuration**: `IMessagingBuilder` for assembly scanning and manual consumer registration
- **Delayed Publishing**: Schedule messages for future delivery
- **Multi-Type Consumers**: Single consumer can handle multiple message types
- **Job Scheduling**: `ScheduledTrigger`, `[Recurring]` attribute, `IScheduledJobManager`, and `IScheduledJobStorage` contracts

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

// Publish with outbox (reliable delivery)
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

## Scheduling

Type-safe job scheduling routed through the messaging infrastructure.

### ScheduledTrigger

Message type consumed by scheduled job handlers. Properties: `JobName`, `ScheduledTime`, `Attempt`, `CronExpression`, `ParentJobId`, `Payload`.

```csharp
public sealed class TokenCleanupJob : IConsume<ScheduledTrigger>
{
    public async ValueTask Consume(
        ConsumeContext<ScheduledTrigger> context,
        CancellationToken cancellationToken)
    {
        var trigger = context.Message;
        // trigger.JobName, trigger.ScheduledTime, trigger.Attempt, etc.
    }
}
```

### [Recurring] Attribute

Marks a consumer as a cron-based recurring job. Uses 6-field cron expressions (second, minute, hour, day-of-month, month, day-of-week).

```csharp
[Recurring("0 0 */6 * * *", Name = "UsageReport", TimeZone = "America/New_York")]
public sealed class UsageReportJob : IConsume<ScheduledTrigger> { /* ... */ }
```

Properties:
- `CronExpression` -- 6-field cron expression
- `Name` -- optional human-readable job name (defaults to consumer type name)
- `TimeZone` -- IANA time zone for cron evaluation (defaults to UTC)
- `RetryIntervals` -- retry delay intervals in seconds
- `SkipIfRunning` -- skip occurrence if previous is still running (default: `true`)

### IScheduledJobManager

Runtime management API for scheduled jobs: `GetAllAsync`, `GetByNameAsync`, `EnableAsync`, `DisableAsync`, `TriggerAsync`, `DeleteAsync`.

### IScheduledJobStorage

Storage provider contract for job and execution persistence. Implementations must guarantee atomic job acquisition to prevent double-pickup across nodes.

### Entity Types

- `ScheduledJob` -- job definition with cron expression, time zone, retry config, lock state, and execution history
- `JobExecution` -- single execution attempt tracking status, duration, retry attempt, and error

### Enums

- `ScheduledJobType` -- `Recurring`, `OneTime`
- `ScheduledJobStatus` -- `Pending`, `Running`, `Completed`, `Failed`, `Disabled`
- `JobExecutionStatus` -- `Pending`, `Running`, `Succeeded`, `Failed`

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
