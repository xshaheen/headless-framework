# Headless.Messaging.Core

Core implementation of the type-safe messaging system with outbox pattern, message processing, and per-dispatch consumer lifecycle management.

## Problem Solved

Provides the foundational runtime for reliable distributed messaging with transactional outbox, automatic retries, scheduled delivery, and type-safe consumer orchestration across multiple transport providers.

## Key Features

- **Outbox Publisher**: Transactional message publishing with database consistency
- **Scheduled Publisher**: Delayed message delivery through the configured scheduler pipeline
- **Unified Publish Contract**: `IDirectPublisher` and `IOutboxPublisher` share the same `PublishAsync(message, options, ct)` surface
- **Consumer Management**: `AddHeadlessMessaging(...)`, `Subscribe*()`, `AddConsumer(...)`, invocation, and per-dispatch lifecycle handling
- **Runtime Delegate Support**: Broker-attached function handlers with scoped DI and the same consume pipeline as class handlers
- **Message Processing**: Retry processor, delayed message scheduler, transport health checks
- **Type-Safe Dispatch**: Reflection-free consumer invocation via compile-time generated code
- **Extension System**: Pluggable storage and transport providers
- **Bootstrapper**: Hosted service for startup and shutdown coordination
- **Circuit Breaker**: Per-consumer-group circuit breaker (Closed → Open → HalfOpen) with exponential open-duration escalation
- **Adaptive Retry Backpressure**: Retry processor backs off polling when circuit-open rate exceeds threshold

## Installation

```bash
dotnet add package Headless.Messaging.Core
```

## Quick Start

```csharp
// Register messaging with storage and transport
builder.Services.AddHeadlessMessaging(setup =>
{
    // Core configuration (value-typed options live under setup.Options)
    setup.Options.SucceedMessageExpiredAfter = 24 * 3600;
    setup.Options.RetryPolicy.MaxPersistedRetries = 50;
    setup.UseConventions(c =>
    {
        c.UseKebabCaseTopics();
        c.UseApplicationId("ordering-api");
        c.UseVersion("v1");
    });

    // Add storage (required)
    setup.UsePostgreSql("connection_string");

    // Add transport (required)
    setup.UseRabbitMQ(rmq =>
    {
        rmq.HostName = "localhost";
        rmq.Port = 5672;
    });

    // Register consumers
    setup.SubscribeFromAssemblyContaining<Program>();
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

- `AddHeadlessMessaging(...)` is the primary DI entry point.
- `SubscribeFromAssemblyContaining<T>()` and `Subscribe<T>()` are the primary registration APIs.
- `AddConsumer<TConsumer, TMessage>(topic)` is the library-author registration API when a package wants to contribute consumers through DI.
- topic and group defaults are deterministic; duplicate registrations fail fast by default.
- direct publish, outbox publish, and runtime delegates preserve the existing diagnostic listener and metric names used by dashboards.
- runtime delegates execute through the same scoped consume pipeline as class handlers, so diagnostics, filters, and correlation behavior stay aligned.
- `IConsumerLifecycle` hooks run per delivery on the scoped consumer instance, not once for application startup or shutdown.
- `IBootstrapper.IsStarted` becomes `true` only after required messaging processors finish startup successfully.
- Concurrent `BootstrapAsync(...)` callers share the same in-flight startup work; canceling a later caller's wait does not abort the shared bootstrap.

## Bootstrap Lifecycle

- `BootstrapAsync(...)` is the readiness boundary for manual startup paths such as tests or custom hosts.
- Wait for `BootstrapAsync(...)` to complete before publishing when you bootstrap manually.
- `BootstrapAsync(...)` completes only after initial transport consumers report broker-side readiness, not merely after their background tasks are queued.
- Startup fails when a required messaging processor cannot start; partial logged startup is not treated as success.
- Runtime delegate subscriptions attached before the consumer register is ready are picked up by the initial startup path.
- Runtime delegate subscriptions attached after the consumer register is ready trigger a consumer refresh so they are not missed during late startup.

## Publisher Options

### IOutboxPublisher (Reliable Delivery)

Use `IOutboxPublisher` for messages that must not be lost:

- **Transactional**: Messages are stored in database before sending
- **At-least-once**: Automatic retries with configurable backoff
- **Ordering**: Preserves publish order within transactions
- **Two identities**: durable providers generate an internal numeric storage ID for retries and operator actions while `PublishOptions.MessageId` remains a logical string capped at `PublishOptions.MessageIdMaxLength` characters

### IScheduledPublisher (Delayed Delivery)

Use `IScheduledPublisher` when publish timing is part of the use case:

- **Delayed delivery**: Schedule messages for future delivery
- **Shared message model**: Uses the same message payloads and `PublishOptions`
- **Composable**: Current core implementation uses the outbox pipeline under the hood

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
            TenantId = "demo",
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

When runtime delegates are attached during application startup, the messaging runtime ensures they are either included in the initial consumer registration pass or trigger a refresh once the consumer register is live. You do not need to manually restart messaging after calling `SubscribeAsync(...)`.

## Configuration

Register in `Program.cs`:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Options.RetryPolicy.MaxPersistedRetries = 50;
    setup.Options.SucceedMessageExpiredAfter = 24 * 3600;
    setup.Options.ConsumerThreadCount = 1;
    setup.Options.DefaultGroupName = "myapp";
});
```

