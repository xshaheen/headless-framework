# Headless.Messaging.Kafka

Apache Kafka transport provider for the messaging system.

## Problem Solved

Enables high-throughput, distributed event streaming using Apache Kafka with consumer groups, partitions, broker-level ordering controls, and Headless's at-least-once delivery contract.

## Key Features

- **High Throughput**: Handle millions of messages per second
- **Partitioning**: Parallel processing with ordered delivery per partition
- **Consumer Groups**: Load balancing across consumers
- **Retention**: Persistent message storage with configurable retention
- **At-Least-Once Delivery**: Broker delivery plus Headless retry/outbox recovery; consumers must remain idempotent
- **Kafka Configuration Access**: Exposes raw Kafka producer and consumer settings through `MainConfig` and consumer-specific options

## Design Notes

Kafka is queue-intent only in this package. `PartitionBy(...)` maps to the Kafka key. The framework does not impose a Kafka key length cap; broker/client configuration owns practical limits. Delivery remains at-least-once; consumers must dedupe by business key or message id. When consumer concurrency is greater than one, successful handlers can finish out of order, but Kafka commits advance only through the contiguous completed offset watermark per partition; a completed high offset does not commit past lower in-flight offsets.

## Installation

```bash
dotnet add package Headless.Messaging.Kafka
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");

    options.UseKafka(kafka =>
    {
        kafka.Servers = "localhost:9092";
    });
});
```

## Configuration

```csharp
options.UseKafka(kafka =>
{
    kafka.Servers = "localhost:9092,localhost:9093";
    kafka.ConnectionPoolSize = 10;

    // Per-consume header augmentation. Receives the raw ConsumeResult and the live DI scope,
    // returns extra headers to attach before the framework dispatches to handlers.
    kafka.CustomHeadersBuilder = (consumeResult, services) => [new KeyValuePair<string, string>("app", "myapp")];

    // Kafka-specific producer/consumer settings via the raw librdkafka config dictionary.
    kafka.MainConfig["enable.idempotence"] = "true";
    kafka.MainConfig["max.in.flight.requests.per.connection"] = "1"; // Strict ordering
});
```

Message-level Kafka knobs attach to `ForMessage<TMessage>(...)`:

```csharp
options.ForMessage<OrderEvent>(message =>
    message.MessageName("orders.events").UseKafka(kafka => kafka.PartitionBy(order => order.CustomerId.ToString()))
);
```

`PartitionBy(...)` stamps `KafkaHeaders.KafkaKey` (`headless-kafka-key`) during publish. The selector output is broker-visible metadata, so do not put secrets or raw PII in it.

Consumer-side Kafka knobs attach to the consumer registration:

```csharp
options.ForMessage<OrderEvent>(message =>
    message.OnQueue<OrderWorker>(consumer =>
        consumer.Group("orders").UseKafka(kafka => kafka.IsolationLevel(IsolationLevel.ReadCommitted))
    )
);
```

## Message Ordering

Kafka provides **strict FIFO ordering within partitions**:

### Partition-Based Ordering

Messages sent to the same partition are delivered in order. Use `UseKafka(...).PartitionBy(...)` to route related messages to the same partition:

```csharp
options.ForMessage<OrderEvent>(message =>
    message.MessageName("orders.events").UseKafka(kafka => kafka.PartitionBy(order => order.CustomerId.ToString()))
);
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

## Messaging Semantics

- Publish writes the serialized body as record bytes and forwards framework headers.
- Delivery remains at-least-once. A broker accept followed by a failed success-mark write can redeliver; configure Kafka idempotence or read-committed isolation for broker-level features, and keep consumers idempotent.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit commits the consumed partition offset. Under concurrent consumption, commits advance only through the contiguous completed offset watermark per partition.
- Reject seeks back to the failed offset so Kafka can redeliver on the next poll.
- `FetchTopicsAsync(...)` creates concrete topics when auto-create is enabled and normalizes wildcard subscriptions.
- `SubscribeAsync(...)` joins the configured consumer group to those topics.
- Partition keys control ordering. Parallel handlers or multiple partitions can reorder observed processing.
- Topic names, header sizes, and record sizes follow Kafka broker limits.

## Dependencies

- `Headless.Messaging.Core`
- `Confluent.Kafka`

## Side Effects

- Creates Kafka topics if they don't exist
- Establishes persistent connections to Kafka brokers
- Joins consumer groups for load balancing
