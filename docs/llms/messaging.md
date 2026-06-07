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
- Use `CorrelationFrom(...)` for payload-derived correlation. Precedence is explicit publish option, message selector, ambient consume context, then generated message id.
- Never write framework metadata through provider hatches. For publish options, use typed properties; raw `Headers.TenantId` is accepted only by the legacy tenant-integrity path and should not be authored directly.
- Treat provider hatches as physical broker routing/configuration. Producer-side hatches live on `IMessageBuilder<TMessage>`; consumer-side hatches live on `IBusConsumerBuilder<TConsumer>` or `IQueueConsumerBuilder<TConsumer>` only when that provider currently exposes consumer settings.
- Kafka and RabbitMQ currently expose consumer-side hatches. AWS, Azure Service Bus, and NATS currently expose producer-side hatches only.
- Keep `docs/llms/messaging.md` and package READMEs aligned when public messaging behavior changes.

## Core Concepts

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
| NATS | Yes | Yes | `SubjectShard(...)` | None |
| Pulsar | Yes | Yes | None | None |
| RabbitMQ | Exchange | Queue | None | `PrefetchCount(...)` |
| Redis | Pub/Sub | Queue-like Redis transport | None | None |

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

### Design Notes

`SubjectShard(...)` appends one safe subject token to the logical message name. It rejects `.`, `*`, `>`, whitespace, and control characters so payload values cannot change the subject hierarchy or wildcard behavior.

### Installation

```bash
dotnet add package Headless.Messaging.Nats
```

### Quick Start

```csharp
setup.UseNats(options => options.Servers = "nats://localhost:4222");

setup.ForMessage<OrderPlaced>(message => message
    .UseNats(nats => nats.SubjectShard(order => order.CustomerId.ToString()))
    .OnBus<OrderProjection>());
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
