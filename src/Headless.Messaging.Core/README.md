# Headless.Messaging.Core

Core implementation of the type-safe messaging system with outbox pattern, message processing, and per-dispatch consumer lifecycle management.

## Problem Solved

Provides the foundational runtime for reliable distributed messaging with transactional outbox, automatic retries, scheduled delivery, and type-safe consumer orchestration across multiple transport providers.

## Key Features

- **Intent-Specific Publishers**: `IBus` / `IOutboxBus` for broadcast and `IQueue` / `IOutboxQueue` for point-to-point delivery
- **Outbox Delivery**: Transactional message publishing with database consistency
- **Scheduled Delivery**: `PublishOptions.Delay` and `EnqueueOptions.Delay` defer outbox dispatch
- **Consumer Management**: `ForMessage<TMessage>(...)`, `setup.ForMessagesFromAssembly(...)`, invocation, and per-dispatch lifecycle handling
- **Registration Builders**: callback interfaces such as `IMessageBuilder<TMessage>` and `IBusConsumerBuilder<TConsumer>` live under `Headless.Messaging.Registration`; lambda setup usually infers them, while explicit references should import that namespace
- **Public Runtime SPI**: the blessed cross-package contracts consumed by storage providers, transports, and dashboards — `IProcessingServer`, `IConsumerServiceSelector`, and `MethodMatcherCache` — live under `Headless.Messaging.Runtime` (the `TransportNaming` / `RuntimeTypeInspection` helpers there are `internal`, shared with first-party transports via `InternalsVisibleTo`) (previously `Headless.Messaging.Internal`, which now holds only implementation detail); monitoring status is the typed `StatusName` enum under `Headless.Messaging.Monitoring`, so `MessageView.StatusName` and the `MessageQuery.StatusName` filter are compile-time safe while the persisted/serialized value stays the enum member name
- **Runtime Delegate Support**: Broker-attached function handlers with scoped DI and the same consume pipeline as class handlers
- **Message Processing**: Retry processor, delayed message scheduler, transport health checks
- **Durable Intent Dispatch**: Outbox rows carry bus/queue intent so retry drainers use the matching transport
- **Type-Safe Dispatch**: Reflection-free consumer invocation via compile-time generated code
- **Extension System**: Pluggable storage and transport providers, with exactly one storage provider required
- **Bootstrapper**: Hosted service for startup and shutdown coordination
- **Circuit Breaker**: Per-consumer-group circuit breaker (Closed → Open → HalfOpen) with exponential open-duration escalation
- **Adaptive Retry Backpressure**: Retry processor backs off polling when circuit-open rate exceeds threshold
- **Distributed Lock Integration**: Optional `IDistributedLock`-backed mutual exclusion for multi-replica retry pickup (`UseStorageLock`)

## Installation

```bash
dotnet add package Headless.Messaging.Core
```

## Quick Start

For a local, dependency-free first run, install the in-memory transport and storage providers alongside Core:

```bash
dotnet add package Headless.Messaging.Core
dotnet add package Headless.Messaging.InMemory
dotnet add package Headless.Messaging.InMemoryStorage
```

```csharp
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemoryStorage();
    setup.UseInMemory();

    setup.ForMessage<OrderPlaced>(message =>
        message
            .MessageName("orders.placed")
            .OnBus<OrderPlacedConsumer>(consumer => consumer.Group("orders").Concurrency(4))
    );
});

using var app = builder.Build();

await app.Services.GetRequiredService<IBootstrapper>()
    .BootstrapAsync(CancellationToken.None);

await app.Services.GetRequiredService<IOutboxBus>()
    .PublishAsync(
        new OrderPlaced("order-123"),
        new PublishOptions { MessageName = "orders.placed" },
        CancellationToken.None
    );

public sealed record OrderPlaced(string OrderId);

public sealed class OrderPlacedConsumer(ILogger<OrderPlacedConsumer> logger) : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing order {OrderId}", context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}
```

Publish through the outbox when durability matters:

```csharp
await serviceProvider.GetRequiredService<IOutboxBus>()
    .PublishAsync(
        new OrderPlaced("order-123"),
        new PublishOptions { MessageName = "orders.placed" },
        cancellationToken
    );
```

For durable infrastructure, replace the in-memory providers with exactly one storage provider and one transport provider:

```csharp
using Polly;
using Polly.Retry;

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("Messaging");
    });

    setup.UseRabbitMq(options =>
    {
        options.HostName = builder.Configuration["RabbitMq:HostName"]!;
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });

    setup.ForMessage<OrderPlaced>(message =>
        message.MessageName("orders.placed").OnBus<OrderPlacedConsumer>()
    );
});
```

