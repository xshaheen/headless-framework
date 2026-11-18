---
domain: Messaging
packages:
---

# Foreign producers

> Part of [Messaging](../messaging.md). Consume messages that a non-Headless producer publishes with its broker's own client, and publish messages a non-Headless consumer can read.

## Foreign producers and raw payloads

A producer that does not use Headless, such as a Python worker publishing JSON with its broker's client, sends none of the Headless envelope headers. By default every consumer client settles such a delivery as malformed before any middleware runs. Declare the contract's lane with `RawPayload()` to consume it through the normal pipeline, with retry policy, circuit breaker, poison storage, and inbox deduplication:

```csharp
services.ConfigureMessaging(m => m.Message<OrderPlaced>("orders.placed", "2").OnQueue(q => q.RawPayload()));
```

- `RawPayload()` exists on both lane builders (`OnBus` and `OnQueue`). Consumers stay `[BusConsumer]` or `[QueueConsumer]` over `IConsume<T>`.
- A delivery that carries the envelope headers takes the normal path on the same lane, so Headless and foreign producers can share one message.
- Two declarations of one contract merge only when both declare `RawPayload()` on the same lanes.
- Use the `Headless.Messaging` abstraction, not a raw client loop. On NATS, consume a foreign producer's own subjects with `ExternalSubject(...)`; see [nats.md § External subjects](nats.md#external-subjects).

