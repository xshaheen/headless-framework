# Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks.

## Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, and release operations.

## Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `AddRedisDistributedLock(...)` registers a Redis-backed lock provider.
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
```

## Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`. Default lock expiration is 20 minutes and default acquire timeout is 30 seconds; override those per lock-acquire call. `LockMonitoringMode` (lease monitoring and auto-extension) is a storage-agnostic provider feature configured through `Headless.DistributedLocks.Core`.

## Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Redis`
- `StackExchange.Redis`

## Side Effects

- Registers `HeadlessRedisScriptsLoader`.
- Registers `IDistributedLockProvider` through `Headless.DistributedLocks.Core`.