## Transactional Outbox (Atomic Publish)

The transactional outbox is **on by default on the EF storage path**. When the host selects EF-context storage with `setup.UseEntityFramework<TContext>()`, a `PublishAsync(...)` inside a coordinated transaction writes its outbox row in the same DB transaction and is discarded on rollback — zero consumer wiring. The EF storage setup auto-registers commit coordination and a DI-registered `IDbContextOptionsConfiguration<TContext>` that attaches the commit-coordination interceptor to the consumer's `DbContext`, even a plain `AddDbContext<TContext>` with no `AddInterceptors(...)`.

- Opt out with `setup.UseEntityFramework<TContext>(o => o.EnableTransactionalOutbox = false)` to restore non-transactional immediate dispatch — the opt-out travels with the EF storage choice, not a separate global call.
- A startup self-probe (`CommitInterceptorStartupGate<TContext>`) commits an empty transaction and asserts the interceptor fired. On a mis-wire it logs a loud warning by default; set `CommitInterceptorProbeMode.Strict` via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitInterceptorProbeMode.Strict)` to fail startup instead.
- On by default applies **only** to the EF-context path. The raw-ADO storage paths (`setup.UsePostgreSql(connString)` / `setup.UseSqlServer(connString)`, no `DbContext`) are unchanged and stay explicit opt-in: register `AddPostgreSqlCommitCoordination()`/`AddSqlServerCommitCoordination()` and use the `EnlistCommitCoordination` / `ExecuteCoordinatedTransactionAsync` helpers (shown in Quick Start below). There is no `DbContext` to attach an interceptor to on those paths.

The write is atomic with the business data; delivery is still at-least-once, so consumers must be idempotent (see [Retry Policy](#retry-policy)). See `Headless.CommitCoordination.EntityFramework` for the interceptor attachment and probe details.

## Defaults And Telemetry

- `AddHeadlessMessaging(...)` is the primary DI entry point.
- `setup.ForMessage<TMessage>(...)` is the primary registration API. `MessageName(...)` sets the publish and consume name for that message type; `OnBus<TConsumer>()` registers broadcast delivery and `OnQueue<TConsumer>()` registers point-to-point delivery.
- `setup.ForMessage<TMessage>(message => message.MessageName("orders.placed"))` is valid without consumers and declares a publisher-only message-name mapping.
- `IServiceCollection.ForMessage<TMessage>(...)` is the library/package registration seam. It can run before or after `AddHeadlessMessaging(...)` during service configuration: both entry points share one `ConsumerRegistry` (found-or-created), so a `MessageName(...)` mapping is registered eagerly and is authoritative regardless of call order — a publish that races ahead of startup (for example from an `IHostedService` in `StartAsync`) still resolves the explicit name instead of falling back to the convention name. Consumer metadata (which needs `MessagingOptions`) still drains into the registry before topology reads at startup.
- `CorrelationFrom(...)` derives `headless-corr-id` from the outgoing payload when `PublishOptions.CorrelationId` is not set. Correlation precedence is explicit publish option, message selector, ambient consume context, then framework message ID.
- Outbound header validation is centralized in the publish factory: reserved framework headers stay typed-only, provider contributions cannot overwrite framework-owned keys, and all stamped header names/values reject control characters before they reach a broker client.
- Explicit `PublishOptions.MessageName` uses the same message-name validator as configured mappings: no leading/trailing dots, no consecutive dots, and only alphanumeric, `.`, `-`, and `_`.
- Provider packages can add message-level escape hatches such as Kafka partition keys, Azure Service Bus partition keys, AWS FIFO message group IDs, and NATS subject shards. These physical-routing selectors run in the typed publish factory and are stamped as provider-owned headers; they do not change the logical `MessageName`.
- Example:

```csharp
setup.ForMessage<OrderPlaced>(message =>
    message.MessageName("orders.placed").CorrelationFrom(order => order.OrderId.ToString())
);
```

- `setup.ForMessagesFromAssembly(...)` and `setup.ForMessagesFromAssemblyContaining<TMarker>()` preserve assembly scanning for closed `IConsume<TMessage>` implementations and register untouched scanned consumers as bus consumers from the `AddHeadlessMessaging(...)` callback. Use the callback overloads to call `OnQueue()`, `Group(...)`, `Concurrency(...)`, `HandlerId(...)`, `WithCircuitBreaker(...)`, or `Skip()` per discovered consumer; message-name overrides stay on explicit `ForMessage<TMessage>(...)` registrations.
- message-name mappings are type-level and registered eagerly. Re-registering the same message type with the same name (case-insensitive) merges consumers; mapping the same message type to two different names fails immediately at registration. Mapping two different message types to the same resolved message name fails at startup.
- message-name and group defaults are deterministic; duplicate registrations fail fast by default.
- persisted published and received rows store `IntentType`; retry pickup and dashboard projections preserve that value. Received-message identity is `(Version, MessageId, Group, IntentType)`, so bus and queue deliveries with the same logical message ID do not collapse into one row.
- a persisted row whose `IntentType` has no registered transport is marked terminal `Failed` with no next retry; the drainer logs the unsupported intent and continues processing later rows.
- direct publish, outbox publish, and runtime delegates preserve the existing diagnostic listener and metric names used by dashboards.
- runtime delegates execute through the same scoped consume pipeline as class handlers, so diagnostics, middleware, and correlation behavior stay aligned.
- callbacks are fire-and-forget async chaining, not request/reply. Set `PublishOptions.CallbackName` on the request and call `context.SetResponse<TResponse>(value)` inside the consumer to publish a typed response body through the durable bus path. No `SetResponse` keeps the callback headers-only; `SetResponse` without `CallbackName` is dropped. Callback delivery is at-least-once — a crash, or a transient failure of the success-mark write after the response outbox row is written, redelivers the request and republishes the response, so make response consumers idempotent (dedupe on `(CorrelationId, CorrelationSequence)`; `CorrelationId` alone is ambiguous across hops because it is set to the immediate parent message id per hop, not the chain root).
- `IConsumerLifecycle` hooks run per delivery on the scoped consumer instance, not once for application startup or shutdown.
- `IBootstrapper.IsStarted` becomes `true` only after required messaging processors finish startup successfully.
- Concurrent `BootstrapAsync(...)` callers share the same in-flight startup work; canceling a later caller's wait does not abort the shared bootstrap.

## Bootstrap Lifecycle

- `BootstrapAsync(...)` is the readiness boundary for manual startup paths such as tests or custom hosts.
- Wait for `BootstrapAsync(...)` to complete before publishing when you bootstrap manually.
- `BootstrapAsync(...)` completes only after initial transport consumers report broker-side readiness, not merely after their background tasks are queued.
- Startup fails when a required messaging processor cannot start; partial logged startup is not treated as success.
- Startup fails when zero or multiple storage providers are configured.
- Runtime delegate subscriptions attached before the consumer register is ready are picked up by the initial startup path.
- Runtime delegate subscriptions attached after the consumer register is ready trigger a consumer refresh so they are not missed during late startup.

## Publisher Options

Publisher services are registered only when their matching transport capability exists: `IBus` / `IOutboxBus` require `IBusTransport`, and `IQueue` / `IOutboxQueue` require `IQueueTransport`. If a custom registration exposes a publisher without the matching transport, messaging bootstrap fails before the host starts.

### Bus Publishers

Use bus publishers for broadcast publish/subscribe delivery:

- `IBus` sends directly to an `IBusTransport`.
- `IOutboxBus` stores first, then dispatches through an `IBusTransport`.
- `PublishOptions.Delay` is honored by `IOutboxBus` and ignored by `IBus`.
- Stored rows and consume contexts carry `IntentType.Bus`.

### Queue Publishers

Use queue publishers for point-to-point competing-worker delivery:

- `IQueue` sends directly to an `IQueueTransport`.
- `IOutboxQueue` stores first, then dispatches through an `IQueueTransport`.
- `EnqueueOptions.Delay` is honored by `IOutboxQueue` and ignored by `IQueue`.
- Stored rows and consume contexts carry `IntentType.Queue`.

### Publisher Contracts

Use intent-specific contracts so delivery semantics are explicit at the call site:

```csharp
public sealed class MetricsPublisher(IBus bus)
{
    public async Task PublishMetric(MetricEvent metric, CancellationToken ct)
    {
        await bus.PublishAsync(metric, new PublishOptions { MessageName = "metrics.events" }, ct);
    }
}
```

Durable publishes use `IOutboxBus` or `IOutboxQueue`. Delayed delivery is expressed with `PublishOptions.Delay` or `EnqueueOptions.Delay`.

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
            new RuntimeSubscriptionOptions { HandlerId = "ProjectionSubscriptions.OrderPlaced" },
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

- `Version` is validated non-empty and at most 20 characters: the SQL storage providers persist it as a literal into a `VARCHAR(20)`/`nvarchar(20)` column, so an over-long value is rejected at startup instead of failing every outbox insert.
- `RetryBatchSize` (default 200, `> 0`) caps the retry-pickup batch; `SchedulerBatchSize` (default 1,000, `> 0`) caps the delayed/queued scheduler batch.

## Middleware

Both sides of the pipeline support cross-cutting middleware via `IConsumeMiddleware<TContext>` and `IPublishMiddleware<TContext>`. Middleware composes russian-doll style around the handler or publisher: code before `await next()` runs before the inner ring, and code after it runs after successful inner work.

```csharp
public sealed class LoggingConsumeMiddleware(ILogger<LoggingConsumeMiddleware> logger)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        logger.LogInformation("Consuming {MessageId}", context.MessageId);
        await next();
    }
}

