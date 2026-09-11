# Writing a Headless Messaging Transport Provider

This guide describes what a `Headless.Messaging.*` transport package should do when integrating a new broker with `Headless.Messaging.Core`.

## What the package is responsible for

A transport package adapts one broker to the core runtime. In this repo, the package normally owns:

- `Setup.cs` exposing `UseMyBroker(...)` on `MessagingOptions`
- `MyBrokerOptions` plus a validator
- `MyBrokerTransport : ITransport`
- `MyBrokerConsumerClientFactory : IConsumerClientFactory`
- `MyBrokerConsumerClient : IConsumerClient`
- broker-specific pools, factories, or helpers when connection reuse matters
- a package `README.md` covering broker-specific setup and limitations

The core runtime already owns serialization, outbox behavior, retries, delayed publishing, consumer invocation, circuit breaking, and diagnostics orchestration. The transport package should not reimplement those policies.

## DI and package shape

Register the broker through `MessagingSetupBuilder.RegisterExtension(...)`. A transport package should add:

- `MessageQueueMarkerService("MyBroker")`
- validated broker options
- immutable `MessagingProviderCapabilities` contributions declaring supported lanes and native affinity routes
- singleton `IBusTransport` and/or `IQueueTransport` for the declared lanes
- singleton `IConsumerClientFactory`
- any broker-owned singletons such as connection pools

```csharp
public static class SetupMessagesMyBroker
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder UseMyBroker(Action<MyBrokerOptions> configure)
        {
            setup.RegisterExtension(new MyBrokerOptionsExtension(configure));
            return setup;
        }
    }

    private sealed class MyBrokerOptionsExtension(Action<MyBrokerOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("MyBroker"));
            services.Configure<MyBrokerOptions, MyBrokerOptionsValidator>(configure);
            services.AddMessagingProviderCapabilities(MessagingProviderCapabilities.Transport(
                "MyBroker", [MessageLane.Queue], supportsIndependentLaneTopology: true));
            services.AddSingleton<IQueueTransport, MyBrokerTransport>();
            services.AddSingleton<IConsumerClientFactory, MyBrokerConsumerClientFactory>();
        }
    }
}
```

## Runtime contract

### `ITransport`

`ITransport.SendAsync(...)` receives a fully prepared `TransportMessage`.

The transport should:

- publish `message.Body` as the broker payload
- preserve `message.Headers`
- return `OperateResult.Success` on broker success
- return `OperateResult.Failed(new PublisherSentFailedException(...))` on broker failure
- let `OperationCanceledException` propagate

`BrokerAddress` feeds diagnostics, OpenTelemetry, and dashboard surfaces. It should be a sanitized operator-facing value, not a raw connection string with credentials.

### `IConsumerClientFactory`

`CreateAsync(groupName, groupConcurrent, cancellationToken)` is called twice in practice:

- once during startup/topology discovery with the host-stopping token
- once for each live consumer thread with its linked consumer-group token

The factory should therefore be safe to call repeatedly, and client construction should not start background receive loops too early.

Factories must let `OperationCanceledException` escape unchanged instead of wrapping shutdown cancellation as `BrokerConnectionException`.

### `IConsumerClient.FetchMessageNamesAsync`

This is the broker-normalization and provisioning hook.

Use it for broker-specific work such as:

- creating topics, streams, queues, or subscriptions
- translating wildcard topics
- mapping friendly topic names to broker-native identifiers like ARNs

If the broker uses topic names as-is, the default pass-through behavior is enough.

The method receives the host-stopping token and must pass it through broker connection and topology operations.
When a provider SDK operation has no native cancellation parameter, await it through a cancellation-aware wait and retain the provider's existing timeout.

### `IConsumerClient.SubscribeAsync`

The method receives the linked consumer-group token and must pass it through broker subscription and topology operations.
Apply the same cancellation-aware wait rule to subscription operations that lack native cancellation.

This should bind the current consumer group to the resolved message names produced by `FetchMessageNamesAsync(...)`.

### `IConsumerClient.ListeningAsync`

This method owns the long-running receive loop.

For every delivery, the consumer client should:

- build a `TransportMessage`
- inject `Headers.Group` with the active group name
- pass a broker-specific commit token to `OnMessageCallback(message, commitToken)`

Do not swallow `OnMessageCallback` exceptions inside the transport. The framework decides whether to commit, reject, retry, or trip the circuit breaker.

