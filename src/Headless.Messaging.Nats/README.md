# Headless.Messaging.Nats

NATS JetStream transport provider for the Headless messaging system.

## Problem Solved

Provides a NATS JetStream transport for Headless messaging so applications can publish and consume durable messages with subject-based routing, JetStream acknowledgements, and provider-specific shard subjects while keeping storage, retry, and consumer registration in the shared messaging pipeline.

## Key Features

- **Small Runtime Footprint**: Minimal resource footprint, cloud-native
- **JetStream**: Persistent streams with at-least-once delivery
- **Subject Routing**: Hierarchical subject/message-name patterns (e.g., `orders.*.created`)
- **Connection Pooling**: Optional round-robin publish pool (defaults to a single multiplexed connection; raise only as a throughput knob)
- **Host-Cancellable Startup**: Connection and JetStream topology setup honor host shutdown while preserving configured timeouts.

## Design Notes

Connection-specific failures (`NatsConnectionFailedException`, `NatsJSConnectionException`, or a `NatsException` wrapping `SocketException`/`IOException`) terminate the listener instead of retrying in place, so the supervising consumer register's health watchdog can replace the failed client. JetStream protocol, timeout, API, and other consumer errors retry per-subject with backoff. As a backstop, a run of `MaxConsecutiveConsumeFailures` (default `10`) consecutive consume-loop failures — regardless of exception type — also terminates the listener for a supervised restart, so a permanently dead connection whose error is not one of the classified connection-failure types cannot spin in place forever (consumer connections set `MaxReconnectRetry = 0` and never reconnect on their own). The streak resets on any forward progress (a successful consumer bind or fetch). The `ConnectError` log entry for these termination paths states that the listener is terminating for supervised restart. During host shutdown, NATS bounds its in-flight handler drain by the remaining shared `MessagingOptions.ShutdownTimeout` budget instead of starting an independent 30-second drain.

Consumer connections disable client-side reconnect (`MaxReconnectRetry = 0`) so a connection fault surfaces to the consume loop and the consumer register's health watchdog — not the per-message circuit breaker, which never observes connection-level failures — owns the rebuild. The publish path pools connections independently and does not disable reconnect.

Commit uses JetStream double acknowledgement and waits for the broker's settlement confirmation before returning. This keeps immediate consumer replacement from racing an unconfirmed `ACK` and redelivering an already-successful message.

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

    // Publish connection pool. Defaults to 1 (a single connection multiplexes all publishers);
    // raise only when a single connection's command writer is the bottleneck at very high send rates.
    nats.ConnectionPoolSize = 1;

    // Consecutive consume-loop failures tolerated before a subject listener is terminated for a
    // supervised restart (bounds in-place spinning on a dead, non-reconnecting connection). Default 10.
    nats.MaxConsecutiveConsumeFailures = 10;

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

### Messaging Semantics

- Publish writes the serialized body and headers to JetStream.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit sends a double `ACK` and waits for JetStream to confirm settlement before returning.
- Reject sends `NAK` so JetStream can redeliver.
- `FetchMessageNamesAsync(...)` groups subjects into streams and creates them when auto-creation is enabled.
- Consumer startup creates filtered durable consumers for each subscribed subject.
- Message-level `SubjectShard(...)` publishes to `{messageName}.{shard}`. Stream auto-creation and durable consumer filters add wildcard coverage only for consumers that declared `.UseNats(c => c.Sharded())`. Shard symmetry is enforced at startup.
- Sequential handling preserves per-subject delivery order best. Parallel handlers and redeliveries can reorder work.
- Subject naming, header sizes, and payload limits follow NATS and JetStream limits.

**Registration overloads:** `UseNats(...)` accepts the standard trio — an `IConfiguration` section, an `Action<NatsMessagingOptions>` delegate, or an `Action<NatsMessagingOptions, IServiceProvider>` delegate — plus the optional server-URL convenience form.

## Dependencies

- `Headless.Messaging.Core`
- `NATS.Net`

## Side Effects

- Establishes persistent connections to NATS servers
- Creates JetStream streams and consumers if enabled
- Subscribes to subjects for message consumption
