---
domain: Distributed Locks
packages: DistributedLocks.Abstractions, DistributedLocks.Core, DistributedLocks.Cache, DistributedLocks.Redis
---

# Distributed Locks

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Efficiency Locks](#efficiency-locks)
    - [Correctness Locks](#correctness-locks)
    - [Weak Fencing With LockId](#weak-fencing-with-lockid)
    - [Lease Lifecycle Monitoring](#lease-lifecycle-monitoring)
    - [Messaging Wake-ups](#messaging-wake-ups)
- [Reader-Writer Locks](#reader-writer-locks)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.DistributedLocks.Abstractions](#headlessdistributedlocksabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.DistributedLocks.Core](#headlessdistributedlockscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.DistributedLocks.Cache](#headlessdistributedlockscache)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.DistributedLocks.Redis](#headlessdistributedlocksredis)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)

> Provider-agnostic distributed locking with automatic renewal, expiration, explicit release, and pluggable storage backends.

## Quick Orientation

Use `IDistributedLockProvider` when only one worker should own a named resource at a time. `TryAcquireAsync(...)` returns `null` on timeout; `AcquireAsync(...)` throws `LockAcquisitionTimeoutException` on timeout. Rate limiting is out of scope for this domain — and the framework does not ship a rate-limiting package. Use `Microsoft.AspNetCore.RateLimiting` (in-process) or `Polly.RateLimiting` + a community Redis-backed `RateLimiter` (distributed) when admission control is needed.

Use `IDistributedReaderWriterLockProvider` when concurrent readers are safe and writers need exclusivity. Reader-writer locks are Redis-only because the generic cache contract cannot atomically coordinate the writer flag and readers set.

## Agent Instructions

- Code against `IDistributedLockProvider` from `Headless.DistributedLocks.Abstractions`; do not inject Redis or cache storage types into application services.
- Use `TryAcquireAsync(...)` when timeout is an expected branch; use `AcquireAsync(...)` when timeout should fail the workflow.
- Per-call configuration is bundled into `DistributedLockAcquireOptions` (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`). Omit the argument to use defaults; use `with` expressions to derive variants.
- Always `await using` the returned lock when `ReleaseOnDispose` is `true` (the default); set `ReleaseOnDispose = false` only when ownership is deliberately transferred and the caller will release explicitly.
- Set `Monitoring = LockMonitoringMode.Monitor` when work should observe lease loss through `IDistributedLock.HandleLostToken`; set `Monitoring = LockMonitoringMode.AutoExtend` only when long work should renew its own lease in the background (it implies `Monitor`).
- Use `GetLockIdAsync(resource)` for operational inspection only; it reads the current lock id and does not renew the lease. If you already hold a monitored handle, observe `HandleLostToken` instead of polling `GetLockIdAsync`.
- Synchronous `TryUsingAsync(..., Action ...)` overloads force `LockMonitoringMode.None` because synchronous delegates cannot observe a lease-lost cancellation token.
- Do not use distributed locks as rate limiters. Use `Microsoft.AspNetCore.RateLimiting` (in-process) or `Polly.RateLimiting` + community Redis (distributed) — the framework does not ship a rate-limiting package.
- Before choosing a backend, classify the use case as efficiency or correctness. Redis and cache locks are efficiency locks, not transaction-coupled correctness locks.
- Default lock expiration is 20 minutes and default acquire timeout is 30 seconds. Override them per call via `DistributedLockAcquireOptions`; `DistributedLockOptions` configures key prefix and waiter/resource limits.
- If `Headless.Messaging` is registered, lock release wake-ups are push-based. If no `IOutboxPublisher` is registered, the provider still works and falls back to polling backoff with a one-time warning.
- `Headless.Messaging.Core` uses a keyed `IDistributedLockProvider` registration under `"headless.messaging"`; an un-keyed app lock provider is not automatically used by message retry processors.
- Use `IDistributedReaderWriterLockProvider` for reader-writer semantics and register it explicitly with `AddRedisDistributedReaderWriterLock(...)`; regular Redis lock setup does not auto-register it.
- Do not build reader-writer locks on `Headless.DistributedLocks.Cache`; `ICache` exposes single-key mutex-shaped operations only.

## Core Concepts

Distributed locks coordinate ownership of a string resource such as `order:123`. The lock store owns acquisition and release; the protected resource still owns data integrity. Treat lock handles as leases that can expire.

### Efficiency Locks

Efficiency locks avoid duplicate work, such as two nodes generating the same report. Occasional violations cost compute or duplicate side effects, not corrupted state. Redis and cache-backed locks fit this category.

### Correctness Locks

Correctness locks protect invariants where a stale owner could corrupt data. TTL-based Redis or cache locks cannot prove correctness through process pauses, partitions, or clock skew. For correctness, use a transaction-coupled backend when one exists, or make the protected resource reject stale writes.

### Weak Fencing With LockId

`IDistributedLock.LockId` is a Snowflake-style `long` and can be passed to a protected write path as a weak fencing token. The target resource must store the last accepted fence and reject older values. This is consumer-enforced and does not turn Redis locks into transaction-coupled locks.

### Lease Lifecycle Monitoring

Lock monitoring is opt-in per acquire call via `DistributedLockAcquireOptions.Monitoring` (a `LockMonitoringMode` enum). `Monitoring = LockMonitoringMode.Monitor` starts a background lease monitor and makes `IDistributedLock.HandleLostToken` cancel when validation detects the stored lock id changed, disappeared, or the lease lifetime exceeds the requested TTL after repeated unknown validation results. With `LockMonitoringMode.None` (default), `HandleLostToken` is `CancellationToken.None` and `IDistributedLock.IsMonitored` is `false`.

If the monitor loop faults, `HandleLostToken` is also cancelled as a fail-safe so a silently dead monitor cannot keep appearing healthy.

Intermediate monitor states (`Held`, `Renewed`, `Lost`, `Unknown`) are not exposed as a public API; they are visible through the `LeaseMonitorStateChanged` log event (`EventId = 30`, name `LeaseMonitorStateChanged`) for programmatic log filtering. The structured fields are `Resource`, `LockId`, `PreviousState`, and `NextState`. `GetActiveMonitorCount` on the provider is `internal` and intended for test/diagnostic use only.

Combining `LockMonitoringMode.Monitor` or `LockMonitoringMode.AutoExtend` with `Timeout.InfiniteTimeSpan` for `TimeUntilExpires` throws `ArgumentException` (`ParamName = "timeUntilExpires"`): lease monitoring requires a finite lease window.

`LockMonitoringMode.AutoExtend` implies monitoring and renews at `DistributedLockOptions.AutoExtensionCadenceFraction` of the TTL. `LockMonitoringMode.Monitor` (validate only) validates at `PollingCadenceFraction` and never renews the lease. These signals narrow stale-work windows; they do not upgrade Redis or cache locks into correctness locks. Fence protected writes with `LockId` when stale owners can corrupt state.

### Messaging Wake-ups

`DistributedLockProvider` can publish `DistributedLockReleased` through `IOutboxPublisher` so waiters wake quickly. The same message also nudges active lease monitors for that resource so loss validation can happen before the next polling cadence. Messaging is optional: when no publisher is registered, lock acquisition and lease monitoring fall back to polling. This keeps distributed locks usable without forcing `Headless.Messaging`.

## Reader-Writer Locks

Use `IDistributedReaderWriterLockProvider` for read-heavy resources where multiple readers can proceed concurrently and writers must run exclusively. Read and write acquires return the same `IDistributedLock` handle shape as mutex locks, so `ReleaseAsync()`, `RenewAsync(...)`, `HandleLostToken`, and `LockMonitoringMode.AutoExtend` work the same way.

Redis reader-writer locks use two keys per resource: `{resource}:writer` for the active writer or writer-waiting marker, and `{resource}:readers` for active reader lock ids. The braces are Redis cluster hash-tags so both keys live on the same slot. Resource names containing `{` or `}` are rejected because storage owns that hash-tag shape.

The reader set is a Redis HASH whose fields are reader lockIds and whose values are per-reader expiry epochs in milliseconds (computed inside Lua via `redis.call('TIME')` so the server clock is authoritative). The hash key itself carries a generous safety-net TTL (2× the lease duration); the per-entry expiry is the source of truth for liveness. Each writer-acquire script run prunes expired reader entries before checking "no live readers" so a crashed reader never strands a queued writer past its own lease.

Writer-preference is intentional. When a writer queues behind active readers, Redis stores a writer-waiting marker. New readers are blocked while that marker exists, preventing steady read traffic from starving the writer. The marker is keyed by the writer's lockId but the plant/refresh branch fires for any caller observing a `:_WRITERWAITING`-suffixed value, so multiple contending writers collectively keep the marker continuously present even if individual writers cancel. The marker TTL is governed by `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30s) rather than the lease TTL, so an abandoned writer cannot block readers for the full lease window. If the writer times out or is cancelled before acquiring, the provider clears its waiting marker via the release path.

Readers running `Monitoring = LockMonitoringMode.AutoExtend` may see `HandleLostToken` fire when a writer queues — the extend-read script refuses to refresh while a writer-waiting marker is present, which the provider classifies as `Lost`. This is the contract that enforces the writer-preference guarantee at the per-reader level: a reader that wants to keep its lease through a writer queue must reacquire from scratch after the writer drains.

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect("localhost:6379")
);

builder.Services.AddRedisDistributedReaderWriterLock(options =>
{
    options.KeyPrefix = "distributed-lock:";
});

await using var read = await readerWriterLocks.AcquireReadLockAsync(
    "catalog:prices",
    new DistributedLockAcquireOptions { Monitoring = LockMonitoringMode.Monitor },
    ct
);

await using var write = await readerWriterLocks.AcquireWriteLockAsync(
    "catalog:prices",
    new DistributedLockAcquireOptions
    {
        TimeUntilExpires = TimeSpan.FromMinutes(2),
        Monitoring = LockMonitoringMode.AutoExtend,
    },
    ct
);
```

The cache provider intentionally does not implement reader-writer locks. Read acquire needs one atomic operation that checks the writer key and mutates the readers set; `ICache` only exposes single-key mutex-shaped operations.

## Choosing a Provider

Choose based on the storage you already operate and the safety category.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.DistributedLocks.Cache` | You already use `ICache` and the cache is distributed for multi-instance apps. | The app cache is in-memory, or the cache provider serves stale local reads such as `HybridCache` and you need lease monitoring or auto-extension. | Reuses cache infrastructure but inherits that cache provider's consistency and availability behavior. |
| `Headless.DistributedLocks.Redis` | You want direct Redis-backed efficiency locks with atomic acquire/release scripts, or Redis-backed reader-writer locks. | You need correctness locks for protected state mutations. | Requires `IConnectionMultiplexer` and Redis script loading, but avoids routing lock operations through a generic cache abstraction. |

---

## Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

### Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

### Key Features

- `IDistributedLockProvider` with `TryAcquireAsync(...)` and `AcquireAsync(...)`.
- `IDistributedReaderWriterLockProvider` with `AcquireReadLockAsync(...)`, `TryAcquireReadLockAsync(...)`, `AcquireWriteLockAsync(...)`, and `TryAcquireWriteLockAsync(...)`.
- `IDistributedLock` handle with `LockId`, `HandleLostToken`, `IsMonitored`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `TryUsingAsync(resource, work, ...)` convenience that acquires, executes work, and releases — prefer this over manual try/finally for simple guarded execution.
- `LockAcquisitionTimeoutException`, `LockHandleLostException`, and `DistributedLockException` for lock-specific failures.
- `GetLockIdAsync(resource)`, `GetLockInfoAsync(resource)`, `ListActiveLocksAsync()`, `GetActiveLocksCountAsync()`, `GetExpirationAsync(resource)` for operational inspection and monitoring. `GetLockIdAsync` does not renew a lease; monitored holders should use `HandleLostToken` for lease-loss observation.

### Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- Per-call configuration (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`) is bundled into `DistributedLockAcquireOptions`. Omit the argument to use defaults; use `with` expressions to derive variants.
- `ReleaseOnDispose = false` prevents dispose-time release but does not disable explicit `ReleaseAsync(...)`.
- `HandleLostToken` is an observability signal. Consumer code decides whether to stop, compensate, or throw `LockHandleLostException`.
- `TimeUntilExpires = null` uses the provider default. Built-in providers use a finite 20-minute default, so `null` is valid with `LockMonitoringMode.AutoExtend`; `Timeout.InfiniteTimeSpan` is not.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Abstractions
```

### Quick Start

```csharp
public sealed class OrderWorker(IDistributedLockProvider lockProvider)
{
    public async Task ProcessAsync(Guid orderId, CancellationToken ct)
    {
        await using var lease = await lockProvider.AcquireAsync(
            $"order:{orderId}",
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromMinutes(5),
                AcquireTimeout = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            ct
        );

        using var lostRegistration = lease.HandleLostToken.Register(() => { /* stop work */ });
        // process the order while the lease is held
    }
}
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.Extensions`

### Side Effects

None.

---

## Headless.DistributedLocks.Core

Provides the `DistributedLockProvider` implementation and setup extensions.

### Problem Solved

Implements lock acquisition, renewal, release, inspection, timeout handling, and optional messaging wake-ups over an `IDistributedLockStorage`.

### Key Features

- `DistributedLockProvider` implements `IDistributedLockProvider`.
- `DistributedReaderWriterLockProvider` implements `IDistributedReaderWriterLockProvider`.
- `DisposableDistributedLock` releases on dispose by default.
- `IDistributedReaderWriterLockStorage` defines atomic read/write acquire, extend, release, and validation operations for storage providers.
- `DistributedLockOptions` configures key prefix, resource name length, waiter limits, and lease-monitor cadence fractions.
- `AddDistributedLock(...)` overloads wire storage, options, time provider, ID generator, and optional release consumers.
- `AddDistributedReaderWriterLock(...)` overloads wire reader-writer storage, options, time provider, and ID generator.

### Design Notes

- `IOutboxPublisher` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
- `TryAcquireAsync(..., new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero })` performs a single storage attempt with an internal safety deadline.
- Lease monitors drain before dispose-time release, so monitoring does not add release retry latency during shutdown.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Core
```

### Quick Start

```csharp
builder.Services.AddDistributedLock(
    sp => sp.GetRequiredService<IDistributedLockStorage>(),
    options =>
    {
        options.KeyPrefix = "distributed-lock:";
        options.MaxResourceNameLength = 512;
    }
);
```

### Configuration

```csharp
options.KeyPrefix = "distributed-lock:";
options.MaxResourceNameLength = 512;
options.MaxConcurrentWaitingResources = 10_000;
options.MaxWaitersPerResource = 1_000;
options.PollingCadenceFraction = 0.5;
options.AutoExtensionCadenceFraction = 1.0 / 3.0;
```

Use `DistributedLockAcquireOptions` to override per-call expiration, acquire timeout, monitoring, and dispose behavior:

```csharp
await lockProvider.AcquireAsync(
    "orders:123",
    new DistributedLockAcquireOptions
    {
        TimeUntilExpires = TimeSpan.FromMinutes(5),
        AcquireTimeout = TimeSpan.FromSeconds(10),
        Monitoring = LockMonitoringMode.Monitor,
    },
    ct
);
```

Use `AutoExtend` when the protected work can exceed the initial TTL and should keep the lease alive while the process is healthy:

```csharp
await using var lease = await lockProvider.AcquireAsync(
    "orders:123",
    new DistributedLockAcquireOptions
    {
        TimeUntilExpires = TimeSpan.FromMinutes(5),
        Monitoring = LockMonitoringMode.AutoExtend,
    },
    ct
);
```

### Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Core`

### Side Effects

- Registers `IDistributedLockProvider` as singleton.
- Registers `IDistributedReaderWriterLockProvider` as singleton when `AddDistributedReaderWriterLock(...)` is called.
- Registers `TimeProvider.System` and `ILongIdGenerator` when absent.
- Registers a `DistributedLockReleased` consumer only when an `IOutboxPublisher` registration exists.

---

## Headless.DistributedLocks.Cache

Cache-backed storage for distributed locks.

### Problem Solved

Stores lock records through `ICache` so applications can reuse an existing cache provider.

### Key Features

- `CacheDistributedLockStorage` implements `IDistributedLockStorage`.
- Uses cache TTL for lock expiration.
- Works with memory, Redis, or custom cache providers through `ICache`.
- Do not use `HybridCache` for monitored or auto-extending leases; local L1 reads can outlive the distributed lock TTL and hide lease loss.
- Does not implement reader-writer locks; the cache contract cannot atomically coordinate the writer flag and readers set.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Cache
```

### Quick Start

```csharp
builder.Services.AddInMemoryCache();

builder.Services.AddDistributedLock<CacheDistributedLockStorage>(options =>
{
    options.KeyPrefix = "distributed-lock:";
});
```

### Configuration

No storage-specific configuration. Configure the selected `ICache` provider and `DistributedLockOptions`.

Avoid `HybridCache` for `LockMonitoringMode.Monitor` and `LockMonitoringMode.AutoExtend`. Lease validation reads the current lock id through `ICache`; `HybridCache` may satisfy that read from its local L1 cache even after the distributed TTL has expired. Use `Headless.DistributedLocks.Redis` for monitored Redis-backed locks, or use a cache provider whose reads are distributed and TTL-accurate.

Reader-writer locks are Redis-only. `ICache` exposes single-key operations and cannot atomically check a writer key while mutating a reader set.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Core`

### Side Effects

None. The package only provides storage; registration is done through `Headless.DistributedLocks.Core`.

---

## Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks.

### Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, and release operations.

### Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `RedisDistributedReaderWriterLockStorage` implements `IDistributedReaderWriterLockStorage`.
- `AddRedisDistributedLock(...)` registers a Redis-backed lock provider.
- `AddRedisDistributedReaderWriterLock(...)` registers a Redis-backed reader-writer lock provider.
- Uses `HeadlessRedisScriptsLoader` for atomic Lua script operations.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Redis
```

### Quick Start

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

### Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`.

Reader-writer storage creates `{resource}:writer` (string holding the active writer id or the `:_WRITERWAITING`-suffixed marker) and `{resource}:readers` (HASH of `lockId → expiry-epoch-ms`) Redis keys internally. Resource names containing `{` or `}` are rejected so the storage-owned Redis cluster hash-tag remains deterministic. The marker TTL is governed by `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30s, validated `0 < ttl <= 5 min`).

### Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Redis`
- `StackExchange.Redis`

### Side Effects

- Registers `HeadlessRedisScriptsLoader`.
- Registers `IDistributedLockProvider` through `Headless.DistributedLocks.Core`.
- Registers `IDistributedReaderWriterLockProvider` through `Headless.DistributedLocks.Core` when `AddRedisDistributedReaderWriterLock(...)` is called.
