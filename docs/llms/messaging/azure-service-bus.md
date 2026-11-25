---
domain: Messaging
packages: Messaging.AzureServiceBus
---

# Azure Service Bus transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.AzureServiceBus` transport: topics and subscriptions for the Bus lane, queues for the Queue lane, sessions, and settlement.

## Headless.Messaging.AzureServiceBus

Use this transport for Azure-hosted topics and queues with sessions and managed operations.

### Setup

```bash
dotnet add package Headless.Messaging.AzureServiceBus
```

```csharp
setup.UseAzureServiceBus(options => options.ConnectionString = connectionString);

services.ConfigureMessaging(messaging =>
    messaging
        .Message<OrderPlaced>("orders.placed")
        .OnBus(bus => bus.UseAzureServiceBus(asb => asb.PartitionKey(order => order.CustomerId.ToString())))
);
```

A `[BusConsumer("orders.projection")]` consumer of `OrderPlaced` gets the subscription `orders.projection` on the Bus topic. Bus and Queue have independent topology, so one contract name can carry both lanes without cross-delivery.

**Verify:** the `messaging-broker` readiness check reports `Healthy` once a receive or send reaches the namespace ([Broker reachability](operations.md#broker-reachability)), and the `describe` host manifest's `messaging.transports` shows `"servicebus"` for both lanes. A missing entity logs `AzureServiceBusSubscriberReceivedError` (EventId 49) with `MessagingEntityNotFound`.

### Configuration

Configure the connection, client, topic and queue behavior, sessions, and SQL filters through `AzureServiceBusMessagingOptions`.

- **Authentication.** Set either `ConnectionString`, or both `Namespace` and `TokenCredential`. The validator requires exactly one mode at start.
- **`ClientOptions`** (`Action<ServiceBusClientOptions>`) configures the one shared `ServiceBusClient` when it is first created: `RetryOptions`, `TransportType` (`AmqpWebSockets` where port 5671 is blocked), `WebProxy`, and `Identifier`. It applies to every publisher and consumer of the namespace.
- **`TopicPath`** (default `messaging`) is the Bus topic. `ConfigureCustomProducer<T>(...)` sends one message type to its own topic instead. With `Headless:Prefix` set to `wt1`, both topics and every queue-lane queue start with `wt1.`; subscriptions keep their names, and a path the prefix pushes past 260 characters fails startup. See [host prefix](transports.md#host-prefix). Validation rejects two custom producers for one message type, because a send looks the producer up by message name.
- **`AutoProvision`** (default `true`) creates topics, subscriptions, queues, and subscription rules. Every-instance subscriptions require it and Manage rights; with it off, an every-instance consumer fails startup.
- **Concurrency comes from the consumer.** A consumer's `Concurrency` sets the processor's `MaxConcurrentCalls`. With `EnableSessions`, it sets `MaxConcurrentSessions` instead, and each session runs one message at a time, so the session stays in order.
- **`PrefetchCount`** (default 0) sets how many messages each processor fetches ahead of its handlers. A prefetched message's lock is not renewed until a handler takes it, so keep `PrefetchCount` near the consumer's concurrency. A prefetched message whose lock expires is redelivered and counts toward `SubscriptionMaxDeliveryCount` (default 10).
- **`SessionIdleTimeout`** is how long a session slot waits on a quiet session before it moves on. It matters when `Concurrency` leaves few slots.
- **Routing affinity.** `RoutingAffinityKey` maps to the native `SessionId` on registered session-enabled routes, at most 128 UTF-16 code units. Queue routes require `EnableSessions`. Bus routes can use global sessions or a matching custom producer with sessions. Raw `SessionId` and `PartitionKey` must both agree with a typed key. Without sessions the key travels as the envelope header only. Startup does not query the broker, so make sure the actual queue or subscription requires sessions.
- **`PartitionKey(...)`** is message-side only and limited to 128 characters. With sessions enabled, Azure Service Bus requires `PartitionKey` to equal `SessionId`, and the message builder rejects a mismatch. When a publish sets `PartitionKey` without a `SessionId`, the session id falls back to the partition key, then to the message id.
- **Settlement is not configurable.** Headless turns off SDK auto-complete and completes or abandons each message itself after the inbox write and the handler outcome.
- Not supported yet: request/reply. A host that sends requests or declares a responder fails startup.

### Design and runtime behavior

- **One connection per namespace.** Bus and Queue publishing and every consumer processor share one `ServiceBusClient` (one AMQP connection) per namespace, with cached senders per destination and a shared administration client. Shutdown drains senders before the client.
- **Deleted entities recover in place.** A competing consumer keeps its processors when its queue or subscription is deleted. Each processor retries the failed receive on the SDK's back-off and receives again once an entity with the same name exists, with no rebuild and no restart. Until then every failed receive logs `AzureServiceBusSubscriberReceivedError` (EventId 49, Error) with `MessagingEntityNotFound`, and the `messaging-broker` readiness check turns `Unhealthy`. Subscription rules sync only when the client subscribes, so recreate a competing subscription with its rules, or restart the host so `AutoProvision` applies them. An every-instance subscription is recreated with its rules by the client.
- **Idle reachability probe.** A processor reports no success while idle. After a failed receive, the client peeks its entity on a real-time back-off (about 1 second, doubling to 30) until a peek succeeds or a message arrives, then reports the broker reachable. A peek locks and settles nothing.
- **Dead-lettering.** A malformed envelope, including one missing a required Messaging header, goes to the entity's dead-letter subqueue with `DeadLetterReason` `MalformedEnvelope` and the exception type as `DeadLetterErrorDescription`. It never reaches the core, so the subqueue is its only record. A message the core poisons on arrival is dead-lettered with the core's reason code (`SubscriberNotFound`, `ReceiveRejected`, `DeserializationFailed`, or `ReceiveFailed`) and description. Retryable handler or custom-header failures are abandoned, not dead-lettered.
- **Every-instance subscriptions.** Named from the identity and the host instance id, with `AutoDeleteOnIdle` of 5 minutes. Created on subscribe, kept across client rebuilds, deleted after a graceful stop (each delete bounded by 30 seconds), and recreated if Azure deleted it during a long disconnect; see [Every-instance Bus delivery](consumers.md#every-instance-bus-delivery).
- **Delivery deadline.** A message with `DeliverWithin` gets `ServiceBusMessage.TimeToLive` set to the remaining time, so an unconsumed message expires inside the broker. An entity that dead-letters on expiration moves it to the dead-letter queue.
- **Stopping.** Stopping a consumer stops every processor at once and returns within the shutdown budget (30 seconds when disposed directly), even while a handler runs. A handler that outlives the budget can still settle its message.
- **Pausing.** When the circuit breaker opens, every processor starts to stop and the pause returns without waiting, because the stop waits for in-flight handlers and the pause runs inside the handler that tripped it. A resume also returns without waiting. The processors start again once the stop finishes, unless a later pause stopped them again.
- **Ordering.** FIFO per session when `EnableSessions = true`; see [Transport-Specific Ordering](ordering.md#transport-specific-ordering).
