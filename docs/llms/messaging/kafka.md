---
domain: Messaging
packages: Messaging.Kafka
---

# Kafka transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.Kafka` transport: Queue lane only, partition keys, offset commits, rebalances, and the consumer lag metric.

## Headless.Messaging.Kafka

Use this transport for ordered partitions and high-throughput streams. It carries only the Queue lane: a `[BusConsumer]` fails startup before any provider or storage side effect, and an `IBus` publish fails when attempted. Startup validates contract routes only on the Queue lane, so any contract is accepted.

### Setup

```bash
dotnet add package Headless.Messaging.Kafka
```

```csharp
using Confluent.Kafka;

setup.UseKafka(options => options.Servers = "localhost:9092");

setup.Tune(PlaceOrderWorker.Identity, consumer =>
    consumer.UseKafka(kafka => kafka.WithIsolationLevel(IsolationLevel.ReadCommitted))
);

// In the Orders module:
services.ConfigureMessaging(messaging =>
{
    messaging.AddModule<Orders.MessagingModule>();
    messaging
        .Message<PlaceOrder>("orders.place")
        .OnQueue(queue => queue.UseKafka(kafka => kafka.PartitionBy(order => order.CustomerId.ToString())));
});

[QueueConsumer(Identity)]
public sealed class PlaceOrderWorker : IConsume<PlaceOrder>
{
    public const string Identity = "orders.place-order";

    public ValueTask ConsumeAsync(ConsumeContext<PlaceOrder> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

**Verify:** the `messaging-broker` readiness check reports `Healthy` once a consumer reaches the brokers ([Broker reachability](operations.md#broker-reachability)), and the `describe` host manifest's `messaging.transports` shows `"queue": "kafka"` with no `bus` entry. A `[BusConsumer]` in the host fails startup with a `MessagingConfigurationException`.

### Configuration

Configure bootstrap servers, `MainConfig`, `TopicOptions` (`NumPartitions`, `ReplicationFactor` for auto-created topics), `CustomHeadersBuilder`, and `RetriableErrorCodes` through `KafkaMessagingOptions`. `UseKafka("host:9092")` sets only the servers.

- **`MainConfig` applies to the producer and the consumers.**
- **Producer.** Idempotence (`enable.idempotence`) is on unless `MainConfig` sets it, or sets something librdkafka cannot combine with it: `acks` other than `all`, more than five in-flight requests, zero retries, or a non-FIFO `queuing.strategy`. Each setting is recognized under every name librdkafka accepts, such as `request.required.acks` for `acks`. `message.timeout.ms` defaults to 5000 and `request.timeout.ms` to 3000.
- **Consumers.** `partition.assignment.strategy` defaults to `cooperative-sticky`, so a rebalance moves only the partitions that change owner. The transport owns offset commits and always overrides `enable.auto.commit` and `enable.auto.offset.store`. `auto.commit.interval.ms` (librdkafka default 5000) still sets how often stored offsets reach the broker.
- **Switching an existing group to cooperative-sticky** from an eager strategy (`range`, `roundrobin`) needs a two-step rolling upgrade, because a group cannot mix eager and cooperative members. First deploy with `MainConfig["partition.assignment.strategy"] = "cooperative-sticky,range"`, then remove `range`.
- **`RetriableErrorCodes`** (default `DefaultRetriableErrorCodes`) are `int` values of Confluent's `ErrorCode` enum, so you configure them without a compile-time `Confluent.Kafka` reference.
- **Routing affinity.** `QueueOptions.RoutingAffinityKey` maps to the native UTF-8 key on registered Queue routes. `PartitionBy(...)` and the `KafkaMessagingHeaders.KafkaKey` header must agree with it. `RequireRoutingAffinity()` rejects a random or unknown `MainConfig["partitioner"]`. The accepted partitioners are `consistent`, `consistent_random` (default), `murmur2`, `murmur2_random`, `fnv1a`, and `fnv1a_random`, all deterministic for a nonempty key. No key-length cap beyond the broker's message limits. Keep partition count, encoding, and partitioner fixed while placement matters. Different keys can share a partition, so affinity alone gives neither FIFO nor exclusive handling. A consumer tuned `Sequential()` handles each key one message at a time, in partition order.
- **Consumer hatch.** `Tune(identity, c => c.UseKafka(k => k.WithIsolationLevel(IsolationLevel.ReadCommitted)))` sets the consumer's isolation level.
- **Group id.** A Queue consumer's Kafka `group.id` is its message name, so every host that consumes the message joins one consumer group.
- Not supported: request/reply (Kafka has no per-process address short of a partition per instance) and every-instance consumers (no Bus lane). A host that sends requests or declares a responder fails startup.

### Consumer lag metric

Each consumer reads librdkafka statistics and reports, per partition it owns, how many messages sit between the group's committed offset and the end of the partition (the high watermark, or the last stable offset under `read_committed`). librdkafka emits statistics only when `statistics.interval.ms` is above zero, and its default is 0, so turn it on:

```csharp
setup.UseKafka(options =>
{
    options.Servers = "localhost:9092";
    options.MainConfig["statistics.interval.ms"] = "15000";
});
```

| Instrument | Kind | Unit | Dimensions |
| --- | --- | --- | --- |
| `headless.messaging.kafka.consumer.lag` | ObservableGauge (`long`) | `{message}` | `messaging.system` (`kafka`), `messaging.destination.name` (topic), `messaging.destination.partition.id` (partition number as a string), `messaging.consumer.group.name` (the Kafka `group.id`) |

**Verify:** subscribe the meter with `AddMessagingInstrumentation()` ([observability](observability.md#instrument-reference)) and the gauge reports a value per owned partition after one statistics interval.

- The value is the latest sample, so it is up to one `statistics.interval.ms` old. It is measured from the committed offset, which trails handler completion by up to `auto.commit.interval.ms`.
- A partition is reported only while this consumer owns it. A revoke, a lost assignment, or shutdown removes it at once. A partition whose lag is not known yet is omitted, not reported as -1.
- The setting also turns on producer statistics, which the transport ignores.
- A statistics payload that cannot be parsed is logged as a consume error and keeps the previous values. It never interrupts consumption.
- Keep the interval in seconds: each sample is a JSON document that grows with brokers and partitions, and cardinality grows with assigned partitions per group.

### Design and runtime behavior

- **Delivery is at-least-once.** Dedupe by business key or message id. A publish succeeds only when Kafka reports `Persisted`. `PossiblyPersisted` is retried, which can produce duplicates.
- **One idempotent producer per process,** built on first publish. Idempotence stops a broker retry from writing a second copy within one producer session. An application retry after a failed publish still writes a new record. Disposal flushes for up to `message.timeout.ms`. A fatal producer error discards the producer, and the next publish builds a new one.
- **Offsets.** A handler's settlement stores its offset, and the background auto-commit sends stored offsets. With concurrency above one, handlers can finish out of order, but the commit advances only to the lowest offset still in flight per partition. A reject seeks back to the rejected offset and keeps every lower in-flight offset. Offsets the application never sees (transaction control records, aborted batches under `read_committed`, compaction holes, tombstones) do not hold the commit back. A crash between storing an offset and the next auto-commit redelivers records handled since the last commit.
- **Rebalances.** A partition revoke waits up to 30 seconds for in-flight handlers on the revoked partitions, then commits synchronously. Tracked offsets of revoked or lost partitions are dropped, so a late handler cannot commit or seek a partition another consumer now owns. A handler that outlives the revoke wait never stores its offset, and the next owner redelivers that record.
- **Shutdown.** The poll loop stops within one `MessagingOptions.ConsumerPollTimeout`, because Kafka's blocking poll does not observe cancellation. Shutdown then waits, within the shutdown budget, for in-flight handlers, and closes the consumer, which commits stored offsets synchronously.
- **Pausing.** A circuit-breaker pause pauses the assigned partitions and keeps calling `Consume`, so the member stays in its group however long the circuit stays open (librdkafka counts `max.poll.interval.ms`, default 300 s, between `Consume` calls). A record that still arrives while paused is not dispatched: the client seeks back to it, and the poll resumes there after the circuit closes.
- **Failures.** A record whose callback throws without settling it is sought back, so Kafka redelivers it. If that seek fails, the client is rebuilt and reads again from the last committed offset. A malformed envelope is logged and its offset joins the per-partition commit, so poison replay is bounded without skipping lower in-flight records.
- **No broker delivery limit.** A `CustomHeadersBuilder` that keeps throwing makes the record redeliver without end; see [Translating other conventions](foreign-producers.md#translating-other-conventions).
- **Delivery deadline.** Kafka has no per-message TTL. A late message is dropped by the consumer's `DeliverWithin` check instead of by the broker.
- **Raw producers.** A raw record's message id is `kafka:{topic}:{partition}:{offset}:{timestampMs}`. Use `message.timestamp.type=LogAppendTime` on raw topics so a recreated topic cannot collide with old inbox rows; see [Message identity](foreign-producers.md#message-identity).
