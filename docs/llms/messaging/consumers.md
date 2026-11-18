---
domain: Messaging
packages:
---

# Messaging consumers

> Part of [Messaging](../messaging.md). Declare consumers and message contracts, register modules, tune consumers per host, and choose competing, every-instance, or runtime delivery.

## Consumer identity and lanes

A consumer is a class that implements `IConsume<T>` for each message it handles and carries exactly one lane attribute: `[BusConsumer(identity)]` or `[QueueConsumer(identity)]`. One class may handle several messages. No fluent call declares a consumer, and nothing scans assemblies at runtime.

- **Lanes.** Bus is broadcast. Every consumer identity gets one copy, shared by the processes that register it. Queue is point-to-point. A message has at most one Queue consumer and one destination keyed by the message name. The publisher verb selects the lane (`IBus.PublishAsync` or `IQueue.EnqueueAsync`), and the attribute selects the consumer's lane. For fan-out, use `[BusConsumer]`.
- **Identity.** The identity is the consumer's durable name, in `owner.name` form, at most 200 characters (`ConsumerMetadata.ConsumerIdentityMaxLength`), with the first segment naming the owning module. The generator checks it at build time ([HM001](../messaging.md#hm001)).
- **What the identity keys.** On the Bus lane it is the broker subscription name (see [Bus subscription names](transports.md#bus-subscription-names)), so processes that register one identity compete for each message wherever the module runs. It also keys the consumer's inbox rows, circuit breaker, metrics, `Tune`, configuration, and `ConsumeOnly`. A consumer is keyed by lane, identity, message name, and contract version, so one identity covers every message its class handles.
- **Renaming.** The identity is independent of the CLR type name, so you can rename or move the class freely. Renaming the identity starts a new consumer with empty inbox history. Changing a contract version is not a way to reset deduplication.
- **Both lanes for one message.** A Bus consumer and a Queue consumer of the same message have independent subscriptions, inbox rows, middleware, circuits, and retry state. Every built-in dual-lane transport keeps the two lanes' topology apart. Kafka is Queue-only: a Bus consumer on Kafka fails startup, and a Bus publish fails when attempted.
- **Second Queue consumer.** A second Queue consumer for one message fails the build ([HM004](../messaging.md#hm004)) within an assembly and fails startup across assemblies.

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

`IBus.PublishAsync(new OrderPlaced(...))` reaches `OrderPlacedConsumer`, and `IQueue.EnqueueAsync(new FulfilOrderCommand(...))` reaches `FulfilOrder`.

**Verify:** run the host's `describe` verb. Its `messaging` section lists one consumer entry per identity and lane, with `runState: runs-here` (see [Host manifest](operations.md#host-manifest)).

## Message contracts

`m.Message<T>(name, version = "1")` sets the logical name and schema version that publishing and consuming resolve `T` to, on both lanes. The message type needs no attribute and no Headless reference.

- Chain `.CorrelateBy(selector)` for the root correlation id, and `.OnBus(b => ...)` and `.OnQueue(q => ...)` for lane settings: `RequireRoutingAffinity()`, `WithDeliveryMode(mode)`, `RawPayload()`, and provider message hatches. See [Delivery Modes](publishing.md#delivery-modes), [Routing affinity](transports.md#routing-affinity), and [Foreign producers](foreign-producers.md#foreign-producers-and-raw-payloads).
- The name and version are stamped on every outgoing envelope and checked before durable dispatch. Consumers check the version header before deserialization and expose it as `ConsumeContext.ContractVersion`. A missing header reads as `"1"`, except on a `RawPayload()` lane, which fills in the contract's own version.
- Identical declarations from several modules merge. A different name, version, correlation selector, or lane setting fails startup and names both sources. Selectors compare by delegate, so share one declaration method instead of writing the same lambda twice.
- The contract callback runs once, synchronously. A contract builder used after the callback returns throws.
- `setup.WithMessageNameMapping<TMessage>("message.name")` maps a type to a name host-wide, without a version or lane settings. With neither mapping, the name comes from `UseConventions(...)`, the type name by default. Mapping one type to two names fails startup.
- Explicit names (contract names and `PublishOptions.MessageName`) go through one validator. Invalid dot shapes and invalid characters are rejected before publish.
- `MessagingOptions.MessageNamePrefix` prefixes every message name. Consumer identities are never prefixed.

## Modules and registration

The Messaging source generator emits one `<AssemblyName>.MessagingModule` per assembly that declares consumers (see [Headless.Messaging.SourceGenerator](../messaging.md#headlessmessagingsourcegenerator)). Nothing registers until a call adds the module.

- The host calls `AddHeadlessMessaging(...)` exactly once, for transport, storage, and options.
- A module contributes from its own `Add{Module}` entry point with `services.ConfigureMessaging(m => { m.AddModule<X.MessagingModule>(); m.Message<T>(...); })`, before or after the host's call. Never call `AddHeadlessMessaging` a second time from a module.
- Adding a module twice registers it once. An assembly that declares no consumer gets no module.
- Every contribution and `WithMessageNameMapping` call is folded once per service provider, when messaging first builds its consumer registry: at startup, or at an earlier publish that needs a message name. Identical contributions merge there. One identity on two consumer classes in the same lane, a conflicting contract, or one type mapped to two names fails at that point and names both sources.
- In a host that never calls `AddHeadlessMessaging`, contributions stay inert.
- A library that declares consumers references `Headless.Messaging` (it carries the generator), ships the generated module compiled in, and exposes an `Add{Module}` entry point. A library never calls `AddHeadlessMessaging`. `ConfigureMessaging`, `Message<T>`, `AddModule<T>`, and `IMessagingModule` live in `Headless.Messaging.Abstractions`. `Tune` needs `Headless.Messaging`.

**Verify:** a conflicting contribution throws at startup naming both sources. A missing `AddModule` call shows as an absent consumer in the `describe` output.

## Host controls

Deployment settings are keyed by consumer identity. An identity that no registered consumer declares fails startup. Tuning cannot declare a consumer or change its identity, lane, or messages.

### Tune

`Tune(identity, c => ...)` on the `AddHeadlessMessaging` setup or on `services.ConfigureMessaging(...)` changes one consumer on this host:

| Call | Effect |
| --- | --- |
| `Concurrency(byte n)` | Handlers running at once for the identity. `0` is rejected |
| `Sequential()` | One message per routing affinity key at a time; see [Sequential consumers](ordering.md#sequential-consumers) |
| `RateLimit(ConsumerRateLimit)` | Throttles handler starts; see [Consumer Rate Limit](ordering.md#consumer-rate-limit) |
| `InboxRetention(TimeSpan)` | Terminal inbox generations kept this long, positive whole seconds. Default 30 days |
| `CircuitBreaker(Action<ConsumerCircuitBreakerOptions>)` | See [Circuit Breaker](retries.md#circuit-breaker) |
| `FailurePolicy<TPolicy>()`, `FailurePolicy(p => ...)` | Replaces the declared policy; see [Consumer failure policy](retries.md#consumer-failure-policy) |
| `UseMiddleware<TMiddleware>()` | Consume middleware for this consumer only; see [Middleware](middleware.md#middleware) |
| `UseKafka(...)`, `UseNats(...)`, `UseRabbitMq(...)` | Provider consumer hatches. AWS and Azure Service Bus have none |

Several `Tune` calls for one identity apply in registration order: a later value wins, and middleware accumulates. A later `Tune` with the same provider config type replaces the earlier one. A consumer that handles several messages takes the settings for each of them. `InboxRetention`, `CircuitBreaker`, `FailurePolicy`, or `Sequential` on an every-instance consumer fails startup.

Inbox retention bounds duplicate suppression. While a message's inbox row is retained, a redelivery of it is suppressed. Once that row expires or an operator purges it, the same message ID is admitted again as a new message and runs the handler again. An unreadable inbox envelope recovered after a crash is recorded as a terminal failure, retained from the database clock for the row's retention, and its redeliveries are suppressed until it expires.

### Configuration

`Headless:Messaging:Consumers:{identity}` binds after every `Tune` call and after the host default fills in:

- `Concurrency` (1 to 255).
- `InboxRetention`, a `TimeSpan` such as `30.00:00:00`.
- `CircuitBreaker:Enabled`, `CircuitBreaker:FailureThreshold`, `CircuitBreaker:OpenDuration`.
- `FailurePolicy:ImmediateRetries`, `FailurePolicy:DelayedRetries`, `FailurePolicy:DelayedInitialDelay`, `FailurePolicy:DelayedMaxDelay`.
- `RateLimit`, holding exactly one of `FixedWindow:PermitLimit` with `FixedWindow:Window`, or `TokenBucket:TokenLimit`, `TokenBucket:TokensPerPeriod`, and `TokenBucket:ReplenishmentPeriod`.
- `Sequential`, `true` or `false`.

The transient-exception predicate and fail rules are code-only. An unknown identity, an unknown setting, an invalid value, or a retention, circuit breaker, or failure policy on an every-instance consumer fails startup.

```json
{
  "Headless": {
    "Messaging": {
      "Consumers": {
        "orders.projection": {
          "Concurrency": 8,
          "InboxRetention": "14.00:00:00",
          "CircuitBreaker": { "FailureThreshold": 3, "OpenDuration": "00:01:00" },
          "FailurePolicy": { "DelayedRetries": 8, "DelayedMaxDelay": "00:30:00" },
          "RateLimit": { "FixedWindow": { "PermitLimit": 100, "Window": "00:00:01" } }
        }
      }
    }
  }
}
```

### ConsumeOnly

Hosts that share modules but split consumption add the same modules to each host and filter with `ConsumeOnly`:

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage ...
    setup.ConsumeOnly("orders.projection"); // every-instance consumers still run
});
```

- An entry is an exact identity or an `owner.*` pattern that matches the first identity segment, such as `ConsumeOnly("orders.*", "billing.invoice-projection")`.
- Consumers outside the filter stay registered, so the host still publishes their messages. Another host consumes them.
- The filter also scopes this host's received-retry and orphan pickup. The host never leases a row of an identity outside the filter, so it never marks another host's healthy row as an orphan or fails its retry for want of a subscriber. A host without the filter picks up rows of every identity.
- Every-instance consumers and runtime subscriptions are never filtered. Pickup includes every competing runtime subscription attached to the host.
- An entry that matches no registered consumer, or only every-instance consumers, fails startup.

**Verify:** the `describe` output shows the tuned values (`concurrency`, `sequential`, `rateLimit`, `circuitBreaker`, `failurePolicy`) and, under `ConsumeOnly`, `consumeOnly: listed` or `not-listed` per competing consumer. See [Host manifest](operations.md#host-manifest).

## Every-instance Bus delivery

A competing Bus subscription gives one copy per consumer identity, shared by every process that registers it. A consumer that holds per-process state, such as an in-memory cache or a lock waiter, needs every process to receive every message. Declare it every-instance:

```csharp
[BusConsumer("pricing.price-cache", EveryInstance = true)]
public sealed class PriceCache(IPriceCacheStore store) : IConsume<PriceChanged>, IOnSubscriptionEstablished
{
    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) =>
        store.EvictAsync(context.Message.Sku, cancellationToken);

    // Messages published while this process was not subscribed never arrive, so drop what may be stale.
    public ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken cancellationToken) =>
        context.IsReconnect ? store.ClearAsync(cancellationToken) : ValueTask.CompletedTask;
}
```

A runtime subscription sets `RuntimeSubscriptionOptions.EveryInstance = true`.

- **Choose it for derived state only.** Every-instance delivery refreshes what a process can rebuild. Work that must happen once, or must not be lost, stays on a competing consumer.
- **Subscription.** Each process opens its own subscription, named from the identity and the host's `MessagingInstanceId` (a GUID generated once per host start, never the host name). The broker removes it after the process stops or crashes, within the bound in the table below.
- **One client per process.** The identity gets one consumer client whatever `ConsumerThreadCount` is, because a second client would deliver every message twice in one process. `Concurrency` still applies inside that client.
- **At most once, no backlog.** A process receives only what is published while it is subscribed. Nothing is stored: no inbox row, no lease, no retry, no circuit breaker, and no dashboard row. Receive middleware, the contract-version check, and deserialization still run. The consumer runs in a fresh scope with consume middleware, then the message is committed. `ConsumeContext.UnitOfWork` is null.
- **Failures are logged and committed.** A consumer exception, a receive-stage reject, an unhandled message, and a fault outside the consumer are logged, counted, and committed, never requeued. `RetryPolicy.OnTerminal` is not called. Only host shutdown or a client rebuild rejects a delivery.
- **Outcomes.** `headless.messaging.every_instance.deliveries` carries `headless.messaging.every_instance.outcome`: `succeeded`, `failed` (`error.type` is the exception's full type name), `dropped` (the message never reached the consumer or faulted outside it; `error.type` is the exception type, or `overflow` for a NATS subscription channel that discarded it), or `skipped` (receive middleware skipped it).
- **Reconnect signal.** A consumer that implements `IOnSubscriptionEstablished` is called once its subscription receives: at host start (`IsReconnect = false`, `Generation = 1`), after every rebuild of its subscription's clients, and after the transport re-establishes the subscription on its own. `IsReconnect = true` means messages may have been missed, so flush or reload derived state. Attaching or detaching a runtime subscription rebuilds only the groups it changes, so an unrelated every-instance hook does not run.
- **Hook execution.** The hook runs in its own scope, on an instance built the way a delivery builds it. A failing hook is logged and the subscription continues. Hooks of one subscription run one at a time, in establishment order. Host startup waits for the first hooks. A rebuild does not wait and holds no lock, so a hook may attach or detach a runtime subscription. Each call is bounded by `MessagingOptions.SubscriptionEstablishedTimeout` (default 30 seconds, `> 0`, `<= 5m`). On expiry the hook's token is canceled, a warning is logged, and startup goes on. Runtime subscriptions have no hook.
- **Startup rules.** An every-instance consumer on a transport without support fails startup with a `MessagingConfigurationException` naming the consumer and provider, before any client or broker object is created. A runtime subscription is checked the same way before it attaches. A `FailurePolicy` on an every-instance consumer is [HM010](../messaging.md#hm010). `EveryInstance` exists only on `[BusConsumer]`. A host whose only consumers are every-instance needs no inbox guarantee from storage.
- **`ConsumeOnly` does not apply.** Every-instance consumers always start, because each process keeps its own state current.

| Provider | Every-instance subscription | Left behind after a crash |
| --- | --- | --- |
| InMemory | A subscription per identity and instance id | Nothing |
| NATS | Core subscription on the Bus subject, not a JetStream consumer. A subject a sharded identity's `name.>` wildcard covers is not subscribed again. The client reconnects on its own and raises the reconnect signal. A consumer slower than the publish rate overflows the pending channel (`NatsOpts.SubPendingChannelCapacity`, 1024 by default), which drops the newest messages, each counted `dropped` with `error.type` `overflow`; a burst raises the reconnect signal once, after a one-second quiet window | Nothing |
| RabbitMQ | Server-named exclusive, non-durable queue bound to the Bus exchange, on its own connection without automatic recovery. Message TTL is a fixed 60 seconds, not `QueueArguments.MessageTTL`. A lost channel or a broker-cancelled consumer rebuilds the client with a new queue and raises the reconnect signal | Nothing |
| Redis | Group-less polled stream read from each stream's tail at subscribe time. A stream no consumer group reads keeps entries for `EveryInstanceStreamMaxAge` (5 minutes by default). A reader that could not read for half of that raises the reconnect signal | Nothing |
| Azure Service Bus | Subscription named from the identity and instance id with `AutoDeleteOnIdle` of 5 minutes. Deleted on graceful stop (30-second timeout per delete) and recreated if Azure deleted it during a long disconnect; the reconnect signal is raised only on a recreate. Needs `AutoProvision` and Manage rights; with `AutoProvision` off, startup fails | The subscription, for up to 5 minutes |
| AWS SNS/SQS | Not supported: no idle auto-delete, so a crash would leak the queue | Startup fails |
| Kafka | Not applicable: no Bus lane | Not applicable |

**Verify:** the `describe` output shows the consumer with `delivery: every-instance`. On an unsupported transport, startup throws `MessagingConfigurationException`.

## Runtime subscriptions

`IRuntimeSubscriber.SubscribeAsync<T>(handler, options)` attaches a delegate to the Bus lane after startup. Use it for ephemeral, broker-attached handlers. Runtime handlers share scoped DI, middleware, diagnostics, retry, and correlation with class consumers.

- `RuntimeSubscriptionOptions` carries `Identity` (the consumer identity and Bus subscription name, derived from `HandlerId` when omitted), `HandlerId` (defaults to `{DeclaringType}|{Method}|{Message}`; required for anonymous delegates), `MessageName`, `Concurrency`, `EveryInstance`, and `DuplicateBehavior` (`RuntimeSubscriptionDuplicateBehavior.Reject` by default, or `Ignore` or `Replace`). A duplicate runtime subscription throws unless you opt out.
- The returned `RuntimeSubscriptionHandle` exposes `Identity`, `HandlerId`, `MessageName`, `SubscriptionId`, and `IsAttached`. Dispose it to unsubscribe.
- A competing runtime subscription retries by the host's `DefaultFailurePolicy`. It keeps no inbox record, so a broker redelivery of a message it already stored finds the stored row: a due row runs again as its next attempt, keeping its retry count, and a row whose retry, circuit deferral, or dispatch grace is still ahead is left to retry pickup.
- On a running host, `SubscribeAsync` and `UnsubscribeAsync` apply the change before they return. While the host is starting or rebuilding clients after a broker failure, they return at once and the change applies afterwards. If old clients do not stop in time, or the broker is unreachable, the call still returns, the host reports itself unhealthy, and the transport health check's full rebuild applies the change. Only subscription groups whose consumers or concurrency changed are restarted.
- Runtime subscriptions do not appear in the host manifest. On RabbitMQ the running host declares their queues itself; see [Runtime create rights](transports.md#runtime-create-rights).

**Verify:** `RuntimeSubscriptionHandle.IsAttached` is `true` after `SubscribeAsync` returns on a running host.

## Consumer lifecycle

- Consumers need no DI registration. Each attempt builds the consumer class from the attempt's own DI scope through generated code: `GetService<T>()` first, so your own registration of the class, or a decorator, is used with its lifetime, then `ActivatorUtilities.CreateInstance<T>` when the class is not registered.
- The dispatcher disposes only an instance it constructed. A container-resolved instance belongs to its scope or container.
- A registered singleton consumer is shared by concurrent deliveries, so make it thread-safe.
- `IConsumerLifecycle` runs per attempt on that instance. Do not use it for application startup or shutdown.
- Consumer client creation, provisioning, and subscription receive the host-stopping token. Shutdown cancellation surfaces as `OperationCanceledException`, not as a broker failure.
- `ConsumeContext.TransportAddress` is the native address the delivery arrived on: the NATS subject (shard token included), the Kafka topic, the RabbitMQ routing key, the Azure Service Bus entity path, the SQS queue URL, or the Redis stream key. It is `null` on the in-memory transport. Read routing tokens such as a tenant from it instead of the payload. The consumer client overwrites any wire value, so a producer cannot choose it, and a retry from storage sees the original address.
- Publish from a consumer through the attempt's unit (`context.GetRequiredUnitOfWork().Outbox`), not through `IBus` or `IQueue`. See [Transactional inbox](storage.md#transactional-inbox).

## Framework messages

Every message a Headless package publishes or consumes is declared by that package as `headless.<domain>.<event>`, version `1`, through `ConfigureMessaging`. The name is a `MessageName` constant on the message type. Host conventions (`UseConventions` prefix, suffix, kebab-case) never rename them, so services with different conventions interoperate on one broker. Only `MessagingOptions.MessageNamePrefix` applies.

An application consumer of a framework message declares only its consumer, never a second `Message<T>` for it.

| Message | Name | Declared by |
| --- | --- | --- |
| `CacheInvalidationMessage` | `headless.caching.hybrid.invalidation` | `Headless.Caching.Hybrid.Messaging` |
| `DistributedLockReleased` | `headless.locks.released` | `Headless.DistributedLocks.Messaging` |
| `SettingChangedMessage` | `headless.settings.changed` | Settings |
| `FeatureChangedMessage` | `headless.features.changed` | Features |
| `PermissionGrantChangedMessage` | `headless.permissions.grant-changed` | Permissions |
| `DynamicPermissionDefinitionsChanged` | `headless.permissions.definitions-changed` | Permissions |

These messages are system messages: they publish without a tenant under required tenancy. See [Strict Publish Tenancy](publishing.md#strict-publish-tenancy).
