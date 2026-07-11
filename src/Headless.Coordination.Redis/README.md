# Headless.Coordination.Redis

Stores coordination membership in Redis with Lua scripts and Redis server time.

## Problem Solved

Provides a Redis-backed membership provider for deployments where Redis is the authoritative coordination store.

## Key Features

- Incarnation allocation uses persistent `INCR` counters.
- Heartbeat, read, leave, and cleanup scripts use Redis `TIME`.
- Heartbeats reject dead, gracefully left, and missing/pruned member payloads for the same incarnation.
- `:known` retains recently dead members so Dead is observable before cleanup.
- `:known` also mirrors current node generations so snapshot reads do not issue one `GET` per member.
- Generation counters are not purged by default.

## Design Notes

Redis keys use a cluster hash tag around `ClusterName`. The durable `:gen:<node-id>` counters carry no TTL, so an `allkeys-*` `maxmemory-policy` can evict a live node's counter under memory pressure. The next heartbeat then fails the generation guard, the node treats its own membership as lost, and under the default `MembershipLostBehavior.StopApplication` the host is asked to stop — a silent eviction surfaces as a spurious shutdown. Run coordination against a Redis instance or logical database configured with `noeviction` or a `volatile-*` policy; coordination keys carry no TTL, so `volatile-*` never evicts them.

**Dead/Left retention divergence (intentional, plan KTD-16).** Redis retains Dead and Left descriptors in the `:known` hash for `RedisKnownNodeRetention` (default 7 days), so liveness snapshots keep surfacing them with `State = Dead` until that window elapses — consumers filter by `NodeLivenessState`. The relational providers prune shortly after `DeadThreshold + DeadRetentionWindow` (tens of seconds). Lower `RedisKnownNodeRetention` to align Redis with relational pruning.

**No per-call command timeout (configure on the multiplexer).** Coordination does not set a per-call command timeout on its Redis operations. A hung or unresponsive Redis will therefore block heartbeat and other membership calls until the socket-level timeout fires. Configure `SyncTimeout`/`AsyncTimeout` on the `IConnectionMultiplexer` you inject so these calls fail fast under that bound instead of stalling membership.

**Generation counters are retained indefinitely (use stable node-ids).** Per-node generation (`INCR`) counters are never purged — they are required to reject stale incarnations after a node restarts. Prefer **stable** node-ids: ephemeral or randomly-generated node-ids cause generation keys to accumulate without bound, since each fresh id allocates a new permanent counter.

**Generation mirrors in `:known` are read-path projections, not authority.** The durable per-node generation key remains the heartbeat guard. Allocation and heartbeat scripts mirror the current value into a reserved `:known` hash field named `__gen:<node-id>`, so read Lua can classify retained member payloads from one `HGETALL` result instead of calling `GET` for every member. Cleanup sweeps a mirror field once its node has no surviving member payload (orphan prune); the durable generation key is never touched, so a restarting node re-mirrors on its next allocate or heartbeat.

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
