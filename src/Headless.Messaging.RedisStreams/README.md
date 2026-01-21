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

## Dependencies

- `Headless.Messaging.Core`
- `StackExchange.Redis`

## Side Effects

- Creates Redis Streams for each topic
- Creates consumer groups for message distribution
- Maintains persistent connections to Redis
- Periodically claims pending messages for retry
