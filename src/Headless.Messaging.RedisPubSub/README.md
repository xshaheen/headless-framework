# Headless.Messaging.RedisPubSub

Redis Pub/Sub bus transport provider for Headless Messaging.

## Problem Solved

Provides low-latency broadcast delivery to subscribers that are connected at publish time. Use it for notifications, cache invalidation, and ephemeral fan-out where replay is not required.

## Key Features

- **Bus-only transport**: Registers `IBusTransport` and does not register `IQueueTransport`.
- **Volatile delivery**: Offline subscribers miss messages published while disconnected.
- **Redis channels**: Publishes each message type to a Redis Pub/Sub channel.
- **Shared Redis options**: Uses native `StackExchange.Redis` `ConfigurationOptions`.

## Design Notes

Redis Pub/Sub has no broker-side queue, consumer group, acknowledgment, or replay model. Choose `Headless.Messaging.RedisStreams` when messages must survive disconnects or be processed by competing workers.

## Installation

```bash
dotnet add package Headless.Messaging.RedisPubSub
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseRedisPubSub("localhost:6379");

    options.SubscribeFromAssemblyContaining<Program>();
});
```

## Configuration

```csharp
options.UseRedisPubSub(redis =>
{
    redis.Configuration = ConfigurationOptions.Parse("localhost:6379,ssl=true,password=secret");
});
```

## Dependencies

- `Headless.Messaging.Core`
- `StackExchange.Redis`

## Side Effects

- Registers a Redis Pub/Sub bus transport.
- Opens a shared Redis connection multiplexer.
- Subscribes consumer clients to Redis channels for discovered message types.
