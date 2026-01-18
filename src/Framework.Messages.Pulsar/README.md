# Framework.Messages.Pulsar

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
dotnet add package Framework.Messages.Pulsar
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

## Dependencies

- `Framework.Messages.Core`
- `DotPulsar`

## Side Effects

- Creates Pulsar topics in configured tenant/namespace
- Establishes persistent connections to Pulsar brokers
- Creates subscriptions for consumer groups
