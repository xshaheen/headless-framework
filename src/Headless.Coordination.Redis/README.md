# Headless.Coordination.Redis

Stores coordination membership in Redis with Lua scripts and Redis server time.

## Problem Solved

Provides a Redis-backed membership provider for deployments where Redis is the authoritative coordination store.

## Key Features

- Incarnation allocation uses persistent `INCR` counters.
- Heartbeat, read, leave, and cleanup scripts use Redis `TIME`.
- `:known` retains recently dead members so Dead is observable before cleanup.
- Generation counters are not purged by default.

## Design Notes

Redis keys use a cluster hash tag around `ClusterName`. Avoid eviction policies that can delete generation counters when stale-heartbeat rejection matters.

## Installation

```bash
dotnet add package Headless.Coordination.Redis
```

## Quick Start

```csharp
services.AddSingleton<IConnectionMultiplexer>(multiplexer);
services.AddRedisCoordination(options =>
{
    options.RedisCleanupInterval = TimeSpan.FromMinutes(5);
});
services.Configure<CoordinationOptions>(options =>
{
    options.ClusterName = "orders";
    options.ConfiguredNodeId = "orders-worker-0";
});
```

## Configuration

Configure `RedisCleanupInterval` and `RedisKnownNodeRetention`. `RedisKnownNodeRetention` is treated as at least `DeadThreshold + DeadRetentionWindow`. Configure shared `CoordinationOptions` for cluster name, node id, thresholds, role, metadata, and membership-loss behavior.

## Dependencies

- `Headless.Coordination.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `StackExchange.Redis`

## Side Effects

Registers the core membership services, Redis membership store, `ProviderCapabilities`, keyed Lua script loader, script initializer hosted service, and cleanup hosted service. Requires an `IConnectionMultiplexer` registration.