public sealed class CorrelationPublishMiddleware : IPublishMiddleware<PublishingContext<OrderPlaced>>
{
    public ValueTask InvokeAsync(PublishingContext<OrderPlaced> context, Func<ValueTask> next)
    {
        context.Options = (context.Options ?? new PublishOptions()) with
        {
            CorrelationId = context.Options?.CorrelationId ?? Guid.NewGuid().ToString(),
        };
        return next();
    }
}

builder.Services.AddHeadlessMessaging(setup => { /* ... */ })
    .AddBusConsumeMiddleware<LoggingConsumeMiddleware>()
    .AddPublishMiddlewareFor<CorrelationPublishMiddleware, OrderPlaced>();
```

Registration scopes:

- `AddBusPublishMiddleware<T>()` / `AddBusConsumeMiddleware<T>()`: object-typed middleware for every publish or consume.
- `AddPublishMiddlewareFor<TMiddleware, TMessage>()`: typed publish middleware for one message type.
- `AddConsumeMiddlewareFor<TMiddleware, TMessage>(group)`: typed consume middleware for one message type and consumer group.
- `.WithPriority(int)`: lower values run first; ties use registration order. Framework tenant propagation middleware uses priority `-1000`, so user middleware defaults (`0`) run after tenant restoration/stamping.

Middleware can short-circuit by returning without calling `next`. Use ordinary `try/catch` around `await next()` for compensation and error policy. The framework still guards two runtime invariants: post-success middleware failures are logged and suppressed only after the inner ring completed, and cancellation matching `context.CancellationToken` is never silently swallowed. `PublishingContext<T>.Options` and `DelayTime` are mutable before `await next()` and throw after `next()` returns; reads, including `IsTransactional`, remain valid.

### Multi-Tenancy Propagation

When a host uses the root tenancy surface, configure messaging tenant posture there:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);
```

