# Headless.Messaging.Redis

Redis transport provider for the messaging system.

## Problem Solved

Provides both Redis Streams queue delivery and Redis Pub/Sub broadcast delivery from one package, so applications can choose queue or bus semantics without referencing separate Redis packages.

## Key Features

- Redis Streams queue transport through `UseRedis(...)`.
- Redis Pub/Sub bus transport through `UseRedisPubSub(...)`.
- Consumer groups, acknowledgment, pending-entry claiming, and durable queue delivery for Streams.
- Volatile broadcast delivery for Pub/Sub to currently connected subscribers.
- Shared StackExchange.Redis dependency and Redis configuration model.
- Streams and Pub/Sub consumer startup honor host cancellation through connection, provisioning, and subscription.

## Design Notes

Redis Streams and Redis Pub/Sub are different broker semantics behind one package. Use Streams (`UseRedis`) for queue consumers and durable work distribution. Use Pub/Sub (`UseRedisPubSub`) for broadcast events where disconnected subscribers may miss messages.

`IOutboxBus + UseRedisPubSub` persists the framework-side publish until Redis accepts `PUBLISH`; broker-side delivery remains volatile after that handoff. Choose Streams when Redis itself must retain messages for disconnected or competing consumers.

## Installation

```bash
dotnet add package Headless.Messaging.Redis
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");

    // Queue delivery through Redis Streams.
    options.UseRedis("localhost:6379");

    // Broadcast delivery through Redis Pub/Sub.
    options.UseRedisPubSub("localhost:6379");
});
```

## Configuration

`UseRedis(string)` configures Redis Streams queue delivery. For richer options use `UseRedis(Action<RedisMessagingOptions>)`; `RedisMessagingOptions.Configuration` is a StackExchange.Redis `ConfigurationOptions` instance:

```csharp
options.UseRedis(redis =>
{
    redis.Configuration = ConfigurationOptions.Parse("localhost:6379,ssl=true,password=secret");
    redis.StreamEntriesCount = 10;
    redis.ConnectionPoolSize = 10;
});
```

`UseRedisPubSub(string)` configures Redis Pub/Sub bus delivery. For richer options use `UseRedisPubSub(Action<RedisPubSubMessagingOptions>)`:

```csharp
options.UseRedisPubSub(redis =>
{
    redis.Configuration = ConfigurationOptions.Parse("localhost:6379,ssl=true,password=secret");
});
```

**Registration overloads:** `UseRedis(...)` and `UseRedisPubSub(...)` each accept the standard trio — an `IConfiguration` section, an `Action<TOptions>` delegate, or an `Action<TOptions, IServiceProvider>` delegate — plus the parameterless and connection-string convenience forms (`RedisMessagingOptions` / `RedisPubSubMessagingOptions`).

## Dependencies

- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Core`
- `Headless.Messaging.Queue.Abstractions`
- `StackExchange.Redis`

## Side Effects

- Registers `IQueueTransport` for Redis Streams when `UseRedis(...)` is called.
- Registers `IBusTransport` for Redis Pub/Sub when `UseRedisPubSub(...)` is called.
- Creates Redis Streams and consumer groups for stream message names as needed.
- Maintains persistent Redis connections.
- Periodically claims pending stream messages for retry.
