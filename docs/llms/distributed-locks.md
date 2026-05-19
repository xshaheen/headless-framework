---
domain: Distributed Locks
packages: DistributedLocks.Abstractions, DistributedLocks.Core, DistributedLocks.Cache, DistributedLocks.Redis
---

# Distributed Locks

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Safety Categories and Fencing Tokens](#safety-categories-and-fencing-tokens)
    - [Efficiency locks](#efficiency-locks)
    - [Correctness locks](#correctness-locks)
    - [Why Redis-backed locks do not meet the correctness bar](#why-redis-backed-locks-do-not-meet-the-correctness-bar)
    - [Using LockId as a weak fencing token](#using-lockid-as-a-weak-fencing-token)
    - [Caveats](#caveats)
- [Headless.DistributedLocks.Abstractions](#headlessdistributedlocksabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Usage](#usage)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.DistributedLocks.Core](#headlessdistributedlockscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration-1)
        - [Options](#options)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.DistributedLocks.Cache](#headlessdistributedlockscache)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.DistributedLocks.Redis](#headlessdistributedlocksredis)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)

> Provider-agnostic distributed locking with automatic renewal, expiration, throttling, and pluggable storage backends (Redis, Cache).

## Quick Orientation

- Install `Headless.DistributedLocks.Abstractions` to depend on interfaces only (e.g., in domain/application layers).
- Install `Headless.DistributedLocks.Core` for the lock provider implementation. Register with `AddDistributedLock(options => ...)`.
- Choose one storage backend:
    - `Headless.DistributedLocks.Redis` — production multi-instance deployments (atomic Lua scripts, high performance).
    - `Headless.DistributedLocks.Cache` — uses `ICache` abstraction; works if your cache is already distributed (e.g., Redis cache).
- Use `IDistributedLockProvider.TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, ct)` to acquire locks. Returns `null` if acquisition fails.
- For rate-limited locking, use `IThrottlingDistributedLockProvider` (requires throttling storage from the chosen backend).

## Agent Instructions

- Always code against `IDistributedLockProvider` from Abstractions. Never reference storage-specific types in application code.
- Before choosing a backend, classify the use case as **efficiency** or **correctness** — see [Safety Categories and Fencing Tokens](#safety-categories-and-fencing-tokens). Picking the wrong backend silently corrupts data in the correctness case.
- Use `DistributedLocks.Redis` for production multi-instance deployments of **efficiency** locks. It uses atomic Lua scripts for acquire/release. For correctness locks, use a transaction-coupled backend instead.
- Use `DistributedLocks.Cache` when you already have a distributed `ICache` (e.g., Redis cache) and don't want a separate Redis connection for locks. Same safety category as `DistributedLocks.Redis` (efficiency-only).
- Always check for `null` after `TryAcquireAsync` — a null return means the lock could not be acquired within the timeout.
- Always `await using` the returned `IDistributedLock` to ensure proper release. Do not manually dispose without `await`.
- Default timeouts: `DefaultTimeUntilExpires = 20 min`, `DefaultAcquireTimeout = 30s`. Override per-call or globally via options.
- Lock resources are string keys (e.g., `"order:{orderId}"`). Use consistent naming conventions across your codebase.
- Both `IDistributedLockProvider` and `IThrottlingDistributedLockProvider` are registered as **singletons**.

## Safety Categories and Fencing Tokens

Distributed locks split into two safety categories per Martin Kleppmann's article ["How to do distributed locking"](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html). The category determines which backend is appropriate. Picking the wrong category corrupts data silently.

### Efficiency locks

- **Purpose:** avoid duplicate work — one node generates the daily report instead of three; one worker processes a queue item instead of two.
- **Cost of violation:** wasted CPU, redundant HTTP calls, duplicate side effects. No data corruption.
- **Required guarantee:** best-effort serialization. Occasional violations are tolerable.
- **Appropriate backends:** any. `Headless.DistributedLocks.Redis`, `Headless.DistributedLocks.Cache`, and (when shipped) `Headless.DistributedLocks.Postgres` / `.SqlServer` / `.AzureBlob` all meet the bar.

### Correctness locks

- **Purpose:** preserve invariants on shared state — no two writers may modify the same row simultaneously; no two leaders may issue conflicting decisions.
- **Cost of violation:** broken invariants, lost writes, corrupted state. Often irreversible.
- **Required guarantee:** serialization that survives GC pauses, process suspensions, network partitions, and clock skew during failover. Standard TTL-based locks cannot give this without help from the protected resource.
- **Appropriate backends:** only transaction-coupled locks, where the lock and the protected mutation succeed or fail atomically as one database operation. `PostgresDistributedLock.AcquireWithTransactionAsync` (planned, see GitHub issue #293) and the equivalent SQL Server static API (#294) provide this. Redis-backed locks DO NOT — see the next subsection.

### Why Redis-backed locks do not meet the correctness bar

The canonical failure mode (single-Redis, RedLock multi-instance, or any TTL-based scheme):

1. Client A acquires the lock with TTL = 30s.
2. Client A's process pauses — GC, kernel preemption, VM live-migration, or a network partition that keeps A's writes from reaching Redis.
3. The 30s TTL expires. Redis releases the lock.
4. Client B acquires the lock. B writes to the protected resource.
5. Client A's pause ends. A still believes it holds the lock. A writes to the protected resource.

**Two writers, one resource, no detection.** No amount of lock-side cleverness fixes this — by the time A's write reaches the resource, the lock service no longer matters. The only fix is the protected resource itself rejecting A's write because it carries a stale fencing token.

This is why `Headless.DistributedLocks.Redis` is correct for efficiency use cases but unsafe for correctness use cases. The RedLock multi-instance algorithm does not help — see [docs/solutions/tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md](../solutions/tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md) for the full rationale.

### Using LockId as a weak fencing token

`IDistributedLock.LockId` is a Snowflake-style `long` from `ILongIdGenerator`. It is monotonic per generator instance and *approximately* monotonic across instances (bounded by NTP clock skew). Consumers willing to plumb a fence parameter through their write path can use `LockId` today as a clock-skew-bounded fencing token:

```csharp
// Caller — protect a SQL row mutation with a fence.
await using var lockHandle = await _lockProvider.AcquireAsync("inventory:sku-123", ct)
    ?? throw new ConcurrencyException("Could not acquire lock");

var fence = lockHandle.LockId;

var affected = await _db.ExecuteAsync(
    """
    UPDATE inventory_state
    SET    quantity   = @quantity,
           last_fence = @fence
    WHERE  sku        = @sku
      AND  (last_fence IS NULL OR last_fence < @fence)
    """,
    new { sku = "sku-123", quantity = 42, fence });

if (affected == 0)
{
    // Either our fence is stale (we lost the lock during a pause) or someone
    // else already wrote with a newer fence. Abort — do not retry blindly.
    throw new StaleFenceException("Lock was likely lost during the operation");
}
```

The pattern depends on three consumer-side disciplines:

1. **Plumbing.** The fence parameter must reach the protected resource on every write. Forget it once, and that write is unfenced.
2. **Storing the last accepted fence.** The protected resource (database row, file metadata, etc.) must track the last accepted fence value.
3. **Rejecting stale fences.** Writes carrying a fence lower than the last accepted value must be rejected and the caller notified.

The framework cannot enforce any of these — that is what makes this a "weak" fencing pattern rather than a first-class API.

### Caveats

- **NTP-bounded monotonicity.** `LockId` ordering across machines depends on clock-skew bounds. Two acquires within ~clock-skew-bound milliseconds can produce out-of-order tokens. Tighter clock sync (PTP, dedicated NTP servers) tightens this bound; commodity NTP gives roughly tens of milliseconds.
- **No protection for external side effects.** Fencing protects state mutations on systems where you control the write path. For external side effects — sending an email, calling a payment API, kicking off a webhook — there is no resource to validate the fence against. Use idempotency keys instead.
- **Validation is the consumer's responsibility.** A fence handed to a write path that doesn't validate it is dead weight; worse, it can create false confidence. Audit the write path before relying on the pattern.
- **For unconditional correctness, use a transaction-coupled backend.** When the planned `PostgresDistributedLock.AcquireWithTransactionAsync` (issue #293) ships, prefer it over `LockId`-as-fence for correctness use cases. Atomicity by construction has no caveats.
- **No first-class `FenceToken` API yet.** A strictly monotonic per-resource fence counter (Redisson-style `INCR`-based) is tracked in issue #287 but explicitly deferred until a consumer surfaces a use case fencing actually fits. The framework's current position: most use cases are efficiency, and the few real correctness consumers should reach for Postgres transactional locks rather than Redis-with-fencing.

---

# Headless.DistributedLocks.Abstractions

Defines the unified interface for distributed resource locking.

## Problem Solved

Provides a provider-agnostic distributed locking API, enabling coordination across multiple instances with features like lock expiration, renewal, and throttling without changing application code.

## Key Features

- `IDistributedLockProvider` - Regular locking with expiration
- `IDistributedLock` - Acquired lock handle with release
- `IThrottlingDistributedLockProvider` - Rate-limited locking
- `IDistributedThrottlingLock` - Throttling lock handle
- Configurable timeouts and expiration

## Installation

```bash
dotnet add package Headless.DistributedLocks.Abstractions
```

## Usage

```csharp
public sealed class OrderService(IDistributedLockProvider lockProvider)
{
    public async Task ProcessOrderAsync(Guid orderId, CancellationToken ct)
    {
        var lockResource = $"order:{orderId}";

        await using var @lock = await lockProvider.TryAcquireAsync(
            lockResource,
            timeUntilExpires: TimeSpan.FromMinutes(5),
            acquireTimeout: TimeSpan.FromSeconds(30),
            ct
        );

        if (@lock is null)
            throw new ConcurrencyException("Could not acquire lock");

        // Process order safely...
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

## None.

# Headless.DistributedLocks.Core

Core implementation of distributed resource locking with storage abstraction.

## Problem Solved

Provides the lock provider implementation with automatic renewal, expiration handling, and support for pluggable storage backends (cache, Redis).

## Key Features

- `DistributedLockProvider` - Full implementation of `IDistributedLockProvider`
- `ThrottlingDistributedLockProvider` - Rate-limited lock provider
- `DisposableDistributedLock` - Auto-releasing lock handle
- Storage interfaces: `IDistributedLockStorage`, `IThrottlingDistributedLockStorage`
- Configurable options for timeouts and expiration

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedLock(options =>
{
    options.DefaultTimeUntilExpires = TimeSpan.FromMinutes(20);
    options.DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
});

// Add storage (cache or Redis)
builder.Services.AddDistributedLockCacheStorage();
// or
builder.Services.AddDistributedLockRedisStorage();
```

## Configuration

### Options

```csharp
services.AddDistributedLock(options =>
{
    options.DefaultTimeUntilExpires = TimeSpan.FromMinutes(20);
    options.DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
});
```

## Dependencies

- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IDistributedLockProvider` as singleton
- Registers `IThrottlingDistributedLockProvider` as singleton (if throttling storage is provided)

---

# Headless.DistributedLocks.Cache

Cache-based resource lock storage using ICache.

## Problem Solved

Provides resource lock storage using the headless's `ICache` abstraction, suitable for single-instance deployments or when using a distributed cache like Redis.

## Key Features

- `CacheDistributedLockStorage` - Lock storage via `ICache`
- `CacheThrottlingDistributedLockStorage` - Throttling lock storage
- Automatic expiration via cache TTL

## Installation

```bash
dotnet add package Headless.DistributedLocks.Cache
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add cache (e.g., Redis cache)
builder.Services.AddRedisCache(options => { /* ... */ });

// Add resource locks with cache storage
builder.Services.AddDistributedLock();
builder.Services.AddSingleton<IDistributedLockStorage, CacheDistributedLockStorage>();
```

## Configuration

No additional configuration required.

## Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Caching.Abstractions`

## Side Effects

- Registers `IDistributedLockStorage` as singleton
- Registers `IThrottlingDistributedLockStorage` as singleton (optional)

---

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