### `CommitAsync` and `RejectAsync`

`CommitAsync(sender)` and `RejectAsync(sender)` must map the callback token back to broker semantics:

- ack / nack
- delete / abandon
- commit / seek
- complete / dead-letter / requeue

If the broker cannot reject, make that explicit and implement the best available no-op or requeue behavior.

### `PauseAsync` and `ResumeAsync`

These methods are used by the circuit breaker for transport-level backpressure.

They should be:

- idempotent
- safe to call concurrently
- effective at stopping new message pulls after `PauseAsync` returns

In-flight deliveries may complete naturally.

### `OnLogCallback`

Emit meaningful `MqLogType` events for:

- connection failures
- broker shutdown
- consumer registration and cancellation
- receive-loop errors

This is how transport health and restart behavior stay accurate in the core runtime.

### `DisposeAsync`

Dispose only resources owned by that client instance. Do not tear down shared pools or shared connections still used elsewhere in the package.

## Header and payload rules

Publish-side headers come from the core pipeline. The transport must round-trip at least:

- `Headers.MessageId`
- `Headers.MessageName`
- `Headers.Type`
- `Headers.CorrelationId`
- `Headers.CorrelationSequence`
- `Headers.SentTime`

It should also preserve optional headers such as:

- `Headers.CallbackName`
- `Headers.DelayTime`
- `Headers.TenantId` (multi-tenancy identifier; populated from `PublishOptions.TenantId`, exposed on `ConsumeContext.TenantId`)
- `Headers.TraceParent`
- custom application headers

Additional rules:

- `Headers.Group` is added on consume, not publish
- `Headers.TenantId` is enforced by a strict 4-case integrity policy in the core publish pipeline; transports must round-trip the value verbatim and never originate, rewrite, or strip it
- the body should be treated as raw bytes unless the broker API forces encoding/decoding
- exception details, credentials, and other secrets must not be leaked through headers or `BrokerAddress`

## Provider-neutral routing affinity

`MessageOptions.RoutingAffinityKey` is the outbound authority. `TransportMessage.RoutingAffinityKey` and `MediumMessage.RoutingAffinityKey` read the reserved `headless-routing-affinity-key` envelope field. Do not accept it through custom application headers or a provider header contribution. Keep the envelope authoritative: all official storages serialize it, and retries retain the same logical key without adding a schema column.

A provider contributes immutable `MessagingRoutingAffinityRoute` entries through its existing `MessagingProviderCapabilities.Transport(...)` contribution. Each entry identifies one registered `(MessageLane, MessageName)` destination and a `MessagingRoutingAffinityMapping` describing the native header adapter, optional maximum key length, printable-ASCII restriction, and additional raw headers that must match. Empty entries explicitly mean no supported native affinity destinations. A factory contribution may resolve and snapshot inert options and registrations; it must never resolve a broker client, transport, processor, or storage implementation to discover support. The composed Core capability model is the sole runtime authority.

For a known, configured destination, a contribution can include:

```csharp
MessagingProviderCapabilities.Transport(
    "MyBroker",
    [MessageLane.Queue],
    supportsIndependentLaneTopology: true,
    routingAffinityRoutes:
    [
        new(MessageLane.Queue, "orders.changed", new MessagingRoutingAffinityMapping("my-native-key")),
    ]);
```

`RequireRoutingAffinity()` declares a startup requirement on the registered route, independently of the optional per-send key. Unsupported required routes fail before clients/processors start. Every keyed publication checks its exact registered destination and raw adapters before outbox insertion or transport I/O. Unknown keyed destination overrides reject; do not guess from a provider-wide flag or auto-provision a different topology. Validate raw application headers before evaluating a selector that could overwrite them, and validate the resulting provider contribution again. Transport adapters also validate before renting producers or creating senders, including retry dispatch.

| Transport | Native mapping | Locally required configuration / limits |
| --- | --- | --- |
| Kafka | String message key, UTF-8 | Queue only; registered topic; deterministic key partitioner; no Headless-specific length cap |
| Pulsar | Native message key | Registered Bus/Queue topic; built-in keyed routing; no Headless-specific length cap |
| Azure Service Bus | `SessionId` | Session-enabled registered route; maximum 128 UTF-16 code units; raw `SessionId` and `PartitionKey` agree |
| AWS | `MessageGroupId` | Registered `.fifo` SNS/SQS destination; 1–128 ASCII characters from `!` through `~`, no spaces |
| NATS, RabbitMQ, Redis, InMemory | Unsupported in current topology | Reject required routes and keyed requests; keep existing unkeyed behavior |