## Filters

Both sides of the pipeline support cross-cutting middleware via `IConsumeFilter` and `IPublishFilter`. Each interface exposes an `Executing` / `Executed` / `Exception` triad. Filters compose into a chain — `Executing` runs in registration order, `Executed` and `Exception` run in reverse (mirroring ASP.NET Core MVC).

```csharp
public sealed class LoggingConsumeFilter(ILogger<LoggingConsumeFilter> logger) : ConsumeFilter
{
    public override ValueTask OnSubscribeExecutingAsync(ExecutingContext context)
    {
        logger.LogInformation("Consuming {MessageId}", context.MediumMessage.Origin.GetId());
        return ValueTask.CompletedTask;
    }
}

public sealed class CorrelationPublishFilter : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        context.Options = (context.Options ?? new PublishOptions()) with
        {
            CorrelationId = context.Options?.CorrelationId ?? Guid.NewGuid().ToString(),
        };
        return ValueTask.CompletedTask;
    }
}

builder.Services.AddHeadlessMessaging(setup => { /* ... */ })
    .AddSubscribeFilter<LoggingConsumeFilter>()
    .AddPublishFilter<CorrelationPublishFilter>();
```

Both registrations are idempotent (`TryAddEnumerable` under the hood). `OnPublishExecutedAsync` runs after the message is accepted; exceptions from that phase are logged and suppressed to avoid caller retries that duplicate the message. Setting `PublishExceptionContext.ExceptionHandled = true` silently swallows a pre-accept publish failure — only do this when the filter has rerouted the message to a durable sink.

### Multi-Tenancy Propagation

When a host uses the root tenancy surface, configure messaging tenant posture there:

```csharp
builder.AddHeadlessTenancy(
    tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);
```

`PropagateTenant()` registers a built-in filter pair that ties the wire envelope to `ICurrentTenant`. `RequireTenantOnPublish()` flips strict publish tenancy so every publish must resolve a tenant from `PublishOptions.TenantId` or the ambient tenant.

`AddHeadlessTenancy(...).Messaging(m => m.PropagateTenant())` is the single composition point for tenant propagation. The previous `MessagingBuilder.AddTenantPropagation()` extension has been removed in favor of the root tenancy seam.

- **Publish:** stamps `PublishOptions.TenantId` from the ambient `ICurrentTenant.Id` when the caller has not set it explicitly. Caller overrides win.
- **Consume:** restores `ICurrentTenant.Change(...)` from the resolved `ConsumeContext<T>.TenantId` for the lifetime of the consume, including the exception path. Whitespace, empty, and oversized header values map to "no tenant".

Tenant propagation requires a real `ICurrentTenant`. `AddHeadlessMessaging()` registers only the safe `NullCurrentTenant` fallback; `Headless.Api` and `Headless.Orm.EntityFramework` setup replace that fallback while preserving consumer-provided tenant implementations.