**Verify:** the host manifest marks the lane with `rawPayload` under `messaging.messages` (see [operations.md § Host manifest](operations.md#host-manifest)), and a message published with the broker's client reaches the consumer.

## How a raw delivery is completed

- **The address names the contract.** On a raw lane, the topic, queue, routing key, subject, or stream a delivery arrived on decides its contract. A `headless-msg-name` header that names another message makes the delivery malformed, so a foreign header cannot steer a message to another contract. Lanes without `RawPayload()` keep header routing. Where an address names no single message, such as a topic reached through a Kafka pattern subscription, a raw delivery must carry `headless-msg-name`.
- **Only missing headers are filled:** the message name, a message id derived from the broker ([Message identity](#message-identity)), the contract's declared version (without it, a contract versioned `"2"` would fail the version check, because a missing version reads as `"1"`), `headless-sent-time` from the broker's storage timestamp where there is one, and `headless-corr-id` set to the message id. A header the producer sent is never replaced.
- **The body is passed unchanged** to the host's `IMessageSerializer`. A body the serializer cannot read takes the poison path at once and is not redelivered.
- **Default JSON rules.** Property names match case-sensitively, so a camelCase producer needs `[JsonPropertyName]` on the message type or `MessagingOptions.JsonSerializerOptions` set for it. The options start from `JsonConstants.CreateStorageJsonOptions()`: unknown members are skipped (a producer can add a field before consumers redeploy), enums travel by camelCase name and a numeric enum is refused, nulls are written, and a repeated property is refused. Every constructor parameter is required, so a body missing one takes the poison path instead of binding `null`. When the producer's JSON fits neither, consume it as [`RawJson`](#consume-the-body-as-raw-json).
- **Trace context continues.** The producer's `traceparent`, `tracestate`, and `baggage` headers are read as for any delivery.

## Consume the body as raw JSON

`RawJson` (in `Headless.Messaging.Abstractions`) is a message type the serializer neither binds nor rewrites. A consumer of `IConsume<RawJson>` gets the producer's UTF-8 bytes in `context.Message.Utf8Json`, with their escaping and whitespace, on the first delivery and every retry. Use it when a foreign producer's JSON does not fit `MessagingOptions.JsonSerializerOptions`, such as snake_case or a schema your own parser enforces, and changing the host-wide options would change every other contract:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

// A stream covering the filter is declared in NatsMessagingOptions.Streams, as in External subjects below.
services.ConfigureMessaging(m =>
    m.Message<RawJson>("runs.result")
        .OnQueue(queue => queue.RawPayload().UseNats(nats => nats.ExternalSubject("acme.*.runs.*.result")))
);

public sealed record RunResult(string RunId, string Status);

[QueueConsumer("runs.ingest-result")]
public sealed class IngestRunResult : IConsume<RawJson>
{
    // The producer's wire format, kept apart from the host's MessagingOptions.JsonSerializerOptions.
    private static readonly JsonSerializerOptions _WorkerJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public ValueTask ConsumeAsync(ConsumeContext<RawJson> context, CancellationToken cancellationToken)
    {
        var result = JsonSerializer.Deserialize<RunResult>(context.Message.Utf8Json.Span, _WorkerJson);
        // ... ingest result
        return ValueTask.CompletedTask;
    }
}
```

- **The body must be one JSON value.** A body that is not JSON, or a JSON `null`, takes the poison path without a retry.
- **Publishing a `RawJson` sends its bytes unchanged.** `new RawJson(utf8Json)` copies the bytes and throws `ArgumentException` unless they hold exactly one complete JSON value.
- **A replacement `IMessageSerializer`** built on `System.Text.Json` keeps this behavior through `RawJson`'s `[JsonConverter]`. One built on another library handles `RawJson` itself.

## Serializing a payload once

A durable message is serialized once. The publish serializes it before the outbox write, and the send after commit reuses that body. The inbox row stores the body as received, after any receive-middleware rewrite.

- The stored envelope is the `{ "Headers": ..., "Value": ... }` JSON that `IMessageSerializer.Serialize(Message)` writes. An inbox row's `Value` is the producer's JSON as received, including members the consumer type does not bind.
- The broker receives the payload as it was at publish. Mutating a message object after publishing does not change what is sent.
- **A replacement `IMessageSerializer` keeps working.** `Serialize(Message, ReadOnlyMemory<byte>)` defaults to `Serialize(Message)`; override it to embed the body without re-serializing. A body produced that way must depend only on `Message.Value`. The send reuses the publish-time `TransportMessage` only when its `Headers` is the envelope's own dictionary; a serializer that returns a copied header dictionary is called again for every send.
- The default serializer falls back to a full serialization when the body is not one strict JSON value (comments or trailing commas the options tolerated on read) or a configured `TypeInfoResolver` cannot describe it.

## Message identity

The message id keys inbox deduplication, so a raw message must get the same id on every delivery. The first match wins:

1. `headless-msg-id`, when the producer sets it.
2. The producer's idempotency key in the broker's native field: the NATS `Nats-Msg-Id` header, the AMQP `message-id` property on RabbitMQ, or the Azure Service Bus `MessageId`.
3. The broker's position for the stored message:

| Provider | Position id | Redelivery |
| --- | --- | --- |
| NATS JetStream | `nats:{stream}:{sequence}:{storedUnixNanos}` | Same id. The timestamp changes after a stream is recreated, so a reused sequence never matches an old inbox row |
| NATS every-instance (core) | A new id per delivery | No inbox on this path |
| Kafka | `kafka:{topic}:{partition}:{offset}:{timestampMs}` | Same id. The timestamp is the producer's create time unless the topic uses `message.timestamp.type=LogAppendTime`. Use `LogAppendTime` on raw topics, or a producer replaying fixed timestamps into a recreated topic can collide with old inbox rows |
| Azure Service Bus | `asb:{entityPath}:{sequenceNumber}:{enqueuedTicks}` | Same id |
| Amazon SQS | `sqs:{MessageId}` | Same id |
| Redis | `redis:{streamKey}:{entryId}` | Same id |
| RabbitMQ | None | A raw delivery without `headless-msg-id` or the AMQP `message-id` is rejected terminally (dead-lettered where a dead-letter exchange is configured), because a delivery tag changes on every redelivery |

A position id longer than 200 characters becomes `{provider}:sha256:{hex}` of itself. A producer-chosen key (rules 1 and 2) is trusted as is: two messages sent with one key are deduplicated, and only the first runs.

## Interop header contract

These headers are a stable wire contract in both directions. A foreign producer may set any of them, and every Headless publish writes them, so a foreign consumer can rely on them. Names are lowercase and case-sensitive.

| Header | Meaning | Format | On a raw lane, when absent |
| --- | --- | --- | --- |
| `headless-msg-id` | Idempotency key; part of the inbox key | Non-blank, at most 200 characters, no control characters | Derived from the broker |
| `headless-msg-name` | Logical message name | The contract's name, including `MessagingOptions.MessageNamePrefix` | The arrival address's contract |
| `headless-contract-version` | Contract schema version | At most 100 characters, no whitespace or control characters | The contract's version |
| `headless-tenant-id` | Tenant | At most 200 characters; a longer value is dropped with a log entry | None |
| `headless-corr-id` | Root correlation | Non-blank | The message id |
| `headless-causation-id` | Immediate cause | Non-blank | None |
| `headless-sent-time` | Send time | ISO 8601 with an offset, such as `2026-10-09T12:00:00.0000000+00:00` | The broker's storage time, else the receive time |
| `traceparent`, `tracestate`, `baggage` | W3C trace context and baggage | W3C | A new trace |

- Every other `headless-*` header is informational and may change. `headless-intent` keeps its `0` (Bus) and `1` (Queue) values and may be omitted. `headless-transport-address` and `headless-msg-consumer-identity` are overwritten on receipt.
- A foreign `headless-tenant-id` is honored as on any lane. The trust boundary is the broker's publish permission on the address. On NATS, a tenant read from a subject token is stronger when producers' headers cannot be trusted.

## Where a foreign producer publishes

On a raw lane, the address Headless derives from the message name is part of the contract. Choose message names you are willing to expose to other teams. Every name includes `MessagingOptions.MessageNamePrefix` when one is set.

| Provider | Address | Headers carried in |
| --- | --- | --- |
| NATS | Subject `headless.{bus\|queue}.{name}`, or a subject of your own through [external subjects](nats.md#external-subjects) | Message headers |
| Kafka (Queue only) | Topic `{name}` | Record headers, UTF-8 values |
| RabbitMQ | Exchange `{ExchangeName}.{bus\|queue}`, routing key `{bus\|queue}.{name}`; set the AMQP `message-id` property | AMQP headers, string or UTF-8 byte values |
| Azure Service Bus | Bus: topic `TopicPath` with the native `Subject` set to `{name}`. Queue: queue `{name}` | Application properties, string values |
| AWS | Bus: SNS topic `bus-{name}`. Queue: SQS queue `queue-{name}`. `.` becomes `-` and `:` becomes `_` | One String attribute `headless-aws-headers-v1` holding a JSON object of strings. Optional on a raw lane; other attributes are ignored |
| Redis | Stream `headless:messaging:{bus\|queue}:{name}`; the `body` field holds the body bytes | A `headers` field holding a JSON object of strings; optional on a raw lane. An entry without `body` is malformed |

On AWS, SNS raw message delivery does not tell the SQS queue which topic a message came from. A raw Bus contract therefore needs its consumer identity's queue to itself: startup fails when that queue also carries another message.

## Translating other conventions

Every broker provider has a `CustomHeadersBuilder` option that derives extra headers from the native message. It runs before raw completion and overwrites wire headers. Use it to map a producer's own naming, such as CloudEvents binary-mode attributes, onto the contract above:

```csharp
using System.Text;

services.AddHeadlessMessaging(setup =>
{
    setup.UseKafka(kafka =>
    {
        kafka.Servers = "localhost:9092";
        kafka.CustomHeadersBuilder = (record, _) =>
            record.Message.Headers.TryGetLastBytes("ce_id", out var id)
                ? [new KeyValuePair<string, string>("headless-msg-id", Encoding.UTF8.GetString(id))]
                : [];
    });
});
```

A builder exception is a retryable failure: the consumer client hands the delivery back for redelivery instead of settling it. NATS negatively acknowledges it, RabbitMQ requeues it, Kafka seeks back, Amazon SQS releases it with a 3-second visibility timeout, Azure Service Bus abandons it, and Redis Streams leaves the entry pending for the claim pass. The broker's delivery limit ends a builder that keeps failing: NATS `MaxDeliver`, a RabbitMQ quorum queue's delivery limit, an SQS redrive policy's `maxReceiveCount`, or the Azure Service Bus `MaxDeliveryCount`. Kafka and Redis Streams have no delivery limit, so a builder that never succeeds is redelivered without end (on Redis, until `StreamMaxAge` trims the entry). An envelope the transport's own check finds malformed still settles terminally.
