# Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks and reader-writer locks.

## Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, release, and reader-writer transitions.

## Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `RedisDistributedReaderWriterLockStorage` implements `IDistributedReaderWriterLockStorage`.
- `AddRedisDistributedLock(...)` registers a Redis-backed lock provider.
- `AddRedisDistributedReaderWriterLock(...)` registers a Redis-backed reader-writer lock provider.
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

builder.Services.AddRedisDistributedReaderWriterLock(options =>
{
    options.KeyPrefix = "distributed-lock:";
});
```

## Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`. Default lock expiration is 20 minutes and default acquire timeout is 30 seconds; override those per lock-acquire call. `LockMonitoringMode` (lease monitoring and auto-extension) is a storage-agnostic provider feature configured through `Headless.DistributedLocks.Core`.

Reader-writer storage creates `{resource}:writer` and `{resource}:readers` Redis keys internally. Resource names containing `{` or `}` are rejected so the storage-owned Redis cluster hash-tag remains deterministic. Writer-preference blocks new readers while a writer is queued.

## Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Redis`
- `StackExchange.Redis`

## Side Effects

- Registers `HeadlessRedisScriptsLoader`.
- Registers `IDistributedLockProvider` through `Headless.DistributedLocks.Core`.
- Registers `IDistributedReaderWriterLockProvider` through `Headless.DistributedLocks.Core` when `AddRedisDistributedReaderWriterLock(...)` is called.
