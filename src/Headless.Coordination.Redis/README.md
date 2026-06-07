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

**Dead/Left retention divergence (intentional, plan KTD-16).** Redis retains Dead and Left descriptors in the `:known` hash for `RedisKnownNodeRetention` (default 7 days), so liveness snapshots keep surfacing them with `State = Dead` until that window elapses — consumers filter by `NodeLivenessState`. The relational providers prune shortly after `DeadThreshold + DeadRetentionWindow` (tens of seconds). Lower `RedisKnownNodeRetention` to align Redis with relational pruning.

## Installation

```bash
dotnet add package Headless.Coordination.Redis
```

## Quick Start

```csharp
services.AddSingleton<IConnectionMultiplexer>(multiplexer);

services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
        options.ConfiguredNodeId = "orders-worker-0";
    });

    setup.UseRedis(options =>
    {
        options.RedisCleanupInterval = TimeSpan.FromMinutes(5);
    });
});
```

## Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `RedisCleanupInterval` and `RedisKnownNodeRetention` with `setup.UseRedis(...)`. `RedisKnownNodeRetention` is treated as at least `DeadThreshold + DeadRetentionWindow`.

## Dependencies

- `Headless.Coordination.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `StackExchange.Redis`

## Side Effects

Registers the core membership services, Redis membership store, keyed Lua script loader, script initializer hosted service, and cleanup hosted service. Requires an `IConnectionMultiplexer` registration.
