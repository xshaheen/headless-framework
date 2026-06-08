# Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

## Problem Solved

Provides Redis-backed caching through the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

## Key Features

- Full `IRemoteCache` implementation using StackExchange.Redis.
- Supports strongly typed `IRemoteCache<T>`.
- Prefix-based key management.
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower).
- Set/list operations with pagination.
- Lua scripts for atomic multi-key operations.
- Redis Cluster support.
- Shared `GetOrAddAsync` fail-safe, factory timeout, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Scalar write operations (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `UpsertAllAsync`) store entries as a versioned binary envelope: a 19-byte header followed by the raw value segment produced by the cache value codec. The header starts with magic/version bytes `0xFF 0x01`, then flags, then logical and physical expiration timestamps encoded as little-endian Unix milliseconds. Physical expiration is mapped to the Redis key TTL; when fail-safe is enabled, Redis retains the key until physical expiration even after logical expiration has passed. Logical expiration rides in the payload so normal value reads can miss while `GetOrAddAsync` still has a fail-safe reserve. Atomic counters (`Increment`, `SetIfHigher`, `SetIfLower`) bypass framing and write raw Redis-native numeric strings.

The envelope byte layout is:

| Offset | Field | Description |
| --- | --- | --- |
| 0 | Magic | `0xFF`, marks a framed entry |
| 1 | Version | `0x01`, current envelope version |
| 2 | Flags | bit0 = `isNull`, bit1 = `hasLogicalExpiresAt`, bit2 = `hasPhysicalExpiresAt` |
| 3-10 | LogicalExpiresAt | `Int64` little-endian Unix milliseconds, present only when bit1 is set |
| 11-18 | PhysicalExpiresAt | `Int64` little-endian Unix milliseconds, present only when bit2 is set |
| 19+ | ValueSegment | raw codec bytes; empty when `isNull` is set |

Null scalar values are represented by a header flag with an empty value segment. The literal string `"@@NULL"` is a normal cacheable string when written through Redis cache APIs. Raw legacy keys containing `"@@NULL"` still read as null. Atomic counters remain raw Redis-native numeric strings so Redis can perform native atomic arithmetic; their read path falls back to the raw value codec.

Factory timeouts are enforced in the shared coordinator before provider writes. A soft-timeout background refresh writes through Redis on success and Redis TTL still follows physical expiration. StackExchange.Redis operation timeouts remain configured on `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`; they are separate from `CacheEntryOptions.FactorySoftTimeout` and `FactoryHardTimeout`.

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

```csharp
options.ConnectionMultiplexer = multiplexer;
options.KeyPrefix = "myapp:";
```

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `Headless.Serializer.Json`
- `StackExchange.Redis`

## Side Effects

- Registers `IRemoteCache` as singleton.
- Registers `IRemoteCache<T>` as singleton.
- Registers a keyed `HeadlessRedisScriptsLoader` bound to `RedisCacheOptions.ConnectionMultiplexer`.
- Registers a hosted `IInitializer` that warms Redis cache Lua scripts on host start.
- Uses existing `IConnectionMultiplexer` if registered, otherwise creates one.
