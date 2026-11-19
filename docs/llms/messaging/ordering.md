---
domain: Messaging
packages:
---

# Messaging ordering and throughput

> Part of [Messaging](../messaging.md). Order handling per key with `Sequential()`, what each broker delivers in order, and how to throttle a consumer with a rate limit.

A consumer handles messages in order only when two things hold: the broker hands them over in order, and the host does not run them side by side. `Sequential()` makes the second hold per routing affinity key on every transport. Without it, order depends on the broker and on running one delivery at a time.

## Sequential consumers

`Tune(identity, c => c.Sequential())` makes a competing consumer handle the messages that share a `RoutingAffinityKey` one at a time, in the order they were admitted to the inbox. Messages with different keys run side by side, up to the consumer's `Concurrency`.

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(rabbit => rabbit.HostName = "localhost");
    setup.UseInMemoryStorage();

    // One order at a time per order id, eight orders at once.
    setup.Tune("orders.projection", consumer => consumer.Concurrency(8).Sequential());
});

// The publisher names the key; every message type the consumer handles for one order carries the same one.
await queue.EnqueueAsync(order, new QueueOptions { RoutingAffinityKey = order.OrderId.ToString() }, cancellationToken);
```

**Verify:** the `describe` output shows `sequential: true` and `activeHost` for the consumer (see [Host manifest](operations.md#host-manifest)). An `activeHost` of `none` with several hosts means ordering across hosts is not enforced.

### Keys and intake

- **The key** is the publisher's `RoutingAffinityKey`. Every route accepts it: where the route has a native mapping the broker also partitions by it, and elsewhere it travels as the `headless-routing-affinity-key` header. See [Routing affinity](transports.md#routing-affinity). A message without a key is ordered against nothing and still takes a `Concurrency` slot.
- **One sequencer per identity per host.** Every message type and lane of the identity shares it, so two message types that carry one key are ordered against each other.
- **Intake is serial, handling is parallel.** The host reads a sequential subscription through one client, one delivery at a time: each delivery passes the receive stage, is stored, and is acknowledged before the next is read. `ConsumerThreadCount` does not apply, and `EnableSubscriberParallelExecute` does not change it. One inbox write at a time per consumer per host bounds intake speed.
- **Backpressure.** A sequencer holds at most `max(32, 8 × Concurrency)` messages, running ones included. When it is full, intake stops and the rest stay on the broker. A key with more work waits behind other waiting keys, so one busy key cannot starve the rest.
- **Watch the wait.** `headless.messaging.sequential.waiting` reports, per consumer and host, messages taken in that have not started. A count near the buffer limit means one key, or the whole consumer, cannot keep up.

### Order across feeds and hosts

- **Order follows the broker's delivery order per feed.** A feed is one queue, stream, partition, or session. Messages of one key on different feeds have no relative order. On the Queue lane each message type is its own queue, on NATS each stream is its own feed, and on Kafka each partition is. Keep messages that must stay ordered on one feed: one Bus consumer identity on RabbitMQ and Redis, one stream on NATS, one partition key on Kafka. A standard SQS queue promises no order; use a FIFO destination.
- **The store orders each key across hosts.** On the in-memory, PostgreSQL, and SQL Server storages, before a keyed message runs, first attempt or retry, the host checks whether an earlier row of the same consumer and key is unfinished. If so, the message does not run and reserves no attempt, and it goes back for retry pickup after `max(RetryPolicy.InitialDispatchGrace, the earlier row's due time - now)`. Hosts that share the store therefore never run two messages of one key at once. The order is the order the store admitted the rows. A failed check is logged, and the message stays with retry pickup instead of running out of order.
- **One host at a time reads the subscription.** Messaging keeps one host's client reading a sequential subscription, chosen from what the transport offers:
  - **Key-routed.** When every message the subscription consumes has a native routing-affinity route (a Kafka partition key, an Azure Service Bus session, an SQS FIFO message group), the broker hands one key to one host, and every host listens.
  - **Broker single active consumer.** RabbitMQ marks the queue so the broker delivers to one consumer at a time and fails over. See [RabbitMQ single active consumer](rabbitmq.md#rabbitmq-single-active-consumer).
  - **Distributed lock.** Elsewhere, with a messaging lock wired through [`UseDistributedLock(...)`](retries.md#distributed-lock-integration), a host opens the client only while it holds a lock named for the subscription, lane, and messaging version. The lease lasts 30 seconds and renews while the host listens. A host without the lock creates no client and retries every 5 seconds. A holder that loses the lease or stops releases the lock, and handlers it already admitted finish. `UseDistributedLock` also turns on `UseStorageLock`.
  - **No lock.** Every host listens and logs Warning 4142 once per sequential subscription at startup. Run one host, or wire a lock.

### Retries and waiting messages

- **Retries hold the key.** Immediate retries run inside the first dispatch. A message waiting for a delayed retry keeps its key until it finishes: later messages of the key wait, other keys keep running. A message that keeps failing stalls only its own key, for as long as its failure policy retries it. A parked backlog resumes on retry pickup, not at once.
- **A waiting message is not retried early.** A message is stored and acknowledged before it waits, and a stored row becomes eligible for retry pickup after `RetryPolicy.InitialDispatchGrace`. The host puts off that pickup while the message waits. A host that stops or crashes leaves its waiting rows to retry pickup, on whichever host claims them, at most one grace after the last deferral.
- **Operator re-execution is not ordered.** A requeue or replay of a row that already ended runs without the store check.
- **Every-instance consumers** cannot be sequential. Tuning one so fails startup.

### Configuration and logs

- `Headless:Messaging:Consumers:{identity}:Sequential` is `true` or `false` and replaces the `Tune` value. Any other value fails startup.
- The host manifest writes `sequential: true` with `activeHost`: `keyRouted`, `broker`, `lock`, or `none`.
- Logs: 4142 (Warning) no messaging lock, every host listens. 4143 (Information) this host took the lock and listens. 4144 (Warning) this host lost the lock and stopped listening. 4145 (Warning) acquiring the lock threw; the host retries after the interval. 4146 (Error) the listening client returned before the host stopped it; the host releases the lock and competes again after the interval. A standby host that finds the lock held logs nothing above Debug.

## Transport-Specific Ordering

What each broker delivers in order, which is the order a sequential consumer admits:

| Transport | Delivery order |
| --- | --- |
| Kafka | Strict per partition. Messages with the same partition key share a partition. With concurrent handlers, offsets commit only up to the lowest offset still in flight per partition |
| Azure Service Bus | FIFO per session when sessions are enabled (`EnableSessions = true`) |
| RabbitMQ | One queue delivers in publish order. Competing consumers each get a share, except on a sequential consumer's queue, where the single active consumer gets all of it |
| AWS SQS | FIFO queues are strict per message group; standard queues are unordered. The client hands one group's messages over one at a time in receive order, whatever its `Concurrency`. When one is rejected, it returns the rest of that group's batch hidden as long as the rejected message, so the group comes back in order |
| Redis Streams | Ordered within a stream. An entry reclaimed after a failed delivery arrives beside newer ones |
| NATS | A Bus consumer receives everything it consumes from one stream in stream order, across message types. Queue-lane messages are ordered per message type only. See [NATS ordering](nats.md#nats-ordering) |
| InMemory | FIFO per destination |

## Configuration impact on ordering

For a consumer that is not sequential:

| Setting | Effect |
| --- | --- |
| `ConsumerThreadCount > 1` | Several clients read the subscription, so messages may run out of order |
| `Concurrency > 1` | Handlers run side by side, so messages may run out of order |
| `EnableSubscriberParallelExecute = true` | Messages are buffered for parallel processing, with no order guarantee |
| `ConsumerThreadCount = 1` and `Concurrency(1)` | One delivery at a time, in transport order |

Choose by need:

- **Order per key:** publish with `RoutingAffinityKey` and tune the consumer `Sequential()`. With several hosts, a shared in-memory, PostgreSQL, or SQL Server store keeps a key's messages from overlapping. To keep publish order too, use a broker that routes by key or offers a single active consumer, or wire a messaging lock.
- **Total order of one consumer:** `ConsumerThreadCount = 1` and `Concurrency(1)`, on one host.
- **Throughput:** run in parallel and make the consumer order-independent.

## Consumer Rate Limit

`Tune(identity, c => c.RateLimit(...))` caps how fast one consumer starts handling deliveries on this host. It throttles: a delivery over the rate waits for a permit and then runs. It is never dropped, rejected, or failed for being over the rate. Use it to protect a downstream dependency with a request quota. Use `Concurrency(n)` to cap how many deliveries run at once.

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...

    // A steady 50 deliveries a second; an idle consumer saves up a burst of at most 200.
    setup.Tune("payments.handler", consumer => consumer
        .Concurrency(8)
        .RateLimit(ConsumerRateLimit.TokenBucket(tokenLimit: 200, tokensPerPeriod: 50, TimeSpan.FromSeconds(1))));

    // At most 1,000 deliveries in each fixed one-minute window.
    setup.Tune("partners.sync", consumer => consumer.RateLimit(ConsumerRateLimit.FixedWindow(1_000, TimeSpan.FromMinutes(1))));
});
```

**Verify:** the `describe` output shows `rateLimit` with its `algorithm` and limits for the consumer.

- **Algorithms.** `ConsumerRateLimit.FixedWindow(permitLimit, window)` allows `permitLimit` deliveries per window, so up to `permitLimit` can start together when a window opens. `ConsumerRateLimit.TokenBucket(tokenLimit, tokensPerPeriod, replenishmentPeriod)` adds `tokensPerPeriod` permits each period and holds at most `tokenLimit`, which bounds the burst after an idle spell. Counts must be greater than zero, and the period greater than zero and at most `ConsumerRateLimit.MaxPeriod` (one day). An invalid value throws `ArgumentOutOfRangeException`. The limiters replenish on real time, not the app `TimeProvider`.
- **Scope.** One limiter per identity per host, shared by every message and lane of the identity. N hosts allow N times the rate. A fleet-wide quota needs a shared limiter of your own, for example in tuned consume middleware.
- **Where the wait happens.** After the receive stage accepts the message, before consume middleware and the handler, and before the inbox lease or transaction. A throttled delivery holds no database connection. Each attempt takes a permit, so inline retries are throttled too.
- **Deadlines.** A message already past its [`DeliverWithin` deadline](publishing.md#delivery-deadline-deliverwithin) or request deadline takes no permit. One whose deadline passes during the wait is dropped as expired.
- **Persisted retries.** Retry pickup never waits for a permit, so a throttled consumer does not hold up another consumer's retries. With a permit available, pickup dispatches the row. Without one, it puts the row back unleased, due again after the rate's window or period, or after `RetryPolicy.InitialDispatchGrace` when that is shorter. A refused pickup keeps one place in the limiter's queue, so retries get permits in turn. A delivery waiting for its permit keeps its row out of retry pickup with one deferral write about every half grace. If that write fails, a pickup on another host can run the row first, and the waiting delivery then ends without running the handler.
- **Shutdown.** The wait observes the delivery's cancellation token, so shutdown releases a waiting delivery at once. It writes nothing and is picked up after restart.
- **Every-instance consumers** accept a rate limit.
- **Broker-side leases.** A delivery held while it waits keeps counting against the broker's lease, such as an SQS visibility timeout or an Azure Service Bus lock. Keep the expected wait, roughly concurrency divided by rate, well under that lease.
- **Configuration.** `Headless:Messaging:Consumers:{identity}:RateLimit` holds exactly one algorithm section and replaces any `Tune` rate limit. An unknown algorithm, two algorithms, a missing or unknown setting, or an out-of-range value fails startup.

```json
{
  "Headless": {
    "Messaging": {
      "Consumers": {
        "payments.handler": { "RateLimit": { "TokenBucket": { "TokenLimit": 200, "TokensPerPeriod": 50, "ReplenishmentPeriod": "00:00:01" } } },
        "partners.sync": { "RateLimit": { "FixedWindow": { "PermitLimit": 1000, "Window": "00:01:00" } } }
      }
    }
  }
}
```
