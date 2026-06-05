# Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks, reader-writer locks, and semaphores.

## Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, release, reader-writer transitions, semaphore slots, and fencing-token issuance.

## Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `RedisDistributedReadWriteLockStorage` implements `IDistributedReadWriteLockStorage`.
- `RedisDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `AddRedisDistributedLock(...)` registers a Redis-backed lock provider.
- `AddRedisDistributedReadWriteLock(...)` registers a Redis-backed reader-writer lock provider.
- `AddRedisDistributedSemaphore(...)` registers a Redis-backed semaphore provider.
- Uses `HeadlessRedisScriptsLoader` for atomic Lua script operations.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Redis
```

## Quick Start

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect("localhost:6379")
);

builder.Services.AddRedisDistributedLock(options =>
{
    options.KeyPrefix = "distributed-lock:";
    options.MaxResourceNameLength = 512;
});

builder.Services.AddRedisDistributedReadWriteLock(options =>
{
    options.KeyPrefix = "distributed-lock:";
});

builder.Services.AddRedisDistributedSemaphore(options =>
{
    options.KeyPrefix = "distributed-lock:";
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
- Registers hosted `IInitializer` warmup for only the Redis lock feature scripts that were registered:
  mutex scripts for `AddRedisDistributedLock(...)`, reader-writer scripts for `AddRedisDistributedReadWriteLock(...)`, and semaphore scripts for `AddRedisDistributedSemaphore(...)`.
- Registers `IDistributedLock` through `Headless.DistributedLocks.Core`.
- Registers `IDistributedReadWriteLock` through `Headless.DistributedLocks.Core` when `AddRedisDistributedReadWriteLock(...)` is called.
- Registers `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core` when `AddRedisDistributedSemaphore(...)` is called.
