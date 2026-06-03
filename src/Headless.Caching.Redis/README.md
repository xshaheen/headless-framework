# Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

## Problem Solved

Provides distributed caching using Redis via the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

## Key Features

- Full `IRemoteCache` implementation using StackExchange.Redis
- Supports strongly-typed `IRemoteCache<T>` pattern
- Prefix-based key management
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower)
- Set/list operations with pagination
- Lua scripts for atomic multi-key operations
- Redis Cluster support

## Design Notes

Redis scalar cache entries are stored as a versioned binary envelope: a 19-byte header followed by the raw value segment produced by the cache value codec. The header starts with magic/version bytes `0xFF 0x01`, then flags, then logical and physical expiration timestamps encoded as little-endian Unix milliseconds. Physical expiration is still mapped to the Redis key TTL; logical expiration rides in the payload so later fail-safe and refresh features can diverge logical staleness from physical eviction without changing the wire format.

Null scalar values are represented by a header flag with an empty value segment. The literal string `"@@NULL"` is now a normal cacheable string when written through Redis cache APIs. Raw legacy keys containing `"@@NULL"` still read as null. Atomic counters (`Increment`, `SetIfHigher`, `SetIfLower`) remain raw Redis-native numeric strings so Redis can perform native atomic arithmetic; their read path falls back to the raw value codec.

## Installation

```bash
dotnet add package Headless.Caching.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddRedisCache(options =>
{
    options.ConnectionMultiplexer = redis;
    options.KeyPrefix = "myapp:";
});
```

## Configuration

### Options

```csharp
options.ConnectionMultiplexer = multiplexer;
options.KeyPrefix = "myapp:";
```

## Cancellation Token Behavior

Cancellation is checked **at the start** of each operation. Once an operation begins, it completes without interruption:

| Operation Type | Cancellation Behavior |
|---------------|----------------------|
| Single-key ops (`GetAsync`, `UpsertAsync`, etc.) | Checked before Redis call; operation completes atomically |
| Batch ops (`UpsertAllAsync`, `RemoveAllAsync`) | Checked before batch starts; all keys processed atomically |
| SCAN-based ops (`RemoveByPrefixAsync`, `GetAllKeysByPrefixAsync`) | Cancellable during iteration (unbounded key sets) |

This design ensures consumers never observe partial results from batch operations.

> **Note:** StackExchange.Redis doesn't support `CancellationToken` in its API. Timeouts are configured via `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Hosting`
- `Headless.Redis`
- `Headless.Serializer.Json`
- `StackExchange.Redis`

## Side Effects

- Registers `IRemoteCache` as singleton
- Registers `IRemoteCache<T>` as singleton
- Registers a keyed `HeadlessRedisScriptsLoader` bound to `RedisCacheOptions.ConnectionMultiplexer`
- Registers a hosted `IInitializer` that warms Redis cache Lua scripts on host start
- Uses existing `IConnectionMultiplexer` if registered, otherwise creates one
