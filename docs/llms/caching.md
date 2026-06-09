---
domain: Caching
packages: Caching.Abstractions, Caching.Core, Caching.Hybrid, Caching.InMemory, Caching.Redis
---

# Caching

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Provider](#choosing-a-provider)
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
- [Headless.Caching.Hybrid](#headlesscachinghybrid)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Caching.InMemory](#headlesscachinginmemory)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Caching.Redis](#headlesscachingredis)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Unified cache abstraction with in-memory, Redis, and hybrid L1+L2 implementations.

## Quick Orientation

Install `Headless.Caching.Abstractions` plus one provider. Code against `ICache` for application cache operations.

- Single-instance or development: `Headless.Caching.InMemory` with `AddInMemoryCache()`.
- Multi-instance shared cache: `Headless.Caching.Redis` with `AddRedisCache(...)`.
- Local hot path plus shared L2: `Headless.Caching.Hybrid` with non-default in-memory cache, Redis, messaging, then `AddHybridCache()`.

`ICache` supports scalar reads/writes, bulk operations, prefix operations, atomic compare/replace and numeric operations, and set operations. `GetOrAddAsync` is the factory-backed path and is the only path that uses `CacheEntryOptions` fail-safe and factory timeout behavior.

## Agent Instructions

- Use `ICache` from `Headless.Caching.Abstractions`, not `Microsoft.Extensions.Caching.Distributed.IDistributedCache`. Use `IRemoteCache` only when a remote/L2 implementation is required.
- In Headless caching docs, `Memory` means `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.
- Use `Headless.Caching.InMemory` for development and single-instance deployments. Use `Headless.Caching.Redis` for production multi-instance deployments. Use `Headless.Caching.Hybrid` when the app needs process-local read speed with remote cache sharing.
- For hybrid caching, register memory cache as non-default (`AddInMemoryCache(isDefault: false)`), then register Redis cache, then call `AddHybridCache()`. The hybrid cache becomes the default `ICache`.
- Always check `CacheValue<T>.HasValue` before accessing `.Value`; cache misses return `HasValue = false`.
- `GetOrAddAsync` takes `CacheEntryOptions`. Passing a `TimeSpan` still works through implicit conversion when only duration is needed.
- Enable fail-safe per factory-backed entry with `CacheEntryOptions.IsFailSafeEnabled = true`. When the factory throws and a logically expired value is still physically retained, `GetOrAddAsync` serves that value and returns `CacheValue<T>.IsStale = true`.
- Fail-safe retention is bounded by `max(Duration, FailSafeMaxDuration)` from entry creation. `FailSafeThrottleDuration` restamps logical expiration to avoid hammering a failing factory, but never extends physical retention.
- Normal value reads (`GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`) use logical expiration. A fail-safe reserve is only consumed by `GetOrAddAsync`.
- Keep direct write operations (`UpsertAsync`, `TryInsertAsync`, set/increment operations) on `TimeSpan?`; they do not establish a fail-safe reserve because they do not carry `CacheEntryOptions`.
- Use `FactorySoftTimeout` only with fail-safe and a stale reserve. When it fires, the caller gets stale data and the factory continues in the background under a detached internal token.
- Do not capture request-scoped disposables in a soft-timeout factory. The background refresh can outlive the request token; create a fresh scope inside the factory when scoped services are needed.
- Use `FactoryHardTimeout` to bound cold-cache factory waits. When it fires with no stale fallback, `GetOrAddAsync` throws `CacheFactoryTimeoutException`; when stale data exists, it serves stale.
- `BackgroundFactoryCeiling` defaults to `Timeout.InfiniteTimeSpan` (no ceiling); a detached background refresh runs to completion. Set a finite, positive value to bound how long it can hold the per-key lock.
- `LockTimeout` defaults to `Timeout.InfiniteTimeSpan`: a caller with no stale reserve waits until the in-flight factory releases the per-key lock. Set a finite, positive value so such a caller degrades to a miss (`CacheValue<T>.NoValue`) instead of blocking. When a stale reserve exists and `FactorySoftTimeout` is finite, the soft timeout governs the wait instead and the caller is served stale.
- Caller cancellation never serves stale: if the `CancellationToken` passed to `GetOrAddAsync` is cancelled, the exception propagates and fail-safe/background completion does not activate from that cancellation.
- StackExchange.Redis does not support `CancellationToken` on its operations. Configure Redis operation timeouts via `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`; factory timeouts are separate coordinator behavior.
- Redis scalar entries use a versioned binary envelope. Do not parse Redis string bytes as the application payload directly; strip the envelope first unless the key is a raw counter.
- Redis key TTL follows physical expiration, not logical expiration, when fail-safe is enabled.
- Use `options.KeyPrefix` to namespace cache keys per application or module.
- InMemory cache supports `CloneValues = true` for value isolation between callers.
- Hybrid cache `DefaultLocalExpiration` controls L1 TTL independently of L2. Set it shorter than L2 for freshness.

## Core Concepts

`CacheValue<T>` distinguishes misses from cached null values with `HasValue`. `IsStale` is set only when the current `GetOrAddAsync` call activates fail-safe or returns stale because a timeout fired.

Entries can carry two expiration timestamps:

- Logical expiration controls ordinary reads and cache freshness.
- Physical expiration controls how long a fail-safe reserve remains available to `GetOrAddAsync`.

Factory timeout selection is a single decision:

| Condition | Effective timeout | Timeout result |
| --- | --- | --- |
| Fail-safe enabled, stale reserve exists, finite `FactorySoftTimeout` | Soft | Return stale and continue the factory in the background. |
| No soft fallback, finite `FactoryHardTimeout` | Hard | Cancel or abandon the factory; serve stale if possible, otherwise throw `CacheFactoryTimeoutException`. |
| Neither applies | None | Preserve existing unbounded factory behavior except for caller cancellation. |

Soft timeout also bounds acquisition of the per-key lock when fail-safe and a stale reserve exist. A concurrent waiter that cannot acquire the lock within `FactorySoftTimeout` returns stale rather than blocking behind an in-flight or background-completing factory. When no stale reserve exists, `LockTimeout` bounds that wait instead: it defaults to `Timeout.InfiniteTimeSpan` (wait until the in-flight factory releases the lock, matching FusionCache's default), and a finite value makes the waiter degrade to a miss (`CacheValue<T>.NoValue`) on elapse rather than blocking. Same-key re-entrant factory calls are only supported under the fail-safe plus stale plus finite-soft combination; otherwise they can still deadlock and are unsupported.

Background completion is per-key, not global. The keyed lock prevents duplicate cooperative factories for the same key while the background refresh runs, but it does not limit refreshes across distinct keys. If the background ceiling abandons a token-ignoring factory, that factory may continue running untracked; the coordinator gates late success writes from the timeout path so it cannot overwrite a newer timeout-path value after abandonment. Direct explicit writes (`Set`, `Upsert`, `Remove`) are not version-checked against a slow background write; a slow successful background refresh can still overwrite an explicit write. CAS/versioned write protection is deferred.

FusionCache alignment is intentional but not exact. Headless uses FusionCache-like soft and hard timeout selection and waiter lock timeout behavior, but Headless detaches the background refresh token from the caller token. Headless hard timeout abandons the factory path instead of allowing background completion after the hard limit.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Caching.InMemory` | Single process, tests, local development, or L1 for Hybrid. | Multiple app instances must share cache state. | Fastest path, but data is per process and retained in memory. |
| `Headless.Caching.Redis` | Multiple app instances need a shared cache and Redis is already operational. | The app cannot tolerate Redis operational dependency. | Distributed and script-backed atomic operations, with network latency and Redis timeout tuning. |
| `Headless.Caching.Hybrid` | Reads are hot enough to benefit from L1 while L2 keeps instances coherent. | Invalidation messaging is not configured or L1 staleness is unacceptable. | Fast local reads plus remote sharing, with extra moving parts and invalidation timing. |

## Headless.Caching.Abstractions

Defines the unified caching interface for in-memory, distributed, and hybrid cache implementations.

### Problem Solved

Provides a provider-agnostic caching API so applications can switch between memory, Redis, and hybrid caches without changing call sites.

### Key Features

- `ICache` - core interface for cache operations:
    - Upsert/Get/Remove with expiration
    - Bulk operations (UpsertAll, GetAll, RemoveAll)
    - Prefix-based operations (GetByPrefix, RemoveByPrefix)
    - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower)
    - Set operations (SetAdd, SetRemove, GetSet)
- `IInMemoryCache` - marker interface for in-memory implementations.
- `IRemoteCache` - marker interface for remote implementations.
- `ICache<T>` - strongly typed cache wrapper.
- `CacheValue<T>` - cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - factory-backed entry options: `Duration`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration`, `FactorySoftTimeout`, `FactoryHardTimeout`, `BackgroundFactoryCeiling`, and `LockTimeout`.
- `CacheFactoryTimeoutException` - `TimeoutException` subtype thrown when a hard factory timeout fires without a stale fallback.

### Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeouts, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

Fail-safe is opt-in and only applies to `GetOrAddAsync`. Direct writes keep the `TimeSpan?` API and write logical expiration equal to physical expiration. A stale value served by fail-safe returns `CacheValue<T>.IsStale = true` only for the activating call; reads during the throttle window are logical hits and return `IsStale = false`.

Factory soft timeouts are useful only when fail-safe is enabled and a stale reserve exists. In that case the caller gets stale data and the factory continues in the background. Soft timeouts configured without fail-safe are inert and logged once per key. Factory hard timeouts cancel or abandon the factory; they serve stale when possible and throw `CacheFactoryTimeoutException` on a cold cache.

Background completion uses a detached coordinator-owned cancellation token, not the caller token. A request token may be cancelled after the stale response is returned and the background refresh can still finish. Factories used with soft timeouts must not capture request-scoped disposables; create a fresh dependency scope inside the factory when scoped services are required after the request path returns.

`BackgroundFactoryCeiling` defaults to `Timeout.InfiniteTimeSpan` (no ceiling): a detached background factory runs to completion, matching the behavior of comparable caches (FusionCache, Caffeine, sturdyc). Set a finite, positive value to bound how long a detached factory may hold the per-key lock. When the ceiling fires, the coordinator cancels the internal token and releases the lock: cooperative factories stop, while non-cooperative factories may continue running untracked, but the coordinator gates late success writes so an abandoned factory cannot clobber a newer cache value through the timeout path.

### Installation

```bash
dotnet add package Headless.Caching.Abstractions
```

### Quick Start

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
                    FactorySoftTimeout = TimeSpan.FromMilliseconds(200),
                    FactoryHardTimeout = TimeSpan.FromSeconds(2),
                    BackgroundFactoryCeiling = TimeSpan.FromMinutes(2),
                },
                ct
            )
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }
}
```

### Configuration

No configuration required. This is an abstractions-only package.

### Dependencies

None.

### Side Effects

None. This is an abstractions package.

---

## Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

### Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, timeout, background completion, and throttle behavior.

### Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads and writes.
- `CacheStoreEntry<T>` - logical and physical expiration snapshot used by the coordinator.
- `CacheStoreEntryExtensions` - shared `IsFresh`/`IsPhysicallyPresent` predicates so every provider and the coordinator agree on the expiration boundary (an entry is expired at the exact tick, `expiresAt <= now`).
- `FactoryCacheCoordinator.IsCallerCancellation` - shared predicate provider composites use so caller cancellation propagates while an unrelated/downstream `OperationCanceledException` activates fail-safe consistently.
- Fail-safe, factory timeout, and background completion logs.

### Design Notes

Providers construct the coordinator directly with their `TimeProvider` and logger; the Core package has no DI setup. Store read failures are treated as misses, and fail-safe restamp writes are best-effort so a stale value can still be returned when the backing store is unhealthy. Cancellation is classified by token identity: the caller's own cancellation propagates and never activates fail-safe, while an `OperationCanceledException` from an unrelated or downstream token is treated as a failure that activates fail-safe.

Factory timeout selection is centralized in the coordinator. If fail-safe is enabled, a stale reserve exists, and `FactorySoftTimeout` is finite, the soft timeout governs. Otherwise a finite `FactoryHardTimeout` governs. Otherwise factory execution is unbounded except for caller cancellation. A finite soft timeout also bounds acquisition of the same per-key lock when stale data exists, so waiters and supported same-key re-entrant calls return stale instead of blocking behind an in-flight refresh. When no stale reserve exists, `LockTimeout` (default `Timeout.InfiniteTimeSpan`) bounds that acquisition instead, and a finite value makes the waiter degrade to a miss rather than block.

The coordinator deliberately diverges from FusionCache on background cancellation. A soft-timed-out factory uses a detached internal token and can outlive the caller request. Hard timeouts cancel or abandon the factory and never allow background completion. The per-key no-duplicate-factory guarantee holds cleanly for cooperative factories; after the background ceiling abandons a token-ignoring factory, another factory may run for that key while the abandoned task continues untracked, but late timeout-path writes are gated off.

### Installation

```bash
dotnet add package Headless.Caching.Core
```

### Quick Start

Consumers normally do not use this package directly. Provider packages reference it to implement `GetOrAddAsync`.

### Configuration

None.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Extensions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

None. Providers own coordinator construction.

---

## Headless.Caching.Hybrid

Two-tier cache combining in-memory L1 with remote L2 and cross-instance invalidation through messaging.

### Problem Solved

Provides one `ICache` implementation that reads from a fast local cache first, falls back to a shared remote cache, and invalidates other instances when writes change cached data.

### Key Features

- L1 + L2 read path: local in-memory first, remote cache second.
- Write path updates L2, updates L1, and publishes invalidation.
- Miss path executes the factory once through the shared `FactoryCacheCoordinator`.
- Supports strongly typed `ICache<T>`.
- Uses `DefaultLocalExpiration` to keep L1 fresher than L2.
- Shared `GetOrAddAsync` fail-safe, factory timeout, and background completion behavior through `Headless.Caching.Core`.

### Design Notes

Register an in-memory cache as non-default, then a remote cache, then the hybrid cache. The hybrid cache becomes the default `ICache` when `isDefault: true`.

Hybrid fail-safe and factory timeouts use the same coordinator semantics as the other providers. A stale reserve can come from L1 or L2. On soft timeout, the stale value is returned to the caller and the detached background factory writes through the composite store on success, so both tiers are refreshed. `DefaultLocalExpiration` still caps L1 physical retention independently of the L2 duration.

On reads, Hybrid promotes L2 entries into L1 only when they are logically fresh. Promoting stale reserves on every read would amplify L1 writes and could overwrite a newer L1 reserve. Fail-safe activation and background success still write through the composite store intentionally.

Publish failures are non-fatal. Other instances may keep their L1 value until TTL or the next successful invalidation, while the local instance still observes the write result.

### Installation

```bash
dotnet add package Headless.Caching.Hybrid
```

### Quick Start

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddInMemoryCache(isDefault: false);
services.AddSingleton<IConnectionMultiplexer>(redis);
services.AddRedisCache(options => options.ConnectionMultiplexer = redis);
services.AddHeadlessMessaging(builder => builder.UseRedis("localhost:6379"));
services.AddHybridCache(options =>
{
    options.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
});
```

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
            new CacheEntryOptions
            {
                Duration = TimeSpan.FromHours(1),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(6),
                FactorySoftTimeout = TimeSpan.FromMilliseconds(250),
                FactoryHardTimeout = TimeSpan.FromSeconds(3),
            },
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultLocalExpiration` | `5 minutes` | Default L1 TTL; when null, L1 uses the L2 expiration. |
| `InstanceId` | Auto-generated | Unique ID for filtering self-originated invalidation messages. |

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Bus.Abstractions`

### Side Effects

- Registers `HybridCache` as singleton.
- Registers `ICache` as singleton when `isDefault: true`.
- Registers keyed `ICache` under `CacheConstants.HybridCacheProvider`.
- Registers `ICache<T>` as singleton.
- Reads configured `HybridCacheOptions`.
- Publishes cache invalidation messages through the registered message bus.

---

## Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

### Problem Solved

Provides process-local caching through the unified `ICache` abstraction, suitable for development, single-instance deployments, or an L1 cache layer.

### Key Features

- Full `IInMemoryCache` implementation.
- Can serve as default `ICache` or alongside a distributed cache.
- Supports strongly typed `ICache<T>`.
- Automatic memory management with configurable limits (`MaxItems` plus LRU eviction).
- Can act as an `IRemoteCache` adapter for single-instance scenarios.
- Optional value cloning for isolation.
- Shared `GetOrAddAsync` fail-safe, factory timeout, and background completion behavior through `Headless.Caching.Core`.

### Design Notes

Memory cache stores entries in an internal envelope with logical expiration and physical expiration. Direct writes set both timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`.

Long `FailSafeMaxDuration` values can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments. Soft-timeout background refreshes also hold values in process while the detached factory runs; `BackgroundFactoryCeiling` bounds how long a cooperative refresh keeps the per-key lock.

`Memory` in Headless caching docs means this package, `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.

### Installation

```bash
dotnet add package Headless.Caching.InMemory
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInMemoryCache();

builder.Services.AddInMemoryCache(options =>
{
    options.MaxItems = 10000;
    options.CloneValues = true;
});

builder.Services.AddInMemoryCache(isDefault: false);
```

### Configuration

```csharp
options.MaxItems = 10000;
options.CloneValues = false;
options.KeyPrefix = "myapp:";
```

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`

### Side Effects

- Registers `IInMemoryCache` as singleton.
- Registers `ICache` as singleton when `isDefault: true`.
- Registers `IRemoteCache` adapter when `isDefault: true`.
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons.

---

## Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

### Problem Solved

Provides Redis-backed caching through the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

### Key Features

- Full `IRemoteCache` implementation using StackExchange.Redis.
- Supports strongly typed `IRemoteCache<T>`.
- Prefix-based key management.
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower).
- Set/list operations with pagination.
- Lua scripts for atomic multi-key operations.
- Redis Cluster support.
- Shared `GetOrAddAsync` fail-safe, factory timeout, and background completion behavior through `Headless.Caching.Core`.

### Design Notes

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

### Installation

```bash
dotnet add package Headless.Caching.Redis
```

### Quick Start

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

### Configuration

```csharp
options.ConnectionMultiplexer = multiplexer;
options.KeyPrefix = "myapp:";
```

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `Headless.Serializer.Json`
- `StackExchange.Redis`

### Side Effects

- Registers `IRemoteCache` as singleton.
- Registers `IRemoteCache<T>` as singleton.
- Registers a keyed `HeadlessRedisScriptsLoader` bound to `RedisCacheOptions.ConnectionMultiplexer`.
- Registers a hosted `IInitializer` that warms Redis cache Lua scripts on host start.
- Uses existing `IConnectionMultiplexer` if registered, otherwise creates one.