`PropagateTenant()` registers built-in middleware that ties the wire envelope to `ICurrentTenant`. `RequireTenantOnPublish()` flips strict publish tenancy so every publish must resolve a tenant from `PublishOptions.TenantId` or the ambient tenant.

`AddHeadlessTenancy(...).Messaging(m => m.PropagateTenant())` is the single composition point for tenant propagation. The previous `MessagingBuilder.AddTenantPropagation()` extension has been removed in favor of the root tenancy seam.

- **Publish:** stamps `PublishOptions.TenantId` from the ambient `ICurrentTenant.Id` when the caller has not set it explicitly. Caller overrides win.
- **Consume:** restores `ICurrentTenant.Change(...)` from the resolved `ConsumeContext<T>.TenantId` for the lifetime of the consume, including the exception path. Whitespace, empty, and oversized header values map to "no tenant".

Tenant propagation requires a real `ICurrentTenant`. `AddHeadlessMessaging()` registers only the safe `NullCurrentTenant` fallback; `Headless.Api` and `Headless.Orm.EntityFramework` setup replace that fallback while preserving consumer-provided tenant implementations.

The consume middleware trusts the inbound envelope. Message names exposed to external producers must layer envelope validation upstream.

### Diagnostics / PII

The `HEADLESS_TENANCY_*` event family is non-PII by design with one exception: `TenantContextSwitched` (EventId 64, Debug level) surfaces the raw tenant identifier as it restores `ICurrentTenant` on the consume side. Operators should gate Debug-level messaging logs accordingly when tenant identifiers contain PII (for example customer email or organization slugs).

## Message Ordering Guarantees

Message ordering guarantees depend on the transport provider and configuration:

### Transport-Specific Ordering

