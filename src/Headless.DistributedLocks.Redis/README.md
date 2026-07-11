# Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks, reader-writer locks, and semaphores.

## Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, release, reader-writer transitions, semaphore slots, and fencing-token issuance.

## Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `RedisDistributedReadWriteLockStorage` implements `IDistributedReadWriteLockStorage`.
- `RedisDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `UseRedis()` registers Redis-backed mutex, reader-writer lock, and semaphore providers through `AddHeadlessDistributedLocks(...)`.
- Uses `HeadlessRedisScriptsLoader` for atomic Lua script operations.
- Mutex compare-and-swap uses Redis `KEEPTTL`, preserving the existing expiration when `ReplaceIfEqualAsync(..., newTtl: null)` is used.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Redis
```

## Quick Start

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
        options.MaxResourceNameLength = 512;
    });

    setup.UseRedis();
});
```

## Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`. Default lock expiration is 20 minutes and default acquire timeout is 30 seconds; override those per lock-acquire call. `LockMonitoringMode` (lease monitoring and auto-extension) is a storage-agnostic provider feature configured through `Headless.DistributedLocks.Core`.

Redis mutex storage issues `IDistributedLease.FencingToken` with an atomic Lua acquire script: only a successful grant increments the no-TTL fence counter. Mutex storage maps logical lock names to internal hash-tagged Redis keys so the lock key and fence counter share a Redis Cluster slot. Redis semaphores use `{resource}:holders` (ZSET of `leaseId → expiry-epoch-ms`) plus `fence:{resource}`. Fencing is best-effort and requires Redis to retain the counter key; avoid `allkeys-*` eviction policies when stale-write rejection depends on Redis fencing.

Reader-writer storage creates `{resource}:writer` (string holding the active writer id or the `:_WRITERWAITING`-suffixed waiting marker) and `{resource}:readers` (HASH of `leaseId → expiry-epoch-ms`, with per-entry expiry computed inside Lua via `redis.call('TIME')`) Redis keys internally. Resource names containing `{` or `}` are rejected so the storage-owned Redis cluster hash-tag remains deterministic. Writer-preference blocks new readers while a writer is queued; readers running `Monitoring = AutoExtend` may see `LostToken` fire when a writer queues — that signals the reader to drop and reacquire after the writer drains. Marker TTL is governed by `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30s).

## Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `StackExchange.Redis`
- Redis server **6.2+** (semaphore lease extension uses grow-only `ZADD GT`, which never shortens a live holder's TTL).

## Side Effects

- Registers a keyed `HeadlessRedisScriptsLoader` bound to the app's `IConnectionMultiplexer`.
- Registers hosted `IInitializer` warmup for Redis mutex, reader-writer, and semaphore scripts.
- Registers `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core`.