The consume filter trusts the inbound envelope. Topics exposed to external producers must layer envelope validation upstream.

### Diagnostics / PII

The `HEADLESS_TENANCY_*` event family is non-PII by design with one exception: `TenantContextSwitched` (EventId 64, Debug level) surfaces the raw tenant identifier as it restores `ICurrentTenant` on the consume side. Operators should gate Debug-level messaging logs accordingly when tenant identifiers contain PII (for example customer email or organization slugs).

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

## Retry Policy

`MessagingOptions.RetryPolicy` is the single composition point for all retry behavior — both inline and persisted retries, on both consume and publish paths. Configure it once; the framework wires it into `SubscribeExecutor` (consume) and `MessageSender` (publish).

### Global Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Options.RetryPolicy.MaxInlineRetries = 2;
    setup.Options.RetryPolicy.MaxPersistedRetries = 15;
    setup.Options.RetryPolicy.BackoffStrategy = new ExponentialBackoffStrategy(
        initialDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromMinutes(5)
    );
    setup.Options.RetryPolicy.OnExhausted = (info, ct) =>
    {
        // Fires only when budget is fully consumed (Exhausted), not on permanent failures (Stop).
        var logger = info.ServiceProvider.GetRequiredService<ILogger<MyService>>();
        logger.LogError(info.Exception, "Message {Id} permanently failed", info.Message.StorageId);
        return Task.CompletedTask;
    };
});
```

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `MaxInlineRetries` | `int` | `2` | Retries to run inline on each delivery before persisting. `>= 0`. |
| `MaxPersistedRetries` | `int` | `15` | Maximum persisted-retry pickups. `>= 0`. Total attempts = `(MaxInlineRetries + 1) × (MaxPersistedRetries + 1)`. |
| `BackoffStrategy` | `IRetryBackoffStrategy` | `new ExponentialBackoffStrategy()` | Strategy returns `RetryDecision.Stop` (permanent) or `RetryDecision.Continue(delay)` (transient). |
| `OnExhausted` | `Func<FailedInfo, CancellationToken, Task>?` | `null` | Fires only on `RetryDecision.Exhausted`. Does NOT fire on `RetryDecision.Stop`. |

### Exhausted vs Stop

`OnExhausted` fires **only on `RetryDecision.Exhausted`** — the retry budget was fully consumed and the failure was transient.

It does **NOT** fire on `RetryDecision.Stop`. Stop is the framework's signal for:

- **Permanent exceptions** classified by the backoff strategy (`SubscriberNotFoundException`, `ArgumentException`, `ArgumentNullException`, `InvalidOperationException`, `NotSupportedException`).
- **Cancellation** (`OperationCanceledException` whose token matches the dispatch cancellation token).

For a single exit point covering both Stop and Exhausted, use an `IConsumeFilter` / `IPublishFilter`.

### Inline vs Persisted Retry Paths

Retries up to `MaxInlineRetries` run inline inside the same `ExecuteAsync` / `SendAsync` call. Once the inline budget is exhausted, the message is persisted with `NextRetryAt` set and picked up by `MessageNeedToRetryProcessor` (up to `MaxPersistedRetries` times). Each pickup then bursts another round of `MaxInlineRetries` inline attempts.

Worked example with `MaxInlineRetries = 2, MaxPersistedRetries = 2` — total (2+1)×(2+1) = 9 attempts:

```
pickup 1 (initial dispatch):
  attempt 1 (original)        ── inline
  attempt 2 (inline retry #1) ── inline, after BackoffStrategy delay
  attempt 3 (inline retry #2) ── inline, after BackoffStrategy delay → persist (1/2)
pickup 2 (persisted retry #1):
  attempt 4                   ── inline
  attempt 5 (inline retry #1) ── inline, after BackoffStrategy delay
  attempt 6 (inline retry #2) ── inline, after BackoffStrategy delay → persist (2/2)
pickup 3 (persisted retry #2):
  attempt 7                   ── inline
  attempt 8 (inline retry #1) ── inline, after BackoffStrategy delay
  attempt 9 (inline retry #2) ── final; on failure → Exhausted → OnExhausted fires
```

For the full property table, migration guide, and `FailedInfo` construction details, see [docs/llms/messaging.md](../../../../docs/llms/messaging.md).

## Circuit Breaker

Per-consumer-group circuit breaker that pauses transport consumption when a dependency is unhealthy, preventing message-retry storms.

**State machine:** Closed → Open (pause transport) → HalfOpen (probe) → Closed (resume) or Open (re-trip). Open duration escalates exponentially on repeated trips and resets after consecutive successful close cycles.

### Global Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // Circuit breaker (applies to all consumer groups)
    setup.Options.CircuitBreaker.FailureThreshold = 5;                       // consecutive transient failures to trip
    setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30);    // initial open duration
    setup.Options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromSeconds(240); // cap after escalation

    // Adaptive retry backpressure
    setup.Options.RetryProcessor.AdaptivePolling = true;
    setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromSeconds(60);     // replaces the old FailedRetryInterval; default 60s
    setup.Options.RetryProcessor.MaxPollingInterval = TimeSpan.FromMinutes(15); // 15 min cap
    setup.Options.RetryProcessor.CircuitOpenRateThreshold = 0.8;              // back off above 80% circuit-open rate
});
```

### Per-Consumer Override

```csharp
setup.Subscribe<PaymentHandler>()
    .Topic("payments.process")
    .WithCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 3;                    // more sensitive
        cb.OpenDuration = TimeSpan.FromSeconds(60); // longer cooldown
    });

// Disable circuit breaker for a best-effort consumer
setup.Subscribe<MetricsHandler>()
    .WithCircuitBreaker(cb => cb.Enabled = false);
```

### Custom Exception Predicate

```csharp
setup.Options.CircuitBreaker.IsTransientException = ex =>
    CircuitBreakerDefaults.IsTransient(ex) || ex is MyCustomTransientException;
```

Default `CircuitBreakerDefaults.IsTransient` covers: `TimeoutException`, `HttpRequestException` (5xx), `SocketException`, `BrokerConnectionException`, `TaskCanceledException` (timeout-only).

### Observability

- **OTel counter**: `messaging.circuit_breaker.trips` (tagged by group)
- **OTel histogram**: `messaging.circuit_breaker.open_duration` (tagged by group)
- State transitions logged at Warning level

### Programmatic Control

Inject `ICircuitBreakerMonitor` for runtime observation and manual recovery:

```csharp
var monitor = app.Services.GetRequiredService<ICircuitBreakerMonitor>();

// Check state
var states = monitor.GetAllStates(); // all groups with current state
var isOpen = monitor.IsOpen("payments");
var state = monitor.GetState("payments"); // Closed, Open, or HalfOpen

// Manual recovery (operator/agent action)
var wasReset = await monitor.ResetAsync("payments"); // true if reset performed
```

Inject `IRetryProcessorMonitor` for adaptive retry backpressure inspection and reset:

```csharp
var retryMonitor = app.Services.GetRequiredService<IRetryProcessorMonitor>();

// Inspect backpressure state
var pollingInterval = retryMonitor.CurrentPollingInterval;
var isBackedOff = retryMonitor.IsBackedOff;

// Manual recovery (operator/agent action)
await retryMonitor.ResetBackpressureAsync(cancellationToken);
```

### Cluster Scope Limitation

The circuit breaker operates **per-process only**. There is no cross-instance coordination — each application instance maintains its own circuit state. In a multi-replica deployment, one instance may have an open circuit while others remain closed.

## Dependencies

- `Headless.Messaging.Abstractions`
- `Headless.Extensions`
- `Headless.Checks`
- `Headless.MultiTenancy`
- Transport package (RabbitMQ, Kafka, etc.)
- Storage package (PostgreSql, SqlServer, etc.)

## Side Effects

- Starts background hosted service for message processing
- Creates database tables for outbox storage (via storage provider)
- Establishes transport connections (via transport provider)
