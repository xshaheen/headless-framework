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

## Agent Instructions

- Code against `IDistributedLockProvider` from `Headless.DistributedLocks.Abstractions`; do not inject Redis or cache storage types into application services.
- Use `TryAcquireAsync(...)` when timeout is an expected branch; use `AcquireAsync(...)` when timeout should fail the workflow.
- Always `await using` the returned lock when `releaseOnDispose` is `true`; use `releaseOnDispose: false` only when ownership is deliberately transferred and the caller will release explicitly.
- Pass `monitoring: LockMonitoringMode.Monitor` when work should observe lease loss through `IDistributedLock.HandleLostToken`; pass `monitoring: LockMonitoringMode.AutoExtend` only when long work should renew its own lease in the background (it implies `Monitor`).
- Do not use distributed locks as rate limiters. Use `Microsoft.AspNetCore.RateLimiting` (in-process) or `Polly.RateLimiting` + community Redis (distributed) — the framework does not ship a rate-limiting package.
- Before choosing a backend, classify the use case as efficiency or correctness. Redis and cache locks are efficiency locks, not transaction-coupled correctness locks.
- Default lock expiration is 20 minutes and default acquire timeout is 30 seconds. Override them per `AcquireAsync(...)` or `TryAcquireAsync(...)` call; `DistributedLockOptions` configures key prefix and waiter/resource limits.
- If `Headless.Messaging` is registered, lock release wake-ups are push-based. If no `IOutboxPublisher` is registered, the provider still works and falls back to polling backoff with a one-time warning.
- `Headless.Messaging.Core` uses a keyed `IDistributedLockProvider` registration under `"headless.messaging"`; an un-keyed app lock provider is not automatically used by message retry processors.

## Core Concepts

Distributed locks coordinate ownership of a string resource such as `order:123`. The lock store owns acquisition and release; the protected resource still owns data integrity. Treat lock handles as leases that can expire.

### Efficiency Locks

Efficiency locks avoid duplicate work, such as two nodes generating the same report. Occasional violations cost compute or duplicate side effects, not corrupted state. Redis and cache-backed locks fit this category.

### Correctness Locks

Correctness locks protect invariants where a stale owner could corrupt data. TTL-based Redis or cache locks cannot prove correctness through process pauses, partitions, or clock skew. For correctness, use a transaction-coupled backend when one exists, or make the protected resource reject stale writes.

### Weak Fencing With LockId

`IDistributedLock.LockId` is a Snowflake-style `long` and can be passed to a protected write path as a weak fencing token. The target resource must store the last accepted fence and reject older values. This is consumer-enforced and does not turn Redis locks into transaction-coupled locks.

### Lease Lifecycle Monitoring

Lock monitoring is opt-in per acquire call via the `LockMonitoringMode` enum. `monitoring: LockMonitoringMode.Monitor` starts a background lease monitor and makes `IDistributedLock.HandleLostToken` cancel when validation detects the stored lock id changed, disappeared, or the lease lifetime exceeds the requested TTL after repeated unknown validation results. With `LockMonitoringMode.None` (default), `HandleLostToken` is `CancellationToken.None` and `IDistributedLock.IsMonitored` is `false`.

Intermediate monitor states (`Held`, `Renewed`, `Lost`, `Unknown`) are not exposed as a public API; they are visible through the `LeaseMonitorStateChanged` log event (`EventId = 1`, name `LeaseMonitorStateChanged`) for programmatic log filtering. `GetActiveMonitorCount` on the provider is `internal` and intended for test/diagnostic use only.

Combining `LockMonitoringMode.Monitor` or `LockMonitoringMode.AutoExtend` with `Timeout.InfiniteTimeSpan` for `timeUntilExpires` throws `ArgumentException` (`ParamName = "timeUntilExpires"`): lease monitoring requires a finite lease window.

`LockMonitoringMode.AutoExtend` implies monitoring and renews at `DistributedLockOptions.AutoExtensionCadenceFraction` of the TTL. `LockMonitoringMode.Monitor` (validate only) validates at `PollingCadenceFraction` and never renews the lease. These signals narrow stale-work windows; they do not upgrade Redis or cache locks into correctness locks. Fence protected writes with `LockId` when stale owners can corrupt state.

### Messaging Wake-ups

`DistributedLockProvider` can publish `DistributedLockReleased` through `IOutboxPublisher` so waiters wake quickly. The same message also nudges active lease monitors for that resource so loss validation can happen before the next polling cadence. Messaging is optional: when no publisher is registered, lock acquisition and lease monitoring fall back to polling. This keeps distributed locks usable without forcing `Headless.Messaging`.

## Choosing a Provider

Choose based on the storage you already operate and the safety category.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.DistributedLocks.Cache` | You already use `ICache` and the cache is distributed for multi-instance apps. | The app cache is in-memory and you need cross-instance coordination. | Reuses cache infrastructure but inherits that cache provider's consistency and availability behavior. |
| `Headless.DistributedLocks.Redis` | You want direct Redis-backed efficiency locks with atomic acquire/release scripts. | You need correctness locks for protected state mutations. | Requires `IConnectionMultiplexer` and Redis script loading, but avoids routing lock operations through a generic cache abstraction. |

---

## Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

### Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

### Key Features

- `IDistributedLockProvider` with `TryAcquireAsync(...)` and `AcquireAsync(...)`.
- `IDistributedLock` handle with `LockId`, `HandleLostToken`, `IsMonitored`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `TryUsingAsync(resource, work, ...)` convenience that acquires, executes work, and releases — prefer this over manual try/finally for simple guarded execution.
- `LockAcquisitionTimeoutException`, `LockHandleLostException`, and `DistributedLockException` for lock-specific failures.
- `GetLockIdAsync(resource)`, `GetLockInfoAsync(resource)`, `ListActiveLocksAsync()`, `GetActiveLocksCountAsync()`, `GetExpirationAsync(resource)` for operational inspection and monitoring.

### Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- `releaseOnDispose: false` prevents dispose-time release but does not disable explicit `ReleaseAsync(...)`.
- `HandleLostToken` is an observability signal. Consumer code decides whether to stop, compensate, or throw `LockHandleLostException`.

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
            timeUntilExpires: TimeSpan.FromMinutes(5),
            acquireTimeout: TimeSpan.FromSeconds(10),
            monitoring: LockMonitoringMode.Monitor,
            cancellationToken: ct
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
- `DisposableDistributedLock` releases on dispose by default.
- `DistributedLockOptions` configures key prefix, resource name length, waiter limits, and lease-monitor cadence fractions.
- `AddDistributedLock(...)` overloads wire storage, options, time provider, ID generator, and optional release consumers.

### Design Notes

- `IOutboxPublisher` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
- `TryAcquireAsync(..., acquireTimeout: TimeSpan.Zero)` performs a single storage attempt with an internal safety deadline.
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

Use method arguments to override per-call expiration and acquire timeout:

```csharp
await lockProvider.AcquireAsync(
    "orders:123",
    timeUntilExpires: TimeSpan.FromMinutes(5),
    acquireTimeout: TimeSpan.FromSeconds(10),
    monitoring: LockMonitoringMode.Monitor,
    cancellationToken: ct
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
- Works with memory, Redis, hybrid, or custom cache providers through `ICache`.

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
- `AddRedisDistributedLock(...)` registers a Redis-backed lock provider.
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
```

### Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`.

### Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Redis`
- `StackExchange.Redis`

### Side Effects

- Registers `HeadlessRedisScriptsLoader`.
- Registers `IDistributedLockProvider` through `Headless.DistributedLocks.Core`.