Kafka accepts `consistent`, `consistent_random`, `murmur2`, `murmur2_random`, `fnv1a`, and `fnv1a_random`; random-only or unrecognized partitioners are unverifiable. Pulsar key routing does not itself request `Key_Shared`. Azure non-session partition keys are insufficient evidence for this contract. Standard SQS group fairness is not the FIFO-group affinity mapping. NATS subject shards, RabbitMQ hash exchanges, and Redis stream sharding are not silently inferred or installed.

Startup checks establish local declaration consistency. Broker I/O must separately prove the actual session/FIFO/partition topology and permissions. Conformance binds every official provider to either native mapping or deterministic rejection, checks direct publication and production outbox dispatch, round-trips all three storages, and preserves keys on broker redelivery. Native SDK observers inspect Kafka keys/partitions, Azure sessions, SQS message groups, and Pulsar keys. A missing Azure test connection skips real broker evidence; mocked senders are not substitutes for that evidence.

Promise only affinity within the configured topology. Do not promise global FIFO, same-key nonconcurrent handling, different-key partition uniqueness, or stable placement when partition count or hashing changes. Delivery remains at-least-once; consumers still own idempotency for external effects.

Stored keyed outbox rows are revalidated against the current frozen destination mapping before attempt reservation or native client resolution. Normal retry pickup may already hold a storage lease at this point. A deployment that removes or invalidates their mapping rejects dispatch until the operator restores a supported configuration. Unkeyed legacy rows keep their existing behavior.

The field is additive and needs no new relational schema. Old envelopes yield null. Raw provider hooks remain adapters; typed/raw disagreement is rejected before effects. Before enabling keys, drain or fence older publishers and outbox/retry workers and upgrade the Messaging package family together: older workers can ignore the neutral field. Verify remote topology before enabling publication, and drain or fence keyed backlog before rollback. Headless does not migrate broker topology or consumer schemas automatically.

Keyed SQS Queue sends encode the complete header dictionary, including null, delivery, trace, and business metadata, as one String attribute named `headless-aws-headers-v1`; the payload body and native `MessageGroupId` remain unchanged. Only the exact `headless-aws-headers-v1` attribute is reserved. Consumers recognize that exact attribute as the bag and otherwise accept legacy individual attributes, including other names with the same prefix. A recognized bag mixed with other attributes, or a malformed bag, is terminally deleted from its source queue without a handler callback. Unkeyed Queue sends retain the existing ten-attribute limit. SNS Bus retains its existing envelope format.

Deploy the new consumers before enabling typed keys, and fence or drain all old consumers plus old publishers/outbox/retry workers. Older consumers cannot decode the keyed SQS bag and may terminally delete it. Rollback after keyed publication requires draining or fencing that backlog; a database rollback alone does not restore wire compatibility.

## What the package should not do

The transport package should not:

- reimplement serialization policy already handled by `ISerializer`
- invent its own retry policy around `OnMessageCallback`
- commit before the framework finishes processing the message
- hide broker failures by swallowing exceptions and returning success
- expose raw credentials in logs, exceptions, or `BrokerAddress`
- couple itself to one app's consumer registration conventions

## README checklist for the provider package

Each provider `README.md` should document:

- how to register the transport with `AddHeadlessMessaging(...)`
- required options and credential setup
- publish semantics, including whether broker-side scheduling exists
- consume semantics for commit, reject, redelivery, and dead-letter behavior
- ordering guarantees under broker-native rules and `ConsumerThreadCount`
- auto-provisioning done by `FetchMessageNamesAsync(...)` or `SubscribeAsync(...)`
- topic naming restrictions, required custom headers, and payload limits

## Good implementation signals

A provider is usually aligned with the framework when:

- direct publishing works through `ITransport` without special-case code in core
- destination provisioning is isolated to `FetchMessageNamesAsync(...)`
- every consumed message reaches `OnMessageCallback(...)` with a valid `Headers.Group`
- commit/reject behavior is broker-correct and symmetric with the callback token
- pause/resume keeps the long-running listener alive; a provider may cancel an in-flight broker receive to reach its pause gate, but it must install fresh receive state before reopening the gate
- health and broker failures surface through `OnLogCallback`
