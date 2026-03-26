# Headless.Messaging.RedisStreams

Redis Streams transport provider for the messaging system.

## Problem Solved

Enables lightweight, high-performance message streaming using Redis Streams with consumer groups, persistence, and at-least-once delivery guarantees.

## Key Features

- **Redis Streams**: Append-only log structure for message streaming
- **Consumer Groups**: Load balancing and parallel processing
- **Persistence**: Durable message storage with configurable retention
- **Claim Messages**: Automatic reprocessing of unacknowledged messages
- **Low Latency**: Sub-millisecond message delivery

## Installation

```bash
dotnet add package Headless.Messaging.RedisStreams
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseRedisStreams(redis =>
    {
        redis.Configuration = "localhost:6379";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseRedisStreams(redis =>
{
    redis.Configuration = "localhost:6379,ssl=true,password=secret";
    redis.StreamEntriesCount = 10;
    redis.ConnectionPoolSize = 10;
});
```

## Messaging Semantics

- Publish appends the serialized body and headers as Redis Stream entries.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit acknowledges the pending entry with `XACK`.
- Reject is a no-op. Pending entries are reclaimed later by the claim loop for redelivery.
- Consumer startup creates streams and consumer groups as needed.
- The transport periodically claims abandoned pending entries and routes them back through the handler.
- Single-threaded consumption follows stream order best. Consumer groups, claiming, and parallel handlers can reorder work.
- Stream names, field sizes, and payload limits follow Redis memory and broker limits.

## Dependencies

- `Headless.Messaging.Core`
- `StackExchange.Redis`

## Side Effects

- Creates Redis Streams for each topic
- Creates consumer groups for message distribution
- Maintains persistent connections to Redis
- Periodically claims pending messages for retry
