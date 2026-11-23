---
domain: Messaging
packages: Messaging.Nats
---

# NATS transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.Nats` transport: JetStream streams and durables, declared and derived streams, shards, external subjects, ordering, and connection handling.

## Headless.Messaging.Nats

Use this transport for subject-based routing on NATS JetStream. Both lanes run on JetStream streams. Request/reply runs on core NATS outside JetStream.

### Setup

```bash
dotnet add package Headless.Messaging.Nats
```

```csharp
setup.UseNats(options =>
{
    options.Servers = "nats://localhost:4222";

    // Name the Queue stream for orders and keep its messages 3 days; undeclared messages keep derived streams.
    options.Streams.Own("ORDERS", stream => stream.Subjects("headless.queue.orders.>").MaxAge(TimeSpan.FromDays(3)));
    options.Streams.Bind("PAYMENTS", "headless.bus.payments.>"); // provisioned outside the application
});

services.ConfigureMessaging(messaging =>
    messaging
        .Message<OrderPlaced>("orders.placed")
        .OnBus(bus => bus.UseNats(nats => nats.SubjectShard(order => order.CustomerId.ToString())))
);

// Only when the producer shards OrderPlaced but this host declares it without SubjectShard:
setup.Tune("orders.projection", consumer => consumer.UseNats(nats => nats.Sharded()));

// A consumer whose receive stage is slow, with bounded redelivery:
setup.Tune("billing.ledger", consumer => consumer.UseNats(nats => nats.AckWait(TimeSpan.FromMinutes(2)).MaxDeliver(20)));
```

An app that also uses NATS directly (key-value store, object store) registers its own connection and hands it to Headless instead of opening a second one:

```csharp
services.AddSingleton<NATS.Client.Core.INatsConnection>(_ => new NATS.Client.Core.NatsConnection(
    new NATS.Client.Core.NatsOpts { Url = "nats://localhost:4222" }));

setup.UseNats(options => options.UseConnection(sp => sp.GetRequiredService<NATS.Client.Core.INatsConnection>()));
```

**Verify:** the `messaging-nats` readiness check reports `Healthy` (it sends a NATS `PING` and fails after 5 seconds without an answer), and the `describe` host manifest's `messaging.transports` shows `"nats"` for both lanes. A diverged stream under `Verify` fails the consumer start or the first publish with the divergent fields.

### Subjects, streams, and durables

