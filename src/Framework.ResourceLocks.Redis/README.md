# Framework.ResourceLocks.Redis

Redis-based resource lock storage using StackExchange.Redis.

## Problem Solved

Provides high-performance distributed locking using Redis with atomic Lua scripts for lock acquisition and release, suitable for multi-instance production deployments.

## Key Features

- `RedisResourceLockStorage` - Atomic lock operations via Redis
- `RedisThrottlingResourceLockStorage` - Rate-limited locking
- Lua scripts for atomic acquire/release
- High performance and reliability

## Installation

```bash
dotnet add package Framework.ResourceLocks.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Add resource locks with Redis storage
builder.Services.AddResourceLock();
builder.Services.AddSingleton<IResourceLockStorage, RedisResourceLockStorage>();
```

## Configuration

No additional configuration beyond Redis connection.

## Dependencies

- `Framework.ResourceLocks.Core`
- `Framework.Redis`
- `StackExchange.Redis`

## Side Effects

- Registers `IResourceLockStorage` as singleton
- Registers `IThrottlingResourceLockStorage` as singleton (optional)
