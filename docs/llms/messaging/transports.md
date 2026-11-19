---
domain: Messaging
packages: Messaging.InMemory
---

# Messaging transports

> Part of [Messaging](../messaging.md). What each transport supports, how it names broker objects, how routing affinity maps, which topology a deployment creates, how to add a second broker as a named transport, and the in-memory transport.

Each broker has its own topic file: [AWS](aws.md), [Azure Service Bus](azure-service-bus.md), [Kafka](kafka.md), [NATS](nats.md), [RabbitMQ](rabbitmq.md), and [Redis](redis.md). To write a transport for another broker, read [Writing a Transport Provider](custom-transport.md#writing-a-transport-provider).

## Provider capabilities

| Provider | Bus | Queue | Same-name lane isolation | Message hatch (`OnBus`/`OnQueue`) | Consumer hatch (`Tune`) | Request/reply |
| --- | --- | --- | --- | --- | --- | --- |
| AWS | SNS topic to one SQS queue per consumer identity | Direct SQS destination | Yes | `UseAws(a => a.MessageGroupId(...))` | None | No; startup fails |
| Azure Service Bus | Topic/subscription per consumer identity | Queue | Yes | `UseAzureServiceBus(a => a.PartitionKey(...))` | None | Not yet; startup fails |
| InMemory | One copy per consumer identity | One owned copy | Yes | None | None | In-process reply channel |
| Kafka | No | Topic; Kafka consumer group named after the message | Not applicable; Queue-only | `OnQueue` only: `UseKafka(k => k.PartitionBy(...))` | `UseKafka(k => k.WithIsolationLevel(...))` | No; startup fails |
| NATS | Interest-retained lane stream, one durable per consumer identity | Work-queue-retained lane stream | Yes | `UseNats(n => n.SubjectShard(...))` | `UseNats(n => n.Sharded())` and JetStream limits | Core NATS subject outside JetStream |
| RabbitMQ | Lane topic exchange, one queue per consumer identity | Lane direct exchange | Yes | None | `UseRabbitMq(r => r.PrefetchCount(...))` | Exclusive reply queue |
| Redis | Lane Redis Stream + Redis consumer group named after the identity | Lane Redis Stream + Redis consumer group named after the message | Yes | None | None | Pub/sub channel |

- **Lanes are validated at startup.** Every transport declares which lanes it carries, whether it supports delayed scheduling, every-instance consumers, request/reply, and independent lane topology. Bootstrap checks the registered consumers and contracts against those declarations before readiness and before any broker call. Startup validates contract routes only on lanes the transport carries, so Kafka (Queue-only) accepts every contract. A `[BusConsumer]` on Kafka fails startup, and an `IBus` publish on Kafka fails when attempted.
- **Hatches live in two places.** Message-side settings go on a contract's lane builder (`.OnBus(b => b.UseNats(...))`, `.OnQueue(q => q.UseKafka(...))`). Consumer-side settings go on `Tune(identity, c => c.UseRabbitMq(...))`. Kafka, RabbitMQ, and NATS have consumer hatches. AWS and Azure Service Bus have message hatches only.
- **Never write framework metadata through a hatch.** Use the typed publish options for tenant, correlation, and routing affinity. A hatch is for physical broker routing and configuration.
- **The dashboard shows the declared capabilities.** The Messaging dashboard's metadata endpoint lists each provider's lanes.
- Every-instance support per transport is in [Every-instance Bus delivery](consumers.md#every-instance-bus-delivery). Request/reply channels and permissions per transport are in [Provider support](request-reply.md#provider-support). Ordering per transport is in [Transport-Specific Ordering](ordering.md#transport-specific-ordering).

## Bus subscription names

On the Bus lane a consumer identity is the broker subscription name. Every provider applies one rule: an identity the broker accepts is used unchanged. Any other identity becomes a readable prefix, with rejected characters replaced by `-`, followed by `-` and the first 12 hex characters of the identity's SHA-256. Every process derives the same name, so replicas that register one identity share one subscription. Two identities that normalize to the same prefix still get different names. Queue destinations are keyed by the message name. On Kafka the Queue consumer group is the message name, derived by the same rule.

| Provider | Bus subscription | Limit and characters kept | Namespace isolation |
| --- | --- | --- | --- |
| AWS | SQS queue `bus-{identity}` (an identity ending `.fifo` gets a FIFO queue) | 80 characters including `bus-` and `.fifo`; letters, digits, `-`, `_` | Account and region |
| Azure Service Bus | Subscription `{identity}` on the Bus topic | 50 characters; letters, digits, `.`, `-`, `_`; starts and ends with a letter or digit | Namespace and `TopicPath` |
| InMemory | `{identity}`, unchanged | None | The process |
| Kafka (Queue only) | Consumer group `{message-name}` | 249 characters; letters, digits, `.`, `-`, `_` (the topic rules) | Topic names, through `MessageNamePrefix` |
| NATS | Durable `bus_{identity}` on each stream the identity reads, filtering on all its subjects there | 255 characters; printable ASCII except `.`, `*`, `>`, `/`, `\` | Account; streams follow the message names |
| RabbitMQ | Queue `bus.{identity}` | 255 characters; letters, digits, `.`, `-`, `_` | Virtual host |
| Redis | Consumer group `{identity}` on each stream | No limit; printable ASCII without spaces | Stream keys follow the message names; database or ACL |

Broker-backed providers keep names ASCII and replace whitespace and control characters even where the broker allows them. No host prefix is added: the identity alone names the subscription, so a module keeps its subscriptions when it moves to another process. To keep separate systems or environments apart on one broker, use the broker namespace in the last column, or distinct identities.

## Routing affinity

Register the logical destination and set one `RoutingAffinityKey` on the publish or enqueue options. A route with a native mapping, listed below, hands the key to the broker, which partitions by it. Every other route carries the key as the `headless-routing-affinity-key` envelope header and does not route on it. A consumer tuned [`Sequential()`](ordering.md#sequential-consumers) orders by that header on any transport.

Call `RequireRoutingAffinity()` on a route whose broker must partition by the key. Startup then fails unless the route has a native mapping. It does not require every publish to supply a key.

| Provider | Destination with a native mapping | Native key | Key bounds and prerequisites |
| --- | --- | --- | --- |
| Kafka | Queue topic | UTF-8 string key | Nonempty, no control characters; a deterministic partitioner and a fixed partition count |
| Azure Service Bus | Session-enabled Bus subscription or Queue | `SessionId` | At most 128 UTF-16 code units; raw `SessionId` and `PartitionKey` must match |
| AWS | FIFO SNS topic or SQS queue | `MessageGroupId` | `.fifo` destination; 1 to 128 ASCII characters `!` to `~`, no spaces |
| NATS, RabbitMQ, Redis, InMemory, a standard AWS destination, Azure Service Bus without sessions | None | None; the key travels as the envelope header | Not blank, no control characters; `RequireRoutingAffinity()` fails startup |

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseKafka("localhost:9092");
    setup.UseInMemoryStorage();
});

services.ConfigureMessaging(messaging =>
    messaging.Message<OrderChanged>("orders.changed").OnQueue(queue => queue.RequireRoutingAffinity())
);

await queue.EnqueueAsync(order, new QueueOptions
{
    RoutingAffinityKey = order.OrderId.ToString(),
    DeliveryMode = DeliveryMode.Durable,
}, cancellationToken);
```

The example uses process-local storage for development. Use PostgreSQL or SQL Server for persistence across restarts.

**Verify:** a route marked `RequireRoutingAffinity()` on a transport without a native mapping fails startup with a `MessagingConfigurationException` naming the message and lane.

- **Validation runs before side effects.** Required-route checks run at startup, before clients and processors start. Per-call validation (invalid key, a raw provider value that disagrees with the typed key, a keyed override for an unknown destination) runs before the outbox write and before any transport call.
- **Raw adapters must agree.** When you set both the typed key and a provider's raw value (`KafkaMessagingHeaders.KafkaKey`, `AwsMessagingHeaders.MessageGroupId`, Azure `SessionId` or `PartitionKey`), they must be equal. Do not write `headless-routing-affinity-key` as a custom header: it is rejected.
- **Stored rows are revalidated.** A stored keyed outbox row is checked against the current mapping before dispatch. A deployment that removes or invalidates the mapping blocks dispatch of those rows until you restore a supported configuration. Unkeyed messages have no requirement.
- **Startup checks are local.** They check your declarations, not the broker. Verify the destination's session, FIFO, or partition configuration on the broker yourself. Keep partition count and hashing stable while placement matters; placement across a topology change is not promised.
- **Affinity alone is not ordering.** It does not promise total FIFO, nonconcurrent handling of one key, separate partitions for different keys, or stable placement. A consumer tuned `Sequential()` adds nonconcurrent, ordered handling per key. Redelivery stays at-least-once.
- Storage keeps the key in the serialized envelope, so retries keep the same key without a schema column.

## Topology and permissions

| Provider | Topology | Operational requirement |
| --- | --- | --- |
| AWS | Bus consumer identities own SQS queues subscribed to SNS; Queue sends directly to SQS | Grant the scoped runtime and provisioning actions in [AWS permissions](aws.md#aws-permissions) |
| Azure Service Bus | Topics and subscriptions for Bus; queues for Queue | A namespace with the required permissions and session configuration |
| InMemory | Process-local channels | A restart loses all state |
| Kafka | Queue-only topics; one Kafka consumer group per message | Configure partitions for the workload |
| NATS | Lane-qualified subjects, streams, retention, and durables | Grant stream and consumer provisioning permissions |
| RabbitMQ | Lane-qualified exchanges, routing keys, and queues, created by `resources provision` | Run `resources provision` with `configure` permission on the shared topology; the running host needs it only for its per-process queues (see [Runtime create rights](#runtime-create-rights)) |
| Redis | Lane-qualified Streams for both lanes | Size Redis memory for `StreamMaxAge` of traffic on streams a consumer group reads, and `EveryInstanceStreamMaxAge` on the rest; the provider creates consumer groups and trims streams |

A deployment still owns its credentials, resource permissions, retention, and restart policy.

## Runtime create rights

Only RabbitMQ provisions at deploy time. AWS SNS/SQS, Azure Service Bus, Kafka, NATS, and Redis Streams create their topology at runtime, as each provider file describes, so `resources provision` creates nothing for them and their runtime identity keeps every create right those files list.

For a transport that offers its topology as a [provisioned resource](../utilities.md#resource-provisioning), a host start never creates the shared topology. The deployment creates it with `resources provision`, and the running host checks once that it exists (see [Topology check](#topology-check)). A consumer started against a reachable broker that lacks its queue fails when it starts consuming, with a `ResourceProvisioningException` that names `resources provision`. An unreachable broker is retried. Objects that belong to one process, or to a subscription added while the host runs, are still created by the running host:

| Provider | Created by `resources provision` | Still created by the running host |
| --- | --- | --- |
| RabbitMQ | The Bus and Queue lane exchanges; one durable queue per competing Bus consumer identity and per Queue-lane message the host consumes or registers, with its bindings; the `.dlx` exchanges and `.dlq` queues when dead-lettering is on | An every-instance consumer's exclusive queue and its binding; the request/reply `headless.reply.*` queue; the queue and bindings of an `IRuntimeSubscriber` subscription. The runtime user needs `configure` on `headless.reply.*`, on the server-named every-instance queues, and on the runtime subscriptions' queues, plus `write` on the Bus exchange to bind them |
| AWS, Azure Service Bus, Kafka, NATS, Redis | Not a provisioned resource | Everything, as described for the provider |

## Topology check

Each provisioned transport topology is a resource named `topology` of kind `RabbitMQ`. Its startup check is `ResourceStartupCheck.GateReadiness` by default ([what a starting host checks](../utilities.md#resource-provisioning)). After the host starts, the `Resources` [warm-up](../utilities.md#warm-up) verifies the registered topology once per transport per host, not once per consumer client.

- Until the check passes, the `warmup` readiness check is `Unhealthy` and names the first missing object, such as `RabbitMQ queue 'queue.orders' does not exist. Run 'resources provision' ...`.
- A failed check is retried with the warm-up back-off, so readiness turns `Healthy` once the deployment runs `resources provision`. Liveness and the host start are not affected.
- To drop the readiness gate for one broker, call `services.ConfigureResourceProvisioning(o => o.StartupChecks["RabbitMQ"] = ResourceStartupCheck.None)`. A missing queue then surfaces only when a consumer starts on it.

## Registration overloads

Every transport `Use{Provider}(...)` except `UseInMemory()` has the standard overload trio, beside any scalar convenience form (such as `UseKafka("localhost:9092")`):

- `Use{Provider}(IConfiguration config)` binds and validates the options from a configuration section.
- `Use{Provider}(Action<TOptions> configure)` configures in code.
- `Use{Provider}(Action<TOptions, IServiceProvider> configure)` configures in code with the service provider, for example to read a secret or credential from DI.

Options are validated at start through their validators. The options types are `AmazonSqsMessagingOptions`, `AzureServiceBusMessagingOptions`, `KafkaMessagingOptions`, `NatsMessagingOptions`, `RabbitMqMessagingOptions`, and `RedisMessagingOptions`. An invalid value fails the host start with an `OptionsValidationException`.

## Named transports

A host has one default transport, the one `setup.Use…` registers. When the host must also publish to and consume from a second broker, register a named transport for it. Do not run a second process or wrap a raw client. For example, RabbitMQ carries internal events and a second broker carries a partner integration:

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);
    setup.UseRabbitMq(builder.Configuration.GetSection("Messaging:Internal")); // default transport
    setup.AddTransport("partner").UseRabbitMq(builder.Configuration.GetSection("Messaging:Partner"));
    setup.Tune("partner.order-sync", consumer => consumer.Transport("partner"));
});

// Publish through the partner broker.
public sealed record OrderShipped(Guid OrderId);

public sealed class PartnerNotifier([FromKeyedServices("partner")] IBus partnerBus)
{
    public Task NotifyAsync(OrderShipped message, CancellationToken cancellationToken) =>
        partnerBus.PublishAsync(message, cancellationToken);
}
```

- **Name.** `AddTransport(name)` takes letters, digits, `-`, and `_`, at most 64 characters (`MessagingSetupBuilder.TransportNameMaxLength`), unique within the host, compared case-insensitively. `default` (`MessagingSetupBuilder.DefaultTransportName`) is reserved for the default transport. `AddHeadlessMessaging` throws for a duplicate, malformed, or `default` name, for an `AddTransport(name)` without a provider, and for a second provider on one `AddTransport(name)`. Choose the name once and keep it: every durable row stores it.
- **Providers.** `AddTransport(name)` returns a `MessagingTransportBuilder`. Only `UseInMemory()` and `UseRabbitMq(...)` (the `string` host name and the standard overload trio) register a named transport today. Each named transport has its own named options (validated at start), connection pool, consumer clients, and topology provisioner, so two transports never share a connection or a setting, even two of one provider. A named in-memory transport is a broker of its own: its messages never reach the default in-memory broker.
- **What is shared.** One storage, one inbox, one retry relay, one failure-policy model, and one dashboard serve every transport. [Additional outboxes](storage.md#additional-outboxes) are independent of transports: any unit of work's outbox can hold rows for any transport.
- **Publishing.** The unkeyed `IBus` and `IQueue` publish through the default transport. The keyed `IBus` and `IQueue` (`[FromKeyedServices("partner")]`, `GetRequiredKeyedService<IBus>("partner")`) publish through the named one, keyed by the name exactly as `AddTransport` spelled it. Inside a unit of work, `unit.Outbox.PublishAsync(message, new OutboxOptions { TransportName = "partner" }, ct)` (and `EnqueueAsync`) picks the transport. `TransportName = null` or `"default"` is the default transport. A name the host does not register throws `MessagingConfigurationException` before any row is written. Each publish is checked against its own transport's capabilities, so that transport refuses a lane or schedule its broker does not support.
- **Durable rows remember their transport.** The published row stores the transport name (`transport` / `Transport`, `NULL` for the default), so the relay, a retry, a delayed claim, and a dead-owner reclaim send the row through the broker it was published through, also after a restart. Every host that shares the storage must register the same named transports. A host that picks up a row naming a transport it does not register fails the row with `MessageTerminalReason.NoTransport`. `MediumMessage.TransportName`, `MessageView.TransportName`, and the dashboard row's `transport` carry the name.
- **Consuming.** A consumer subscribes on the default transport unless `Tune(identity, c => c.Transport("partner"))` or `Headless:Messaging:Consumers:{identity}:Transport` binds it to a named one; `Transport("default")` binds it back. It then subscribes on that broker only. Startup fails, naming the identity, for a binding to a name the host does not register and for a responder bound to a named transport. `ConsumerMetadata.TransportName` carries the binding, spelled as in `AddTransport`.
- **Failure isolation.** Each transport reports into its own broker check (`messaging-broker-{name}`; see [Health checks](operations.md#health-checks)), and its consumer clients are rebuilt alone. When a named broker is down at startup, the host starts, the default transport's consumers run, and the transport check creates the missing subscriptions once the broker answers. Publishes to the named transport are stored and wait for the relay. Its topology is its own resource, `topology-{name}`, with the default `GateReadiness` startup check, so a named broker that is down holds readiness without stopping the host.
- **Topology.** `resources provision` creates each named transport's topology from the consumers bound to it. The declared message contracts provision on the default transport only, so a Queue-lane message published through a named transport needs its consumer's topology on that broker, as for a foreign producer.
- **Request/reply** runs on the default transport only. A named transport opens no reply channel.
- **Test hosts.** `ReplaceMessagingTransportWithInMemory()` replaces each named broker with a named in-memory transport of its own, so routing stays as in production. `AddMessagingTestHarness()` records publishes through every transport.

**Verify:** `messaging-broker-partner` reports `Healthy` after the first send or receive on that broker, and the `describe` host manifest lists the transport under `messaging.transports.named`.

## Headless.Messaging.InMemory

In-process Bus and Queue delivery with no broker, for development and tests. Messages are lost when the process exits, so never use it in production.

### Setup

```bash
dotnet add package Headless.Messaging.InMemory
```

```csharp
setup.UseInMemory();
```

**Verify:** the `describe` host manifest's `messaging.transports` shows `"bus": "in_memory"` and `"queue": "in_memory"`. Outside the Development and Test environments the host also logs warning `3013` (`InMemoryTransportOutsideDevelopment`) at start.

### Design and runtime behavior

- **Delivery.** Bus fan-out per consumer identity, competing replicas, Queue ownership, every-instance subscriptions, and same-name Bus/Queue isolation, all within the process. Request/reply uses an in-process reply channel per listener.
- **Routing affinity.** No native mapping. `RequireRoutingAffinity()` fails startup. A supplied `RoutingAffinityKey` travels as the envelope header, which a consumer tuned `Sequential()` orders by. In-memory storage keeps the key on stored rows.
- **Not for production.** Warning `3013` does not stop the host, because a single-process deployment can choose this transport on purpose.
- **Redelivery delay.** A rejected delivery is requeued to the same consumer client after a jittered back-off: about one second for the first delivery, doubling with each redelivery up to 30 seconds. The core rejects every delivery while a consumer's circuit is open or its half-open probe is out, so without the delay those deliveries would spin. A requeue still pending when the client stops is dropped. The delay runs on the [back-off clock](test-harness.md#headlessmessagingtesting).
- **Shutdown.** Stopping a consumer client waits, within the shutdown budget (30 seconds when the client is disposed directly), for its running handlers. Messages still queued in the client are dropped.
- **Testing.** `ReplaceMessagingTransportWithInMemory()` swaps an application's broker transport for this one in tests; see [Headless.Messaging.Testing](test-harness.md#headlessmessagingtesting).