- **Subjects.** Bus publishes to `headless.bus.{logical-name}`. Queue publishes to `headless.queue.{logical-name}`. Every name includes `MessagingOptions.MessageNamePrefix` when set.
- **Host prefix.** With `Headless:Prefix` set to `wt1`, subjects start with `wt1.`, derived and `Own` streams with `wt1_`, and durables with `wt1_`; `Bind` streams and external subjects keep their names. See [host prefix](transports.md#host-prefix).
- **Durables.** A Bus consumer identity has one `bus_{consumer-identity}` durable on each stream it reads, filtering on the subjects of every message it consumes there. Filtering on more than one subject needs NATS server 2.10 or later; an older server fails the consumer's start instead of delivering the whole stream. A Queue message has one shared `queue-{logical-name}` durable.
- **Which stream holds a message.** The stream declared in `NatsMessagingOptions.Streams` whose subjects cover the message's subject. Otherwise a derived stream `headless-{lane}-{key}`, where the key is the message name's first dot-separated segment. A derived stream carries the wildcard `headless.{lane}.{key}.>`, plus the bare `headless.{lane}.{key}` only for a message named exactly the key, so every host asks for the same subjects. Consumer filters stay exact per message.
- **Retention.** Derived Bus streams are interest-retained. Derived Queue streams are work-queue-retained. `StreamOptions` may tune storage, replicas, and limits but cannot replace provider-owned names, subjects, or retention.

**Declare a stream** in `Streams` when its name, subjects, retention, or limits matter to operators, or when something outside the app manages it:

- `Own(name, s => ...)` declares a stream Headless creates and keeps in shape under `StreamProvisioning`. It takes `Subjects(...)` (wildcards allowed, at least one required), defaults to limits retention, and accepts `Retention(...)`, `MaxAge(...)`, `MaxBytes(...)`, `DuplicateWindow(...)`, and `Configure(config => ...)`. `Configure` runs before `StreamOptions`, and neither may change the name, subjects, or retention. Owned declared streams are created in the background at host start. A failure there logs EventId 16 (`NatsStreamWarmupFailed`) and never fails startup, because the first publish or consumer start ensures the stream again.
- `Bind(name, subjects)` declares a stream created by the NATS CLI, Terraform, or a Kubernetes operator. Headless never creates, updates, or deletes it, even under `Reconcile`. It fails a publish or consumer start when the stream is missing, lacks the declared subjects, or uses work-queue retention for Bus messages.
- A declaration is checked when you make it. The name may not contain whitespace, `.`, `*`, `>`, `/`, or `\`, may not start with the reserved `headless-`, and may not repeat another declaration. Subjects may not overlap another declared stream (JetStream refuses overlapping streams) or the reserved `headless.reply.>`, `$JS.>`, `$SYS.>`, or `_INBOX.>`.
- Keep declared Headless messages on the lane prefixes. A stream that covers `headless.queue.orders.>` carries only the Queue lane.
- Choose retention for the lane. Work-queue retention refuses a second Bus consumer identity, and interest retention discards a Queue message no consumer exists for yet. Limits retention bounded by an age suits both lanes and is the default for `Own`. On the Bus lane, retention changes storage, not delivery: a Bus consumer starts at messages published after it was created.

**`StreamProvisioning`** decides what a host does about a stream it owns (derived, or declared with `Own`) at consumer start and before the first publish to it:

| Value | Behavior |
| --- | --- |
| `Verify` (default) | Creates a missing stream. Throws with the divergent fields instead of writing to one that exists |
| `Reconcile` | Updates fields JetStream accepts in place. Reports divergence on an immutable field, such as storage type, with a recreate-or-migrate remedy |
| `Disabled` | Neither creates nor modifies |

The comparison covers only fields the provider or your `StreamOptions` callback sets. Extra subjects on a live stream are ignored. A subject this host requires that the stream does not cover is a divergence, because JetStream delivers nothing, with no error, to a filter that matches no subject. A host whose streams are provisioned outside the app should declare them with `Bind`, not set `Disabled`: under `Verify` an undeclared message gets a derived stream, and JetStream refuses one that overlaps another. Keep `Disabled` for a host that must never call JetStream's stream API.

**Age and duplicate window.**

- Every owned stream gets an age limit: its declared `MaxAge`, else `DefaultStreamMaxAge` (7 days). `TimeSpan.Zero` keeps messages without an age limit. Under `Verify` an existing stream with a different age limit fails as divergent, and `Reconcile` updates it.
- Every publish carries the Headless message id as `Nats-Msg-Id`, and a stream drops a repeat it still remembers. An owned stream remembers ids for its declared `DuplicateWindow(...)`, else `DefaultDuplicateWindow` (2 minutes, the server default). The default shrinks to fit a shorter age limit. A declared window longer than the age limit fails. An outbox retry after the window stores a second copy, which the consumer's inbox still delivers once. A longer window costs server memory per remembered id.

**Publishing and stream ensure.**

- A publisher ensures each stream once per process, so a publish-only host works before any consumer host starts.
- A failed ensure fails that publish (a durable publish retries from the outbox), and later publishes to the stream fail with the same error, without a broker request, until a back-off ends. A configuration fault (a diverged stream, a missing or mismatched bound stream, a 4xx API error) backs off 30 seconds, doubling to 5 minutes. Any other fault (a `StreamCreateTimeout`, a meta leader election, a lost connection) backs off 1 second, doubling to 30 seconds.
- A publish no stream answers (`NatsJSPublishNoResponseException`, or an acknowledgement with `seq=0`) fails and makes the next publish ensure again, which recovers a stream deleted while the host ran. A lost or late acknowledgement can also cause the no-response exception, and its message names both causes.
- A `StreamCreateTimeout` (default 30 s) that expires during a publish-time ensure fails that publish with `PublisherSentFailedException`. Only your own cancellation propagates as `OperationCanceledException`.
- On an interest-retained Bus stream with no consumer yet, a publish is discarded. The first ensure that finds no consumer logs one warning per stream (EventId 17, `NatsInterestStreamWithoutConsumer`). Start Bus consumers before publishers, or send a message that must wait for its consumer on the Queue lane.
- To keep environments apart on one NATS server, give each its own `Headless:Prefix` (see [host prefix](transports.md#host-prefix)) or its own NATS account. Only an account separates credentials.

### Shards

`SubjectShard(...)` appends one subject token to the logical message name. It rejects `.`, `*`, `>`, whitespace, and control characters, so payload values cannot change the subject hierarchy.

A consumer of a message whose contract, declared in this host, uses `SubjectShard(...)` filters on `{subject}.>` automatically. When another service shards a message that this host declares without `SubjectShard(...)`, tune the consumer with `.UseNats(n => n.Sharded())`. Without it NATS delivers zero messages, with no error. Sharing one contract declaration for the message in both services avoids the gap.

### Consumer settings

`Tune(identity, c => c.UseNats(nats => ...))` sets one consumer's JetStream limits on each of its durables. They apply after the host-wide `NatsMessagingOptions.ConsumerOptions` callback, so the consumer's value wins. Neither may change the durable name, filter subjects, or delivery policy. A later `UseNats(...)` for the same consumer replaces the earlier one's settings, so set them in one call.

| Setting | Default | Effect |
| --- | --- | --- |
| `AckWait(TimeSpan)` | 30 s | How long JetStream waits for settlement before it redelivers |
| `MaxAckPending(int)` | Server default 1000 | Deliveries that may wait for settlement across every instance of the consumer; counts every message type the identity consumes on the stream |
| `MaxDeliver(int)` | No limit | How many times JetStream delivers a message. Rejections count. While a circuit is open, a message that reaches the limit is dropped without Headless storing it, and JetStream publishes a `MAX_DELIVERIES` advisory. With the redelivery delay below, a limit of 10 survives roughly three minutes of an open circuit. A message the inbox already stored retries from storage instead |
| `InactiveThreshold(TimeSpan)` | Off | JetStream deletes the durables after no client has pulled for that long. A Bus durable starts at messages published after it is created, so one deleted during a longer outage loses the Bus messages published meanwhile. Set it well above any outage or deploy the consumer must survive. A Queue durable loses nothing the stream still holds |

### NATS ordering

A competing Bus consumer pulls each stream through one durable and one pull loop, so it receives that stream's messages in stream order across every message type it consumes there. Two events about one entity, such as `customers.registered` then `customers.updated`, arrive in the order the stream stored them when they share a stream: the same derived key (first segment of the message name) or one declared stream. Stream order is the order the publishes reached JetStream. An outbox that sends rows concurrently can store them in another order than the transactions committed.

A consumer tuned [`Sequential()`](ordering.md#sequential-consumers) keeps this order per routing affinity key at any `Concurrency`. Otherwise handler order holds only when one delivery runs at a time:

- `ConsumerThreadCount = 1` and the consumer's `Concurrency` left at 1. A higher thread count opens several pulls on one durable, and a higher concurrency runs handlers side by side.
- One running replica of the consumer identity. Replicas share the durable, so each gets part of the stream.
- Immediate retries keep the order. A delayed retry, a reject (an open circuit, a failed inbox write), an `AckWait` expiry, or a crash puts the message behind later ones.

Messages on different streams have no relative order, so keep messages that must stay ordered on one stream key, or declare a stream that covers them. Queue-lane messages are ordered per message type only.

**Changing an identity's messages during a rolling deploy.** Replicas of one identity share its durable, and each bind sets the filter subjects of its own release. Removing a message from an identity stops its delivery as soon as one new replica binds. Adding one is risky on an interest-retained stream: an old replica that binds again before it stops narrows the filter back, and the stream can drop the new message's pending deliveries. Ship a release that adds a message to an identity with a stop-and-start deploy, or accept that deliveries of the new message published during the rollout can be lost.

### External subjects

A producer that owns its own subjects, such as a Python worker publishing on `acme.{tenant}.runs.{run}.result`, is consumed by binding the contract's lane to a subject filter with `ExternalSubject(...)`. Declare the stream that captures the subjects in `NatsMessagingOptions.Streams`: `Own` when Headless should create it, `Bind` when it is managed elsewhere.

```csharp
using NATS.Client.JetStream.Models;

services.AddHeadlessMessaging(setup =>
{
    setup.UseNats(options =>
    {
        options.Servers = "nats://localhost:4222";
        options.Streams.Own(
            "ACME_RUNS",
            stream => stream.Subjects("acme.*.runs.*.result").Retention(StreamConfigRetention.Workqueue)
        );
    });
});

services.ConfigureMessaging(m =>
    m.Message<RunResult>("runs.result")
        .OnQueue(queue =>
            queue
                .RawPayload()
                .UseNats(nats =>
                    nats.ExternalSubject(
                        "acme.*.runs.*.result",
                        subject => subject.PublishTo(run => $"acme.{run.TenantId}.runs.{run.RunId}.result").TenantFromSubjectToken(1)
                    )
                )
        )
);

public sealed record RunResult(string TenantId, string RunId);
```

**Verify:** a message published on a matching subject reaches the consumer, and a misconfigured filter fails startup naming the contract, lane, and filter (checks below).

- **Consume.** The lane gets one durable on the covering stream with the filter as its subject filter. An every-instance Bus consumer opens a core subscription on the filter. The subject is the routing authority: a `headless-msg-name` header naming another contract makes the delivery malformed. `ExternalSubject` does not imply `RawPayload()`, so a binding whose producer sends Headless headers can stay strict; see [Foreign producers and raw payloads](foreign-producers.md#foreign-producers-and-raw-payloads).
- **Publish.** `PublishTo` computes each message's subject when the publish is created and stores it with the outbox row, so a retry publishes to the same subject. A computed subject that is not literal or does not match the filter throws `InvalidOperationException` before anything is stored. Without `PublishTo`, a literal filter is the publish subject, and a wildcard filter makes the lane consume-only. A publish with a null payload cannot run `PublishTo`: with a wildcard filter it throws `InvalidOperationException`, and with a literal filter it publishes to the filter.
- **Tenant.** `TenantFromSubjectToken(i)` reads zero-based token `i` of the arrival subject into `ConsumeContext.TenantId` and the inbox key. A missing token, one longer than 200 characters, or a `headless-tenant-id` header that differs from it makes the delivery malformed. Prefer the subject when producers' headers cannot be trusted: NATS permissions can restrict which subjects a user publishes to, not which header values it writes.
- **Startup checks,** all before any broker call: the filter is a valid subject pattern; it does not overlap `headless.>`, `_INBOX.>`, `$JS.>`, or `$SYS.>`; exactly one declared stream covers the whole filter (no stream is derived for a foreign subject); the lane does not also use `SubjectShard`; two contracts on one lane do not have overlapping filters; the tenant token lies inside a filter that does not end in `>`; and a Bus binding is not covered by an `Own` stream with work-queue retention. A `Bind` stream is checked against the server instead.

### Connection and configuration

Configure servers, credentials, stream behavior, and connection settings through `NatsMessagingOptions`:

- **`UseConnection(sp => ...)`** hands Headless a connection the app owns, for example the one `services.AddNatsClient(...)` registers. Publishing, request/reply, stream provisioning, and the `messaging-nats` health check use it. Headless never disposes it. `Servers`, `ConfigureConnection`, and `ConnectionPoolSize` do not apply, and a pool size other than `1` fails validation. Each consumer client still opens its own connection, copied from the supplied connection's options, because a stuck consumer is recovered by replacing its connection and NATS disconnects a slow consumer's whole connection.
- **`ConnectionPoolSize`** (default 1): one connection multiplexes all publishers. Raise it only for throughput.
- **`MaxConsecutiveConsumeFailures`** (default 10): a run of that many consume-loop failures of any type rebuilds the consumer client. A pull that misses heartbeats for 10 seconds counts as a failure, so a pull silent for about 100 seconds is rebuilt. Only a delivered message resets the run.
- **Routing affinity.** No native mapping. `RequireRoutingAffinity()` fails startup. A supplied `RoutingAffinityKey` travels as the envelope header, which a consumer tuned `Sequential()` orders by. No subject or stream is chosen from it.

### Design and runtime behavior

- **Pull consumption.** Each durable keeps a pull open. The client never buffers more messages than its handlers take at once (one for a sequential consumer, the concurrency otherwise), because a buffered message's `AckWait` runs while it waits. It pulls again once half the buffer is gone.
- **Work-in-progress signal.** While a delivery waits for a handler slot and runs its receive stage, the consumer tells JetStream every half `AckWait` that work is in progress, so a slow receive stage never causes a duplicate delivery. A crashed consumer still redelivers after `AckWait`.
- **Commit** uses JetStream double acknowledgement and waits for the broker's confirmation, so a replacement consumer cannot race an unconfirmed ack and redeliver a handled message.
- **Reject** sends a `NAK` with a jittered redelivery delay: about one second for the first delivery, doubling to 30 seconds. When the core asks for a longer delay, the `NAK` carries that one.
- **Malformed envelopes** (missing message id or name, unreadable headers) are terminated with `AckTerminate`, logged without payload or headers, and JetStream publishes a `MSG_TERMINATED` advisory. On server 2.10.4 and later the terminate carries the reason (`malformed headless envelope: <exception type>`). A message the core poisons on arrival is terminated the same way with the core's reason code and description. JetStream has no dead-letter queue, so the advisory and the core's poison row are the record.
- **Pausing** (circuit breaker open) and stopping cancel the pull and drain it: a message already buffered goes back to the stream with a plain `NAK`, so it redelivers at once to another instance, or to this one after a resume.
- **Connection failures** (`NatsConnectionFailedException`, `NatsJSConnectionException`, or a `NatsException` wrapping `SocketException` or `IOException`) end the listener so the client is rebuilt. JetStream protocol, timeout, and API errors, including a consumer deleted on the server, retry per durable with back-off and bind the durable again.
- **Shutdown.** NATS waits for every running handler before it closes the connection, within the remaining `MessagingOptions.ShutdownTimeout`, so a handler that finishes in time gets its ack to the server. Once shutdown starts, no new handler starts, and a delivery still waiting for a slot goes back with a plain `NAK`.
- **Per-message durables from older releases.** On first bind a Bus identity takes over any `bus-{consumer-identity}-{logical-name}` durables, starts after the earliest point they stopped, and deletes them, logging the takeover. The runtime user needs JetStream consumer info and delete permissions on them. A refused delete is logged on every bind with the `nats consumer rm` command to run.
- **Every-instance consumers** use a core subscription, not a JetStream consumer; see [Every-instance Bus delivery](consumers.md#every-instance-bus-delivery).
- **Request/reply** uses a core subscription on `headless.reply.{32 hex}`. Keep JetStream streams off `headless.reply.>`; see [Provider support](request-reply.md#provider-support).
- **Delivery deadline.** JetStream has no per-message TTL that Headless sets. A late message is dropped by the consumer's `DeliverWithin` check.
- **Raw producers.** A raw message's id is the `Nats-Msg-Id` header, else `nats:{stream}:{sequence}:{storedUnixNanos}`; see [Message identity](foreign-producers.md#message-identity).
