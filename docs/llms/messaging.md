---
domain: Messaging
packages: Messaging.Abstractions, Messaging.Bus.Abstractions, Messaging.Queue.Abstractions, Messaging.Core, Messaging.Dashboard, Messaging.Dashboard.K8s, Messaging.Aws, Messaging.AzureServiceBus, Messaging.InMemory, Messaging.InMemoryStorage, Messaging.Kafka, Messaging.Nats, Messaging.Pulsar, Messaging.RabbitMq, Messaging.Redis, Messaging.Storage.PostgreSql, Messaging.Storage.SqlServer, Messaging.Testing
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
    - [Design Notes](#design-notes-2)
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
- [OpenTelemetry (native, in Headless.Messaging.Core)](#opentelemetry-native-in-headlessmessagingcore)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-3)
    - [Span attributes and toggles](#span-attributes-and-toggles)
    - [Quick Start](#quick-start-6)
- [Headless.Messaging.Aws](#headlessmessagingaws)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)
- [Headless.Messaging.AzureServiceBus](#headlessmessagingazureservicebus)
    - [Problem Solved](#problem-solved-8)
    - [Key Features](#key-features-8)
    - [Design Notes](#design-notes-5)
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
    - [Design Notes](#design-notes-6)
    - [Installation](#installation-11)
    - [Quick Start](#quick-start-11)
    - [Configuration](#configuration-11)
    - [Dependencies](#dependencies-11)
    - [Side Effects](#side-effects-11)
- [Headless.Messaging.Nats](#headlessmessagingnats)
    - [Problem Solved](#problem-solved-12)
    - [Key Features](#key-features-12)
    - [Design Notes](#design-notes-7)
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
    - [Design Notes](#design-notes-8)
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
    - [Design Notes](#design-notes-9)
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

    setup.ForMessage<OrderPlaced>(message =>
        message
            .MessageName("orders.placed")
            .CorrelationFrom(order => order.OrderId.ToString())
            .OnBus<OrderProjection>(consumer =>
                consumer.Group("orders-projection").Concurrency(4).UseRabbitMq(rabbit => rabbit.PrefetchCount(20))
            )
    );
});
```

## Agent Instructions

- **App install pattern**: install `Messaging.Core` + exactly one transport + exactly one storage. Bootstrap fails when zero or multiple storage providers are configured. Core brings shared/bus/queue abstractions transitively for applications.
- **Library contract pattern**: install `Messaging.Abstractions`, `Messaging.Bus.Abstractions`, or `Messaging.Queue.Abstractions` directly only when a library exposes consumers/envelopes or publisher interfaces without bootstrapping Core.
- **Use `InMemory` + `InMemoryStorage` only for dev/testing**, never in production. Data is lost on restart.
- **OpenTelemetry is native to `Messaging.Core`** (no satellite package). Subscribe traces/metrics with `AddMessagingInstrumentation()` on the `TracerProviderBuilder`/`MeterProviderBuilder`, and configure enrichers/suppression via `setup.Instrumentation` inside `AddHeadlessMessaging(...)`.
- **Add `Messaging.Testing`** in test projects for integration testing with awaitable assertions. Use `AddMessagingTestHarness()` to decorate an existing host's DI container (WebApplicationFactory, IHost), or `MessagingTestHarness.CreateAsync()` for standalone harness.
- **Add `Messaging.Dashboard`** when monitoring UI is needed; it exposes operational message actions and requires an explicit authentication choice (the host fails to start otherwise), so configure `WithBasicAuth`, `WithApiKey`, `WithHostAuthentication`, or `WithCustomAuth` — and `SetCorsOrigins` if the SPA is served cross-origin — before production exposure.
- **Messages are type-safe**: Define message types as classes/records. Register explicit consumers implementing `IConsume<TMessage>` with `setup.ForMessage<TMessage>(...)`. Use `setup.ForMessagesFromAssemblyContaining<TMarker>()` or `setup.ForMessagesFromAssembly(assembly)` inside `AddHeadlessMessaging(...)` for assembly scanning. Use the scan callback overloads to set per-consumer queue/bus intent, group, concurrency, handler id, circuit-breaker override, or `Skip()`; keep message-name overrides on explicit `ForMessage<TMessage>(...)` registrations.
- **Library-owned consumers can register out of order**: `IServiceCollection.ForMessage<TMessage>(...)` can run before or after `AddHeadlessMessaging(...)` during service configuration — both entry points share one found-or-created `ConsumerRegistry`. `MessageName(...)` mappings are registered eagerly, so a publish that races ahead of startup (e.g. an `IHostedService` publishing in `StartAsync`) still resolves the explicit name rather than the convention fallback. Consumer metadata still drains before startup topology reads, so package setup methods do not need to force an app-level call order.
- **Runtime handlers are first-class**: Use `IRuntimeSubscriber` for ephemeral broker-attached delegates. They share scoped DI, middleware, diagnostics, retry, and correlation semantics with class handlers.
- **Choose publisher by intent**: Use `IBus` / `IOutboxBus` for broadcast publish/subscribe and `IQueue` / `IOutboxQueue` for point-to-point work queues.
- **Choose durability separately**: `IBus` and `IQueue` send directly to the broker; `IOutboxBus` and `IOutboxQueue` persist first and drain later with at-least-once semantics.
- **Publisher services are capability-gated**: Core registers `IBus` / `IOutboxBus` only when a provider registers `IBusTransport`, and registers `IQueue` / `IOutboxQueue` only when a provider registers `IQueueTransport`. A manually registered publisher without its matching transport fails during messaging bootstrap.
- **Outbox intent is durable**: Storage rows carry `IntentType`; retry drainers dispatch bus rows through `IBusTransport` and queue rows through `IQueueTransport`. A persisted row whose intent has no registered transport is terminally failed and the drainer continues.
- **Do NOT use raw transport client libraries** (e.g., `RabbitMQ.Client`, `Confluent.Kafka`) directly -- always use the `Headless.Messaging` abstraction layer.
- **Ordering depends on transport**: Kafka orders by partition key. Azure Service Bus orders by session. RabbitMQ has no ordering with multiple consumers. Set `ConsumerThreadCount = 1` for strict ordering.
- **RabbitMQ credentials**: The framework rejects default `guest`/`guest` credentials. Always configure explicit username/password.
- **AWS SQS redrive is external**: Configure a dead-letter queue and redrive policy with a bounded receive count. Headless releases malformed SNS envelopes for retry but does not provision redrive infrastructure.
- **Message-name mapping**: Map message types to logical message names via `setup.ForMessage<TMessage>(x => x.MessageName("message.name"))` (primary) or conventions. `IMessagingBuilder.WithMessageNameMapping<TMessage>("message.name")` remains available inside the `AddHeadlessMessaging` callback for standalone/publisher-only overrides.
- **Fail-fast defaults**: Duplicate consumer or runtime registrations are rejected by default. Anonymous runtime delegates must provide `HandlerId`.
- **Telemetry parity**: Existing diagnostic listener and metric names stay stable across direct publish, outbox publish, and runtime subscriptions.
- **Consumer lifecycle semantics**: `IConsumerLifecycle` runs per delivery on the scoped consumer instance. Do not treat it as application startup or shutdown.
- **Consumer startup is host-cancellable**: consumer factory creation, metadata provisioning, and subscription receive the host-stopping token. Provider implementations preserve `OperationCanceledException`; do not wrap shutdown cancellation as a broker failure.
- **Core handles outbox automatically** when paired with EF Core -- messages are stored in database before being dispatched to transport.
- **Atomic outbox is on by default on the EF storage path** (`setup.UseEntityFramework<TContext>()`): a publish inside a coordinated transaction is atomic with the DB write, zero consumer wiring — do not hand-wire commit coordination for it. Opt out with `setup.UseEntityFramework<TContext>(o => o.EnableTransactionalOutbox = false)` (the opt-out travels with the EF storage choice). Raw-ADO paths (`UsePostgreSql`/`UseSqlServer` by connection string) stay explicit opt-in: wire `AddPostgreSqlCommitCoordination()`/`AddSqlServerCommitCoordination()` plus the coordinated-transaction helpers.
- **Mis-wire fails loud at startup**: if the outbox is enabled but the commit interceptor is not firing, `CommitInterceptorStartupGate<TContext>` logs a warning by default; set `CommitProbeMode.Strict` (via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitProbeMode.Strict)`) to fail startup instead of shipping a silently non-transactional outbox.
- **Dashboard.K8s requires RBAC** permissions to read pods/endpoints in the Kubernetes API.
- **Callbacks enable async response routing**: Set `CallbackName` on `PublishOptions` (bus) **or** `EnqueueOptions` (queue) to a response message name. When the consumer completes, a correlated response message is automatically published to that name through the durable bus path — regardless of which intent delivered the request. The consumer calls `context.SetResponse<TResponse>(value)` to publish a typed response body; if it does not, the callback still goes out as a headers-only message when response headers are present. This is **not** request/reply — the caller does not `await` the response. A separate consumer must handle the response message. Use `context.Headers.RemoveCallback()` to suppress, `RewriteCallback()` to redirect, or `AddResponseHeader()` to attach extra headers to the response. Callback delivery is **at-least-once** — a crash, or a transient failure of the success-mark write after the response outbox row is written, redelivers the request and republishes the response, so make response consumers idempotent (dedupe on `(CorrelationId, CorrelationSequence)`; `CorrelationId` alone is ambiguous across hops because it is set to the immediate parent message id per hop, not the chain root). **Footgun on the bus path:** a published (pub/sub) request is delivered to *every* matching subscriber, so each one fires its own callback — N subscribers produce N response messages. Point-to-point (`IQueue` / `IOutboxQueue`) delivers to one consumer and produces exactly one response; prefer it for command→result chaining unless you intend scatter-gather (correlate the fan-in via `CorrelationId` / `CorrelationSequence`).
- **Strict publish tenancy is opt-in**: Use `builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(m => m.PropagateTenant().RequireTenantOnPublish()))`. The previous `MessagingBuilder.AddTenantPropagation()` extension has been removed; the root tenancy seam is the single composition point. When neither `PublishOptions.TenantId` nor ambient `ICurrentTenant` is set, the publish wrapper throws `Headless.Abstractions.MissingTenantContextException`. See [Strict Publish Tenancy](#strict-publish-tenancy) and the multi-tenancy doc's [Message Consumers](multi-tenancy.md#message-consumers) section.
- **Retry behavior is configured via `MessagingOptions.RetryPolicy`**. `RetryStrategy` is a public Polly `RetryStrategyOptions` contract; `MaxPersistedRetries`, durable scheduling, leases, and terminal callbacks remain Messaging-owned. Configure `ShouldHandle` explicitly. `OnExhausted` fires only after a matched failure consumes the complete budget and the owned terminal write succeeds.
- **Distributed lock**: see [Distributed Lock Integration](#distributed-lock-integration) for when to enable, when to skip, and the two-layer model (per-row `LockedUntil` lease + coarse-grained distributed lock).
- **Never write framework metadata through provider hatches**. For publish options, use typed properties; raw `Headers.TenantId` is accepted only by the legacy tenant-integrity path and should not be authored directly.
- **Treat provider hatches as physical broker routing/configuration**. Producer-side hatches live on `IMessageBuilder<TMessage>`; consumer-side hatches live on `IBusConsumerBuilder<TConsumer>` or `IQueueConsumerBuilder<TConsumer>` only when that provider currently exposes consumer settings.
- **Kafka, RabbitMQ, and NATS currently expose consumer-side hatches**. AWS and Azure Service Bus currently expose producer-side hatches only.
- **Keep `docs/llms/messaging.md` and package READMEs aligned** when public messaging behavior changes.

## Core Concepts

- **Transactional outbox (atomic publish) — on by default on the EF storage path**: when the host chooses the EF-context storage path (`setup.UseEntityFramework<TContext>()`), the atomic outbox is ON BY DEFAULT with zero consumer wiring. A `producer.PublishAsync(...)` issued inside a coordinated transaction writes its outbox row in the SAME DB transaction and is discarded on rollback, so the message is durable if and only if the business data committed. The EF storage setup auto-registers commit coordination and a DI-registered `IDbContextOptionsConfiguration<TContext>` that attaches the commit-coordination interceptor to the consumer's `DbContext` — including a plain `AddDbContext<TContext>` with no `AddInterceptors(...)`. Opt out with `setup.UseEntityFramework<TContext>(o => o.EnableTransactionalOutbox = false)` to restore non-transactional immediate dispatch (the opt-out travels with the EF storage choice). A startup self-probe (`CommitInterceptorStartupGate<TContext>`) commits an empty transaction and asserts the interceptor fired; on a mis-wire it logs a loud warning (default) or fails startup (`CommitProbeMode.Strict`). This applies **only** to the EF-context path: the raw-ADO storage paths (`UsePostgreSql(connString)` / `UseSqlServer(connString)`, no `DbContext`) are unchanged and stay explicit opt-in — there is no `DbContext` to attach an interceptor to, so they register `AddPostgreSqlCommitCoordination()` / `AddSqlServerCommitCoordination()` and use the `EnlistCommitCoordination` / `ExecuteCoordinatedTransactionAsync` helpers. See [commit-coordination.md](commit-coordination.md) for the interceptor attachment and probe modes. This is an atomicity guarantee for the *write*, not exactly-once delivery — dispatch is still at-least-once (next bullet).
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

### Registration Overloads

Every transport `Use{Provider}(...)` (except `UseInMemory()`, which has no options) exposes the standard overload trio alongside any scalar-convenience form:

- `Use{Provider}(IConfiguration config)` — binds and validates the options from a configuration section.
- `Use{Provider}(Action<TOptions> configure)` — imperative configuration.
- `Use{Provider}(Action<TOptions, IServiceProvider> configure)` — imperative configuration with access to the resolved service provider (for example to pull a secret, connection string, or credential from DI while configuring).

Options are validated on start through their FluentValidation validators. Each provider keeps the `{ProviderToken}MessagingOptions` naming shape (`AmazonSqsMessagingOptions`, `AzureServiceBusMessagingOptions`, `KafkaMessagingOptions`, `NatsMessagingOptions`, `PulsarMessagingOptions`, `RabbitMqMessagingOptions`, `RedisMessagingOptions`, `RedisPubSubMessagingOptions`).

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
- **Retry row owners** — persisted `published` and `received` rows include nullable `Owner` (`node@incarnation`). It is stamped only when a Coordination membership identity is active and is cleared when `LockedUntil` is cleared.

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
- Host-cancellable consumer factory creation, metadata provisioning, and subscription.

### Design Notes

Core owns logical metadata and provider-independent correctness. Provider packages own broker-specific values and limits. `CorrelationFrom(...)` is a universal logical knob; partition keys, routing keys, subject shards, and message group ids are provider hatches because their semantics differ.

The public consumer startup contracts accept trailing optional cancellation tokens: both `IConsumerClientFactory.CreateAsync(...)` overload shapes, `IConsumerClient.FetchMessageNamesAsync(...)`, and `IConsumerClient.SubscribeAsync(...)`. Core passes the host-stopping token to metadata startup and a linked group token to worker creation and subscription. Implementations must let `OperationCanceledException` escape unchanged.

The blessed cross-package SPI (the contracts that storage providers, transports, and dashboards resolve or implement) lives in the public `Headless.Messaging.Runtime` namespace: `IProcessingServer` (implement to attach a long-running unit to the bootstrap sequence) and `IConsumerServiceSelector` / `MethodMatcherCache` (inspect the resolved consumer topology). The `TransportNaming` (`WildcardToRegex`, `Normalize`) and `RuntimeTypeInspection` (`IsComplexType`, `DeclaresFieldOfType`) helpers in the same namespace are `internal` and shared with the first-party transports via `InternalsVisibleTo` — they are not part of the NuGet contract. These types were previously exposed under `Headless.Messaging.Internal`; that namespace now holds only genuine implementation detail. The monitoring status is a typed enum — `StatusName` (in `Headless.Messaging.Monitoring`, next to `MessageView`/`MessageQuery`) — so `MessageView.StatusName` and the `MessageQuery.StatusName` filter are compile-time safe. Storage providers persist and compare the enum member names verbatim as strings, so the SQL column contract is unchanged, and the dashboard serializes the status by name to keep the SPA wire shape stable.

### Installation

```bash
dotnet add package Headless.Messaging.Core
dotnet add package Headless.Messaging.InMemory
dotnet add package Headless.Messaging.InMemoryStorage
```

### Quick Start

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UseInMemoryStorage();

    setup.ForMessage<OrderPlaced>(message =>
        message
            .MessageName("orders.placed")
            .CorrelationFrom(order => order.OrderId.ToString())
            .OnBus<OrderPlacedConsumer>(consumer => consumer.Group("orders"))
    );
});
```

### Configuration

- `MessagingOptions.DefaultGroupName`, `GroupNamePrefix`, `MessageNamePrefix`, and `Version` control naming and isolation. `Version` is validated non-empty and at most 20 characters — the SQL storage providers persist it as a literal into a `VARCHAR(20)`/`nvarchar(20)` column, so an over-long value is rejected at startup instead of failing every outbox insert.
- Retry configuration lives under `RetryPolicy`, publish/receive retry processors, and storage cleanup options. `RetryBatchSize` (default 200, `> 0`) caps the retry-pickup batch and `SchedulerBatchSize` (default 1,000, `> 0`) caps the delayed/queued scheduler batch.
- `UseStorageLock` coordinates retry processors through a messaging-keyed distributed lock provider.
- `DeadNodeReconcileInterval` (default 1 minute, `> 0`) sets the always-on dead-owner recovery reconcile cadence (see [Dead-owner recovery](#dead-owner-recovery)). Independent of `UseStorageLock`.
- `ShutdownTimeout` (default 30 seconds, `> 0`, `<= 5m`) is one end-to-end messaging shutdown bound shared by the consumer-register listener drain, concurrent consumer-client disposal, provider-specific in-flight drains, and the dispatcher loop drain. Cleanup still running when the deadline expires continues fault-observed in the background.
- Register middleware through `MessagingBuilder.AddBusPublishMiddleware<T>()`, `AddBusConsumeMiddleware<T>()`, `AddPublishMiddlewareFor<TMiddleware,TMessage>()`, and `AddConsumeMiddlewareFor<TMiddleware,TMessage>(groupName)`.
- Runtime subscriptions attach handlers after startup through `IRuntimeSubscriber`.

### Dependencies

`Headless.Messaging.Abstractions`, `Headless.Messaging.Bus.Abstractions`, `Headless.Messaging.Queue.Abstractions`, `Headless.Coordination.Abstractions`, `Headless.Coordination.Core`, `Headless.Hosting`, `Headless.Abstractions`, `Headless.Checks`, `Polly.Core`. (`Headless.Coordination.Core` hosts the shared `DeadOwnerRecoveryBridge`.)

### Side Effects

Registers messaging services, hosted processors, publishers, consumers, storage abstractions, runtime registries, middleware registries, keyed messaging lock defaults, and the always-on `DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>` hosted service.

## Retry Policy

`RetryStrategy.MaxRetryAttempts` excludes the original execution and controls inline retries through a reusable Polly `ResiliencePipeline`. Once the inline budget is exhausted, Messaging persists `NextRetryAt` and `MessageNeedToRetryProcessor` performs up to `MaxPersistedRetries` pickups. `InlineAttempts` is reserved atomically before each invocation, so process recovery cannot reset the current burst.

`NextRetryAt` remains application-scheduled through the injected `TimeProvider`, while lease ownership is store-authoritative for fresh dispatch and retry pickup. The public `IDataStorage` SPI accepts a `DispatchTimeout` duration; PostgreSQL and SQL Server compare and stamp leases from one database-clock snapshot, and InMemoryStorage uses its injected `TimeProvider`. A successful call returns the persisted `(LockedUntil, Owner)` identity on the message for fenced attempt and state writes. This eliminates client-clock skew from relational ownership, not duplicate delivery: genuine `DispatchTimeout` expiry permits a successor, and a process paused beyond its lease can resume already-running work alongside it. Delivery remains at-least-once.

```csharp
using Polly;
using Polly.Retry;

builder.Services.AddHeadlessMessaging(setup =>
{
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
});
```

The framework's default classification (retry anything that is not a cancellation and not classified permanent) is exposed as `RetryPolicyOptions.DefaultShouldHandle` — reuse or compose it when replacing `RetryStrategy` so a custom strategy does not silently drop the built-in failure classification.

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

### FailedInfo construction (for tests / fakes)

`FailedInfo` has six required-init properties — `Exception`, `StorageId`, and `RetryCount` are now part of the contract:

```csharp
var info = new FailedInfo
{
    ServiceProvider = scope.ServiceProvider, // live dispatch scope, NOT the root provider
    MessageType = MessageType.Subscribe, // or MessageType.Publish
    Message = message,
    Exception = ex, // the exhausting exception
    StorageId = mediumMessage.StorageId, // storage row identifier for DLQ correlation
    RetryCount = mediumMessage.Retries, // final persisted-retry count
};
```

`ServiceProvider` is the **live per-message DI scope** — the same scope used while the consume / publish attempts ran. Scoped services resolved through `FailedInfo.ServiceProvider` are the same instances seen by the consumer/handler.

### RetryProcessorOptions

`MessagingOptions.RetryProcessor` controls the persisted-retry processor's polling cadence:

| Property | Default | Notes |
| --- | --- | --- |
| `BaseInterval` | `60s` | Base polling interval. Replaces the old `FailedRetryInterval`. |
| `AdaptivePolling` | `true` | When enabled, polling interval halves on healthy cycles and doubles when circuit-open skip rate exceeds threshold. |
| `MaxPollingInterval` | `15m` | Cap on adaptive doubling. |
| `CircuitOpenRateThreshold` | `0.8` | Above this fraction of circuit-open skips, the processor backs off. |

### Migration from pre-RetryPolicy primitives

| Old property | New property | Notes |
| --- | --- | --- |
| `FailedRetryCount` | `RetryPolicy.MaxPersistedRetries` | Controls persisted-retry pickups. Total attempts = `(RetryStrategy.MaxRetryAttempts + 1) × (MaxPersistedRetries + 1)`. |
| `FailedRetryInterval` | `RetryProcessorOptions.BaseInterval` | Default `60s`. |
| `FallbackWindowLookbackSeconds` | *removed* | No replacement — `MessageNeedToRetryProcessor` now polls without a lookback window. |
| `RetryBackoffStrategy` | `RetryPolicy.RetryStrategy` | Configure Polly's `RetryStrategyOptions` directly, including explicit `ShouldHandle`, backoff, jitter, delay generator, cap, and `OnRetry`. |
| `FailedThresholdCallback` | `RetryPolicy.OnExhausted` | The Messaging-owned callback fires only after an owned terminal transition following complete retryable-budget exhaustion. |

## Distributed Lock Integration

`MessagingOptions.UseStorageLock` (default `false`) enables `IDistributedLock`-backed mutual exclusion in `MessageNeedToRetryProcessor`. When `true`, the retry processor acquires a named distributed lock before each publish-retry and receive-retry pickup, gating the entire retry-pickup tick.

Use `MessagingBuilder.UseDistributedLock(...)` to wire the provider. Calling this method implicitly sets `UseStorageLock = true`:

```csharp
// Instance overload — when you already have an IDistributedLock
var lockProvider = new MyDistributedLock(...);
builder.Services.AddHeadlessMessaging(setup => { ... })
    .UseDistributedLock(lockProvider);

// Factory overload — when the provider depends on other DI services
builder.Services.AddHeadlessMessaging(setup => { ... })
    .UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>());
```

Messaging keeps its lock provider under an **internal keyed-DI key** (`"headless.messaging"`) so it never conflicts with any `IDistributedLock` registered at the application level for other purposes.

### What this is and isn't (correctness vs coordination)

- Per-row `LockedUntil` (set to `DispatchTimeout` before each publish/consume attempt — see the [Retry Policy](#retry-policy) section) is the storage concurrency primitive. `NextRetryAt` remains scheduling state: pickup compares it against the injected `TimeProvider` that created the schedule. PostgreSQL and SQL Server independently compare lease expiry and stamp the next `LockedUntil` from one database-clock snapshot inside the same atomic claim command; no separate clock query is added per polling tick. It reduces concurrent dispatch of the same row and works whether or not the distributed lock is enabled, but delivery is still at-least-once under crash, broker redelivery, and broker-accept/storage-mark races.
- Dead-owner recovery is a separate **always-on acceleration primitive**, independent of this lock (see [Dead-owner recovery](#dead-owner-recovery)). With a real `INodeMembership` a recovery bridge reclaims rows owned by `Dead` incarnations and pulls `LockedUntil` back to now; without Coordination, `Owner` remains `null` and rows recover at the normal `LockedUntil` floor.
- The distributed lock is a **coarse-grained pickup mutex**, not a correctness requirement. It gates the entire retry-pickup tick so only one replica scans the backlog at a time.
- Disabling `UseStorageLock` does not change the at-least-once delivery contract. It introduces wasted pickup work on contended backlogs. If an acquired retry lock's `LostToken` fires (EventId 79), no new pickup starts under an already-lost lease; any in-flight dispatch remains governed by the per-row `LockedUntil` lease.

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
- Without a real provider, only `NoOpDistributedLock` is active (the keyed-DI fallback). The bootstrapper emits two mutually-exclusive Warnings depending on what it finds: **EventId 77** when no real provider is wired under any registration, and **EventId 78** when a real provider is registered un-keyed (e.g., via `AddHeadlessDistributedLocks(setup => setup.UseRedis())`) but not flowed through `MessagingBuilder.UseDistributedLock(...)`. Alert on either.
- `UseDistributedLock(...)` is **last-wins** — calling it twice replaces the prior registration rather than stacking duplicates.

**NoOp introspection contract:** when `NoOpDistributedLock` is the resolved messaging-keyed provider, the introspection methods (`IsLockedAsync`, `GetLockInfoAsync`, `ListActiveLocksAsync`, `GetActiveLocksCountAsync`) silently return empty/false/null. They cannot be used to verify lock state in that mode; rely on the EventId 77 / 78 warning at startup as the operational signal.

### Lock names

- `messaging.publish-retry-{version}` — held while processing published-message retries.
- `messaging.receive-retry-{version}` — held while processing received-message retries.

Both names follow the literal pattern shown above. They are constructed internally by `Headless.Messaging.Core`; downstream consumers must not depend on the internal helper that builds them — register a real provider exclusively via `MessagingBuilder.UseDistributedLock(...)` and let messaging resolve its own keyed-DI slot.

`{version}` comes from `MessagingOptions.Version` and is the **cross-process isolation key**. Two services that share a single lock store (e.g., both pointed at the same Redis) MUST set distinct `Version` values — otherwise their retry processors collide on the same lock resource and starve each other. Both locks use `acquireTimeout: TimeSpan.Zero` (non-blocking try-once), a finite lease window equal to the current polling interval, and `Monitoring = LockMonitoringMode.AutoExtend`; when another replica holds the lock the processor skips that pickup cycle and waits for the next polling tick.

**When `UseStorageLock = false`** (default): `IDistributedLock` is never called and distributed lock wiring is not required. Dead-owner recovery is unaffected — it runs independently of this lock (see [Dead-owner recovery](#dead-owner-recovery)).

### Dead-owner recovery

Dead-incarnation retry recovery runs **always-on**, independent of `UseStorageLock`. On every messaging host a `DeadOwnerRecoveryBridge` (an `IHostedService` from `Headless.Coordination.Core`, registered unconditionally) drives reclaim from the membership substrate on two triggers:

- a `WatchAsync` loop that reclaims a node's rows on a `NodeLeft` event (low-latency), and
- a periodic liveness-snapshot reconcile every `MessagingOptions.DeadNodeReconcileInterval` (default 1 minute) that reclaims every `Dead` incarnation as the authoritative backstop — a watch-loop failure degrades to reconcile, not to no recovery.

Reclaim is **dead-only**: only owners the snapshot classifies `Dead` are reclaimed. A `Suspected` owner (likely still alive and mid-dispatch) is never reclaimed, so a transient GC pause, thread-pool starvation, or network blip does not trigger duplicate delivery. Reclaim is idempotent — an in-memory dedup set suppresses duplicate work between the watch and reconcile paths, and the owner-scoped conditional `UPDATE` (which only pulls leases still in the future) makes a repeated reclaim, or a peer's concurrent reclaim, a no-op. Reclaim writes use `CancellationToken.None` so a reclaim racing host shutdown is not torn mid-write.

`LockedUntil` remains the correctness floor: a row whose lease has already expired is recovered by normal pickup regardless of the bridge, and an owner that ages out of the snapshot before reclaim still recovers via lease expiry.

Because reclaim acts only on the `Dead` set (never `Suspected`), a transient suspect window — GC pause, thread-pool starvation, brief network blip — no longer triggers reclaim, which is the v1 duplicate-delivery footgun this design removes. One operational invariant still holds, though: set Coordination's dead threshold no lower than the largest retry `DispatchTimeout`. A node starved long enough to miss heartbeats is classified `Dead` even if it is still completing an in-flight dispatch; with `DeadThreshold >= DispatchTimeout` that node's lease has already passed its window by the time it is declared `Dead`, so reclaim's `LockedUntil > now` predicate matches nothing and the floor already owns recovery. Set it lower and reclaim can pull a still-valid lease and re-dispatch a row the original owner is still handling.

With only `NullNodeMembership` (no Coordination provider) the bridge is a benign no-op and recovery falls back to the floor — startup logs EventId 88.

| Configuration | Recovery behavior | Startup signal |
| --- | --- | --- |
| No Coordination membership (`NullNodeMembership`) | Floor-only (`LockedUntil`); the bridge is a benign no-op. | EventId 88 Information |
| Real Coordination membership | Always-on dead-owner reclaim: `NodeLeft` watch + `DeadNodeReconcileInterval` reconcile, dead-only. | None on success; bridge EventIds 1–3 on failure |

`UseStorageLock` is orthogonal to recovery — it only serializes retry *pickup* across replicas, it does not gate reclaim.

### EventIds

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 77 | `UseStorageLockWithNoOpProvider` | Warning | `UseStorageLock = true` but no real provider is registered under any key. | Wire a real provider via `MessagingBuilder.UseDistributedLock(...)`, or set `UseStorageLock = false`. |
| 78 | `UseStorageLockWithNoOpProviderButRealUnkeyed` | Warning | `UseStorageLock = true`, real provider registered un-keyed, but not flowed through `MessagingBuilder.UseDistributedLock(...)`. | Re-register the provider via `MessagingBuilder.UseDistributedLock(...)` so it lands under messaging's keyed slot. |
| 79 | `RetryLockLeaseLost` | Warning | The acquired published- or received-retry lease's `LostToken` was already canceled before pickup, or fired while pickup was in flight. | Investigate lock-store TTLs, clock skew, and auto-extension health if frequent; per-row `LockedUntil` remains the correctness boundary. |
| 81 | `PublishedRetryLockAcquireFailed` | Warning | `TryAcquireAsync` threw on the published-retry path. | Investigate lock-store health if persistent; the pickup is skipped. |
| 82 | `PublishedRetryLockAcquireFailureEscalated` | Error | Three consecutive published-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 83 | `ReceivedRetryLockAcquireFailed` | Warning | `TryAcquireAsync` threw on the received-retry path. | Investigate lock-store health if persistent; the pickup is skipped. |
| 84 | `ReceivedRetryLockAcquireFailureEscalated` | Error | Three consecutive received-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 88 | `MessagingRecoveryUsingLockedUntilFloorOnly` | Information | Only `NullNodeMembership` is registered, so dead-owner recovery falls back to the `LockedUntil` floor (independent of `UseStorageLock`). | Register a Coordination provider to enable dead-owner reclaim, or accept floor-only recovery. |
| 91 | `MessagingDeadOwnerRowsReclaimed` | Information | The dead-owner reclaimer recovered N orphaned rows (published or received) for a dead owner. | Informational — no action needed. |
| 94 | `MessagingDeadThresholdBelowDispatchTimeout` | Warning | A real Coordination membership is registered but `DeadThreshold` is below the retry `DispatchTimeout`, so a still-alive node crossing the dead threshold mid-dispatch is reclaimed and re-dispatched. | Set Coordination `DeadThreshold` >= the retry `DispatchTimeout`. |

The always-on `DeadOwnerRecoveryBridge` logs failures under its own category, `Headless.Coordination.DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>` (EventIds restart at 1 within that category):

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 1 | `MembershipWatchFailed` | Error | The `WatchAsync` event loop failed; recovery falls back to the periodic reconcile. | Investigate Coordination store health if frequent; the reconcile backstop still recovers. |
| 2 | `DeadNodeReconcileFailed` | Error | A reconcile tick failed. | Investigate Coordination/storage health if persistent; retries on the next `DeadNodeReconcileInterval`. |
| 3 | `DeadNodeReclaimFailed` | Error | A single dead owner's reclaim threw; the owner is removed from the dedup set and retried on the next reconcile. | Investigate storage health if persistent. |

### Pros and cons

- **Pros:** less wasted pickup work, cleaner logs at scale, halves backlog scan load per added replica.
- **Cons:** extra lock-store round trip per tick, extra dependency, more EventIds to monitor (79 for lease loss, 81-84 for acquire failures).

---

## Strict Publish Tenancy

`MessagingOptions.TenantContextRequired` is the messaging sibling of the EF write guard (#234) and the HTTP authorization requirement. Defaults to `false` to preserve today's behavior. When set to `true`, every publish must resolve a tenant identifier:

1. `PublishOptions.TenantId` if set (the source of truth — see `Headers.TenantId` integrity rules in [Multi-Tenancy / Message Consumers](multi-tenancy.md#message-consumers)).
2. Otherwise, the ambient `ICurrentTenant.Id`.
3. If neither resolves, the publish wrapper throws `Headless.Abstractions.MissingTenantContextException`.

The U2 raw-header checks (`ReservedTenantHeader`, `TenantIdMismatch`) still run first, so flipping `TenantContextRequired` cannot bypass them.

Root tenancy setup:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);
```

Messaging-only setup must still go through the root tenancy seam — `AddTenantPropagation()` has been removed. Combine `AddHeadlessMessaging` with the root tenancy registration:

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.TenantContextRequired = true;
});

builder.AddHeadlessTenancy(tenancy =>
    tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);
```

**Remediation for background workers / `IHostedService` callers (no ambient HTTP scope):**

```csharp
// Option A: pass the tenant explicitly
await publisher.PublishAsync(message, new PublishOptions { TenantId = tenantId }, cancellationToken);

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
    : IPublishMiddleware<PublishContext<OrderPlaced>>
{
    public ValueTask InvokeAsync(PublishContext<OrderPlaced> context, Func<ValueTask> next)
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

**Publish context rules:** `PublishContext<T>.Options` and `DelayTime` are mutable before `await next()`. After the inner publisher completes, the context is marked read-only and setters throw `InvalidOperationException`; reads still work. `PublishContext<T>.IsTransactional` is `true` only when the publish was buffered into the outbox under an ambient commit coordinator carrying a relational transaction, whose commit is the caller's responsibility.

**Cancellation token swaps:** middleware that creates per-attempt or per-operation tokens must call `context.WithCancellationToken(...)` before `await next()`. Downstream middleware must re-read `context.CancellationToken` at each await boundary; do not capture it once at method entry.

### Multi-tenancy

The framework ships built-in middleware that propagates the originating tenant on the wire:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
```

The root tenancy seam registers `TenantPropagationPublishMiddleware` (stamps `PublishOptions.TenantId` from ambient `ICurrentTenant.Id`) and `TenantPropagationConsumeMiddleware` (calls `ICurrentTenant.Change(...)` for the lifetime of the consume). Caller-set values on `PublishOptions.TenantId` are preserved verbatim — set it explicitly to override the ambient tenant. See the multi-tenancy doc's [Message Consumers](multi-tenancy.md#message-consumers) section for the trust boundary and the strict-tenancy guard.

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

## Circuit Breaker

Per-consumer-group circuit breaker that pauses transport consumption when a dependency is unhealthy, preventing message-retry storms.

**State machine:** Closed → Open (pause transport) → HalfOpen (probe) → Closed (resume) or Open (re-trip).

Open duration escalates exponentially on repeated trips and resets after consecutive successful close cycles.

### Global Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // Global circuit breaker (applies to all consumer groups)
    setup.Options.CircuitBreaker.FailureThreshold = 5; // consecutive transient failures to trip
    setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30); // initial open duration
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

## Headless.Messaging.Dashboard

Web-based dashboard for monitoring and managing distributed messaging infrastructure.

### Problem Solved

Provides real-time visibility into message processing, failures, retries, and system health through an embedded web UI for operations and troubleshooting.

### Key Features

- **Real-Time Monitoring**: Live message throughput and latency metrics
- **Message Explorer**: Search, filter, and inspect messages
- **Failure Management**: View and retry failed messages
- **Node Discovery**: Multi-instance cluster visibility through async `INodeDiscoveryProvider` operations with optional trailing cancellation tokens; implementations propagate caller-requested cancellation instead of converting it to an empty or not-found result
- **Performance Metrics**: Consumer processing stats and bottlenecks
- **Five authentication modes** (shared with the Jobs Dashboard via `Headless.Dashboard.Authentication`): none, Basic, API key, host-app auth, custom.

### Design Notes

The dashboard exposes operational endpoints for inspecting, retrying, re-executing, and deleting message records. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls. Production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, `WithApiKey(...)`, or `WithCustomAuth(...)`, and should set an explicit CORS policy before exposing the dashboard cross-origin.

### Installation

```bash
dotnet add package Headless.Messaging.Dashboard
```

### Quick Start

The dashboard is enabled on the `MessagingSetupBuilder` via `UseDashboard(...)`; it does not need an explicit `app.Use...` call:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.UseDashboard(dashboard => dashboard.WithBasicAuth("admin", password));
});
```

### Configuration

Configured through `MessagingDashboardOptionsBuilder` inside `UseDashboard(...)`. Authentication must be chosen explicitly — if no `WithXxx` auth method (including `WithNoAuth()`) is called, the host fails to start. No CORS policy is applied by default (same-origin only); use `SetCorsOrigins(...)` for the cross-origin SPA case.

| Method | Default | Description |
| --- | --- | --- |
| `WithNoAuth()` | (no default — auth is required) | Explicitly opt out of authentication; development or trusted-network use only. |
| `WithBasicAuth(username, password)` | — | HTTP Basic authentication. |
| `WithApiKey(apiKey)` | — | API-key authentication. |
| `WithHostAuthentication(policy?)` | — | Reuse the host app's auth, with an optional authorization policy. |
| `WithCustomAuth(validator)` | — | Custom `(token, services) => bool` validation. |
| `WithSessionTimeout(minutes)` | `60` | Auth session lifetime. |
| `SetBasePath(path)` | `/messaging` | Dashboard URL path. |
| `SetStatsPollingInterval(ms)` | `2000` | `/stats` endpoint polling interval. |
| `SetCorsPolicy(builder)` | none | CORS policy for cross-origin access. |

### Dependencies

- `Headless.Messaging.Core`
- `Headless.Dashboard.Authentication`
- `Consul`
- `Microsoft.AspNetCore.App` (framework reference)

### Side Effects

Mounts the embedded web UI and monitoring API through an `IStartupFilter` (no explicit middleware call required), and registers dashboard and node-discovery services.

## Headless.Messaging.Dashboard.K8s

### Problem Solved

Enables automatic discovery and monitoring of messaging nodes in Kubernetes clusters by querying Services for multi-instance dashboard visibility.

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

Configure `K8sDiscoveryOptions` through `UseK8sDiscovery(...)`:

- `K8sClientConfig` — Kubernetes client configuration used to query the cluster. Defaults to `KubernetesClientConfiguration.BuildDefaultConfig()`.
- `ShowOnlyExplicitVisibleNodes` — when `true` (default), only Services labeled `headless.messaging.visibility:show` are listed as visible dashboard nodes. Set to `false` to show all discovered Services.

### Dependencies

- `Headless.Messaging.Dashboard`
- `KubernetesClient`

### Side Effects

- Registers a Kubernetes-backed node discovery provider.
- Queries the Kubernetes API for Services and namespaces.
- Requires RBAC permissions to read Services and namespaces.

## OpenTelemetry (native, in Headless.Messaging.Core)

### Problem Solved

Spans and metrics for messaging publish, persist, consume, and subscriber-invoke flows are emitted **natively** from `Headless.Messaging.Core` via BCL `System.Diagnostics` `ActivitySource`/`Meter` primitives. There is **no** separate `Headless.Messaging.OpenTelemetry` satellite package (removed) and no `DiagnosticSource` bridge — Core references only the 80 KB, dependency-free `OpenTelemetry.Api` (for W3C context propagation and the typed provider-builder helpers), never the SDK. Any consumer subscribes: an OpenTelemetry exporter, a raw `ActivityListener`/`MeterListener`, Application Insights, or `dotnet-counters`. Cross-cutting naming, PII, and registration rules for all Headless instrumentation live in [OpenTelemetry instrumentation conventions](../solutions/conventions/opentelemetry-instrumentation-conventions.md).

### Key Features

- The Meter/ActivitySource is named `Headless.Messaging` — exposed as the `public const string MessagingDiagnostics.SourceName`.
- Typed `AddMessagingInstrumentation()` extensions on both `TracerProviderBuilder` (namespace `OpenTelemetry.Trace`) and `MeterProviderBuilder` (namespace `OpenTelemetry.Metrics`) — thin `AddSource`/`AddMeter` wrappers over the const. Subscribing by name is equally supported.
- Instrument names + standard dimensions follow the OpenTelemetry messaging **semconv** (`messaging.publish.messages`, `messaging.consume.duration`, dims `messaging.operation` / `messaging.system` / `messaging.consumer.group` / `error.type` / `messaging.subscriber` / `messaging.persistence.type`); framework-specific span attributes are bespoke `headless.messaging.*`.
- W3C `traceparent` + baggage are injected on publish headers and extracted on consume — **always on whenever any messaging telemetry is enabled**, no toggle. A metrics-only service (meter subscribed, no trace listener) — or a sampled-out publish — **relays** the incoming/ambient parent context verbatim onto outgoing messages instead of dropping it, so trace continuity survives non-tracing hops; a consumed message's context flows to publishes made from its handler even without a span. A fully unobserved host (no listeners at all) pays nothing and forwards nothing. The framework never fabricates a root: relay happens only when a parent actually exists. The app's OpenTelemetry setup must assign `Propagators.DefaultTextMapPropagator` (the standard `AddOpenTelemetry().WithTracing()` does this).
- `IActivityTagEnricher` extension point, invoked **synchronously at span start** (`void Enrich(Activity activity, in MessagingEnrichmentContext context)`), with per-enricher exception isolation.

### Design Notes

- Metrics are always registered; **subscribing a meter is the toggle** — there is no `EnableMetrics` flag. Emission is near-free when unobserved (`ActivitySource.HasListeners()` / `Counter.Enabled` early-outs).
- Enricher registration and the built-in suppression toggles live on the **messaging setup builder** (`setup.Instrumentation`), not at OpenTelemetry-registration time. This is what fixes the old bridge's fire-and-forget async-enricher wart: enrichers run synchronously, so every tag they add is attached before the span can end.
- **PII guardrails.** Enrichers must not write the reserved namespaces `messaging.*`, `server.*`, `headless.messaging.*`, `exception.*` (the framework/SDK overwrite them). `headless.messaging.tenant_id` is suppressible. Never serialize raw `context.Headers` onto tags — they may carry tokens/PII.

### Span attributes and toggles

| Tag / attribute | Emitted by | Toggle |
| --- | --- | --- |
| `headless.messaging.intent` (`bus`/`queue`) + `messaging.destination.kind` | built-in `IntentTagEnricher` | `setup.Instrumentation.SuppressIntentTags` |
| `headless.messaging.tenant_id` | built-in `TenantIdTagEnricher` | `setup.Instrumentation.SuppressTenantIdTag` |
| `headless.messaging.retry_count` | built-in `RetryCountTagEnricher` (subscriber-invoke) | `setup.Instrumentation.SuppressRetryCountTag` |
| custom tags | your `IActivityTagEnricher` | `setup.Instrumentation.AddEnricher(...)` |

### Quick Start

```csharp
// 1. Register enrichers / suppression on the messaging setup builder (optional).
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(/* ... */);
    setup.Instrumentation.SuppressTenantIdTag = true;      // opt out of tenant-id tagging
    setup.Instrumentation.AddEnricher(new MyTagEnricher()); // custom tags
});

// 2. Subscribe the messaging scope on your OpenTelemetry providers.
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddMessagingInstrumentation())
    .WithMetrics(metrics => metrics.AddMessagingInstrumentation());
```

### Instrument reference

All instruments register on the `Headless.Messaging` meter. Names and standard dimensions follow the OTel messaging semantic conventions; span attributes specific to the framework are namespaced `headless.messaging.*`.

| Instrument | Kind | Dimensions |
| --- | --- | --- |
| `messaging.publish.messages` | Counter | `messaging.operation`, `messaging.system` |
| `messaging.publish.errors` | Counter | `messaging.operation`, `messaging.system`, `error.type` |
| `messaging.publish.duration` | Histogram (ms) | `messaging.operation`, `messaging.system` |
| `messaging.consume.messages` | Counter | `messaging.operation`, `messaging.system`, `messaging.consumer.group` |
| `messaging.consume.errors` | Counter | `messaging.operation`, `messaging.system`, `error.type`, `messaging.consumer.group` |
| `messaging.consume.duration` | Histogram (ms) | `messaging.operation`, `messaging.system`, `messaging.consumer.group` |
| `messaging.subscriber.invocations` | Counter | `messaging.subscriber`, `messaging.operation` |
| `messaging.subscriber.errors` | Counter | `messaging.subscriber`, `messaging.operation`, `error.type` |
| `messaging.subscriber.duration` | Histogram (ms) | `messaging.subscriber`, `messaging.operation` |
| `messaging.persistence.duration` | Histogram (ms) | `messaging.operation`, `messaging.persistence.type` |
| `messaging.message.size` | Histogram (bytes) | `messaging.operation`, `messaging.system` |

Framework span attributes: `headless.messaging.intent` (`bus`/`queue`), `headless.messaging.tenant_id` (suppressible), `headless.messaging.retry_count` (suppressible), plus per-phase duration attributes (`headless.messaging.persistence.duration_ms`, `send.duration_ms`, `receive.duration_ms`, `invoke.duration_ms`) retained verbatim from the pre-migration bridge.

## Headless.Messaging.Aws

### Problem Solved

Provides AWS SNS bus transport and AWS SQS queue transport.

### Key Features

- `setup.UseAws(...)`.
- SNS topics for bus publishing.
- SQS queues for queue delivery.
- FIFO topic/queue support.
- Producer hatch: `UseAws(aws => aws.MessageGroupId(message => ...))`.
- Consumer startup honors host cancellation through SNS/SQS provisioning and subscription.

### Design Notes

`MessageGroupId(...)` is producer-side only because it is stamped while publishing. The provider maps it to native FIFO `MessageGroupId`; it is not a custom message attribute. Values longer than 128 characters are rejected.

Malformed SNS envelopes are released with a 3-second visibility timeout. Headless does not provision a dead-letter queue or redrive policy; configure SQS redrive externally to bound repeated malformed deliveries.

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

setup.ForMessage<OrderPlaced>(message =>
    message
        .MessageName("orders-placed.fifo")
        .UseAws(aws => aws.MessageGroupId(order => order.CustomerId.ToString()))
        .OnQueue<OrderWorker>()
);
```

### Configuration

Configure AWS region, service URLs, and credentials through `AmazonSqsMessagingOptions`.

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
- Consumer startup honors host cancellation through client, topology, and processor setup.
- Shared connection: bus and queue publishing and consumer processors share one `ServiceBusClient` (one AMQP connection) per namespace with per-destination cached senders and a shared administration client; senders are drained before the client on shutdown, and consumers stop their processors without touching the shared client.

### Design Notes

`PartitionKey(...)` is producer-side only and limited to 128 characters. When sessions are enabled, Azure Service Bus requires `PartitionKey` to equal `SessionId`; the message builder rejects mismatches.

Headless disables Azure SDK auto-complete internally and settles messages explicitly after durable receive storage and handler outcome.

### Installation

```bash
dotnet add package Headless.Messaging.AzureServiceBus
```

### Quick Start

```csharp
setup.UseAzureServiceBus(options => options.ConnectionString = connectionString);

setup.ForMessage<OrderPlaced>(message =>
    message.UseAzureServiceBus(asb => asb.PartitionKey(order => order.CustomerId.ToString())).OnBus<OrderProjection>()
);
```

### Configuration

Configure connection string or namespace, retry/client settings, queue/topic behavior, session support, and SQL filters through `AzureServiceBusMessagingOptions`. Authentication is an either/or contract: supply either `ConnectionString` or both `Namespace` and `TokenCredential` — both are nullable (`string?`) and the validator enforces that exactly one mode is configured at start. Processor settlement is not configurable; Headless disables Azure SDK auto-complete and completes or abandons messages explicitly.

### Dependencies

Azure.Messaging.ServiceBus, `Headless.Messaging.Core`.

### Side Effects

Registers a shared client pool (one `ServiceBusClient` per namespace, shared by the bus and queue transports and every consumer client, plus a shared administration client), transports, consumer client factory, and producer descriptor services.

## Headless.Messaging.InMemory

### Problem Solved

Provides in-process bus and queue transport for local development and tests.

### Key Features

- `setup.UseInMemory()`.
- In-process bus and queue delivery.
- No external broker.
- Consumer startup implements the same host-cancellable contract as broker-backed providers.

### Installation

```bash
dotnet add package Headless.Messaging.InMemory
```

### Quick Start

```csharp
setup.UseInMemory();
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

InMemoryStorage uses its injected `TimeProvider` for both application-scheduled `NextRetryAt` and authoritative lease ownership. It implements the same duration-based lease SPI and returns the persisted `(LockedUntil, Owner)` identity.

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
- Consumer startup honors host cancellation while creating topics and subscriptions.

### Design Notes

Kafka is queue-intent only in this package. `PartitionBy(...)` maps to the Kafka key. The framework does not impose a Kafka key length cap; broker/client configuration owns practical limits. Delivery remains at-least-once; consumers must dedupe by business key or message id. A publish succeeds only when Kafka reports `Persisted`; `PossiblyPersisted` is retried and can therefore produce duplicates. When consumer concurrency is greater than one, successful handlers can finish out of order, but Kafka commits advance only through the contiguous completed offset watermark per partition; a completed high offset does not commit past lower in-flight offsets. Rebalances invalidate tracked offsets for revoked or lost partitions so late handlers cannot commit or seek partitions now owned by another consumer.

### Installation

```bash
dotnet add package Headless.Messaging.Kafka
```

### Quick Start

```csharp
setup.UseKafka(options => options.Servers = "localhost:9092");

setup.ForMessage<OrderPlaced>(message =>
    message
        .MessageName("orders.placed")
        .UseKafka(kafka => kafka.PartitionBy(order => order.CustomerId.ToString()))
        .OnQueue<OrderWorker>(consumer =>
            consumer.UseKafka(kafka => kafka.IsolationLevel(IsolationLevel.ReadCommitted))
        )
);
```

### Configuration

Configure bootstrap servers, main Kafka config, topic options, custom headers, and retriable error codes through `KafkaMessagingOptions`. `RetriableErrorCodes` / `DefaultRetriableErrorCodes` are `int` values of Confluent's `ErrorCode` enum (not the native enum type), so configuring retries needs no compile-time `Confluent.Kafka` reference; the framework casts back to `ErrorCode` internally.

### Dependencies

Confluent.Kafka, `Headless.Messaging.Core`.

### Side Effects

Registers Kafka transports, connection pool, consumer factory, and provider-specific message/consumer config support.

## Headless.Messaging.Nats

### Problem Solved

Provides a NATS JetStream transport for Headless messaging so applications can publish and consume durable messages with subject-based routing, JetStream acknowledgements, and provider-specific shard subjects while keeping the core messaging API provider-neutral.

### Key Features

- `setup.UseNats(...)`.
- Stream auto-creation and durable consumers.
- Producer hatch: `UseNats(nats => nats.SubjectShard(message => ...))`.
- Consumer hatch: `consumer.UseNats(nats => nats.Sharded())`.
- Consumer startup honors host cancellation while connecting and provisioning JetStream topology, while preserving configured topology timeouts.

### Design Notes

`SubjectShard(...)` appends one safe subject token to the logical message name. It rejects `.`, `*`, `>`, whitespace, and control characters so payload values cannot change the subject hierarchy or wildcard behavior.

Shard symmetry is required: when a message uses `SubjectShard(...)` on the producer side, every consumer registered for that message must call `.UseNats(c => c.Sharded())` on its consumer registration. This is validated at startup and throws `InvalidOperationException` if violated. The reason: NATS delivers zero messages with no error when a FilterSubject does not match any shard subject — the asymmetry causes silent data loss that is otherwise very difficult to diagnose.

Connection-specific failures (`NatsConnectionFailedException`, `NatsJSConnectionException`, or a `NatsException` wrapping `SocketException`/`IOException`) terminate the listener instead of retrying in place, so the supervising consumer register's health watchdog can replace the failed client. JetStream protocol, timeout, API, and other consumer errors retry per-subject with backoff. As a backstop, a run of `NatsMessagingOptions.MaxConsecutiveConsumeFailures` (default `10`) consecutive consume-loop failures of any exception type also terminates the listener for a supervised restart — bounding in-place spinning when a permanently dead connection surfaces an error that is not one of the classified connection-failure types (consumer connections set `MaxReconnectRetry = 0` and never reconnect on their own). The streak resets on any forward progress (a successful consumer bind or fetch). Consumer connection faults are owned by the health watchdog, not the per-message circuit breaker, which never observes connection-level failures. `NatsMessagingOptions.ConnectionPoolSize` defaults to `1` — a single connection multiplexes all publishers, so raise it only as a throughput knob. During host shutdown, NATS bounds its in-flight handler drain by the remaining shared `MessagingOptions.ShutdownTimeout` budget instead of starting an independent 30-second drain.

### Installation

```bash
dotnet add package Headless.Messaging.Nats
```

### Quick Start

```csharp
setup.UseNats(options => options.Servers = "nats://localhost:4222");

setup.ForMessage<OrderPlaced>(message =>
    message
        .UseNats(nats => nats.SubjectShard(order => order.CustomerId.ToString()))
        .OnBus<OrderProjection>(consumer => consumer.UseNats(nats => nats.Sharded()))
);
```

### Configuration

Configure NATS servers, credentials, stream behavior, durable names, and connection settings through `NatsMessagingOptions`.

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
- Consumer startup honors host cancellation while acquiring the client and subscribing, while preserving configured timeouts.

### Installation

```bash
dotnet add package Headless.Messaging.Pulsar
```

### Quick Start

```csharp
setup.UsePulsar(options => options.ServiceUrl = "pulsar://localhost:6650");
```

### Configuration

Configure service URL, authentication, and TLS through `PulsarMessagingOptions`.

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
- Consumer startup threads host cancellation through connection, channel, exchange, queue, and binding operations.

### Design Notes

RabbitMQ currently exposes consumer-side QoS only in this cluster. Publish routing still follows the logical message name because subscription topology binds queues by logical message name. When `PublishConfirms` is enabled, publish completion awaits the broker acknowledgement or negative acknowledgement.

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
    options.UserName = "app_user"; // required
    options.Password = "app_secret"; // required
});

setup.ForMessage<OrderPlaced>(message =>
    message.OnBus<OrderProjection>(consumer => consumer.UseRabbitMq(rabbit => rabbit.PrefetchCount(20)))
);
```

### Configuration

Configure host, credentials, exchange, queue arguments, QoS defaults, and custom headers through `RabbitMqMessagingOptions`. `UserName` and `Password` are `required` and must be set explicitly; the validator rejects the RabbitMQ default `guest`/`guest` credentials for production safety.

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
- Streams and Pub/Sub consumer startup honor host cancellation through connection, provisioning, and subscription.

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

Fresh dispatch and retry pickup accept a lease duration, atomically compare and stamp ownership from one PostgreSQL clock snapshot, and return the persisted `(LockedUntil, Owner)` identity for fenced writes.

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

- **`DdlCommandTimeout`** (`TimeSpan?`, default `null`): timeout budget for schema-init DDL — the `CREATE INDEX CONCURRENTLY` / `DROP INDEX CONCURRENTLY` builds, the `CREATE EXTENSION` probe, and the advisory-lock waits that gate them. Decoupled from the OLTP `MessagingOptions.CommandTimeout` (~30s) because these can run for minutes-to-hours on a large table; a premature kill leaves a `CONCURRENTLY` index `INVALID` for the next boot to repair. Default `null` (and `TimeSpan.Zero`) mean **no timeout** (wait indefinitely). A negative value is rejected at validation time.
- **`pg_trgm` on managed PostgreSQL**: dashboard content (ILIKE) search uses GIN trigram indexes that need the `pg_trgm` extension. The initializer runs `CREATE EXTENSION IF NOT EXISTS pg_trgm` best-effort **outside** the schema transaction. On managed PostgreSQL (AWS RDS, Azure, Neon, Supabase) the app role usually lacks `CREATE EXTENSION`; it logs a warning, **skips the trigram content indexes**, and continues — write/retry paths are unaffected, only dashboard content search is disabled until a DBA pre-installs `pg_trgm`. (Previously `CREATE EXTENSION` ran as the first statement of the schema transaction, so a permission error rolled back the entire schema batch and left messaging dead at startup.)
- **Bootstrap indexes**: fresh schemas directly create `("StatusName","Added")` indexes for dashboard timelines/statistics and a partial `("Version","ExpiresAt") WHERE "StatusName" = 'Queued'` index for delayed-message scheduling. The initializer is schema bootstrap, not a migration runner, so it does not alter legacy columns or drop superseded indexes.

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

Fresh dispatch and retry pickup accept a lease duration, atomically compare and stamp ownership from one SQL Server clock snapshot, and return the persisted `(LockedUntil, Owner)` identity for fenced writes.

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

Fresh schemas directly create `([StatusName],[Added])` indexes for dashboard timelines/statistics. The initializer creates the final schema shape and does not carry legacy migration DDL.

### Dependencies

Microsoft.Data.SqlClient, EF Core provider packages, `Headless.Messaging.Core`.

### Side Effects

Registers SQL Server storage, monitoring API, storage initializer, and transaction integration.

## Headless.Messaging.Testing

### Problem Solved

Provides test harness utilities for observing messaging behavior without coupling tests to provider internals.

### Key Features

- `MessagingTestHarness` records messages at the bus/queue transport layer.
- `WaitForPublished<T>(...)`, `WaitForConsumed<T>(...)`, `WaitForFaulted<T>(...)`, and `WaitForExhausted<T>(...)` block until a match arrives or the timeout elapses.
- `WaitForPublished<T>(IntentType.Bus)` and `WaitForPublished<T>(IntentType.Queue)` distinguish identical payloads sent through bus and queue paths.
- Predicate overloads for filtering by payload shape.
- `TestConsumer<T>` captures messages without custom handler logic.

### Design Notes

Use the testing package for application tests that need to assert published messages or consumed messages. Provider conformance still belongs in provider-specific or shared harness tests.

### Installation

```bash
dotnet add package Headless.Messaging.Testing
```

### Quick Start

```csharp
services.AddMessagingTestHarness();

var harness = provider.GetRequiredService<MessagingTestHarness>();
await harness.WaitForPublished<OrderPlaced>(TimeSpan.FromSeconds(5));
```

### Configuration

None. `MessagingTestHarness` has no configuration class or options object. The per-call `timeout` parameter controls how long `WaitFor*` methods wait; when omitted it defaults to `MessagingTestHarness.DefaultTimeout`.

### Dependencies

- `Headless.Messaging.Core`
- `Headless.Messaging.InMemory`
- `Headless.Messaging.InMemoryStorage`

### Side Effects

- `CreateAsync(...)` builds and owns a test `ServiceProvider`; dispose the harness after each test.
- `AddMessagingTestHarness()` decorates the host's existing messaging registrations with recording wrappers; call it after `AddHeadlessMessaging(...)`.
- Transport parallelism is disabled inside the harness for deterministic test execution.
