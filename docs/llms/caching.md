---
domain: Caching
packages: Caching.Abstractions, Caching.Core, Caching.InMemory, Caching.Redis, Caching.Hybrid
---

# Caching

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Caching.Abstractions](#headlesscachingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Caching.Core](#headlesscachingcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Caching.InMemory](#headlesscachinginmemory)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
        - [Options](#options)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Caching.Redis](#headlesscachingredis)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
        - [appsettings.json](#appsettingsjson)
        - [Options](#options-1)
    - [Cancellation Token Behavior](#cancellation-token-behavior)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Caching.Hybrid](#headlesscachinghybrid)
    - [Installation](#installation-4)
    - [Prerequisites](#prerequisites)
    - [Usage](#usage)
        - [Basic Setup](#basic-setup)
        - [Using the Cache](#using-the-cache)
    - [Architecture](#architecture)
        - [Read Path](#read-path)
        - [Write/Invalidation Path](#writeinvalidation-path)
    - [Configuration](#configuration-4)
    - [Exception Handling](#exception-handling)
    - [Metrics](#metrics)

> Unified caching abstraction with in-memory, Redis distributed, and hybrid (L1+L2) implementations with cross-instance invalidation.

## Quick Orientation

Install `Headless.Caching.Abstractions` plus one provider. Code against `ICache` for all cache operations.

- **Single-instance / development**: `Headless.Caching.InMemory` — call `AddInMemoryCache()`. High performance, per-process, LRU eviction.
- **Multi-instance / production**: `Headless.Caching.Redis` — call `AddRedisCache(...)`. Distributed cache shared across instances via StackExchange.Redis.
- **L1 + L2 hybrid**: `Headless.Caching.Hybrid` — call `AddHybridCache()`. Combines in-memory L1 with Redis L2 and automatic cross-instance invalidation via pub/sub messaging.

`ICache` supports: upsert/get/remove with TTL, bulk operations, prefix-based operations, atomic operations (increment, compare-and-swap, SetIfHigher/Lower), and set operations.

Use `CacheValue<T>` return type — check `.HasValue` before accessing `.Value`.

## Agent Instructions

- Use `ICache` from `Headless.Caching.Abstractions` — NOT `Microsoft.Extensions.Caching.Distributed.IDistributedCache`. Use `IRemoteCache` only when a remote/L2 implementation is required.
- Use `Caching.InMemory` (`AddInMemoryCache()`) for development and single-instance deployments. Use `Caching.Redis` (`AddRedisCache()`) for production multi-instance deployments.
- For hybrid caching, register memory cache as non-default (`AddInMemoryCache(isDefault: false)`), then register Redis cache, then call `AddHybridCache()`. The hybrid cache becomes the default `ICache`.
- Always check `CacheValue<T>.HasValue` before accessing `.Value` — cache misses return `HasValue = false`, not null.
- `GetOrAddAsync` takes `CacheEntryOptions`. Passing a `TimeSpan` still works through implicit conversion when only duration is needed.
- Enable fail-safe per factory-backed entry with `CacheEntryOptions.IsFailSafeEnabled = true`. When the factory throws and a logically-expired value is still physically retained, `GetOrAddAsync` serves that value and returns `CacheValue<T>.IsStale = true`.
- Fail-safe retention is bounded by `max(Duration, FailSafeMaxDuration)` from entry creation. `FailSafeThrottleDuration` restamps logical expiration to avoid hammering a failing factory, but never extends physical retention.
- Normal value reads (`GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`) use logical expiration. A fail-safe reserve is only consumed by `GetOrAddAsync`.
- Keep direct write operations (`UpsertAsync`, `TryInsertAsync`, set/increment operations) on `TimeSpan?`; they do not establish a fail-safe reserve because they do not carry `CacheEntryOptions`.
- Caller cancellation never serves stale: if the `CancellationToken` you pass to `GetOrAddAsync` is cancelled, the exception propagates and fail-safe does not activate. A factory or store `OperationCanceledException` carrying an unrelated/downstream token (for example a timeout) is treated as a failure and *does* activate fail-safe. The distinction is by token identity, so a token-less `OperationCanceledException` under a non-cancelable caller token also activates fail-safe.
- Key length validation is the consumer's responsibility. The framework does not enforce key length limits for DoS protection — validate at your application boundary.
- StackExchange.Redis does not support `CancellationToken` — timeouts are configured via `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`. Cancellation is checked at the start of operations only.
- For Redis, SCAN-based operations (`RemoveByPrefixAsync`, `GetAllKeysByPrefixAsync`) are cancellable during iteration; single-key and batch operations complete atomically once started.
- Redis scalar entries use a versioned binary envelope. Do not parse Redis string bytes as the application payload directly; strip the envelope first unless the key is a raw counter.
- Redis key TTL follows physical expiration, not logical expiration, when fail-safe is enabled.
- Use `options.KeyPrefix` to namespace cache keys per application or module.
- Memory cache supports `CloneValues = true` for value isolation between callers — useful when cached objects are mutated after retrieval.
- Hybrid cache `DefaultLocalExpiration` controls L1 TTL independently of L2. Set to shorter durations than L2 for freshness.

---

# Headless.Caching.Abstractions

Defines the unified caching interface for both in-memory and distributed cache implementations.

## Problem Solved

Provides a provider-agnostic caching API, enabling seamless switching between memory and Redis caches without changing application code.

## Key Features

- `ICache` - Core interface for all cache operations:
    - Upsert/Get/Remove with expiration
    - Bulk operations (UpsertAll, GetAll, RemoveAll)
    - Prefix-based operations (GetByPrefix, RemoveByPrefix)
    - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower)
    - Set operations (SetAdd, SetRemove, GetSet)
- `IInMemoryCache` - Marker interface for in-memory implementations
- `IRemoteCache` - Marker interface for remote implementations
- `ICache<T>` - Strongly-typed cache wrapper
- `CacheValue<T>` - Cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - Factory-backed entry options: `Duration`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, and `FailSafeThrottleDuration`.

## Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeout, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

Fail-safe is opt-in and only applies to `GetOrAddAsync`. Direct writes keep the `TimeSpan?` API and write logical expiration equal to physical expiration. A stale value served by fail-safe returns `CacheValue<T>.IsStale = true` only for the activating call; reads during the throttle window are logical hits and return `IsStale = false`.

## Installation

```bash
dotnet add package Headless.Caching.Abstractions
```

## Quick Start

```csharp
public sealed record Product(int Id, string Name);

public interface IProductRepository
{
    ValueTask<Product?> GetAsync(int id, CancellationToken cancellationToken);
}

public sealed class ProductService(ICache cache, IProductRepository repository)
{
    public async Task<Product?> GetProductAsync(int id, CancellationToken ct)
    {
        var key = $"product:{id}";
        var cached = await cache
            .GetOrAddAsync(key, token => repository.GetAsync(id, token), TimeSpan.FromMinutes(10), ct)
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }

    public async Task<Product?> GetProductWithOptionsAsync(int id, CancellationToken ct)
    {
        var key = $"product:{id}";
        var cached = await cache
            .GetOrAddAsync(
                key,
                token => repository.GetAsync(id, token),
                new CacheEntryOptions
                {
                    Duration = TimeSpan.FromMinutes(10),
                    IsFailSafeEnabled = true,
                    FailSafeMaxDuration = TimeSpan.FromHours(1),
                    FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                },
                ct
            )
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None. This is an abstractions package.

# Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

## Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, and throttle behavior.

## Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads and writes.
- `CacheStoreEntry<T>` - logical and physical expiration snapshot used by the coordinator.
- `CacheStoreEntryExtensions` - shared `IsFresh`/`IsPhysicallyPresent` predicates so every provider and the coordinator agree on the expiration boundary (an entry is expired at the exact tick, `expiresAt <= now`).
- `FactoryCacheCoordinator.IsCallerCancellation` - shared predicate provider composites use so caller cancellation propagates while an unrelated/downstream `OperationCanceledException` activates fail-safe consistently.
- Fail-safe activation log when stale data is served.

## Design Notes

Providers construct the coordinator directly with their `TimeProvider` and logger; the Core package has no DI setup. Store read failures are treated as misses, and fail-safe restamp writes are best-effort so a stale value can still be returned when the backing store is unhealthy. Cancellation is classified by token identity: the caller's own cancellation propagates and never activates fail-safe, while an `OperationCanceledException` from an unrelated or downstream token is treated as a failure that activates fail-safe.

## Installation

```bash
dotnet add package Headless.Caching.Core
```

## Quick Start

Consumers normally do not use this package directly. Provider packages reference it to implement `GetOrAddAsync`.

## Configuration

None.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Extensions`

## Side Effects

None. Providers own coordinator construction.

# Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

## Problem Solved

Provides high-performance in-memory caching using the unified `ICache` abstraction, suitable for single-instance deployments or as an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation
- Can serve as default `ICache` or alongside distributed cache
- Supports strongly-typed `ICache<T>` pattern
- Automatic memory management with configurable limits (MaxItems + LRU eviction)
- Can act as `IRemoteCache` adapter for single-instance scenarios
- Optional value cloning for isolation

## Design Notes

Memory cache stores entries in an internal envelope with logical expiration and physical expiration. Direct writes set both timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`.

Long `FailSafeMaxDuration` values can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments.

## Installation

```bash
dotnet add package Headless.Caching.InMemory
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// As default cache
builder.Services.AddInMemoryCache();

// Or with options
builder.Services.AddInMemoryCache(options =>
{
    options.MaxItems = 10000;
    options.CloneValues = true;
});

// As non-default (use alongside distributed cache)
builder.Services.AddInMemoryCache(isDefault: false);
```

## Configuration

### Options

```csharp
options.MaxItems = 10000;       // Maximum cached items (LRU eviction when exceeded)
options.CloneValues = false;    // Clone values on get/set for isolation
options.KeyPrefix = "myapp:";   // Optional key prefix
```

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`

## Side Effects

- Registers `IInMemoryCache` as singleton
- Registers `ICache` as singleton (if isDefault: true)
- Registers `IRemoteCache` adapter (if isDefault: true)
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons

---

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

Scalar write operations (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `UpsertAllAsync`) store entries as a versioned binary envelope: a 19-byte header followed by the raw value segment produced by the cache value codec. The header starts with magic/version bytes `0xFF 0x01`, then flags, then logical and physical expiration timestamps encoded as little-endian Unix milliseconds. Physical expiration is mapped to the Redis key TTL; when fail-safe is enabled, Redis retains the key until physical expiration even after logical expiration has passed. Logical expiration rides in the payload so normal value reads can miss while `GetOrAddAsync` still has a fail-safe reserve. Atomic counters (`Increment`, `SetIfHigher`, `SetIfLower`) bypass framing and write raw Redis-native numeric strings (see below).

The envelope byte layout is:

| Offset | Field | Description |
| --- | --- | --- |
| 0 | Magic | `0xFF` — marks a framed entry |
| 1 | Version | `0x01` — current envelope version |
| 2 | Flags | bit0 = `isNull`, bit1 = `hasLogicalExpiresAt`, bit2 = `hasPhysicalExpiresAt` |
| 3–10 | LogicalExpiresAt | `Int64` little-endian Unix milliseconds (present only when bit1 is set) |
| 11–18 | PhysicalExpiresAt | `Int64` little-endian Unix milliseconds (present only when bit2 is set) |
| 19+ | ValueSegment | raw codec bytes; empty when `isNull` is set |

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

| Operation Type                                                    | Cancellation Behavior                                      |
| ----------------------------------------------------------------- | ---------------------------------------------------------- |
| Single-key ops (`GetAsync`, `UpsertAsync`, etc.)                  | Checked before Redis call; operation completes atomically  |
| Batch ops (`UpsertAllAsync`, `RemoveAllAsync`)                    | Checked before batch starts; all keys processed atomically |
| SCAN-based ops (`RemoveByPrefixAsync`, `GetAllKeysByPrefixAsync`) | Cancellable during iteration (unbounded key sets)          |

This design ensures consumers never observe partial results from batch operations.

> **Note:** StackExchange.Redis doesn't support `CancellationToken` in its API. Timeouts are configured via `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
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

---

# Headless.Caching.Hybrid

Two-tier hybrid cache combining fast in-memory L1 cache with distributed L2 cache, featuring automatic cross-instance cache invalidation via messaging.

## Installation

```bash
dotnet add package Headless.Caching.Hybrid
```

## Prerequisites

- In-memory cache: `Headless.Caching.InMemory`
- Distributed cache: `Headless.Caching.Redis`
- Messaging: Any messaging provider (e.g., `Headless.Messaging.Redis`)

## Usage

### Basic Setup

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddInMemoryCache(isDefault: false);
services.AddSingleton<IConnectionMultiplexer>(redis);
services.AddRedisCache(options => options.ConnectionMultiplexer = redis);
services.AddHeadlessMessaging(builder => builder.UseRedis("localhost:6379"));
services.AddHybridCache(options => options.DefaultLocalExpiration = TimeSpan.FromMinutes(5));
```

### Using the Cache

```csharp
public sealed record Product(string Id, string Name);

public interface IProductRepository
{
    ValueTask<Product?> GetByIdAsync(string id, CancellationToken cancellationToken);
}

public sealed class ProductService(ICache cache, IProductRepository repository)
{
    public async Task<Product?> GetProductAsync(string id, CancellationToken ct)
    {
        var cached = await cache.GetOrAddAsync(
            $"product:{id}",
            token => repository.GetByIdAsync(id, token),
            TimeSpan.FromHours(1),
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         HybridCache                              │
│  ┌─────────────┐    ┌─────────────┐    ┌──────────────────────┐ │
│  │ L1 Cache    │    │ L2 Cache    │    │ Message Bus          │ │
│  │ (InMemory)  │    │ (Redis)     │    │ (Pub/Sub)            │ │
│  │             │    │             │    │                      │ │
│  │ - Fast      │    │ - Shared    │    │ - Invalidation       │ │
│  │ - Per-inst. │    │ - Durable   │    │ - Cross-instance     │ │
│  └─────────────┘    └─────────────┘    └──────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Read Path

1. Check L1 (local in-memory) - fastest, per-instance
2. On L1 miss, check L2 (distributed) - slower but shared
3. On L2 miss, execute factory, populate both caches

### Write/Invalidation Path

1. Update L2 (distributed cache)
2. Update L1 (local cache)
3. Publish invalidation message
4. Other instances receive message and invalidate their L1

## Configuration

| Option                   | Default        | Description                                      |
| ------------------------ | -------------- | ------------------------------------------------ |
| `KeyPrefix`              | `""`           | Prefix for all cache keys                        |
| `DefaultLocalExpiration` | `5 minutes`    | Default L1 TTL (uses L2 TTL if null)             |
| `InstanceId`             | Auto-generated | Unique ID for filtering self-originated messages |

## Exception Handling

| Scenario                     | Behavior                                           |
| ---------------------------- | -------------------------------------------------- |
| L2 write fails               | Log warning, continue to populate L1               |
| Publish fails                | Log warning, other instances serve stale until TTL |
| L1 write fails               | Propagate exception (indicates serious issue)      |
| L2 read fails                | Treat as miss for `GetOrAddAsync`; serve any L1 fail-safe reserve if available |
| Caller-token cancellation    | Propagate; fail-safe is not activated              |
| Unrelated/downstream `OperationCanceledException` | Treated as a failure (by token identity); activates fail-safe and serves stale if available |

## Metrics

The `HybridCache` exposes metrics:

```csharp
var cache = services.GetRequiredService<HybridCache>();
Console.WriteLine($"L1 hits: {cache.LocalCacheHits}");
Console.WriteLine($"Invalidation calls: {cache.InvalidateCacheCalls}");
```