- **Kafka**: Messages with same partition key are strictly ordered within partitions. With concurrent consumers, Headless commits offsets only through the contiguous completed watermark for each partition, so a fast high offset does not acknowledge lower in-flight messages.
- **Azure Service Bus**: FIFO ordering when sessions are enabled (`EnableSessions = true`)
- **RabbitMQ**: No ordering guarantees by default; consumers may process messages concurrently
- **AWS SQS**: FIFO queues provide strict ordering; standard queues do not
- **Redis Streams**: Ordered within consumer group, but parallel consumers may process out of order
- **NATS**: Ordering preserved per subject, but concurrent consumers introduce variability
- **Pulsar**: Ordered within partitions when using partition key
- **InMemory**: FIFO ordering with single consumer thread

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
    setup.Options.RetryPolicy.MaxPersistedRetries = 15;
    setup.Options.RetryPolicy.DispatchTimeout = TimeSpan.FromMinutes(5);
    setup.Options.TransportPublishTimeout = TimeSpan.FromSeconds(10);
    setup.Options.CommandTimeout = TimeSpan.FromSeconds(30);
    setup.Options.OutboxFlushTimeout = TimeSpan.FromSeconds(30);
    setup.Options.RetryPolicy.RetryStrategy = new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        MaxDelay = TimeSpan.FromMinutes(5),
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is TimeoutException or HttpRequestException
        ),
    };
    setup.Options.RetryPolicy.OnExhausted = (info, ct) =>
    {
        // Fires only after the retryable budget is fully consumed and stored terminally.
        var logger = info.ServiceProvider.GetRequiredService<ILogger<MyService>>();
        logger.LogError(info.Exception, "Message {Id} permanently failed", info.Message.StorageId);
        return Task.CompletedTask;
    };
});
```

`MediumMessage.StorageId` and `FailedInfo.StorageId` are `Guid` row identifiers. Storage providers generate them through provider-keyed `IGuidGenerator` strategies, persist them as provider-native GUID columns (`UUID` on PostgreSQL through `Version7`, `uniqueidentifier` on SQL Server through the `SqlServer` comb), and dashboard message routes use GUID route values.

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `RetryStrategy` | `Polly.Retry.RetryStrategyOptions` | Exponential, jittered, 2 retries | Public Polly configuration for inline retry classification, delay, and observation. Configure `ShouldHandle` explicitly. |
| `MaxPersistedRetries` | `int` | `15` | Maximum persisted-retry pickups. `>= 0`. Total attempts = `(RetryStrategy.MaxRetryAttempts + 1) × (MaxPersistedRetries + 1)`. |
| `InitialDispatchGrace` | `TimeSpan` | `30s` | Initial `NextRetryAt` delay before crash-recovery pickup can see a newly stored row. |
| `DispatchTimeout` | `TimeSpan` | `5m` | Active delivery lease written to `LockedUntil` before each publish/consume attempt. `> 0`, `<= 1h`. Handlers exceeding this remain at-least-once and may be re-dispatched. |
| `OnExhausted` | `Func<FailedInfo, CancellationToken, Task>?` | `null` | Fires only after a retryable failure consumes the complete budget and the owned terminal write succeeds. |
| `OnExhaustedTimeout` | `TimeSpan` | `30s` | Bounds the exhausted callback wait. |

Top-level messaging timeouts that influence retry behavior:

| `MessagingOptions` property | Type | Default | Notes |
| --- | --- | --- | --- |
| `TransportPublishTimeout` | `TimeSpan` | `10s` | Linked with host shutdown and passed to transport publish calls. If the broker client honors cancellation, stuck publishes fail into the retry policy instead of outliving shutdown. |
| `CommandTimeout` | `TimeSpan` | `30s` | Applied to SQL-backed storage commands, including terminal writes that deliberately use `CancellationToken.None`. |
| `OutboxFlushTimeout` | `TimeSpan` | `30s` | Bounds the post-commit drain that flushes buffered outbox messages to the transport. The drain runs with `CancellationToken.None`, so an unresponsive broker would otherwise hold the request thread, DI scope, and DB connection indefinitely. Undispatched messages stay durable and are recovered by the relay sweep. `> 0`, `<= 5m`. |
| `DeadNodeReconcileInterval` | `TimeSpan` | `1m` | Cadence of the always-on dead-owner recovery reconcile backstop. `> 0` (no upper bound — the per-row `LockedUntil` floor owns correctness). Independent of `UseStorageLock`. |

Persisted retries use two independent timestamps: `NextRetryAt` controls when a row is due, and `LockedUntil` controls whether an active attempt still owns the row. Retry pickup filters on both. Retry state writes clear `LockedUntil`; counter advances use an optimistic `Retries == originalRetries` predicate so concurrent replicas cannot overwrite each other's retry budget.

**Delivery semantics are at-least-once; consumers must be idempotent.** The framework never promises exactly-once: the commit-edge drain and the relay sweep can both deliver the same message in a narrow window (the `LockedUntil` lease plus the Succeeded/Failed terminal-row guard minimize but do not eliminate duplicates), and a crash between broker accept and the success-mark write redelivers on recovery. Dedupe in consumers by business key or message id.

When a Coordination provider is registered, storage rows also stamp nullable `Owner` as `node@incarnation` when `LockedUntil` is written. An always-on `DeadOwnerRecoveryBridge` then reclaims rows owned by `Dead` incarnations — on a `NodeLeft` event and a periodic `DeadNodeReconcileInterval` reconcile — by moving `LockedUntil` back to now. Reclaim is dead-only (a `Suspected` owner is never reclaimed) and runs independently of `UseStorageLock`. `LockedUntil` remains the correctness floor; the bridge only reduces orphan recovery latency. Without Coordination, `Owner` stays `null` and the bridge is a no-op. See [Coordination Recovery](#coordination-recovery).

### Exhausted vs Stop

`OnExhausted` fires only after the retry budget was fully consumed by a failure matched by Polly's configured `ShouldHandle`.

When tenant propagation is configured, `OnExhausted` runs under the message envelope tenant for publish, consume, and poisoned-on-arrival paths. Poisoned broker redelivery skips the callback when storage reports the terminal row was not mutated.

It does not fire for:

- **Permanent exceptions** rejected by `RetryStrategy.ShouldHandle`.
- **Cancellation** (`OperationCanceledException` whose token matches the dispatch cancellation token).

For a single exit point covering both Stop and Exhausted, use consume/publish middleware.

### Inline vs Persisted Retry Paths

`RetryStrategy.MaxRetryAttempts` excludes the original execution and controls inline retries in the reusable Polly pipeline. Once that budget is exhausted, Messaging persists `NextRetryAt` and the retry processor performs up to `MaxPersistedRetries` pickups. Messaging reserves each attempt durably in `InlineAttempts` before invoking user or transport code, so a crash cannot reset the current burst.

Worked example with `RetryStrategy.MaxRetryAttempts = 2, MaxPersistedRetries = 2` — total (2+1)×(2+1) = 9 attempts:

```
pickup 1 (initial dispatch):
  attempt 1 (original)        ── inline
  attempt 2 (inline retry #1) ── inline, after Polly delay
  attempt 3 (inline retry #2) ── inline, after Polly delay → persist (1/2)
pickup 2 (persisted retry #1):
  attempt 4                   ── inline
  attempt 5 (inline retry #1) ── inline, after Polly delay
  attempt 6 (inline retry #2) ── inline, after Polly delay → persist (2/2)
pickup 3 (persisted retry #2):
  attempt 7                   ── inline
  attempt 8 (inline retry #1) ── inline, after Polly delay
  attempt 9 (inline retry #2) ── final; on failure → Exhausted → OnExhausted fires
```

For the full property table and `FailedInfo` construction details, see [docs/llms/messaging.md](../../../../docs/llms/messaging.md).

## Circuit Breaker

Per-consumer-group circuit breaker that pauses transport consumption when a dependency is unhealthy, preventing message-retry storms.

**State machine:** Closed → Open (pause transport) → HalfOpen (probe) → Closed (resume) or Open (re-trip). Open duration escalates exponentially on repeated trips and resets after consecutive successful close cycles.

### Global Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // Circuit breaker (applies to all consumer groups)
    setup.Options.CircuitBreaker.FailureThreshold = 5; // consecutive transient failures to trip
    setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30); // initial open duration
    setup.Options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromSeconds(240); // cap after escalation

    // Adaptive retry backpressure
    setup.Options.RetryProcessor.AdaptivePolling = true;
    setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromSeconds(60); // replaces the old FailedRetryInterval; default 60s
    setup.Options.RetryProcessor.MaxPollingInterval = TimeSpan.FromMinutes(15); // 15 min cap
    setup.Options.RetryProcessor.CircuitOpenRateThreshold = 0.8; // back off above 80% circuit-open rate
});
```

### Per-Consumer Override

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessage<PaymentProcessed>(message =>
        message
            .MessageName("payments.process")
            .OnBus<PaymentHandler>(consumer =>
                consumer.WithCircuitBreaker(cb =>
                {
                    cb.FailureThreshold = 3; // more sensitive
                    cb.OpenDuration = TimeSpan.FromSeconds(60); // longer cooldown
                })
            )
    );

    // Disable circuit breaker for a best-effort consumer
    setup.ForMessage<MetricsUpdated>(message =>
        message.OnBus<MetricsHandler>(consumer => consumer.WithCircuitBreaker(cb => cb.Enabled = false))
    );
});
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
var isOpen = monitor.IsOpen(IntentType.Bus, "payments");
var state = monitor.GetState(IntentType.Bus, "payments"); // Closed, Open, or HalfOpen

// Manual recovery (operator/agent action)
var wasReset = await monitor.ResetAsync(IntentType.Bus, "payments"); // true if reset performed
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

## Distributed Lock Integration

`MessagingOptions.UseStorageLock` (default `false`) enables `IDistributedLock`-backed mutual exclusion in `MessageNeedToRetryProcessor`. When `true`, the retry processor acquires a named lock before each retry pickup cycle, reducing duplicate retry-pickup work across replicas. Delivery remains at-least-once; consumers must still be idempotent.

Use `MessagingBuilder.UseDistributedLock(...)` to wire the provider — calling this implicitly sets `UseStorageLock = true`:

```csharp
// Instance overload
var lockProvider = new MyDistributedLock(...);
builder.Services.AddHeadlessMessaging(setup => { ... })
    .UseDistributedLock(lockProvider);

// Factory overload (provider depends on other DI services)
builder.Services.AddHeadlessMessaging(setup => { ... })
    .UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>());
```

Messaging registers its lock provider under an **internal keyed-DI key** so it never conflicts with any `IDistributedLock` registered at the application level for other purposes.

Without a real provider, only `NoOpDistributedLock` is active (the keyed-DI fallback). The bootstrapper logs **EventId 77 Warning** on startup when `UseStorageLock = true` but only the no-op provider is found under the messaging key. If a real provider is registered un-keyed at the app level but not flowed through `MessagingBuilder.UseDistributedLock(...)`, the bootstrapper instead emits **EventId 78 Warning** to disambiguate the misconfiguration.

If only the default `NullNodeMembership` is registered, the bootstrapper logs **EventId 88 Information** (independent of `UseStorageLock`). Retry recovery still works through `LockedUntil`; registering a Coordination provider enables faster dead-incarnation owner reclaim via the always-on recovery bridge.

> **NoOp introspection contract:** when `NoOpDistributedLock` is active, the introspection-style methods (`IsLockedAsync`, `GetLockInfoAsync`, `ListActiveLocksAsync`, `GetActiveLocksCountAsync`) always return empty/false/null and cannot be used to verify lock state. The EventId 77 / 78 warnings are the only operational signal that the no-op is in play — treat introspection results as "unknown", not "no locks held".

Retry locks use non-blocking acquire (`AcquireTimeout = TimeSpan.Zero`), a finite lease window equal to the current polling interval, and `LockMonitoringMode.AutoExtend`. If a lease's `LostToken` fires, the processor logs EventId 79 and does not start new pickup under an already-lost lease; in-flight dispatch remains guarded by per-row `LockedUntil`.

When `UseStorageLock = false` (default), `IDistributedLock` is never called; skip this for single-replica deployments or when the storage provider natively prevents duplicate retry pickup. This does not change the at-least-once delivery contract. Dead-owner recovery is unaffected — it runs independently of this lock (see [Coordination Recovery](#coordination-recovery)).

### Coordination Recovery

Dead-incarnation recovery runs **always-on**, independent of `UseStorageLock`. Every messaging host registers a `DeadOwnerRecoveryBridge` (an `IHostedService` from `Headless.Coordination.Core`) that reclaims rows owned by `Dead` incarnations on two triggers: a `NodeLeft` watch event (low-latency) and a periodic `DeadNodeReconcileInterval` reconcile (default 1 minute) that is the authoritative backstop. A watch-loop failure degrades to reconcile, not to no recovery.

Recovery layers:

1. Per-row `LockedUntil` is always active and remains the correctness boundary — an expired lease is recovered by normal pickup with or without the bridge.
2. A real `INodeMembership` lets the always-on bridge reclaim rows still owned by dead `node@incarnation` values, ahead of lease expiry.
3. `UseStorageLock = true` is orthogonal — it only serializes retry *pickup* across replicas, it does not gate recovery.

Reclaim is **dead-only**: a `Suspected` owner (likely still alive, mid-dispatch) is never reclaimed, so transient stalls do not cause duplicate delivery. Reclaim is idempotent across the watch/reconcile paths and across concurrent hosts (the owner-scoped conditional `UPDATE` only pulls leases still in the future).

Operational invariant: set Coordination's dead threshold no lower than the largest retry `DispatchTimeout`. A node starved long enough to miss heartbeats is classified `Dead` even while still completing an in-flight dispatch; with `DeadThreshold >= DispatchTimeout` its lease has already expired by the time it is declared `Dead`, so reclaim is a no-op the `LockedUntil` floor already covered. Set it lower and reclaim can shorten a still-valid lease and re-dispatch a row the original owner is still handling.

| Configuration | Recovery behavior | Startup signal |
| --- | --- | --- |
| No Coordination membership (`NullNodeMembership`) | `LockedUntil` floor only; the bridge is a benign no-op. | EventId 88 Information |
| Real Coordination membership | Always-on dead-owner reclaim: `NodeLeft` watch + `DeadNodeReconcileInterval` reconcile, dead-only. | None on success; bridge EventIds 1–3 on failure |

## Dependencies

- `Headless.Messaging.Abstractions`
- `Headless.Coordination.Abstractions`
- `Headless.Coordination.Core` (hosts the shared `DeadOwnerRecoveryBridge`)
- `Headless.Extensions`
- `Headless.Checks`
- `Headless.MultiTenancy`
- `Polly.Core`
- Transport package (RabbitMQ, Kafka, etc.)
- Storage package (PostgreSql, SqlServer, etc.)

## Side Effects

- Starts background hosted services for message processing
- Starts the always-on `DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>` hosted service
- Creates database tables for outbox storage (via storage provider)
- Establishes transport connections (via transport provider)

## EventIds

Retry-processor distributed-lock EventIds (emitted when `UseStorageLock = true`):

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 77 | `UseStorageLockWithNoOpProvider` | Warning | No real provider registered under any key. | Wire `MessagingBuilder.UseDistributedLock(...)` or set `UseStorageLock = false`. |
| 78 | `UseStorageLockWithNoOpProviderButRealUnkeyed` | Warning | Real provider registered un-keyed but not flowed through `UseDistributedLock(...)`. | Re-register via `MessagingBuilder.UseDistributedLock(...)`. |
| 79 | `RetryLockLeaseLost` | Warning | The acquired published- or received-retry lease's `LostToken` was already canceled before pickup, or fired while pickup was in flight. | Investigate lock-store TTLs, clock skew, and auto-extension health if frequent; per-row `LockedUntil` remains the correctness boundary. |
| 81 | `PublishedRetryLockAcquireFailed` | Warning | Acquire threw on the published-retry path. | Investigate lock-store health if persistent. |
| 82 | `PublishedRetryLockAcquireFailureEscalated` | Error | Three consecutive published-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 83 | `ReceivedRetryLockAcquireFailed` | Warning | Acquire threw on the received-retry path. | Investigate lock-store health if persistent. |
| 84 | `ReceivedRetryLockAcquireFailureEscalated` | Error | Three consecutive received-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 88 | `MessagingRecoveryUsingLockedUntilFloorOnly` | Information | Only `NullNodeMembership` is registered, so dead-owner recovery falls back to the `LockedUntil` floor (independent of `UseStorageLock`). | Register a Coordination provider to enable dead-owner reclaim, or accept floor-only recovery. |
| 91 | `MessagingDeadOwnerRowsReclaimed` | Information | The dead-owner reclaimer recovered N orphaned rows (published or received) for a dead owner. | Informational — no action needed. |
| 94 | `MessagingDeadThresholdBelowDispatchTimeout` | Warning | A real Coordination membership is registered but `DeadThreshold` is below the retry `DispatchTimeout`, so a still-alive node crossing the dead threshold mid-dispatch is reclaimed and re-dispatched. | Set Coordination `DeadThreshold` >= the retry `DispatchTimeout`. |

The always-on `DeadOwnerRecoveryBridge` logs failures under its own category, `Headless.Coordination.DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>` (EventIds restart at 1):

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 1 | `MembershipWatchFailed` | Error | The `WatchAsync` event loop failed; recovery falls back to the periodic reconcile. | Investigate Coordination store health if frequent; the reconcile backstop still recovers. |
| 2 | `DeadNodeReconcileFailed` | Error | A reconcile tick failed. | Investigate Coordination/storage health if persistent; retries on the next `DeadNodeReconcileInterval`. |
| 3 | `DeadNodeReclaimFailed` | Error | A single dead owner's reclaim threw; the owner is removed from the dedup set and retried on the next reconcile. | Investigate storage health if persistent. |

See [Distributed Lock Integration](../../docs/llms/messaging.md#distributed-lock-integration) for the two-layer correctness model and when to enable / skip.
