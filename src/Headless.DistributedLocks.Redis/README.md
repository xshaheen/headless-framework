# Headless.DistributedLocks.Redis

Redis-based resource lock storage using StackExchange.Redis.

## Problem Solved

Provides high-performance distributed locking using Redis with atomic Lua scripts for lock acquisition and release, suitable for multi-instance production deployments.

## Key Features

- `RedisDistributedLockStorage` - Atomic lock operations via Redis
- `RedisThrottlingDistributedLockStorage` - Rate-limited locking
- Lua scripts for atomic acquire/release
- High performance and reliability

## Installation

```bash
dotnet add package Headless.DistributedLocks.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Add resource locks with Redis storage
builder.Services.AddDistributedLock();
builder.Services.AddSingleton<IDistributedLockStorage, RedisDistributedLockStorage>();
```

## Configuration

No additional configuration beyond Redis connection.

## Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Redis`
- `StackExchange.Redis`

## Side Effects

- Registers `IDistributedLockStorage` as singleton
- Registers `IThrottlingDistributedLockStorage` as singleton (optional)
