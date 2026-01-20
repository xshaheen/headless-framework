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

    // Kafka-specific producer settings for ordering
    kafka.MainConfig["enable.idempotence"] = "true";
    kafka.MainConfig["max.in.flight.requests.per.connection"] = "1"; // Strict ordering
});
```

## Message Ordering

Kafka provides **strict FIFO ordering within partitions**:

### Partition-Based Ordering

Messages sent to the same partition are delivered in order. Use message keys to route related messages to the same partition:

```csharp
// Publish with partition key for ordered delivery
await publisher.PublishAsync("orders.events", order,
    headers: new Dictionary<string, string>
    {
        { "PartitionKey", order.CustomerId.ToString() }
    });
```

### Configuration for Strict Ordering

```csharp
kafka.MainConfig["enable.idempotence"] = "true";
kafka.MainConfig["max.in.flight.requests.per.connection"] = "1";
kafka.MainConfig["acks"] = "all";
```

### Consumer Configuration

Set `ConsumerThreadCount = 1` for sequential processing:

```csharp
options.ConsumerThreadCount = 1; // Sequential processing maintains partition order
options.EnableSubscriberParallelExecute = false; // Disable parallel execution
```

### Ordering Guarantees

- Messages with same partition key: Strictly ordered
- Messages without partition key: Round-robin distribution, no ordering guarantee
- Multiple consumer threads (`ConsumerThreadCount > 1`): May process out of order

## Dependencies

- `Framework.Messages.Core`
- `Confluent.Kafka`

## Side Effects

- Creates Kafka topics if they don't exist
- Establishes persistent connections to Kafka brokers
- Joins consumer groups for load balancing
