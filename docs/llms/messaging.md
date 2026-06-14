---
domain: Messaging
packages: Messaging.Abstractions, Messaging.Bus.Abstractions, Messaging.Queue.Abstractions, Messaging.Core, Messaging.Dashboard, Messaging.Dashboard.K8s, Messaging.OpenTelemetry, Messaging.Aws, Messaging.AzureServiceBus, Messaging.InMemory, Messaging.InMemoryStorage, Messaging.Kafka, Messaging.Nats, Messaging.Pulsar, Messaging.RabbitMq, Messaging.Redis, Messaging.Storage.PostgreSql, Messaging.Storage.SqlServer, Messaging.Testing
---

# Messaging

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Provider](#choosing-a-provider)
- [Provider Capabilities](#provider-capabilities)
- [Headless.Messaging.Abstractions](#headlessmessagingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Messaging.Bus.Abstractions](#headlessmessagingbusabstractions)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Messaging.Queue.Abstractions](#headlessmessagingqueueabstractions)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Messaging.Core](#headlessmessagingcore)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Messaging.Dashboard](#headlessmessagingdashboard)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Messaging.Dashboard.K8s](#headlessmessagingdashboardk8s)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Messaging.OpenTelemetry](#headlessmessagingopentelemetry)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Messaging.Aws](#headlessmessagingaws)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)
- [Headless.Messaging.AzureServiceBus](#headlessmessagingazureservicebus)
    - [Problem Solved](#problem-solved-8)
    - [Key Features](#key-features-8)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-8)
    - [Quick Start](#quick-start-8)
    - [Configuration](#configuration-8)
    - [Dependencies](#dependencies-8)
    - [Side Effects](#side-effects-8)
- [Headless.Messaging.InMemory](#headlessmessaginginmemory)
    - [Problem Solved](#problem-solved-9)
    - [Key Features](#key-features-9)
    - [Installation](#installation-9)
    - [Quick Start](#quick-start-9)
    - [Configuration](#configuration-9)
    - [Dependencies](#dependencies-9)
    - [Side Effects](#side-effects-9)
- [Headless.Messaging.InMemoryStorage](#headlessmessaginginmemorystorage)
    - [Problem Solved](#problem-solved-10)
    - [Key Features](#key-features-10)
    - [Installation](#installation-10)
    - [Quick Start](#quick-start-10)
    - [Configuration](#configuration-10)
    - [Dependencies](#dependencies-10)
    - [Side Effects](#side-effects-10)
- [Headless.Messaging.Kafka](#headlessmessagingkafka)
    - [Problem Solved](#problem-solved-11)
    - [Key Features](#key-features-11)
    - [Design Notes](#design-notes-5)
    - [Installation](#installation-11)
    - [Quick Start](#quick-start-11)
    - [Configuration](#configuration-11)
    - [Dependencies](#dependencies-11)
    - [Side Effects](#side-effects-11)
- [Headless.Messaging.Nats](#headlessmessagingnats)
    - [Problem Solved](#problem-solved-12)
    - [Key Features](#key-features-12)
    - [Design Notes](#design-notes-6)
    - [Installation](#installation-12)
    - [Quick Start](#quick-start-12)
    - [Configuration](#configuration-12)
    - [Dependencies](#dependencies-12)
    - [Side Effects](#side-effects-12)
- [Headless.Messaging.Pulsar](#headlessmessagingpulsar)
    - [Problem Solved](#problem-solved-13)
    - [Key Features](#key-features-13)
    - [Installation](#installation-13)
    - [Quick Start](#quick-start-13)
    - [Configuration](#configuration-13)
    - [Dependencies](#dependencies-13)
    - [Side Effects](#side-effects-13)
- [Headless.Messaging.RabbitMq](#headlessmessagingrabbitmq)
    - [Problem Solved](#problem-solved-14)
    - [Key Features](#key-features-14)
    - [Design Notes](#design-notes-7)
    - [Installation](#installation-14)
    - [Quick Start](#quick-start-14)
    - [Configuration](#configuration-14)
    - [Dependencies](#dependencies-14)
    - [Side Effects](#side-effects-14)
- [Headless.Messaging.Redis](#headlessmessagingredis)
    - [Problem Solved](#problem-solved-15)
    - [Key Features](#key-features-15)
    - [Installation](#installation-15)
    - [Quick Start](#quick-start-15)
    - [Configuration](#configuration-15)
    - [Dependencies](#dependencies-15)
    - [Side Effects](#side-effects-15)
- [Headless.Messaging.Storage.PostgreSql](#headlessmessagingstoragepostgresql)
    - [Problem Solved](#problem-solved-16)
    - [Key Features](#key-features-16)
    - [Installation](#installation-16)
    - [Quick Start](#quick-start-16)
    - [Configuration](#configuration-16)
    - [Dependencies](#dependencies-16)
    - [Side Effects](#side-effects-16)
- [Headless.Messaging.Storage.SqlServer](#headlessmessagingstoragesqlserver)
    - [Problem Solved](#problem-solved-17)
    - [Key Features](#key-features-17)
    - [Installation](#installation-17)
    - [Quick Start](#quick-start-17)
    - [Configuration](#configuration-17)
    - [Dependencies](#dependencies-17)
    - [Side Effects](#side-effects-17)
- [Headless.Messaging.Testing](#headlessmessagingtesting)
    - [Problem Solved](#problem-solved-18)
    - [Key Features](#key-features-18)
    - [Design Notes](#design-notes-8)
    - [Installation](#installation-18)
    - [Quick Start](#quick-start-18)
    - [Configuration](#configuration-18)
    - [Dependencies](#dependencies-18)
    - [Side Effects](#side-effects-18)

> Messaging is the framework's durable publish/consume layer: typed bus and queue APIs, explicit message registration, storage-backed retry/outbox state, provider-native transports, dashboards, telemetry, and test harness support.

## Quick Orientation

Use `Headless.Messaging.Core` as the composition package, then add exactly one transport provider and one storage provider for a production host. Bus publishing uses `IBus`; point-to-point queue publishing uses `IQueue`. Consumers implement `IConsume<TMessage>` and are registered with `setup.ForMessage<TMessage>(...)` or assembly scanning.

The current registration surface is message-first:

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options => options.HostName = "localhost");
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);

    setup.ForMessage<OrderPlaced>(message => message
        .MessageName("orders.placed")
        .CorrelationFrom(order => order.OrderId.ToString())
        .OnBus<OrderProjection>(consumer => consumer
            .Group("orders-projection")
            .Concurrency(4)
            .UseRabbitMq(rabbit => rabbit.PrefetchCount(20))));
});
```

## Agent Instructions

- Register consumers with `setup.ForMessage<TMessage>(...)`; do not use removed `AddBusConsumer`, `AddQueueConsumer`, or topic-based naming.
- Use `MessageName(...)` for logical message names. Provider-native names such as Kafka topic, RabbitMQ routing key, NATS subject, and Azure Service Bus topic remain provider details.
- Use `CorrelationFrom(...)` for payload-derived correlation. Precedence is explicit publish option, message selector, ambient consume context, then generated message id. Some brokers cap correlation-id length (e.g. RabbitMQ and NATS at ~255 characters); the framework does not truncate or validate — caller's responsibility.
- Never write framework metadata through provider hatches. For publish options, use typed properties; raw `Headers.TenantId` is accepted only by the legacy tenant-integrity path and should not be authored directly.
- Treat provider hatches as physical broker routing/configuration. Producer-side hatches live on `IMessageBuilder<TMessage>`; consumer-side hatches live on `IBusConsumerBuilder<TConsumer>` or `IQueueConsumerBuilder<TConsumer>` only when that provider currently exposes consumer settings.
- Kafka, RabbitMQ, and NATS currently expose consumer-side hatches. AWS and Azure Service Bus currently expose producer-side hatches only.
- On the EF storage path (`UseEntityFramework<TContext>()`) the atomic outbox is on by default; do not hand-wire commit coordination for it. To turn it off, call `setup.WithoutTransactionalOutbox()` — do not strip the interceptor by hand. On the raw-ADO paths (`UsePostgreSql`/`UseSqlServer` by connection string) it stays explicit opt-in; wire it with `AddPostgreSqlCommitCoordination()`/`AddSqlServerCommitCoordination()` plus the coordinated-transaction helpers.
- If the outbox is enabled but the commit interceptor is not firing (a mis-wire), the startup gate logs a warning by default; set `CommitInterceptorProbeMode.Strict` (via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitInterceptorProbeMode.Strict)`) to fail startup instead of shipping a silently non-transactional outbox.
- Keep `docs/llms/messaging.md` and package READMEs aligned when public messaging behavior changes.

## Core Concepts

- **Transactional outbox (atomic publish) — on by default on the EF storage path**: when the host chooses the EF-context storage path (`setup.UseEntityFramework<TContext>()`), the atomic outbox is ON BY DEFAULT with zero consumer wiring. A `producer.PublishAsync(...)` issued inside a coordinated transaction writes its outbox row in the SAME DB transaction and is discarded on rollback, so the message is durable if and only if the business data committed. The EF storage setup auto-registers commit coordination and a DI-registered `IDbContextOptionsConfiguration<TContext>` that attaches the commit-coordination interceptor to the consumer's `DbContext` — including a plain `AddDbContext<TContext>` with no `AddInterceptors(...)`. Opt out with `setup.WithoutTransactionalOutbox()` to restore non-transactional immediate dispatch. A startup self-probe (`CommitInterceptorStartupGate<TContext>`) commits an empty transaction and asserts the interceptor fired; on a mis-wire it logs a loud warning (default) or fails startup (`CommitInterceptorProbeMode.Strict`). This applies **only** to the EF-context path: the raw-ADO storage paths (`UsePostgreSql(connString)` / `UseSqlServer(connString)`, no `DbContext`) are unchanged and stay explicit opt-in — there is no `DbContext` to attach an interceptor to, so they register `AddPostgreSqlCommitCoordination()` / `AddSqlServerCommitCoordination()` and use the `EnlistCommitCoordination` / `ExecuteCoordinatedTransactionAsync` helpers. See [commit-coordination.md](commit-coordination.md) for the interceptor attachment and probe modes. This is an atomicity guarantee for the *write*, not exactly-once delivery — dispatch is still at-least-once (next bullet).
- **Delivery semantics — at-least-once, consumer idempotency required**: the framework never promises exactly-once. The commit-edge drain and the relay sweep can both deliver the same message in a narrow window (the `LockedUntil` lease and the Succeeded/Failed terminal-row guard minimize but do not eliminate duplicates), and a crash between broker accept and the success-mark write redelivers. Consumers must be idempotent — dedupe by business key or message id.
- **Intent**: Bus is broadcast/pub-sub. Queue is point-to-point. Received-message identity includes intent so bus and queue deliveries do not collapse into one storage row.
- **Envelope**: All transport messages carry framework headers such as message id, correlation id, message name, type, sent time, intent, and optional tenant id.
- **Reserved headers**: `MessageId`, `CorrelationId`, `CorrelationSequence`, `CallbackName`, `MessageName`, `Type`, `SentTime`, `DelayTime`, and `Intent` are rejected in custom publish headers and provider contributions. `TenantId` is also framework-owned; provider contributions cannot write it, while raw publish headers are handled by the stricter tenant-integrity policy for compatibility.
- **Header validation**: custom header names, custom header values, and framework/provider-stamped header values all reject control characters before publish. This includes explicit `MessageId`, `CorrelationId`, `CallbackName`, and typed `TenantId`.
- **Explicit message names**: `PublishOptions.MessageName` follows the same validator as registered message mappings. Invalid dot shapes and invalid characters are rejected before publish.
- **Correlation**: `PublishOptions.CorrelationId` wins. If absent, `CorrelationFrom(...)` runs against the payload. If absent, publishes inside a consumer inherit ambient `ConsumeContext.CorrelationId`. If absent, the message id becomes the correlation id.
- **Tenant integrity**: use `MessageOptions.TenantId` or ambient tenancy. Do not write `Headers.TenantId` directly.
- **Provider config bag**: provider packages attach opaque config objects keyed by config type. Consumer config overlays message config for the same provider type. Repeated message metadata registrations are deterministic: later metadata for the same message/config type overrides earlier metadata.
- **Metadata resolution**: when a concrete payload resolves to interface/base message metadata, default publish `MessageName` and `Type` use the resolved metadata type. Explicit `PublishOptions.MessageName` and `MessageType` still win.
- **Provider header contributions**: producer-side provider hatches compute typed payload values before the payload is erased to bytes. Core validates contributed header names and values, then transports map those headers to native broker fields.
- **Azure Service Bus sessions**: when sessions are enabled and a publish config sets `PartitionKey` without a `SessionId`, the provider falls back to that partition key as the session id before it falls back to the framework message id.
- **NATS shard coverage**: wildcard subject coverage is added only for consumers that declare `.UseNats(c => c.Sharded())`; unsharded consumers no longer subscribe to broad `messageName.>` subjects. Shard symmetry is enforced at startup: if a message uses `SubjectShard(...)` on the producer side, every registered consumer for that message must also declare `.Sharded()`. NATS delivers zero messages with no error when a FilterSubject does not match any shard subject, so the invariant prevents silent data loss.
- **Ambient consume context**: `IConsumeContextAccessor` is AsyncLocal-backed and restored in a `finally` block after each consume pipeline execution.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| InMemory | Local development, unit tests, demos | Production durability or multi-process delivery | No external dependency, no durability |
| RabbitMQ | General-purpose broker, routing keys, queue semantics | Strict partitioned ordering across large streams | Mature routing model, topology must match custom routing keys |
| Kafka | Ordered partitions, consumer groups, high-throughput streams | Broadcast bus semantics through this package | Queue-intent provider; partition key has no framework length cap |
| Azure Service Bus | Azure-hosted topics/queues, sessions, managed operations | Non-Azure deployments | PartitionKey is limited to 128 chars and must match SessionId when sessions are enabled |
| AWS SNS/SQS | AWS-native pub-sub and queue workloads | Non-AWS deployments | FIFO entities use MessageGroupId and deduplication ids |
| NATS | Subject-based routing, lightweight broker, JetStream | Complex per-consumer storage-specific routing | Subject shards must be a single safe token |
| Pulsar | Pulsar-native transport | Projects not already on Pulsar | Provider exists without the Cluster 0.4 provider-hatch surface |
| Redis | Redis pub/sub transport | Durable broker requirements | Simple infrastructure, ephemeral delivery model |

## Provider Capabilities

| Provider | Bus | Queue | Producer hatch | Consumer hatch |
| --- | --- | --- | --- | --- |
| AWS | SNS | SQS | `MessageGroupId(...)` | None |
| Azure Service Bus | Topic | Queue | `PartitionKey(...)` | None |
| InMemory | Yes | Yes | None | None |
| Kafka | No | Yes | `PartitionBy(...)` | `IsolationLevel(...)` |
| NATS | Yes | Yes | `SubjectShard(...)` | `Sharded()` |
| Pulsar | Yes | Yes | None | None |
| RabbitMQ | Exchange | Queue | None | `PrefetchCount(...)` |
| Redis | Pub/Sub | Queue-like Redis transport | None | None |

### Storage Providers

| Provider          | Outbox + persisted retry storage | Schema initializer            |
|-------------------|----------------------------------|-------------------------------|
| `PostgreSql`      | yes (`IDataStorage`)             | yes (`IStorageInitializer`)   |
| `SqlServer`       | yes (`IDataStorage`)             | yes (`IStorageInitializer`)   |
| `InMemoryStorage` | yes (`IDataStorage`, in-memory)  | yes (`IStorageInitializer`)   |

How to read each column:

- **Outbox + persisted retry storage** — the framework's combined storage contract. There is no separate `IRetryStorage` or `ISubscriptionStorage` abstraction; outbox writes and persisted-retry pickups go through the same `IDataStorage` implementation. The brainstorm proposed a "Subscriptions" column; the live code does not expose a subscription-tracking storage seam, so the column was dropped during planning rather than padded with "n/a" values.
- **Schema initializer** — `IStorageInitializer` is the seam each storage uses to create or migrate its tables (PostgreSql/SqlServer) or initialize in-process state (InMemoryStorage). All three storages implement it.
- **Storage row IDs** — `MediumMessage.StorageId`, monitoring APIs, dashboard routes, and bulk storage actions use `Guid`. Storage providers generate row IDs through provider-keyed `IGuidGenerator` strategies, not database defaults. PostgreSQL creates `UUID` `Id` columns and resolves the `Version7` strategy; SQL Server creates `uniqueidentifier` `Id` columns, resolves the `SqlServer` comb strategy, and creates a `uniqueidentifier` table-valued ID-list type.

Internal-wiring asymmetries (for example, `Headless.Messaging.Storage.SqlServer` additionally registers `DiagnosticProcessorObserver` and a `DiagnosticRegister` background server for SQL Server-specific telemetry that PostgreSql does not need) are deliberately not surfaced as matrix columns — they are implementation details, not chooser-relevant capabilities.

## Headless.Messaging.Abstractions

### Problem Solved

Defines shared messaging contracts and envelope types used by all bus, queue, core, and provider packages.

### Key Features

- `IConsume<TMessage>` consumer contract.
- `MessageOptions` base options, including headers, correlation, delay, message id, message type, and tenant id.
- `MessageHeader`, `Headers`, `TransportMessage`, and broker address primitives.
- Common transport pause/resume and retry/backoff abstractions.

### Design Notes

Headers are not a free-form control plane. Framework-owned headers are reserved because transports, storage, tenancy, retry, and diagnostics depend on their integrity.

### Installation

```bash
dotnet add package Headless.Messaging.Abstractions
```

### Quick Start

```csharp
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

### Configuration

None.

### Dependencies

None.

### Side Effects

None.

## Headless.Messaging.Bus.Abstractions

### Problem Solved

Defines bus publishing contracts without requiring a concrete messaging host or provider package.

### Key Features

- `IBus.PublishAsync(...)`.
- `IOutboxBus` for durable/outbox-aware bus publishing.
- `PublishOptions` with value-equality and `with` support.

### Installation

```bash
dotnet add package Headless.Messaging.Bus.Abstractions
```

### Quick Start

```csharp
await bus.PublishAsync(new OrderPlaced(orderId), new PublishOptions { CorrelationId = correlationId });
```

### Configuration

None.

### Dependencies

`Headless.Messaging.Abstractions`.

### Side Effects

None.

## Headless.Messaging.Queue.Abstractions

### Problem Solved

Defines point-to-point queue publishing contracts independently from concrete providers.

### Key Features

- `IQueue.EnqueueAsync(...)`.
- `IOutboxQueue` for durable/outbox-aware queue publishing.
- Queue options align with `MessageOptions`.

### Installation

```bash
dotnet add package Headless.Messaging.Queue.Abstractions
```

### Quick Start

```csharp
await queue.EnqueueAsync(new RebuildProjection(commandId));
```

### Configuration

None.

### Dependencies

`Headless.Messaging.Abstractions`.

### Side Effects

None.

## Headless.Messaging.Core

### Problem Solved

Wires messaging into dependency injection: registration, publishing, dispatch, middleware, retry, storage, outbox, circuit breaker, runtime subscriptions, and monitoring.

### Key Features

- `services.AddHeadlessMessaging(setup => ...)`.
- `setup.ForMessage<TMessage>(...)` and assembly scanning.
- `MessageName(...)`, `CorrelationFrom(...)`, `OnBus<TConsumer>()`, `OnQueue<TConsumer>()`.
- Consumer settings: `Group(...)`, `Concurrency(...)`, `HandlerId(...)`, `WithCircuitBreaker(...)`.
- Publish and consume middleware.
- Strict publish tenancy via `RequireTenantOnPublish()`.
- Storage-backed retry/outbox and cleanup processors.
- Circuit breaker monitor/control APIs.

### Design Notes

Core owns logical metadata and provider-independent correctness. Provider packages own broker-specific values and limits. `CorrelationFrom(...)` is a universal logical knob; partition keys, routing keys, subject shards, and message group ids are provider hatches because their semantics differ.

### Installation

```bash
dotnet add package Headless.Messaging.Core
```

### Quick Start

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemoryTransport();
    setup.UseInMemoryStorage();

    setup.ForMessage<OrderPlaced>(message => message
        .MessageName("orders.placed")
        .CorrelationFrom(order => order.OrderId.ToString())
        .OnBus<OrderPlacedConsumer>(consumer => consumer.Group("orders")));
});
```

### Configuration

- `MessagingOptions.DefaultGroupName`, `GroupNamePrefix`, `MessageNamePrefix`, and `Version` control naming and isolation.
- Retry configuration lives under `RetryPolicy`, publish/receive retry processors, and storage cleanup options.
- `UseStorageLock` coordinates retry processors through a messaging-keyed distributed lock provider.
- Register middleware through `MessagingBuilder.AddBusPublishMiddleware<T>()`, `AddBusConsumeMiddleware<T>()`, `AddPublishMiddlewareFor<TMiddleware,TMessage>()`, and `AddConsumeMiddlewareFor<TMiddleware,TMessage>(groupName)`.
- Runtime subscriptions attach handlers after startup through `IRuntimeSubscriber`.

### Dependencies

`Headless.Messaging.Abstractions`, `Headless.Messaging.Bus.Abstractions`, `Headless.Messaging.Queue.Abstractions`, `Headless.Hosting`, `Headless.Abstractions`, `Headless.Checks`.

### Side Effects

Registers messaging services, hosted processors, publishers, consumers, storage abstractions, runtime registries, middleware registries, and keyed messaging lock defaults.

## Headless.Messaging.Dashboard

### Problem Solved

Provides dashboard services and endpoints for inspecting messaging health, storage state, nodes, and operations.

### Key Features

- Dashboard option builder.
- Node discovery abstraction and Consul discovery support.
- Authentication options for dashboard access.
- Monitoring API integration.

### Installation

Retries up to `MaxInlineRetries` run **inline** inside the same `ExecuteAsync` / `SendAsync` call (with `Task.Delay` between attempts). Once the inline budget is exhausted, the message is persisted with `NextRetryAt` set and picked up by `MessageNeedToRetryProcessor` (up to `MaxPersistedRetries` times). Each pickup then bursts another round of `MaxInlineRetries` inline attempts.

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

#### FailedInfo construction (for tests / fakes)

`FailedInfo` has six required-init properties — `Exception`, `StorageId`, and `RetryCount` are now part of the contract:

```csharp
var info = new FailedInfo
{
    ServiceProvider = scope.ServiceProvider, // live dispatch scope, NOT the root provider
    MessageType     = MessageType.Subscribe, // or MessageType.Publish
    Message         = message,
    Exception       = ex,                    // the exhausting exception
    StorageId       = mediumMessage.StorageId, // storage row identifier for DLQ correlation
    RetryCount      = mediumMessage.Retries,   // final persisted-retry count
};
```

`ServiceProvider` is the **live per-message DI scope** — the same scope used while the consume / publish attempts ran. Scoped services resolved through `FailedInfo.ServiceProvider` are the same instances seen by the consumer/handler.

#### RetryProcessorOptions

`MessagingOptions.RetryProcessor` controls the persisted-retry processor's polling cadence:

| Property | Default | Notes |
| --- | --- | --- |
| `BaseInterval` | `60s` | Base polling interval. Replaces the old `FailedRetryInterval`. |
| `AdaptivePolling` | `true` | When enabled, polling interval halves on healthy cycles and doubles when circuit-open skip rate exceeds threshold. |
| `MaxPollingInterval` | `15m` | Cap on adaptive doubling. |
| `CircuitOpenRateThreshold` | `0.8` | Above this fraction of circuit-open skips, the processor backs off. |

#### Migration from pre-RetryPolicy primitives

| Old property | New property | Notes |
| --- | --- | --- |
| `FailedRetryCount` | `RetryPolicy.MaxPersistedRetries` | Controls persisted-retry pickups. Total attempts = `(MaxInlineRetries + 1) × (MaxPersistedRetries + 1)`. Set both to `0` to disable retries. |
| `FailedRetryInterval` | `RetryProcessorOptions.BaseInterval` | Default `60s`. |
| `FallbackWindowLookbackSeconds` | *removed* | No replacement — `MessageNeedToRetryProcessor` now polls without a lookback window. |
| `RetryBackoffStrategy` | `RetryPolicy.BackoffStrategy` | Strategy contract is now one `Compute(int persistedRetryCount, int inlineRetryCount, Exception exception)` method returning `RetryDecision`. |
| `FailedThresholdCallback` | `RetryPolicy.OnExhausted` | **Semantic change:** the callback now fires only on `RetryDecision.Exhausted`, not on permanent exceptions or cancellation (`RetryDecision.Stop`). |

## Distributed Lock Integration

`MessagingOptions.UseStorageLock` (default `false`) enables `IDistributedLockProvider`-backed mutual exclusion in `MessageNeedToRetryProcessor`. When `true`, the retry processor acquires a named distributed lock before each publish-retry and receive-retry pickup, gating the entire retry-pickup tick.

Use `MessagingBuilder.UseDistributedLock(...)` to wire the provider. Calling this method implicitly sets `UseStorageLock = true`:

```csharp
// Instance overload — when you already have an IDistributedLockProvider
var lockProvider = new MyDistributedLockProvider(...);
builder.Services.AddHeadlessMessaging(setup => { ... })
    .UseDistributedLock(lockProvider);

// Factory overload — when the provider depends on other DI services
builder.Services.AddHeadlessMessaging(setup => { ... })
    .UseDistributedLock(sp => sp.GetRequiredService<IDistributedLockProvider>());
```

Messaging keeps its lock provider under an **internal keyed-DI key** (`"headless.messaging"`) so it never conflicts with any `IDistributedLockProvider` registered at the application level for other purposes.

### What this is and isn't (correctness vs coordination)

- Per-row `LockedUntil` (set to `DispatchTimeout` before each publish/consume attempt — see the [Retry Policy](#retry-policy) section) is the **correctness primitive**. It prevents the same row from being dispatched twice and works whether or not the distributed lock is enabled.
- The distributed lock is a **coarse-grained pickup mutex**, not a correctness requirement. It gates the entire retry-pickup tick so only one replica scans the backlog at a time.
- Disabling `UseStorageLock` does not introduce double-dispatch risk. It introduces wasted pickup work on contended backlogs. If renewal of an in-flight lock fails (EventId 79), the handle is cleared but the consume task keeps running — the per-row `LockedUntil` lease takes over as the correctness boundary.

### When to enable

- Multi-replica deployment where many retry pickups would otherwise contend. Each tick on each replica scans the same backlog table; the distributed lock makes only one replica do the scan per tick.
- Pickup queries that are expensive (large backlog, complex filter, secondary indexes scanned). Even at two replicas, halving the pickup load is observable.
- Operationally noisy retries without it: lots of "0 messages picked up" log lines from sibling replicas competing for the same backlog.

### When to skip

- Single replica. No contention exists; the lock provider is overhead.
- Storage provider that natively prevents duplicate pickup (e.g., row-level locking under a `SELECT ... FOR UPDATE` pattern). Per-row `LockedUntil` already covers correctness; the distributed lock would only deduplicate the SELECT itself.
- Tolerable duplicate pickup churn. Duplicate *pickup* attempts are not duplicate *delivery*; the per-row `LockedUntil` lease still prevents double-dispatch.

### Requirements

- Call `UseDistributedLock(...)` on the returned `MessagingBuilder` to supply a real provider (e.g. from `Headless.DistributedLocks.Core` + a cache/DB backend).
- Without a real provider, only `NoOpDistributedLockProvider` is active (the keyed-DI fallback). The bootstrapper emits two mutually-exclusive Warnings depending on what it finds: **EventId 77** when no real provider is wired under any registration, and **EventId 78** when a real provider is registered un-keyed (e.g., via `AddDistributedLocks()`) but not flowed through `MessagingBuilder.UseDistributedLock(...)`. Alert on either.
- `UseDistributedLock(...)` is **last-wins** — calling it twice replaces the prior registration rather than stacking duplicates.

**NoOp introspection contract:** when `NoOpDistributedLockProvider` is the resolved messaging-keyed provider, the introspection methods (`IsLockedAsync`, `GetLockInfoAsync`, `ListActiveLocksAsync`, `GetActiveLocksCountAsync`) silently return empty/false/null. They cannot be used to verify lock state in that mode; rely on the EventId 77 / 78 warning at startup as the operational signal.

### Lock names

- `messaging.publish-retry-{version}` — held while processing published-message retries.
- `messaging.receive-retry-{version}` — held while processing received-message retries.

Both names follow the literal pattern shown above. They are constructed internally by `Headless.Messaging.Core`; downstream consumers must not depend on the internal helper that builds them — register a real provider exclusively via `MessagingBuilder.UseDistributedLock(...)` and let messaging resolve its own keyed-DI slot.

`{version}` comes from `MessagingOptions.Version` and is the **cross-process isolation key**. Two services that share a single lock store (e.g., both pointed at the same Redis) MUST set distinct `Version` values — otherwise their retry processors collide on the same lock resource and starve each other. Both locks use `acquireTimeout: TimeSpan.Zero` (non-blocking try-once); when another replica holds the lock the processor skips that pickup cycle and waits for the next polling tick.

**When `UseStorageLock = false`** (default): `IDistributedLockProvider` is never called and distributed lock wiring is not required.

### EventIds

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 77 | `UseStorageLockWithNoOpProvider` | Warning | `UseStorageLock = true` but no real provider is registered under any key. | Wire a real provider via `MessagingBuilder.UseDistributedLock(...)`, or set `UseStorageLock = false`. |
| 78 | `UseStorageLockWithNoOpProviderButRealUnkeyed` | Warning | `UseStorageLock = true`, real provider registered un-keyed, but not flowed through `MessagingBuilder.UseDistributedLock(...)`. | Re-register the provider via `MessagingBuilder.UseDistributedLock(...)` so it lands under messaging's keyed slot. |
| 79 | `ReceivedRetryLockOwnershipLost` | Warning | `RenewAsync` returned `false`; the coarse lock was lost. Per-row `LockedUntil` takes over for the in-flight consume task. | Investigate lock-store TTLs and clock skew if frequent; not a correctness issue. |
| 80 | `ReceivedRetryLockRenewalFailed` | Warning | `RenewAsync` threw a non-cancellation exception. | Investigate lock-store health if frequent; the in-flight task continues. |
| 81 | `PublishedRetryLockAcquireFailed` | Warning | `TryAcquireAsync` threw on the published-retry path. | Investigate lock-store health if persistent; the pickup is skipped. |
| 82 | `PublishedRetryLockAcquireFailureEscalated` | Error | Three consecutive published-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 83 | `ReceivedRetryLockAcquireFailed` | Warning | `TryAcquireAsync` threw on the received-retry path. | Investigate lock-store health if persistent; the pickup is skipped. |
| 84 | `ReceivedRetryLockAcquireFailureEscalated` | Error | Three consecutive received-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |

### Pros and cons

- **Pros:** less wasted pickup work, cleaner logs at scale, halves backlog scan load per added replica.
- **Cons:** extra lock-store round trip per tick, extra dependency, more EventIds to monitor (79/80 for renewal, 81-84 for acquire failures).

---

## Strict Publish Tenancy

`MessagingOptions.TenantContextRequired` is the messaging sibling of the EF write guard (#234) and the HTTP authorization requirement. Defaults to `false` to preserve today's behavior. When set to `true`, every publish must resolve a tenant identifier:

1. `PublishOptions.TenantId` if set (the source of truth — see `Headers.TenantId` integrity rules in [Multi-Tenancy / Message Consumers](multi-tenancy.md#message-consumers)).
2. Otherwise, the ambient `ICurrentTenant.Id`.
3. If neither resolves, the publish wrapper throws `Headless.Abstractions.MissingTenantContextException`.

The U2 raw-header checks (`ReservedTenantHeader`, `TenantIdMismatch`) still run first, so flipping `TenantContextRequired` cannot bypass them.

Root tenancy setup:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy
    .Messaging(messaging => messaging
        .PropagateTenant()
        .RequireTenantOnPublish()));
```

Messaging-only setup must still go through the root tenancy seam — `AddTenantPropagation()` has been removed. Combine `AddHeadlessMessaging` with the root tenancy registration:

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.TenantContextRequired = true;
});

builder.AddHeadlessTenancy(tenancy => tenancy
    .Messaging(messaging => messaging
        .PropagateTenant()
        .RequireTenantOnPublish()));
```

**Remediation for background workers / `IHostedService` callers (no ambient HTTP scope):**

```csharp
// Option A: pass the tenant explicitly
await publisher.PublishAsync(
    message,
    new PublishOptions { TenantId = tenantId },
    cancellationToken);

// Option B: scope the AsyncLocal accessor before publishing
using (currentTenant.Change(tenantId))
{
    await publisher.PublishAsync(message, cancellationToken);
}
```

Catch `MissingTenantContextException` directly (it inherits from `Exception`, not `InvalidOperationException`) when a cross-cutting handler needs to map it to an HTTP 4xx or suppress retries.

## Middleware

The pipeline supports cross-cutting middleware on both sides via typed russian-doll contracts:

- `IConsumeMiddleware<TContext>` where `TContext : ConsumeContext`
- `IPublishMiddleware<TContext>` where `TContext : PublishContext`

Middleware receives one mutable context plus `Func<ValueTask> next`. Code before `await next()` runs before the inner ring; code after it runs after a successful inner ring. Use ordinary `try/catch` around `await next()` for compensation, retries, and error policy. Returning without calling `next` short-circuits the inner handler or publisher.

```csharp
public sealed class AuditConsumeMiddleware(ILogger<AuditConsumeMiddleware> logger)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        logger.LogInformation("Consuming {MessageId}", context.MessageId);
        await next();
    }
}

public sealed class CorrelationPublishMiddleware
    : IPublishMiddleware<PublishingContext<OrderPlaced>>
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

builder.Services.AddHeadlessMessaging(options => { /* ... */ })
    .AddBusConsumeMiddleware<AuditConsumeMiddleware>()
    .AddPublishMiddlewareFor<CorrelationPublishMiddleware, OrderPlaced>();
```

**Registration scopes:**

- `AddBusPublishMiddleware<T>()` / `AddBusConsumeMiddleware<T>()`: object-typed middleware for every publish or consume. Bus scope must implement `IPublishMiddleware<PublishContext>` or `IConsumeMiddleware<ConsumeContext>`.
- `AddPublishMiddlewareFor<TMiddleware, TMessage>()`: typed publish middleware for one message type.
- `AddConsumeMiddlewareFor<TMiddleware, TMessage>(group)`: typed consume middleware for one message type and consumer group.
- Each call returns a registration handle with `.WithPriority(int)`. Lower priority runs first and wraps later middleware. Ties use registration order. Default priority is `0`; first-party tenant propagation uses `-1000`.

**Framework guarantees:**

- Post-success middleware failures are logged and suppressed only after the inner handler/publisher completed successfully, avoiding duplicate publish or consume retries.
- `OperationCanceledException` whose token matches `context.CancellationToken` is never silently swallowed, including recursive `AggregateException` cases.
- After middleware returns normally, the pipeline rechecks `context.CancellationToken.IsCancellationRequested` and throws OCE if the current context token is canceled.

**Publish context rules:** `PublishingContext<T>.Options` and `DelayTime` are mutable before `await next()`. After the inner publisher completes, the context is marked read-only and setters throw `InvalidOperationException`; reads still work. `PublishingContext<T>.IsTransactional` is `true` only when the publish was buffered into the outbox under an ambient commit coordinator carrying a relational transaction, whose commit is the caller's responsibility.

**Cancellation token swaps:** middleware that creates per-attempt or per-operation tokens must call `context.WithCancellationToken(...)` before `await next()`. Downstream middleware must re-read `context.CancellationToken` at each await boundary; do not capture it once at method entry.

### Multi-tenancy

The framework ships built-in middleware that propagates the originating tenant on the wire:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy
    .Messaging(messaging => messaging.PropagateTenant()));
```

The root tenancy seam registers `TenantPropagationPublishMiddleware` (stamps `PublishOptions.TenantId` from ambient `ICurrentTenant.Id`) and `TenantPropagationConsumeMiddleware` (calls `ICurrentTenant.Change(...)` for the lifetime of the consume). Caller-set values on `PublishOptions.TenantId` are preserved verbatim — set it explicitly to override the ambient tenant. See the multi-tenancy doc's [Message Consumers](multi-tenancy.md#message-consumers) section for the trust boundary and the strict-tenancy guard.

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
- **InMemory**: FIFO ordering with single consumer thread

### Configuration Impact on Ordering

- **`ConsumerThreadCount > 1`**: Enables concurrent message consumption, messages may process out of order
- **`EnableSubscriberParallelExecute = true`**: Buffers messages in-memory queue for parallel processing, no ordering guarantee
- **Single consumer thread (`ConsumerThreadCount = 1`)**: Sequential processing, maintains transport order

### Recommendations

- For strict ordering: Use `ConsumerThreadCount = 1` with Kafka (partition key), Azure Service Bus (sessions), or AWS SQS (FIFO)
- For high throughput: Use parallel processing; design consumers to be order-independent
- Test ordering behavior with your specific transport and configuration

## Circuit Breaker

Per-consumer-group circuit breaker that pauses transport consumption when a dependency is unhealthy, preventing message-retry storms.

**State machine:** Closed → Open (pause transport) → HalfOpen (probe) → Closed (resume) or Open (re-trip).

Open duration escalates exponentially on repeated trips and resets after consecutive successful close cycles.

### Global Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // Global circuit breaker (applies to all consumer groups)
    setup.Options.CircuitBreaker.FailureThreshold = 5;          // consecutive transient failures to trip
    setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30);   // initial open duration
    setup.Options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromSeconds(240); // cap after escalation

    // Adaptive retry backpressure
    setup.Options.RetryProcessor.AdaptivePolling = true;
    setup.Options.RetryProcessor.MaxPollingInterval = TimeSpan.FromMinutes(15);
    setup.Options.RetryProcessor.CircuitOpenRateThreshold = 0.8; // back off above 80% circuit-open rate
});
```

### Per-Consumer Override

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessage<PaymentProcessed>(message =>
        message.MessageName("payments.process").OnBus<PaymentHandler>(consumer => consumer.WithCircuitBreaker(cb =>
        {
            cb.FailureThreshold = 3;                    // more sensitive
            cb.OpenDuration = TimeSpan.FromSeconds(60); // longer cooldown
        })));

    // Disable circuit breaker for a best-effort consumer
    setup.ForMessage<MetricsUpdated>(message =>
        message.OnBus<MetricsHandler>(consumer => consumer.WithCircuitBreaker(cb => cb.Enabled = false)));
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

// Enumerate registered group names (available before any messages are processed)
IReadOnlySet<string> groups = monitor.KnownGroups;

// Check state
var states = monitor.GetAllStates(); // all groups with current state
var isOpen = monitor.IsOpen(IntentType.Bus, "payments");
var state = monitor.GetState(IntentType.Bus, "payments"); // Closed, Open, HalfOpen, or null if unregistered

// Rich snapshot with escalation and timing details
CircuitBreakerSnapshot? snapshot = monitor.GetSnapshot(IntentType.Bus, "payments");
// snapshot.State, snapshot.EscalationLevel, snapshot.ConsecutiveFailures,
// snapshot.FailureThreshold, snapshot.OpenedAt, snapshot.EstimatedRemainingOpenDuration,
// snapshot.EffectiveOpenDuration

// Manual recovery (operator/agent action)
var wasReset = await monitor.ResetAsync(IntentType.Bus, "payments"); // true if reset performed
var wasOpened = await monitor.ForceOpenAsync(IntentType.Bus, "payments"); // true if force-opened
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

The circuit breaker operates per-process only. There is no cross-instance coordination — each application instance maintains its own circuit state. In a multi-replica deployment, one instance may have an open circuit while others remain closed.

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

---

# Headless.Messaging.Dashboard

Web-based dashboard for monitoring and managing distributed messaging infrastructure.

## Problem Solved

Provides real-time visibility into message processing, failures, retries, and system health through an embedded web UI for operations and troubleshooting.

## Key Features

- **Real-Time Monitoring**: Live message throughput and latency metrics
- **Message Explorer**: Search, filter, and inspect messages
- **Failure Management**: View and retry failed messages
- **Node Discovery**: Multi-instance cluster visibility
- **Performance Metrics**: Consumer processing stats and bottlenecks

## Installation

```bash
dotnet add package Headless.Messaging.Dashboard
```

### Quick Start

```csharp
services.AddMessagingDashboard(options => options.UseBasicAuth("admin", password));
```

### Configuration

Configure authentication, node discovery, dashboard pathing, and monitoring API access through the dashboard option builder.

### Dependencies

`Headless.Messaging.Core`, ASP.NET Core packages.

### Side Effects

Registers dashboard services, request mapping, authentication helpers, and node discovery services.

## Headless.Messaging.Dashboard.K8s

### Problem Solved

Adds Kubernetes node discovery for messaging dashboards.

### Key Features

- Kubernetes service and namespace discovery.
- `UseK8sDiscovery(...)` extension.

### Installation

```bash
dotnet add package Headless.Messaging.Dashboard.K8s
```

### Quick Start

```csharp
services.AddHeadlessMessaging(setup => setup.UseK8sDiscovery());
```

### Configuration

Configure namespace and service discovery through the Kubernetes discovery options.

### Dependencies

`Headless.Messaging.Dashboard`, Kubernetes client libraries.

### Side Effects

Registers a Kubernetes-backed node discovery provider.

## Headless.Messaging.OpenTelemetry

### Problem Solved

Adds tracing and metrics instrumentation for messaging publish, consume, retry, circuit breaker, and transport flows.

### Key Features

- `AddMessagingInstrumentation(...)` for traces and metrics.
- `IActivityTagEnricher` extension point.
- Built-in tags for broker, message name, group, intent, status, and retry/circuit state.

### Design Notes

Payload-derived routing values may contain sensitive data. Instrumentation must log presence and stable metadata, not raw selector outputs.

### Installation

```bash
dotnet add package Headless.Messaging.OpenTelemetry
```

### Quick Start

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddMessagingInstrumentation())
    .WithMetrics(metrics => metrics.AddMessagingInstrumentation());
```

### Configuration

Use `MessagingInstrumentationOptions` and `AddEnricher(...)` for custom tags. Enrichers should avoid unbounded cardinality.

### Dependencies

OpenTelemetry tracing and metrics packages, `Headless.Messaging.Core`.

### Side Effects

Registers OpenTelemetry instrumentation hooks and meters/tracers.

## Headless.Messaging.Aws

### Problem Solved

Provides AWS SNS bus transport and AWS SQS queue transport.

### Key Features

- `setup.UseAws(...)`.
- SNS topics for bus publishing.
- SQS queues for queue delivery.
- FIFO topic/queue support.
- Producer hatch: `UseAws(aws => aws.MessageGroupId(message => ...))`.

### Design Notes

`MessageGroupId(...)` is producer-side only because it is stamped while publishing. The provider maps it to native FIFO `MessageGroupId`; it is not a custom message attribute. Values longer than 128 characters are rejected.

### Installation

```bash
dotnet add package Headless.Messaging.Aws
```

### Quick Start

```csharp
setup.UseAws(options =>
{
    options.Region = Amazon.RegionEndpoint.USEast1;
});

setup.ForMessage<OrderPlaced>(message => message
    .MessageName("orders-placed.fifo")
    .UseAws(aws => aws.MessageGroupId(order => order.CustomerId.ToString()))
    .OnQueue<OrderWorker>());
```

### Configuration

Configure AWS region, service URLs, and credentials through `AmazonSqsOptions`.

### Dependencies

AWS SNS/SQS SDK packages, `Headless.Messaging.Core`.

### Side Effects

Registers SNS/SQS clients, bus/queue transports, and AWS consumer client services.

## Headless.Messaging.AzureServiceBus

### Problem Solved

Provides Azure Service Bus topic and queue transports.

### Key Features

- `setup.UseAzureServiceBus(...)`.
- Topic and queue transport support.
- Session-aware processing.
- Producer hatch: `UseAzureServiceBus(asb => asb.PartitionKey(message => ...))`.

### Design Notes

`PartitionKey(...)` is producer-side only and limited to 128 characters. When sessions are enabled, Azure Service Bus requires `PartitionKey` to equal `SessionId`; the message builder rejects mismatches.

### Installation

```bash
dotnet add package Headless.Messaging.AzureServiceBus
```

### Quick Start

```csharp
setup.UseAzureServiceBus(options => options.ConnectionString = connectionString);

setup.ForMessage<OrderPlaced>(message => message
    .UseAzureServiceBus(asb => asb.PartitionKey(order => order.CustomerId.ToString()))
    .OnBus<OrderProjection>());
```

### Configuration

Configure connection string or namespace, retry/client settings, queue/topic behavior, and session support through provider options.

### Dependencies

Azure.Messaging.ServiceBus, `Headless.Messaging.Core`.

### Side Effects

Registers Service Bus clients, transports, consumer client factory, and producer descriptor services.

## Headless.Messaging.InMemory

### Problem Solved

Provides in-process bus and queue transport for local development and tests.

### Key Features

- `setup.UseInMemoryTransport()`.
- In-process bus and queue delivery.
- No external broker.

### Installation

```bash
dotnet add package Headless.Messaging.InMemory
```

### Quick Start

```csharp
setup.UseInMemoryTransport();
```

### Configuration

None.

### Dependencies

`Headless.Messaging.Core`.

### Side Effects

Registers in-memory transports and consumer client factory. Messages are lost when the process exits.

## Headless.Messaging.InMemoryStorage

### Problem Solved

Provides in-process messaging storage for local development and tests.

### Key Features

- `setup.UseInMemoryStorage()`.
- Stores published, received, failed, and monitoring state in memory.

### Installation

```bash
dotnet add package Headless.Messaging.InMemoryStorage
```

### Quick Start

```csharp
setup.UseInMemoryStorage();
```

### Configuration

None.

### Dependencies

`Headless.Messaging.Core`.

### Side Effects

Registers in-memory storage and monitoring services. State is lost when the process exits.

## Headless.Messaging.Kafka

### Problem Solved

Provides Kafka queue-intent transport for partitioned, consumer-group processing.

### Key Features

- `setup.UseKafka(...)`.
- Kafka topic auto-creation support.
- Producer hatch: `UseKafka(kafka => kafka.PartitionBy(message => ...))`.
- Consumer hatch: `consumer.UseKafka(kafka => kafka.IsolationLevel(IsolationLevel.ReadCommitted))`.

### Design Notes

Kafka is queue-intent only in this package. `PartitionBy(...)` maps to the Kafka key. The framework does not impose a Kafka key length cap; broker/client configuration owns practical limits.

### Installation

```bash
dotnet add package Headless.Messaging.Kafka
```

### Quick Start

```csharp
setup.UseKafka(options => options.Servers = "localhost:9092");

setup.ForMessage<OrderPlaced>(message => message
    .MessageName("orders.placed")
    .UseKafka(kafka => kafka.PartitionBy(order => order.CustomerId.ToString()))
    .OnQueue<OrderWorker>(consumer => consumer.UseKafka(kafka => kafka.IsolationLevel(IsolationLevel.ReadCommitted))));
```

### Configuration

Configure bootstrap servers, main Kafka config, topic options, custom headers, and retriable error codes through `MessagingKafkaOptions`.

### Dependencies

Confluent.Kafka, `Headless.Messaging.Core`.

### Side Effects

Registers Kafka transports, connection pool, consumer factory, and provider-specific message/consumer config support.

## Headless.Messaging.Nats

### Problem Solved

Provides NATS and JetStream transport support with subject-based routing.

### Key Features

- `setup.UseNats(...)`.
- Stream auto-creation and durable consumers.
- Producer hatch: `UseNats(nats => nats.SubjectShard(message => ...))`.
- Consumer hatch: `consumer.UseNats(nats => nats.Sharded())`.

### Design Notes

`SubjectShard(...)` appends one safe subject token to the logical message name. It rejects `.`, `*`, `>`, whitespace, and control characters so payload values cannot change the subject hierarchy or wildcard behavior.

Shard symmetry is required: when a message uses `SubjectShard(...)` on the producer side, every consumer registered for that message must call `.UseNats(c => c.Sharded())` on its consumer registration. This is validated at startup and throws `InvalidOperationException` if violated. The reason: NATS delivers zero messages with no error when a FilterSubject does not match any shard subject — the asymmetry causes silent data loss that is otherwise very difficult to diagnose.

### Installation

```bash
dotnet add package Headless.Messaging.Nats
```

### Quick Start

```csharp
setup.UseNats(options => options.Servers = "nats://localhost:4222");

setup.ForMessage<OrderPlaced>(message => message
    .UseNats(nats => nats.SubjectShard(order => order.CustomerId.ToString()))
    .OnBus<OrderProjection>(consumer => consumer.UseNats(nats => nats.Sharded())));
```

### Configuration

Configure NATS servers, credentials, stream behavior, durable names, and connection settings through `MessagingNatsOptions`.

### Dependencies

NATS client packages, `Headless.Messaging.Core`.

### Side Effects

Registers NATS connection pool, transports, consumer factory, and stream initialization behavior.

## Headless.Messaging.Pulsar

### Problem Solved

Provides Apache Pulsar transport support.

### Key Features

- `setup.UsePulsar(...)`.
- Pulsar bus and queue transport support.
- TLS-related options through provider configuration.

### Installation

```bash
dotnet add package Headless.Messaging.Pulsar
```

### Quick Start

```csharp
setup.UsePulsar(options => options.ServiceUrl = "pulsar://localhost:6650");
```

### Configuration

Configure service URL, authentication, and TLS through `MessagingPulsarOptions`.

### Dependencies

Pulsar client packages, `Headless.Messaging.Core`.

### Side Effects

Registers Pulsar connection factory, transports, and consumer client factory.

## Headless.Messaging.RabbitMq

### Problem Solved

Provides RabbitMQ exchange and queue transport support.

### Key Features

- `setup.UseRabbitMq(...)`.
- Bus exchange and queue delivery.
- Consumer hatch: `consumer.UseRabbitMq(rabbit => rabbit.PrefetchCount(...))`.

### Design Notes

RabbitMQ currently exposes consumer-side QoS only in this cluster. Publish routing still follows the logical message name because subscription topology binds queues by logical message name.

### Installation

```bash
dotnet add package Headless.Messaging.RabbitMq
```

### Quick Start

```csharp
setup.UseRabbitMq(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
});

setup.ForMessage<OrderPlaced>(message => message
    .OnBus<OrderProjection>(consumer => consumer.UseRabbitMq(rabbit => rabbit.PrefetchCount(20))));
```

### Configuration

Configure host, credentials, exchange, queue arguments, QoS defaults, and custom headers through `RabbitMqOptions`.

### Dependencies

RabbitMQ.Client, `Headless.Messaging.Core`.

### Side Effects

Registers RabbitMQ connection/channel pool, bus/queue transport, consumer client factory, and provider-specific config support.

## Headless.Messaging.Redis

### Problem Solved

Provides Redis-backed messaging transport options.

### Key Features

- `setup.UseRedis(...)`.
- Redis pub/sub bus transport.
- Redis transport support for messaging scenarios that can accept Redis delivery semantics.

### Installation

```bash
dotnet add package Headless.Messaging.Redis
```

### Quick Start

```csharp
setup.UseRedis(options => options.Configuration = "localhost:6379");
```

### Configuration

Configure Redis connection and pub/sub behavior through Redis messaging options.

### Dependencies

StackExchange.Redis, `Headless.Messaging.Core`.

### Side Effects

Registers Redis transports, consumers, and Redis connection services.

## Headless.Messaging.Storage.PostgreSql

### Problem Solved

Provides PostgreSQL durable storage for messaging publish/receive state, retries, monitoring, and outbox behavior.

### Key Features

- `setup.UsePostgreSql(...)`.
- PostgreSQL schema/table configuration.
- EF/Core.Db integration and startup initialization.
- **GUID Row IDs**: Message storage identifiers come from the `Version7` keyed `IGuidGenerator` and are persisted as PostgreSQL `UUID` columns.

### Installation

```bash
dotnet add package Headless.Messaging.Storage.PostgreSql
```

### Quick Start

```csharp
setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);
```

### Configuration

Configure connection string, schema, table names, and provider-specific storage options through `PostgreSqlOptions`.

### Dependencies

Npgsql, EF Core provider packages, `Headless.Messaging.Core`.

### Side Effects

Registers PostgreSQL storage, monitoring API, storage initializer, and transaction integration.

## Headless.Messaging.Storage.SqlServer

### Problem Solved

Provides SQL Server durable storage for messaging publish/receive state, retries, monitoring, and outbox behavior.

### Key Features

- `setup.UseSqlServer(...)`.
- SQL Server schema/table configuration.
- EF/Core.Db integration and startup initialization.
- **GUID Row IDs**: Message storage identifiers come from the `SqlServer` keyed `IGuidGenerator` and are persisted as SQL Server `uniqueidentifier` columns.

### Installation

```bash
dotnet add package Headless.Messaging.Storage.SqlServer
```

### Quick Start

```csharp
setup.UseSqlServer(builder.Configuration.GetConnectionString("Messaging")!);
```

### Configuration

Configure connection string, schema, table names, and provider-specific storage options through `SqlServerOptions`.

### Dependencies

Microsoft.Data.SqlClient, EF Core provider packages, `Headless.Messaging.Core`.

### Side Effects

Registers SQL Server storage, monitoring API, storage initializer, and transaction integration.

## Headless.Messaging.Testing

### Problem Solved

Provides test harness utilities for observing messaging behavior without coupling tests to provider internals.

### Key Features

- `AddMessagingTestHarness(...)`.
- Recorded published and consumed messages.
- Wait helpers for asynchronous assertions.
- `TestConsumer<T>` helpers.

### Design Notes

Use the testing package for application tests that need to assert published messages or consumed messages. Provider conformance still belongs in provider-specific or shared harness tests.

### Installation

```bash
dotnet add package Headless.Messaging.Testing
```

### Quick Start

```csharp
services.AddMessagingTestHarness();

var harness = provider.GetRequiredService<IMessagingTestHarness>();
await harness.Published.WaitForAsync<OrderPlaced>();
```

### Configuration

None.

### Dependencies

`Headless.Messaging.Core`.

### Side Effects

Registers recording transport wrappers and in-memory observable collections for tests.
