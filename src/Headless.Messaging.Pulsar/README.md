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

## Installation

```bash
dotnet add package Headless.Messaging.Pulsar
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UsePulsar(pulsar =>
    {
        pulsar.ServiceUrl = "pulsar://localhost:6650";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UsePulsar(pulsar =>
{
    pulsar.ServiceUrl = "pulsar://localhost:6650";
    pulsar.TenantName = "public";
    pulsar.NamespaceName = "default";
    pulsar.ConnectionPoolSize = 10;
});
```

## Messaging Semantics

- Publish sends the serialized body as Pulsar payload bytes and preserves headers as properties.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit acknowledges the message.
- Reject sends a negative acknowledgment so Pulsar can redeliver under subscription policy.
- Consumer startup subscribes the group name to the configured topics in the tenant and namespace.
- Topic creation and retention still follow broker configuration for that tenant and namespace.
- Shared subscriptions favor throughput over strict ordering. Single-threaded consumption gives the most stable order.
- Topic names, property sizes, and payload limits follow Pulsar broker limits.

## Dependencies

- `Headless.Messaging.Core`
- `DotPulsar`

## Side Effects

- Creates Pulsar topics in configured tenant/namespace
- Establishes persistent connections to Pulsar brokers
- Creates subscriptions for consumer groups
