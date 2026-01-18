# Framework.Messages.Kafka

Apache Kafka transport provider for the messaging system.

## Problem Solved

Enables high-throughput, distributed event streaming using Apache Kafka with consumer groups, partitions, and exactly-once semantics.

## Key Features

- **High Throughput**: Handle millions of messages per second
- **Partitioning**: Parallel processing with ordered delivery per partition
- **Consumer Groups**: Load balancing across consumers
- **Retention**: Persistent message storage with configurable retention
- **Exactly-Once**: Transactional publishing and consuming

## Installation

```bash
dotnet add package Framework.Messages.Kafka
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseKafka(kafka =>
    {
        kafka.Servers = "localhost:9092";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseKafka(kafka =>
{
    kafka.Servers = "localhost:9092,localhost:9093";
    kafka.ConnectionPoolSize = 10;
    kafka.CustomHeaders = headers => headers.Add("app", "myapp");
});
```

## Dependencies

- `Framework.Messages.Core`
- `Confluent.Kafka`

## Side Effects

- Creates Kafka topics if they don't exist
- Establishes persistent connections to Kafka brokers
- Joins consumer groups for load balancing
