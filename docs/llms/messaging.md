---
domain: Messaging
packages: Messaging.Abstractions, Messaging.Bus.Abstractions, Messaging.Queue.Abstractions, Messaging, Messaging.SourceGenerator
---

# Messaging

> Durable publish and consume: typed Bus and Queue APIs, compile-time consumer registration, a storage-backed outbox, inbox, and retries, provider transports, request/reply, telemetry, and a test harness.

## Orientation

Install `Headless.Messaging`, exactly one transport, and exactly one storage. `IBus.PublishAsync` sends on the Bus lane (broadcast: one copy per consumer identity). `IQueue.EnqueueAsync` sends on the Queue lane (point-to-point: one consumer per message). Message types are plain classes, records, or interfaces with no Headless reference. A host that bridges a second broker adds it as a [named transport](messaging/transports.md#named-transports).

Registration has three parts:

1. A consumer class declares itself with `[BusConsumer(identity)]` or `[QueueConsumer(identity)]` and implements `IConsume<T>` for each message it handles.
2. The Messaging source generator emits one `MessagingModule` per assembly. The module that owns the consumers contributes it, with its message contracts, through `services.ConfigureMessaging(...)`.
3. The host calls `AddHeadlessMessaging(...)` once for transport, storage, and options, and tunes consumers by identity.

### Default setup

```csharp
using Microsoft.EntityFrameworkCore;

// Orders assembly: message, consumer, and the module entry point.
public sealed record OrderPlaced(Guid OrderId);

[BusConsumer(Identity)]
public sealed class OrderProjection(IOrderReadModel readModel) : IConsume<OrderPlaced>
{
    public const string Identity = "orders.projection";

    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        readModel.ApplyAsync(context.Message, cancellationToken);
}

public static class OrdersMessaging
{
    public static IServiceCollection AddOrders(this IServiceCollection services)
    {
        services.ConfigureMessaging(messaging =>
        {
            messaging.AddModule<Orders.MessagingModule>(); // generated for the Orders assembly
            messaging.Message<OrderPlaced>("orders.placed").CorrelateBy(order => order.OrderId.ToString());
        });

        return services;
    }
}

// Host: one AddHeadlessMessaging call, then the modules, in any order.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("App"))
);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });
    // EF-backed storage declares the Transactional inbox guarantee that durable consumers require by default.
    setup.UsePostgreSqlEntityFramework<AppDbContext>();

    setup.Tune(OrderProjection.Identity, consumer => consumer
        .Concurrency(4)
        .UseRabbitMq(rabbit => rabbit.PrefetchCount(20)));
});

builder.Services.AddOrders();
```

This is the production default: `Headless.Messaging.RabbitMq` for the transport and `Headless.Messaging.Storage.PostgreSql.EntityFramework` for storage over the application's `DbContext`. The consumer identity (`orders.projection`) is the consumer's durable name: the Bus subscription name, and the key of its inbox rows, circuit breaker, metrics, and tuning. A host without EF Core uses the raw `UsePostgreSql(...)` or `UseSqlServer(...)` storage, which declares the same `Transactional` inbox guarantee; see [Raw storage handlers](messaging/storage.md#raw-storage-handlers).

Deploy the RabbitMQ topology and the storage tables with `resources provision` before the host serves traffic. The running host only checks that they exist.

**Verify:** after `resources provision`, the `messaging`, `messaging-broker`, and `messaging-storage-postgresql-default` readiness checks report `Healthy`, the `warmup` readiness check reports `Healthy` once the topology check passes, and `describe` prints a `messaging` section that lists `orders.projection` with `runState: runs-here`. See [Health checks](messaging/operations.md#health-checks) and [Host manifest](messaging/operations.md#host-manifest).

## Agent Rules

- **Install one transport and one storage.** Startup fails with zero or several storage providers. `Headless.Messaging` brings the Bus, Queue, and shared abstractions with it.
- **Use `InMemory` and `InMemoryStorage` only for development and tests.** All state is lost on restart, and the in-memory storage cannot satisfy a durable inbox guarantee.
- **Libraries reference only the abstractions.** A library that declares contracts, publishes, or ships consumers calls `services.ConfigureMessaging(...)` from its own `Add{Module}` entry point and never calls `AddHeadlessMessaging`. `ConfigureMessaging`, `Message<T>`, `AddModule<T>`, and `IMessagingModule` live in `Headless.Messaging.Abstractions`. `Tune` needs `Headless.Messaging`. A library that declares consumers still needs the source generator at build time, which `Headless.Messaging` carries.
- **Declare each consumer with exactly one lane attribute.** Put `[BusConsumer("owner.name")]` or `[QueueConsumer("owner.name")]` on a class that implements `IConsume<T>`. No fluent call declares a consumer, and nothing scans assemblies at run time. See [Consumers](messaging/consumers.md#consumer-identity-and-lanes).
- **Treat the consumer identity as durable.** It is at most 200 characters in `owner.name` form, and it keys the Bus subscription, the inbox rows, the circuit breaker, metrics, `Tune`, and configuration. Renaming it starts a new consumer with no inbox history.
- **Give a message at most one Queue consumer.** A second one fails the build (HM004) in one assembly and fails startup across assemblies. Use `[BusConsumer]` for fan-out.
- **Contribute with `ConfigureMessaging`, never a second `AddHeadlessMessaging`.** Nothing registers until some call adds the generated module. Adding a module twice registers it once. Conflicting contributions fail startup naming both sources.
- **Declare a message contract once, for both lanes.** `m.Message<T>(name, version = "1")` sets the name and schema version on both lanes. Share one declaration method between modules: selectors compare by delegate, so two identical lambdas conflict.
- **Never put a secret in a message payload.** A payload is stored in the published and received tables for the retention window (`SucceedMessageExpiredAfter` one day, `FailedMessageExpiredAfter` 15 days, inbox generations 30 days), re-sent by retries and operator actions, copied to the broker, and shown in full by the dashboard. Send a reference, such as the user ID and the purpose, and let the consumer generate or resolve the secret (a one-time code, a reset token) right before it calls the sender. This covers passwords, one-time codes, reset or invitation tokens, API keys, and connection strings. Messaging has no payload encryption; do not build one for this.
- **The receiver decides enlistment.** `IBus` and `IQueue` never join a unit of work: their rows survive the caller's rollback. `unit.Outbox.PublishAsync(...)` writes inside the unit's transaction and is discarded on rollback. No option moves a publish between them. See [Delivery Modes](messaging/publishing.md#delivery-modes).
- **Publish from a consumer through the attempt's unit.** Call `context.GetRequiredUnitOfWork().Outbox.PublishAsync(...)`, so the message commits with the inbox row. An `IBus` publish from a handler that then fails is sent again on the retry. See [Transactional inbox](messaging/storage.md#transactional-inbox).
- **Make consumers idempotent.** Delivery is at least once. Deduplicate by business key or message ID.
- **A consumer retries by its failure policy, not by `RetryPolicy`.** Without one, a competing consumer retries 2 times at once, then 5 times from 30 seconds capped at 15 minutes. `MessagingOptions.RetryPolicy.RetryStrategy` and `MaxPersistedRetries` govern publishing only. See [Retries](messaging/retries.md#consumer-failure-policy).
- **Order by key with `Sequential()`.** `Tune(identity, c => c.Concurrency(n).Sequential())` runs the messages that share a `RoutingAffinityKey` one at a time in admission order, on every transport. Without it, `Concurrency` above 1 or `ConsumerThreadCount` above 1 gives no order. See [Ordering](messaging/ordering.md#sequential-consumers).
- **Use the messaging abstractions, not a broker client.** Do not consume with `RabbitMQ.Client`, `Confluent.Kafka`, or another raw client. Consume a non-Headless producer's messages through a `RawPayload()` contract; see [Foreign producers](messaging/foreign-producers.md#foreign-producers-and-raw-payloads). An app that also uses NATS directly hands its connection to Headless with `NatsMessagingOptions.UseConnection(...)`.
- **Set explicit RabbitMQ credentials.** The options validator rejects `guest`/`guest`.
- **Kafka is Queue-only.** A `[BusConsumer]` on Kafka fails startup, and an `IBus` publish fails when attempted.
- **Request/reply needs a supporting transport.** Only InMemory, RabbitMQ, NATS, and Redis support `IRequestClient`. A host on another transport that sends requests or declares a responder fails startup. See [Request/reply](messaging/request-reply.md#requestreply).
- **Configure tenancy through `AddHeadlessTenancy`.** `tenancy.Messaging(m => m.PropagateTenant().RequireTenantOnPublish())` stamps and enforces the tenant. Set `PublishOptions.TenantId` or the ambient tenant. Never write `Headers.TenantId`. See [Strict Publish Tenancy](messaging/publishing.md#strict-publish-tenancy).
- **Choose a dashboard authentication mode before production.** The Messaging dashboard module shows every payload to anyone its view policy admits. See [Dashboard](messaging/dashboard.md#headlessmessagingdashboard).
- **Test through the harness.** In a test host that registers a broker, call `ReplaceMessagingTransportWithInMemory()`, then `AddMessagingTestHarness()`, and assert with `WaitFor*`. See [Test harness](messaging/test-harness.md#headlessmessagingtesting).

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| InMemory | Local development, unit tests, demos | Production durability or multi-process delivery | No external dependency, no durability |
| RabbitMQ | General-purpose broker, routing keys, queue semantics | Strict partitioned ordering across large streams | Topology is deployed with `resources provision`; ordering per key needs `Sequential()` |
| Kafka | Ordered partitions, high-throughput streams | Bus (broadcast) semantics | Queue-only; no request/reply |
| Azure Service Bus | Azure-hosted topics and queues, sessions | Non-Azure deployments | `PartitionKey` at most 128 characters and must equal `SessionId` with sessions; no request/reply yet |
| AWS SNS/SQS | AWS-native pub-sub and queue workloads | Non-AWS deployments, every-instance consumers | FIFO destinations use `MessageGroupId`; no request/reply |
| NATS | Subject-based routing, JetStream | Per-consumer storage-specific routing | Subject shards must be one safe token |
| Redis | Redis Streams already in the stack | Workloads that need a broker dead-letter queue | Pending entries reclaimed after 60 s; streams trimmed by age (7 days) |

| Storage | Inbox guarantee | Use when |
| --- | --- | --- |
| `UsePostgreSqlEntityFramework<TContext>()`, `UseSqlServerEntityFramework<TContext>()` | `Transactional` | The app uses EF Core and publishes atomically with its writes |
| `UsePostgreSql(...)`, `UseSqlServer(...)` | `Transactional` | The app uses raw ADO.NET or Dapper |
| `UseInMemoryStorage()` | `ProcessLocal` | Development and tests only |

[Transports](messaging/transports.md#provider-capabilities) compares lanes, hatches, every-instance support, and request/reply per provider. [Storage](messaging/storage.md#choosing-storage) covers the inbox guarantees.

## Topics

| Read when | Topic |
| --- | --- |
| You declare a consumer, a message contract, or a module, tune a consumer, use `ConsumeOnly`, every-instance delivery, or a runtime subscription. | [Consumers](messaging/consumers.md) |
| You choose `Durable` or `Direct`, publish atomically through `unit.Outbox`, delay, schedule, revoke, or expire a message, set headers or correlation, or enforce a tenant on publish. | [Publishing](messaging/publishing.md) |
| You choose storage, write from a consumer inside the inbox transaction, set the schema, or add an outbox for another database. | [Storage](messaging/storage.md) |
| You change how a consume or publish failure retries, handle `OnTerminal`, configure the circuit breaker, or wire the retry lock. | [Retries and failures](messaging/retries.md) |
| You need per-key ordering, need to know what a broker orders, or throttle a consumer. | [Ordering and rate limits](messaging/ordering.md) |
| You add publish, receive, or consume middleware, or propagate the tenant. | [Middleware](messaging/middleware.md) |
| A caller must await a typed answer from another service. | [Request/reply](messaging/request-reply.md) |
| A non-Headless producer publishes the messages, or you parse a foreign JSON body. | [Foreign producers](messaging/foreign-producers.md) |
| You compare transports, need broker object names, routing affinity, topology permissions, a second broker in one host (named transports), or the in-memory transport. | [Transports](messaging/transports.md) |
| You use AWS SNS/SQS. | [AWS](messaging/aws.md) |
| You use Azure Service Bus. | [Azure Service Bus](messaging/azure-service-bus.md) |
| You use Kafka. | [Kafka](messaging/kafka.md) |
| You use NATS JetStream, declare streams, or consume external subjects. | [NATS](messaging/nats.md) |
| You use RabbitMQ, dead-lettering, or quorum queues. | [RabbitMQ](messaging/rabbitmq.md) |
| You use Redis Streams. | [Redis](messaging/redis.md) |
| You write a transport for another broker. | [Custom transport](messaging/custom-transport.md) |
| You write a storage provider for another database. | [Custom storage](messaging/custom-storage.md) |
| You wire readiness, read the host manifest, operate on stored messages, or tune history retention. | [Operations](messaging/operations.md) |
| You export traces or metrics, add a tag enricher, or alert on the backlog. | [Observability](messaging/observability.md) |
| You add the Messaging dashboard module or node discovery. | [Dashboard](messaging/dashboard.md) |
| You test code that publishes or consumes. | [Test harness](messaging/test-harness.md) |

---

## Headless.Messaging.Abstractions

The shared contracts: `IConsume<T>`, the consumer attributes, `ConsumeContext`, `MessageOptions`, `unit.Outbox`, `RawJson`, and the contribution surface (`ConfigureMessaging`). Reference it directly from a library that declares contracts or consumers without bootstrapping messaging.

### Setup

```bash
dotnet add package Headless.Messaging.Abstractions
```

```csharp
[BusConsumer("orders.projection")]
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

A consumer that publishes a follow-up message publishes it through the attempt's unit, so the message commits with the inbox row and a retried attempt does not send it twice:

```csharp
[QueueConsumer("orders.fulfillment")]
public sealed class OrderFulfillmentConsumer : IConsume<OrderPlaced>
{
    public async ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        var unit = context.GetRequiredUnitOfWork();

        await unit.Outbox.PublishAsync(new OrderChanged(context.Message.OrderId), cancellationToken);
    }
}
```

A library that ships consumers exposes an `Add{Module}` entry point that adds its generated module; see [Orientation](#orientation).

**Verify:** a consumer that the module does not add is never called. After the host starts, `describe` lists the consumer identity in its `messaging` section.

### API

- `IConsume<TMessage>` with exactly one of `[BusConsumer(identity)]` (optional `EveryInstance`, `FailurePolicy`) or `[QueueConsumer(identity)]` (optional `FailurePolicy`). `IOnSubscriptionEstablished` is the reconnect hook of an every-instance consumer. See [Consumers](messaging/consumers.md).
- `ConsumeContext.UnitOfWork` is the unit the inbox transaction enlisted the attempt in, or `null` under a weaker inbox guarantee and for an every-instance consumer. `ConsumeContext.GetRequiredUnitOfWork()` returns it or throws `InvalidOperationException` naming the setting that removed it.
- `IRespond<TRequest, TResponse>` is the request/reply responder contract on a `[QueueConsumer]`. See [Request/reply](messaging/request-reply.md#responders).
- `MessageOptions` is the shared base of `PublishOptions`, `QueueOptions`, and `OutboxOptions`: headers, correlation, causation, `Delay` or `ScheduledAt`, `DeliverWithin`, message ID, tenant, and `RoutingAffinityKey`. See [Publishing](messaging/publishing.md#publish-options-and-receipts).
- `unit.Outbox` (an extension property on `IUnitOfWork`, namespace `Headless.UnitOfWork`) publishes inside the unit's transaction. It throws `InvalidOperationException` naming `AddHeadlessMessaging` when the host registered no messaging. See [Delivery Modes](messaging/publishing.md#delivery-modes).
- `IMessageRevoker` deletes a scheduled row before its first dispatch. See [Scheduling and revocation](messaging/publishing.md#scheduling-and-revocation).
- `RawJson` holds a body exactly as produced. See [Consume the body as raw JSON](messaging/foreign-producers.md#consume-the-body-as-raw-json).
- `services.ConfigureMessaging(...)` with `Message<T>(name, version)`, `AddModule<T>()`, and the generated-module contract `IMessagingModule`. See [Modules and registration](messaging/consumers.md#modules-and-registration).

Framework-owned headers are reserved: a custom header with a reserved name throws at the publish call. See [Envelope, headers, and correlation](messaging/publishing.md#envelope-headers-and-correlation).

## Headless.Messaging.Bus.Abstractions

`IBus`, the autonomous Bus publisher, with `PublishOptions`, `PublishOptionsBuilder`, and the callback overload. A library references it to publish without bootstrapping messaging.

### Setup

```bash
dotnet add package Headless.Messaging.Bus.Abstractions
```

```csharp
using Headless.Messaging;

public sealed class OrderEvents(IBus bus)
{
    public Task PublishAsync(OrderPlaced message, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, cancellationToken);
    }

    public Task PublishWithMetadataAsync(OrderPlaced message, string correlationId, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}

public sealed record OrderPlaced(Guid OrderId);
```

**Verify:** `PublishAsync` returns a `PublishReceipt` with a non-null `StorageId` under the default `Durable` mode. In a test, `MessagingTestHarness.WaitForPublishedAsync<T>(MessageLane.Bus)` returns the recorded message.

### Behavior

- `IBus` is a singleton that never joins a unit of work. The default `Durable` mode stores the row first, standalone, and the relay dispatches it: the row survives a rollback of any unit open in the calling scope.
- Acceptance waits for storage, not for any consumer. Restart survival needs persistent storage.
- Every Bus publish carries `MessageLane.Bus` through storage, tracing, the dashboard, and the consume context.
- Options, builders, receipts, and delivery-mode precedence: [Publish options and receipts](messaging/publishing.md#publish-options-and-receipts). The package registers no services; runtime wiring comes from `Headless.Messaging`.

## Headless.Messaging.Queue.Abstractions

`IQueue`, the autonomous Queue publisher, with `QueueOptions` and `QueueOptionsBuilder`, and `IRequestClient`, the request/reply caller.

### Setup

```bash
dotnet add package Headless.Messaging.Queue.Abstractions
```

```csharp
using Headless.Messaging;

public sealed class ImportJobs(IQueue queue)
{
    public Task EnqueueAsync(ImportRequested message, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, cancellationToken);
    }

    public Task EnqueueWithMetadataAsync(ImportRequested message, string correlationId, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}

public sealed record ImportRequested(Guid ImportId);
```

**Verify:** `EnqueueAsync` returns a `PublishReceipt` with a non-null `StorageId` under the default `Durable` mode. In a test, `MessagingTestHarness.WaitForPublishedAsync<T>(MessageLane.Queue)` returns the recorded message.

### Behavior

- `IQueue` behaves as `IBus` does, on the Queue lane: a singleton, never enlisted, `Durable` by default.
- `IRequestClient.RequestAsync<TRequest, TResponse>` and `GetReplyAsync<TRequest, TResponse>` send a request and await one response. `RequestOptions` carries `Timeout`, `TenantId`, `CorrelationId`, and `Headers` only. Failures derive from `RequestReplyException`, except the caller's own cancellation (`OperationCanceledException`) and an unreadable response body (`MessageDeserializationException`). Only `setup.AddRequestReply()` registers `IRequestClient`. See [Request/reply](messaging/request-reply.md#requestreply).
- The package registers no services; runtime wiring comes from `Headless.Messaging`.

## Headless.Messaging

The runtime: `AddHeadlessMessaging`, publishers, consumers, the outbox relay, retries, middleware, request/reply, telemetry, readiness checks, and the host manifest section. It carries the source generator as an analyzer.

### Setup

```bash
dotnet add package Headless.Messaging
dotnet add package Headless.Messaging.InMemory
dotnet add package Headless.Messaging.Storage.InMemory
```

```csharp
[BusConsumer("orders.projection")]
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

[QueueConsumer("orders.fulfil-order")]
public sealed class FulfilOrder : IConsume<FulfilOrderCommand>
{
    public ValueTask ConsumeAsync(ConsumeContext<FulfilOrderCommand> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UseInMemoryStorage();
    setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
    setup.AddModule<Orders.MessagingModule>();
});

services.ConfigureMessaging(messaging =>
{
    messaging.Message<OrderPlaced>("orders.placed").CorrelateBy(order => order.OrderId.ToString());
    messaging.Message<FulfilOrderCommand>("orders.fulfil");
});
```

`IBus.PublishAsync(new OrderPlaced(...))` reaches `OrderPlacedConsumer`, and `IQueue.EnqueueAsync(new FulfilOrderCommand(...))` reaches `FulfilOrder`. Hosts that share modules but split consumption add the same modules to each host and filter with `ConsumeOnly`:

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage ...
    setup.ConsumeOnly("orders.projection"); // every-instance consumers still run
});
```

**Verify:** the `messaging` readiness check reports `Healthy` once messaging has started. `describe` lists `orders.projection` and `orders.fulfil-order` in the `messaging` section, with `runState: runs-here` for every consumer the host's `ConsumeOnly` filter keeps.

### API

- `services.AddHeadlessMessaging(setup => ...)`, called once by the host. `MessagingSetupBuilder` configures the transport, storage, `Options`, `Instrumentation`, conventions (`UseConventions`), `AddModule<TModule>()`, `Tune(identity, ...)`, `ConsumeOnly(...)`, `DefaultFailurePolicy(...)`, `WithMessageNameMapping<T>(name)`, `AddOutbox(name)`, and `AddRequestReply(...)`. It returns the `MessagingBuilder` that registers middleware and `UseDistributedLock(...)`.
- Message names: `m.Message<T>("name")` is the normal way to name a message. `setup.WithMessageNameMapping<T>("name")` maps a type to a name without a version or lane settings. With neither, `UseConventions(...)` derives the name (the type name by default).
- `IRuntimeSubscriber` attaches a Bus delegate after startup. See [Runtime subscriptions](messaging/consumers.md#runtime-subscriptions).
- `ICircuitBreakerMonitor` and `IRetryProcessorMonitor` observe and reset consumer circuits and retry back-off. See [Circuit Breaker](messaging/retries.md#circuit-breaker).
- `IMonitoringApi`, `IInboxOperationsApi`, `IPublishedOperationsApi`, and `IScheduledDeliveryOperationsApi` read and operate on stored messages. See [Operator APIs](messaging/operations.md#operator-apis).
- Register every middleware while you configure services. Registering one after the host started throws `InvalidOperationException`. See [Middleware](messaging/middleware.md#middleware).

### Host options

Set these on `setup.Options` inside `AddHeadlessMessaging`. Topic files own the options of their area: retry and lease options in [Retries](messaging/retries.md), circuit breaker in [Circuit Breaker](messaging/retries.md#circuit-breaker), request/reply in [Request/reply](messaging/request-reply.md#setup), history retention and orphan probes in [Operations](messaging/operations.md#operation-history-retention), and `BacklogSamplingInterval` in [Outbox and inbox backlog](messaging/observability.md#outbox-and-inbox-backlog).

| Option | Default | Effect |
| --- | --- | --- |
| `MessageNamePrefix` | `null` | Prefixes every message name, framework `headless.*` names included. Consumer identities are never prefixed. |
| `Version` | `v1` | Isolates services that share tables or a lock store. At most 20 characters; a longer value fails startup. |
| `DefaultDeliveryMode` | `Durable` | Delivery mode of an `IBus` or `IQueue` publish that sets none and whose contract pins none. See [Delivery Modes](messaging/publishing.md#delivery-modes). |
| `MinimumInboxGuarantee` | `Transactional` | Guarantee the storage must declare for durable consumers (`ProcessLocal` < `Durable` < `Transactional`). A weaker value is an explicit opt-down. |
| `SucceedMessageExpiredAfter` | 86,400 s (1 day) | Retention of succeeded rows. |
| `FailedMessageExpiredAfter` | 1,296,000 s (15 days) | Retention of terminally failed rows, counted from the terminal write. |
| `CollectorCleaningInterval` | 300 s | Pause between collector passes once a pass finds nothing to delete. |
| `ConsumerThreadCount` | 1 | Consumer clients per subscription (1 to 1,024). Above 1, messages can run out of order. |
| `EnableSubscriberParallelExecute` | `false` | Buffers deliveries for parallel execution; no ordering. `SubscriberParallelExecuteThreadCount` (processor count) and `SubscriberParallelExecuteBufferFactor` (1) size it; their product must not exceed 100,000. |
| `EnablePublishParallelSend` | `false` | Sends stored rows on a sliding window of `PublishBatchSize` (1 to 500) concurrent sends, unordered. |
| `RetryBatchSize` | 200 | Rows one retry pickup claims (1 to 100,000). |
| `SchedulerBatchSize` | 1,000 | Rows one delayed claim takes, and the size of the in-memory scheduler queue (1 to 100,000). |
| `DelayedMessagePollInterval` | 60 s | Poll of storage for delayed messages due within two minutes (at most 2 minutes). |
| `ConsumerPollTimeout` | 1 s | Listening timeout of every consumer client (at most 30 s). A wait bound on Kafka and in-memory, the idle sleep on Redis and SQS, ignored on RabbitMQ and Azure Service Bus. |
| `TransportPublishTimeout` | 10 s | A send the transport does not confirm in this time fails its attempt with `TimeoutException`. It also bounds a send's confirm wait during shutdown, which does not cancel a send already handed to the transport. |
| `CommandTimeout` | 30 s | Timeout of each storage command. |
| `ShutdownTimeout` | 30 s | One end-to-end shutdown bound (at most 5 minutes). Give the host or orchestrator a longer termination grace. |
| `SubscriptionEstablishedTimeout` | 30 s | Bound on each `IOnSubscriptionEstablished` call (at most 5 minutes). |
| `UseStorageLock` | `false` | Serializes retry pickup across replicas through the messaging lock. `UseDistributedLock(...)` sets it. See [Distributed Lock Integration](messaging/retries.md#distributed-lock-integration). |
| `DeadNodeReconcileInterval` | `null` (half the coordination `DeadRetentionWindow`) | Cadence of dead-owner recovery. See [Dead-owner recovery](messaging/retries.md#dead-owner-recovery). |
| `MaxPoisonEnvelopeBytes` | 1 MiB | Cap on a stored poison envelope. |
| `JsonSerializerOptions` | storage JSON options | Serializer settings of the default `IMessageSerializer`. See [How a raw delivery is completed](messaging/foreign-producers.md#how-a-raw-delivery-is-completed). |
| `BackoffClockServiceKey` (constant) | `TimeProvider.System` registered under it | Clock of the failure waits. Production hosts leave it alone; see [Test harness](messaging/test-harness.md#headlessmessagingtesting). |

**Set a distinct `Version` per service that shares a lock store.** The retry-pickup locks are named from `Version`, so two services that point at one lock store (one Redis, for example) with the same `Version` contend for the same lock resources and starve each other's retry pickup. The default `v1` is safe only for one service per lock store. See [Lock names and isolation](messaging/retries.md#lock-names-and-isolation).

**Give each environment its own `Headless:Prefix` to share one broker.** Worktrees or preview stacks that point at one NATS, Redis, RabbitMQ, Kafka, AWS, or Azure Service Bus set different [isolation prefixes](utilities.md#isolation-prefix), and each transport puts the prefix in front of the broker names it derives, so the environments do not receive each other's messages. Message names, storage, and dashboards keep the unprefixed names. The prefix also isolates retry locks on a Redis or in-memory lock store, so environments need no distinct `Version` there; services inside one environment still do. See [Host prefix](messaging/transports.md#host-prefix).

Tenant propagation and strict publishing are configured through `AddHeadlessTenancy(tenancy => tenancy.Messaging(...))`; `MessagingOptions.TenantContextRequired` reports the setting and has no public setter.

### Runtime behavior

Registers the messaging services, hosted processors, publishers, consumers, the middleware registries, the always-on dead-owner recovery service, the `messaging`, `messaging-broker`, and `messaging-consumers` readiness checks (`messaging-outboxes` when the host adds an outbox, and `messaging-broker-{name}` for each `AddTransport(name)`), and the `messaging` host manifest section. Messaging starts in the background after the host serves requests; the `messaging` check holds readiness until it has started. See [Operations](messaging/operations.md).

## Headless.Messaging.SourceGenerator

The Roslyn incremental generator that registers `[BusConsumer]` and `[QueueConsumer]` classes at compile time. `Headless.Messaging` carries it as an analyzer, so a project that references `Headless.Messaging` needs no separate reference.

### Setup

```bash
dotnet add package Headless.Messaging.SourceGenerator
```

Reference it directly only in a library that declares consumers and references `Headless.Messaging.Abstractions` instead of `Headless.Messaging`.

**Verify:** the build emits `<AssemblyName>.MessagingModule`, so `messaging.AddModule<Orders.MessagingModule>()` compiles. A declaration error fails the build with an HM rule below.

### Behavior

- **Explicit registration.** The generated `MessagingModule.g.cs` declares `<AssemblyName>.MessagingModule`. Nothing registers until a call adds it with `AddModule<…MessagingModule>()`. An assembly that declares no consumer gets no module. There is no module initializer and no run-time scanning.
- **Module namespace.** An assembly name that is not a valid C# namespace maps one `.`-separated segment at a time: an invalid identifier character becomes `_`, a segment that starts with a digit gets a `_` prefix, an empty segment becomes `_`, and a C# keyword is escaped with `@`. `Orders-Api` gives `Orders_Api.MessagingModule`, `2024.Billing` gives `_2024.Billing.MessagingModule`, and `Orders.class` gives `Orders.@class.MessagingModule`.
- **One entry per message.** A class registers an entry for every `IConsume<T>` and `IRespond<TRequest, TResponse>` it implements, with the attribute's identity, lane, `EveryInstance`, and a factory for its `FailurePolicy` type. The host builds the policy once per identity.
- **Construction.** Each attempt resolves the consumer class from the attempt's DI scope when the application registered it, and otherwise creates it with `ActivatorUtilities.CreateInstance<T>`. Only an instance the dispatcher created is disposed by it. A registered singleton consumer is shared by concurrent deliveries, so it must be thread-safe. Dispatch uses no reflection.
- **Responder metadata.** For each responder the generator emits `[assembly: ResponderMetadataAttribute(typeof(TRequest), typeof(TResponse))]`, which a referencing project's generator reads for HM014. Do not apply it by hand.

### Diagnostics

<a id="messaging-source-generator-diagnostics"></a>Every rule is reported at compile time in category `Headless.Messaging.SourceGenerator`. HM006 and HM014 are warnings; every other rule is an error. The table below is each rule's help link target.

| Rule | Reported when | Fix |
| --- | --- | --- |
| <a id="hm001"></a>HM001 | The identity is not a compile-time constant, or is not in `owner.name` form: it is empty, longer than 200 characters, has no `.` separator or an empty segment, has surrounding white space, or contains a control character. | Pass a literal or `const` identity such as `"billing.invoice-projection"`, whose first segment names the owning module. |
| <a id="hm002"></a>HM002 | Two consumer classes in one compilation use the same identity on the same lane. Nothing is generated. | Give each consumer class its own identity; one class covers several messages by implementing several `IConsume<T>`. |
| <a id="hm003"></a>HM003 | The class carries a consumer attribute but implements neither `IConsume<T>` nor `IRespond<TRequest, TResponse>`. | Implement `IConsume<T>` for every message the consumer handles, or `IRespond<TRequest, TResponse>` for every request a `[QueueConsumer]` answers. |
| <a id="hm004"></a>HM004 | A second `[QueueConsumer]` class in one compilation consumes or responds to a message that already has a Queue consumer or responder. A responder counts as its request's Queue consumer. Nothing is generated. | Keep one Queue consumer or responder per message; use `[BusConsumer]` for fan-out. |
| <a id="hm005"></a>HM005 | The `FailurePolicy` type does not derive from `Headless.Reliability.FailurePolicy`, is not a class, is abstract, still has an open type parameter (its own or a containing type's), is private, protected, or `file`-local, or has no public parameterless constructor, so the generated factory cannot construct it. A closed generic such as `RetryTwice<Payments>` is accepted. Nothing is generated. | Point `FailurePolicy` at a concrete, closed `public` or `internal` class derived from `FailurePolicy` with a public parameterless constructor. |
| <a id="hm006"></a>HM006 | A consumer that is not an every-instance `[BusConsumer]` implements `IOnSubscriptionEstablished`, whose hook never runs for it. | Set `EveryInstance = true` on a `[BusConsumer]`, or remove the interface. |
| <a id="hm007"></a>HM007 | The consumer class, a type containing it, or a consumed message type is private, protected, or `file`-local, so generated code cannot name it. | Make the type and every type containing it `public` or `internal`. |
| <a id="hm008"></a>HM008 | The consumer class is abstract or generic, or nested in a generic type, so a delivery cannot construct it. | Put the attribute on a concrete, non-generic class. |
| <a id="hm009"></a>HM009 | One class carries both `[BusConsumer]` and `[QueueConsumer]`. Nothing is generated. | Keep one lane attribute; split the class when it must consume on both lanes. |
| <a id="hm010"></a>HM010 | A `[BusConsumer]` with `EveryInstance = true` declares a `FailurePolicy`. Every-instance deliveries are at most once and never stored, so no policy can run. Nothing is generated. | Remove `FailurePolicy`, or make the consumer competing by removing `EveryInstance = true`. |
| <a id="hm011"></a>HM011 | A class that implements `IRespond<TRequest, TResponse>` carries `[BusConsumer]`. A reply goes to one caller, and only the Queue lane gives a request exactly one consumer. | Put `[QueueConsumer]` on the responder. |
| <a id="hm012"></a>HM012 | One class implements both `IConsume<T>` and `IRespond<T, TResponse>` for the same `T`. | Implement only `IRespond<T, TResponse>`; a responder that a plain enqueue reaches runs and discards its result. |
| <a id="hm013"></a>HM013 | One class implements `IRespond<TRequest, TResponse>` for the same request with two or more response types. | Answer each request with one response type; put alternatives in one response contract. |
| <a id="hm014"></a>HM014 | A `RequestAsync<TRequest, TResponse>` or `GetReplyAsync<TRequest, TResponse>` call expects a `TResponse` that no responder visible to the project answers `TRequest` with. A responder is visible when it is declared in the calling project or in a referenced assembly built with this generator. Without the fix, the call fails with `ResponseContractMismatchException`. | Request the responder's response type. When the call deliberately reads the reply into another type registered under the same message contract name and version, suppress the warning at the call with a reason. |
