# Headless.Messaging.Redis

Redis transport provider for the messaging system.

## Problem Solved

Provides durable Bus and Queue delivery over Redis Streams with lane isolation and consumer-group competition.

## Key Features

- Redis Streams Bus and Queue transports through `UseRedis(...)`.
- One Bus copy per logical subscriber group, with replicas competing inside the group.
- One Queue copy owned by competing destination replicas.
- Consumer groups, acknowledgement, pending-entry claiming, and at-least-once delivery.
- Lane-qualified stream keys isolate the same logical name on Bus and Queue.
- Shared StackExchange.Redis dependency and Redis configuration model.
- Consumer startup honors host cancellation through connection, provisioning, and subscription.

## Design Notes

`UseRedis(...)` registers both lanes. Physical stream keys use `headless:messaging:bus:{logical-name}` and `headless:messaging:queue:{logical-name}`. Bus subscriber groups each receive one retained copy; replicas in a group share its Redis consumer group. Queue replicas share the destination group.

The former `UseRedisPubSub(...)` API and volatile Pub/Sub runtime are removed. Existing deployments must fence old producers and consumers, drain or explicitly reconcile legacy stream/channel traffic, deploy the Streams-only package family in lockstep, and retain legacy resources until the abort window closes. The provider does not delete legacy resources automatically.

## Installation

```bash
dotnet add package Headless.Messaging.Redis
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForConsumersFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");

    // Bus and Queue delivery through lane-qualified Redis Streams.
    options.UseRedis("localhost:6379");
});
```

## Configuration

`UseRedis(string)` configures Redis Streams for both lanes. For richer options use `UseRedis(Action<RedisMessagingOptions>)`; `RedisMessagingOptions.Configuration` is a StackExchange.Redis `ConfigurationOptions` instance:

```csharp
options.UseRedis(redis =>
{
    redis.Configuration = ConfigurationOptions.Parse("localhost:6379,ssl=true,password=secret");
    redis.StreamEntriesCount = 10;
    redis.ConnectionPoolSize = 10;
});
```

**Registration overloads:** `UseRedis(...)` accepts an `IConfiguration` section, an `Action<RedisMessagingOptions>` delegate, or an `Action<RedisMessagingOptions, IServiceProvider>` delegate, plus the parameterless and connection-string convenience forms.

## Dependencies

- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Core`
- `Headless.Messaging.Queue.Abstractions`
- `StackExchange.Redis`

## Side Effects

- Registers both `IBusTransport` and `IQueueTransport` for Redis Streams when `UseRedis(...)` is called.
- Creates lane-qualified Redis Streams and consumer groups for message names as needed.
- Maintains persistent Redis connections.
- Periodically claims pending stream messages for retry.
