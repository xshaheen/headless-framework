---
domain: Messaging
packages: Messaging.RabbitMq
---

# RabbitMQ transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.RabbitMq` transport: lane exchanges and queues, deploy-time topology through `resources provision`, prefetch, publish confirms, dead-lettering, and single active consumer.

## Headless.Messaging.RabbitMq

Use this transport for a general-purpose broker with routing keys and queue semantics. It is the only transport whose shared topology is a [provisioned resource](../utilities.md#resource-provisioning): the deployment creates it, and the running host only checks it.

### Setup

```bash
dotnet add package Headless.Messaging.RabbitMq
```

```csharp
setup.UseRabbitMq(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "app_user"; // required
    options.Password = "app_secret"; // required
});

setup.Tune("orders.projection", consumer => consumer.UseRabbitMq(rabbit => rabbit.PrefetchCount(20)));
```

Then create the topology as a deploy step, before the new release serves traffic:

```bash
dotnet MyApp.dll resources provision -k RabbitMQ
```

**Verify:** `resources provision` prints the `RabbitMQ` `topology` line with status `Ok`, then the `warmup` readiness check turns `Healthy` once the host's [topology check](transports.md#topology-check) passes, and `messaging-broker` reports `Healthy`. Until provisioning runs, `warmup` stays `Unhealthy` and names the first missing object, such as `RabbitMQ queue 'queue.orders' does not exist. Run 'resources provision' ...`.

### Names and topology

For the base exchange `ExchangeName` (default `messaging.default.router`):

| Lane | Exchange | Routing key | Queue |
| --- | --- | --- | --- |
| Bus | `{ExchangeName}.bus` (topic) | `bus.{logical-name}` | `bus.{consumer-identity}`, one per competing consumer identity |
| Queue | `{ExchangeName}.queue` (direct) | `queue.{logical-name}` | `queue.{logical-name}`, one per message |

Bus and Queue have independent topology, so one contract name can carry both lanes without cross-delivery.

With `Headless:Prefix` set to `wt1`, the base exchange becomes `wt1.{ExchangeName}` and each live queue starts with `wt1.`, so environments that share one virtual host keep separate topology. Routing keys stay the same. A prefix that makes an exchange or a queue-lane queue longer than 255 characters fails startup. See [host prefix](transports.md#host-prefix).

Dead-letter queues start with `headless_dlq.` followed by the live queue's physical name, including any isolation prefix. If that combination exceeds 255 characters, the suffix is the SHA-256 of the physical name. The reserved first segment lets a valid live queue name end in `.dlq`.

**What the topology resource does:**

- `provision` declares both lane exchanges, one queue per competing consumer subscription and per registered Queue-lane message (consumed on this host or not), the dead-letter queues when dead-lettering is on, and the bindings. Every shared queue is durable, not exclusive, and not auto-deleted.
- `verify` passively declares the lane exchanges, live queues, and any configured dead-letter queues. AMQP cannot check a binding, so no host start binds a shared queue. A release that adds a subscription, or a message to an existing one, must run `resources provision`, or its new messages never reach the queue.
- `purge` purges each queue this host consumes and its dead-letter queue.
- `delete` deletes those queues and keeps the exchanges.
- `status` reports each queue's message and consumer counts.
- Purge and delete never touch a queue the host only publishes to, because it belongs to the service that consumes it. The lane exchanges and their `.dlx` are never deleted, because every service on the broker shares `ExchangeName` by default. Delete them by hand, or drop the vhost, once no service uses the exchange name.

When upgrading from `{queue}.dlq` names, run `resources provision` for the existing routes to bind the new dead-letter queues. Remove each old binding from its lane's `.dlx` exchange to stop duplicate delivery. The binding's routing key is the physical live queue name. See the [RabbitMQ HTTP API](https://www.rabbitmq.com/docs/http-api-reference).

Verify that the old queue serves only as a dead-letter queue before draining it. To transfer its backlog, use a dynamic Shovel from the old queue to the new dead-letter queue with `ack-mode` set to `on-confirm`. If a new live route needs the old queue name, delete the old queue after draining it and before provisioning that route. See [RabbitMQ dynamic Shovels](https://www.rabbitmq.com/docs/shovel-dynamic).

**What the running host does:**

- Consumer clients make no declare for shared topology and fail with `ResourceProvisioningException` naming `resources provision` when a queue they consume is missing.
- A publisher checks the lane exchanges on each new pooled channel, and a Queue-lane queue once per process before its first send.
- The host still creates per-process objects: an every-instance consumer's exclusive queue, the request/reply queue, and runtime subscriptions' queues. See [Runtime create rights](transports.md#runtime-create-rights) for the permissions they need.

### Configuration

Configure host, credentials, exchange, queue arguments, QoS, and custom headers through `RabbitMqMessagingOptions`. `UserName` and `Password` are `required`, and the validator rejects the RabbitMQ default `guest`/`guest`.

| Option | Default | Effect |
| --- | --- | --- |
| `PublishConfirms` | `true` | A publish waits for the broker acknowledgement. A negative acknowledgement or an unroutable Queue-lane return fails it. `false` trades those guarantees for lower latency: a publish succeeds once written to the socket, and a `basic.return` is discarded |
| `ChannelPoolSize` | `15` | The most AMQP channels the publish pool holds and hands out at once. A publish holds a channel for its duration, so this caps publishes in flight on one connection. Raise it for a high-volume publisher waiting on confirms. Must be greater than 0 |
| `ExchangeName` | `messaging.default.router` | Base name of the lane exchanges |
| `QueueArguments.EnableDeadLettering` | `false` | Each shared queue dead-letters to the direct exchange `{lane exchange}.dlx` with the live queue name as routing key. Its durable `headless_dlq.` queue receives malformed envelopes, messages the core poisons on arrival, messages whose `MessageTTL` expires, and messages past `DeliveryLimit`. AMQP's reject carries no reason, so the dead-lettered copy's `x-death` header records `rejected`. The core's reason code stays in its poison row. Use broker tooling to inspect, shovel, or purge the dead-letter queue |
| `QueueArguments.DeliveryLimit` | `null` | Sets `x-delivery-limit`: how many times a quorum queue redelivers a returned message before dropping or dead-lettering it. Requires `QueueType = "quorum"`. Must be greater than 0. RabbitMQ 4.x quorum queues apply a broker default of 20 when unset |
| `QueueArguments.MessageTTL` | 864000000 ms (10 days) | Queue message TTL |
| `BasicQosOptions` | unset | Host-wide QoS; see prefetch below |
| `ConnectionFactoryOptions` | unset | Configures the RabbitMQ.Client `ConnectionFactory`, for example `MaxInboundMessageBodySize` |

```csharp
setup.UseRabbitMq(options =>
{
    options.UserName = "app_user";
    options.Password = "app_secret";
    options.QueueArguments.QueueType = "quorum";
    options.QueueArguments.DeliveryLimit = 10;
    options.QueueArguments.EnableDeadLettering = true;
});
```

RabbitMQ never changes the arguments of an existing queue. Changing `EnableDeadLettering`, `DeliveryLimit`, `QueueType`, `QueueMode`, or `MessageTTL` for a queue that exists fails its declare with `PRECONDITION_FAILED`. Delete the queue and run `resources provision` again, or apply the setting by broker policy.

**Prefetch.** A consumer with neither `PrefetchCount(...)` on `Tune` nor `BasicQosOptions` gets a prefetch of `max(1, Concurrency) * 4` (4 for a sequential consumer), so each handler slot has its next delivery buffered. The cost: each consumer holds up to that many unacknowledged messages other instances cannot take, so work spreads less evenly and a slow instance sits on its buffer. `PrefetchCount(...)` on `Tune` wins over `BasicQosOptions`, which wins over the default. Set `PrefetchCount(1)` for strict one-at-a-time fair dispatch. A sequential consumer still dispatches one delivery at a time, unless `ConnectionFactoryOptions` raises the client's `ConsumerDispatchConcurrency` above 1.

**Routing affinity.** No native mapping. `RequireRoutingAffinity()` fails startup. A supplied `RoutingAffinityKey` travels as the envelope header, which a consumer tuned `Sequential()` orders by, and does not change the routing key.

### RabbitMQ single active consumer

The queue of a competing consumer tuned [`Sequential()`](ordering.md#sequential-consumers) carries `x-single-active-consumer = true`. The broker delivers to one consumer at a time across every host and fails over to a standby when the active one goes. `resources provision` declares the argument, and so does a host whose client declares its own queue. Because it is a queue argument, turning `Sequential()` on or off for a consumer whose queue exists fails that queue's declare with `PRECONDITION_FAILED`: delete the queue and run `resources provision` again. Every-instance and non-sequential consumers keep queues without it.

**Verify:** the `describe` host manifest shows `"sequential": true` with `"activeHost": "broker"` for the consumer.

### Design and runtime behavior

- **A Queue-lane message is never dropped for want of a consumer.** `resources provision` creates and binds `queue.{logical-name}` for every registered Queue-lane message. Before its first send of a message name, each process checks the queue exists. A missing queue fails the publish with a message naming `resources provision`, and the next publish checks again. Queue-lane publishes are `mandatory`, so an unroutable message (a deleted queue, an unbound operator-managed queue) comes back as `basic.return`. With `PublishConfirms`, the publish then fails with a `PublishReturnException` inner exception and the outbox retries it. A Bus publish with no subscribed identity succeeds, because nobody is subscribed.
- **Malformed envelopes** are rejected without requeue (they dead-letter when dead-lettering is on). Ordinary handler rejection requeues.
- **Each delivery is settled at most once.** A repeated or unknown delivery tag is ignored, because the broker closes the channel on a tag it does not know. A callback that throws without settling the delivery requeues it.
- **Body size.** RabbitMQ.Client closes a consumer's connection when a body exceeds `ConnectionFactory.MaxInboundMessageBodySize` (64 MiB by default, below the broker's 128 MiB). The transport therefore fails a larger publish before it reaches the broker, and the reply transport throws `PublisherSentFailedException`. Raise the limit through `ConnectionFactoryOptions`.
- **Connection recovery.** RabbitMQ.Client's automatic recovery owns a lost connection. During recovery a publish fails and the outbox retries it, without cancelling the recovery. The pool replaces a connection the client will not recover: one this process closed, one refused with `ACCESS_REFUSED`, or one still not back two minutes after it was lost.
- **Shutdown** stops dispatching, waits for running handlers within the remaining `MessagingOptions.ShutdownTimeout` (30 seconds when disposed directly), then closes the channel, so a handler that finishes in time gets its ack. A handler past the budget loses its ack and the message is redelivered.
- **Ordering.** A queue delivers to one consumer in publish order. Competing consumers each get a share, except on a sequential consumer's queue. See [Transport-Specific Ordering](ordering.md#transport-specific-ordering).
- **Delivery deadline.** A message with `DeliverWithin` gets `BasicProperties.Expiration` set to the remaining time. RabbitMQ expires a per-message TTL only at the head of its queue, so behind a backlog the consumer's deadline check drops it instead. With dead-lettering on, an expired message goes to the dead-letter queue.
- **Every-instance consumers** get a server-named exclusive, non-durable queue on a connection of their own, with a fixed 60-second message TTL; see [Every-instance Bus delivery](consumers.md#every-instance-bus-delivery).
- **Request/reply** uses an exclusive `headless.reply.{32 hex}` queue per calling process, with a new address after a connection loss; see [Provider support](request-reply.md#provider-support).
- **Raw producers** must set the AMQP `message-id` property or the `headless-msg-id` header. A raw delivery without either is rejected terminally; see [Message identity](foreign-producers.md#message-identity).
