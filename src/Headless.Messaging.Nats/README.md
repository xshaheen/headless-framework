# Headless.Messaging.Nats

NATS JetStream transport provider for the Headless messaging system.

## Problem Solved

Adds a NATS-backed transport to Headless messaging so the core bus and queue abstractions can publish to JetStream subjects while keeping storage, retry, and consumer registration in the shared messaging pipeline.

## Key Features

- **Lightweight**: Minimal resource footprint, cloud-native
- **JetStream**: Persistent streams with at-least-once delivery
- **Subject Routing**: Hierarchical subject/message-name patterns (e.g., `orders.*.created`)
- **Connection Pooling**: Round-robin pool for publish throughput

## Installation

```bash
dotnet add package Headless.Messaging.Nats
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");

    options.UseNats(nats =>
    {
        nats.Servers = "nats://localhost:4222";
    });
});
```

## Configuration

```csharp
options.UseNats(nats =>
{
    nats.Servers = "nats://localhost:4222,nats://localhost:4223";
    nats.ConnectionPoolSize = 10;

    // Customize stream creation (defaults to File storage)
    nats.StreamOptions = config =>
    {
        config.Storage = StreamConfigStorage.Memory; // for dev/testing
    };

    // Customize NATS connection
    nats.ConfigureConnection = opts => opts with { ConnectTimeout = TimeSpan.FromSeconds(10) };
});

options.ForMessage<OrderEvent>(message =>
    message
        .MessageName("orders.events")
        .UseNats(nats => nats.SubjectShard(order => order.CustomerId.ToString()))
        .OnBus<OrderEventConsumer>(consumer => consumer.UseNats(nats => nats.Sharded()))
);
```

`SubjectShard(...)` stamps `NatsMessagingHeaders.SubjectShard` (`headless-nats-subject-shard`) during publish. The provider appends it as one safe subject token, producing subjects such as `orders.events.42`; `.`/`*`/`>`/whitespace/control characters are rejected. The selector output is broker-visible metadata, so do not put secrets or raw PII in it.

When `SubjectShard(...)` is set on the producer side, every consumer registered for that message must declare `.UseNats(c => c.Sharded())`. This is validated at startup and throws `InvalidOperationException` if violated. The reason: NATS delivers zero messages with no error when a FilterSubject does not match any shard subject — the asymmetry causes silent data loss.

### Stream Auto-Creation

By default, consumer clients create JetStream streams and subjects on startup via
`EnableSubscriberClientStreamAndSubjectCreation`. For production deployments requiring
fine-grained control, disable this and manage streams externally:

```csharp
nats.EnableSubscriberClientStreamAndSubjectCreation = false;
```

## Messaging Semantics

- Publish writes the serialized body and headers to JetStream.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit sends `ACK`.
- Reject sends `NAK` so JetStream can redeliver.
- `FetchMessageNamesAsync(...)` groups subjects into streams and creates them when auto-creation is enabled.
- Consumer startup creates filtered durable consumers for each subscribed subject.
- Message-level `SubjectShard(...)` publishes to `{messageName}.{shard}`. Stream auto-creation and durable consumer filters add wildcard coverage only for consumers that declared `.UseNats(c => c.Sharded())`. Shard symmetry is enforced at startup.
- Sequential handling preserves per-subject delivery order best. Parallel handlers and redeliveries can reorder work.
- Subject naming, header sizes, and payload limits follow NATS and JetStream limits.

## Dependencies

- `Headless.Messaging.Core`
- `NATS.Net`

## Side Effects

- Establishes persistent connections to NATS servers
- Creates JetStream streams and consumers if enabled
- Subscribes to subjects for message consumption
