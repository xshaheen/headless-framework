# Headless.Messaging.Pulsar

Apache Pulsar transport provider for the messaging system.

## Problem Solved

Enables cloud-native, multi-tenant messaging using Apache Pulsar with geo-replication, tiered storage, and unified streaming and queuing models.

## Key Features

- **Multi-Tenancy**: Native namespace and tenant isolation
- **Geo-Replication**: Cross-datacenter message replication
- **Tiered Storage**: Offload old messages to S3/GCS/Azure Blob
- **Unified Model**: Both streaming and queuing semantics
- **Schema Registry**: Built-in schema validation and evolution
- **Negative-Ack Redelivery**: One-minute default with a validated 100-millisecond minimum.
- **Host-Cancellable Startup**: Client acquisition and subscription honor host shutdown while preserving the provider timeout.

## Installation

```bash
dotnet add package Headless.Messaging.Pulsar
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForMessage<OrderPlaced>(message =>
        message.Consumer<OrderPlacedConsumer>(consumer =>
            consumer.ConsumerIdentity("orders.order-placed")
        )
    );
    options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
    options.UsePostgreSql("connection_string");

    options.UsePulsar(pulsar =>
    {
        pulsar.ServiceUrl = "pulsar://localhost:6650";
    });
});
```

## Configuration

`RoutingAffinityKey` on publish/enqueue options maps to the native Pulsar message key on registered Bus and Queue routes. The optional `PulsarMessagingHeaders.PulsarKey` adapter must agree. The configured client uses its built-in key hashing; Headless adds no key-length limit beyond broker message limits. Keep routing configuration and partition topology fixed while relying on placement. This does not select a `Key_Shared` subscription, guarantee FIFO, or prevent concurrent handling.

```csharp
options.UsePulsar(pulsar =>
{
    pulsar.ServiceUrl = "pulsar://localhost:6650";
    pulsar.EnableClientLog = false;
    pulsar.NegativeAckRedeliveryDelay = TimeSpan.FromMinutes(1); // minimum: 100 ms
    // pulsar.TlsOptions = new PulsarTlsOptions { ... }; // optional TLS settings
    // Tenant and namespace are encoded into the broker topic name (e.g.,
    // "persistent://public/default/orders.events"), not surfaced as options here.
});
```

## Messaging Semantics

- Publish sends the serialized body as Pulsar payload bytes and preserves headers as properties. Bus topics use `headless-bus-` and Queue topics use `headless-queue-` before the local topic name, preserving any `persistent://tenant/namespace/` prefix.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit acknowledges the message.
- Reject sends a negative acknowledgment so Pulsar can redeliver under subscription policy. An envelope that cannot be constructed is terminally acknowledged and logged without payload or properties, preventing a broker redelivery storm.
- `NegativeAckRedeliveryDelay` controls how soon rejected messages become eligible for redelivery. It defaults to one minute and must be at least 100 milliseconds; smaller values fail startup validation instead of being silently clamped by Pulsar.Client.
- Consumer startup subscribes the group name to the configured topics in the tenant and namespace.
- Topic creation and retention still follow broker configuration for that tenant and namespace.
- Shared subscriptions favor throughput over strict ordering. Single-threaded consumption gives the most stable order.
- Topic names, property sizes, and payload limits follow Pulsar broker limits.

Bus subscriptions are lane-qualified by logical subscriber group, so replicas in one group compete while independent groups each receive one copy. Queue uses one `headless-queue` subscription per lane-qualified topic. The same logical name can therefore be declared on Bus and Queue without cross-delivery.

**Registration overloads:** `UsePulsar(...)` accepts the standard trio — an `IConfiguration` section, an `Action<PulsarMessagingOptions>` delegate, or an `Action<PulsarMessagingOptions, IServiceProvider>` delegate — plus the service-URL convenience form.

## Dependencies

- `Headless.Messaging.Core`
- `Pulsar.Client`

## Side Effects

- Creates Pulsar topics in configured tenant/namespace
- Establishes persistent connections to Pulsar brokers
- Creates subscriptions for consumer groups
