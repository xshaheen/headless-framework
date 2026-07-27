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
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");

    options.UsePulsar(pulsar =>
    {
        pulsar.ServiceUrl = "pulsar://localhost:6650";
    });
});
```

## Configuration

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

This topology replaces legacy unqualified topics and subscriptions. Before deployment, stop old and new publishers, inventory producer/consumer versions and topic-creation permissions, and drain each legacy subscription to a measured zero-backlog signal. Deploy consumers before publishers behind a version fence. Abort before the first lane-qualified publication if drain or provisioning fails. After lane-qualified publication begins, recovery is roll-forward-only: restore new consumers, reconcile legacy and lane-qualified backlog counts, and retain legacy topics until the deployment owner signs off.

**Registration overloads:** `UsePulsar(...)` accepts the standard trio — an `IConfiguration` section, an `Action<PulsarMessagingOptions>` delegate, or an `Action<PulsarMessagingOptions, IServiceProvider>` delegate — plus the service-URL convenience form.

## Dependencies

- `Headless.Messaging.Core`
- `Pulsar.Client`

## Side Effects

- Creates Pulsar topics in configured tenant/namespace
- Establishes persistent connections to Pulsar brokers
- Creates subscriptions for consumer groups
