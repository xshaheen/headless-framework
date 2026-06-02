---
domain: Distributed Locks
packages: DistributedLocks.Abstractions, DistributedLocks.Core, DistributedLocks.Redis
---

# Distributed Locks

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Efficiency Locks](#efficiency-locks)
    - [Correctness Locks](#correctness-locks)
    - [Fencing Tokens](#fencing-tokens)
    - [Lease Lifecycle Monitoring](#lease-lifecycle-monitoring)
    - [Messaging Wake-ups](#messaging-wake-ups)
    - [Observability](#observability)
- [Reader-Writer Locks](#reader-writer-locks)
- [Semaphores](#semaphores)
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
- [Headless.DistributedLocks.Redis](#headlessdistributedlocksredis)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Provider-agnostic distributed locking with automatic renewal, expiration, explicit release, and pluggable storage backends.

## Quick Orientation

Use `IDistributedLockProvider` when only one worker should own a named resource at a time. `TryAcquireAsync(...)` returns `null` on timeout; `AcquireAsync(...)` throws `LockAcquisitionTimeoutException` on timeout. Rate limiting is out of scope for this domain — and the framework does not ship a rate-limiting package. Use `Microsoft.AspNetCore.RateLimiting` (in-process) or `Polly.RateLimiting` + a community Redis-backed `RateLimiter` (distributed) when admission control is needed.

Use `IDistributedReaderWriterLockProvider` when concurrent readers are safe and writers need exclusivity. Use `IDistributedSemaphoreProvider.CreateSemaphore(resource, maxCount)` when up to N holders may work concurrently. Redis is the only shipped backend today for mutex, reader-writer locks, and semaphores; in-process scenarios use test-only fakes.

## Agent Instructions

- Code against `IDistributedLockProvider` from `Headless.DistributedLocks.Abstractions`; do not inject Redis storage types into application services.
- Use `TryAcquireAsync(...)` when timeout is an expected branch; use `AcquireAsync(...)` when timeout should fail the workflow.
- Per-call configuration is bundled into `DistributedLockAcquireOptions` (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`). Omit the argument to use defaults; use `with` expressions to derive variants.
- Always `await using` the returned lock when `ReleaseOnDispose` is `true` (the default); set `ReleaseOnDispose = false` only when ownership is deliberately transferred and the caller will release explicitly.
- Set `Monitoring = LockMonitoringMode.Monitor` when work should observe lease loss through `IDistributedLock.HandleLostToken`; set `Monitoring = LockMonitoringMode.AutoExtend` only when long work should renew its own lease in the background (it implies `Monitor`).
- Use `GetLockIdAsync(resource)` for operational inspection only; it reads the current lock id and does not renew the lease. If you already hold a monitored handle, observe `HandleLostToken` instead of polling `GetLockIdAsync`.
- Synchronous `TryUsingAsync(..., Action ...)` overloads force `LockMonitoringMode.None` because synchronous delegates cannot observe a lease-lost cancellation token.
- Do not use distributed locks (or the semaphore) as rate limiters. A semaphore caps *concurrent holders* (concurrency control); a rate limiter caps *throughput per time window* (rate control). For rate control, delegate to `Microsoft.AspNetCore.RateLimiting` (in-process), `RedisRateLimiting` (distributed), or `Polly.RateLimiting` (composition) — the framework ships no rate-limiting package.
- Use `IDistributedLock.FencingToken` for stale-write rejection when the backend supplies it. Do not repurpose `LockId` as the fence; `LockId` remains the opaque ownership token used for renew/release equality.
- Before choosing a backend, classify the use case as efficiency or correctness. Redis locks are efficiency locks, not transaction-coupled correctness locks.
- Default lock expiration is 20 minutes and default acquire timeout is 30 seconds. Override them per call via `DistributedLockAcquireOptions`; `DistributedLockOptions` configures key prefix and waiter/resource limits.
- If `Headless.Messaging` is registered, lock release wake-ups are push-based. If no `IOutboxBus` is registered, the provider still works and falls back to polling backoff with a one-time warning.
- `Headless.Messaging.Core` uses a keyed `IDistributedLockProvider` registration under `"headless.messaging"`; an un-keyed app lock provider is not automatically used by message retry processors.
- Use `IDistributedReaderWriterLockProvider` for reader-writer semantics and register it explicitly with `AddRedisDistributedReaderWriterLock(...)`; regular Redis lock setup does not auto-register it.

## Core Concepts

Distributed locks coordinate ownership of a string resource such as `order:123`. The lock store owns acquisition and release; the protected resource still owns data integrity. Treat lock handles as leases that can expire.

### Efficiency Locks

Efficiency locks avoid duplicate work, such as two nodes generating the same report. Occasional violations cost compute or duplicate side effects, not corrupted state. Redis-backed locks fit this category.

### Correctness Locks

Correctness locks protect invariants where a stale owner could corrupt data. TTL-based Redis locks cannot prove correctness through process pauses, partitions, or clock skew. For correctness, use a transaction-coupled backend when one exists, or make the protected resource reject stale writes.

### Fencing Tokens

`IDistributedLock.FencingToken` is a nullable per-resource monotonic grant counter. A protected resource can store the last accepted token and reject writes carrying an older token. `LockId` is separate: it remains the opaque ownership token used for renew and release equality.

Redis mutex locks and Redis semaphores issue fencing tokens with an atomic Lua acquire path: the lock/slot grant and `INCR` of the per-resource fence key happen in the same script, and failed acquires do not advance the counter. Redis mutex storage maps logical lock names to internal hash-tagged keys so the lock key and fence counter share a Redis Cluster slot. Redis fencing is best-effort: the fence key intentionally has no TTL and monotonicity holds only while Redis retains the key. Avoid `allkeys-*` eviction policies for Redis deployments that rely on fencing. Durable DB-sequence fencing belongs to database-backed providers.

### Lease Lifecycle Monitoring

Lock monitoring is opt-in per acquire call via `DistributedLockAcquireOptions.Monitoring` (a `LockMonitoringMode` enum). `Monitoring = LockMonitoringMode.Monitor` starts a background lease monitor and makes `IDistributedLock.HandleLostToken` cancel when validation detects the stored lock id changed, disappeared, or the lease lifetime exceeds the requested TTL after repeated unknown validation results. With `LockMonitoringMode.None` (default), `HandleLostToken` is `CancellationToken.None` and `IDistributedLock.IsMonitored` is `false`.

If the monitor loop faults, `HandleLostToken` is also cancelled as a fail-safe so a silently dead monitor cannot keep appearing healthy.

Intermediate monitor states (`Held`, `Renewed`, `Lost`, `Unknown`) are not exposed as a public API; they are visible through the `LeaseMonitorStateChanged` log event (`EventId = 30`, name `LeaseMonitorStateChanged`) for programmatic log filtering. The structured fields are `Resource`, `LockId`, `PreviousState`, and `NextState`. `GetActiveMonitorCount` on the provider is `internal` and intended for test/diagnostic use only.

Combining `LockMonitoringMode.Monitor` or `LockMonitoringMode.AutoExtend` with `Timeout.InfiniteTimeSpan` for `TimeUntilExpires` throws `ArgumentException` (`ParamName = "timeUntilExpires"`): lease monitoring requires a finite lease window.

`LockMonitoringMode.AutoExtend` implies monitoring and renews at `DistributedLockOptions.AutoExtensionCadenceFraction` of the TTL. `LockMonitoringMode.Monitor` (validate only) validates at `PollingCadenceFraction` and never renews the lease. These signals narrow stale-work windows; they do not upgrade Redis or cache locks into correctness locks. Fence protected writes with `FencingToken` when stale owners can corrupt state.

### Messaging Wake-ups

`DistributedLockProvider` can publish `DistributedLockReleased` through `IOutboxBus` so waiters wake quickly. The same message also nudges active lease monitors for that resource so loss validation can happen before the next polling cadence. Messaging is optional: when no outbox bus is registered, lock acquisition and lease monitoring fall back to polling. This keeps distributed locks usable without forcing `Headless.Messaging`.

### Observability

The package emits OpenTelemetry metrics and traces under a single instrumentation name, `Headless.DistributedLocks` (used for both the `Meter` and the `ActivitySource`). Register them with `AddMeter("Headless.DistributedLocks")` and `AddSource("Headless.DistributedLocks")` in your OpenTelemetry setup.

| Instrument | Kind | Unit | Meaning |
| --- | --- | --- | --- |
| `headless.lock.failed` | Counter (`int`) | count | Incremented when a mutex / reader-writer acquire fails or times out. |
| `headless.lock.wait.time` | Histogram (`double`) | milliseconds | Time spent waiting to acquire a lock, recorded once per acquire attempt (success or failure). |
| `headless.semaphore.failed` | Counter (`int`) | count | Incremented when a semaphore slot acquire fails or times out. |
| `headless.semaphore.wait.time` | Histogram (`double`) | milliseconds | Time spent waiting to acquire a semaphore slot, recorded once per acquire attempt (success or failure). |

Acquire paths start activities on the `ActivitySource` for distributed tracing. Lease-monitor state transitions (`Held`, `Renewed`, `Lost`, `Unknown`) are not metrics; they surface through the `LeaseMonitorStateChanged` log event (see [Lease Lifecycle Monitoring](#lease-lifecycle-monitoring)).

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

## Semaphores

Use `IDistributedSemaphoreProvider` when a resource may have N concurrent holders. `CreateSemaphore(resource, maxCount)` binds capacity to the returned semaphore instance, so its acquire calls cannot disagree about `maxCount`; all callers must use the same `maxCount` for a given distributed resource because mixed counts are undefined. Acquired slots return the same `IDistributedLock` handle used by mutex locks: `ReleaseAsync()`, `RenewAsync(...)`, `HandleLostToken`, `LockMonitoringMode.Monitor`, `LockMonitoringMode.AutoExtend`, and `FencingToken` all flow through the same surface.

Redis semaphores store live holders in a ZSET keyed by lock id with expiration timestamps as scores. Lua uses Redis server `TIME`; acquire prunes expired holders before checking capacity, while count and validate stay read-only and exclude expired scores without mutating the ZSET. The holders key gets a safety TTL of at least `ttl * 2` without shrinking an existing longer key TTL. Each successful slot grant increments the same per-resource fence counter model used by Redis mutex locks. Semaphore release publishes `DistributedLockReleased`, so waiters can wake through the same optional outbox path as mutex waiters; without messaging they fall back to polling backoff.

```csharp
var semaphore = semaphoreProvider.CreateSemaphore("downstream:billing-api", maxCount: 5);

await using var slot = await semaphore.AcquireAsync(
    new DistributedLockAcquireOptions
    {
        TimeUntilExpires = TimeSpan.FromMinutes(2),
        Monitoring = LockMonitoringMode.AutoExtend,
    },
    ct
);
```

## Choosing a Provider

Redis is the only shipped backend. Use it when you operate Redis and need efficiency locks (mutex, reader-writer, or semaphore) with atomic Lua scripts. Do not use distributed locks for correctness locks on protected state mutations without stale-write rejection through `FencingToken`. Unit tests that need in-process storage should provide a project-local fake rather than reaching for `ICache`.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.DistributedLocks.Redis` | You want direct Redis-backed efficiency locks, reader-writer locks, or N-holder semaphores. | You need durable transaction-coupled fencing. | Requires `IConnectionMultiplexer`; Redis fencing is best-effort unless the fence key is retained. |

---

## Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

### Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

### Key Features

- `IDistributedLockProvider` with `TryAcquireAsync(...)` and `AcquireAsync(...)`.
- `IDistributedReaderWriterLockProvider` with `AcquireReadLockAsync(...)`, `TryAcquireReadLockAsync(...)`, `AcquireWriteLockAsync(...)`, and `TryAcquireWriteLockAsync(...)`.
- `IDistributedSemaphoreProvider` and `IDistributedSemaphore` for creation-time `maxCount` concurrency control.
- `IDistributedLock` handle with `LockId`, nullable `FencingToken`, `HandleLostToken`, `IsMonitored`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
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
- `DistributedSemaphoreProvider` implements `IDistributedSemaphoreProvider`.
- `DisposableDistributedLock` releases on dispose by default.
- `IDistributedReaderWriterLockStorage` defines atomic read/write acquire, extend, release, and validation operations for storage providers.
- `IDistributedSemaphoreStorage` defines acquire, extend, validate, release, and holder-count operations for storage providers.
- `DistributedLockOptions` configures key prefix, resource name length, waiter limits, and lease-monitor cadence fractions.
- `AddDistributedLock(...)` overloads wire storage, options, time provider, ID generator, and optional release consumers.
- `AddDistributedReaderWriterLock(...)` overloads wire reader-writer storage, options, time provider, and ID generator.
- `AddDistributedSemaphore(...)` overloads wire semaphore storage, options, time provider, and ID generator.

### Design Notes

- `IOutboxBus` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
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
- Registers `IDistributedSemaphoreProvider` as singleton when `AddDistributedSemaphore(...)` is called.
- Registers `TimeProvider.System` and `ILongIdGenerator` when absent.
- Registers a `DistributedLockReleased` consumer only when an `IOutboxBus` registration exists.

---

## Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks, reader-writer locks, and semaphores.

### Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, release, reader-writer transitions, semaphore slots, and fencing-token issuance.

### Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `RedisDistributedReaderWriterLockStorage` implements `IDistributedReaderWriterLockStorage`.
- `RedisDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `AddRedisDistributedLock(...)` registers a Redis-backed lock provider.
- `AddRedisDistributedReaderWriterLock(...)` registers a Redis-backed reader-writer lock provider.
- `AddRedisDistributedSemaphore(...)` registers a Redis-backed semaphore provider.
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

builder.Services.AddRedisDistributedSemaphore(options =>
{
    options.KeyPrefix = "distributed-lock:";
});
```

### Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`.

Redis mutex storage maps each logical lock name to an internal hash-tagged lock key and one no-TTL fence counter key in the same Redis Cluster slot. Redis semaphore storage creates `{resource}:holders` (ZSET of `lockId → expiry-epoch-ms`) and `fence:{resource}`. Resource names containing `{` or `}` are rejected where storage-owned hash-tags are required.

Reader-writer storage creates `{resource}:writer` (string holding the active writer id or the `:_WRITERWAITING`-suffixed marker) and `{resource}:readers` (HASH of `lockId → expiry-epoch-ms`) Redis keys internally. Resource names containing `{` or `}` are rejected so the storage-owned Redis cluster hash-tag remains deterministic. The marker TTL is governed by `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30s, validated `0 < ttl <= 5 min`).

### Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `StackExchange.Redis`

### Side Effects

- Registers a keyed `HeadlessRedisScriptsLoader` bound to the app's `IConnectionMultiplexer`.
- Registers hosted `IInitializer` warmup for only the Redis lock feature scripts that were registered:
  mutex scripts for `AddRedisDistributedLock(...)`, reader-writer scripts for `AddRedisDistributedReaderWriterLock(...)`, and semaphore scripts for `AddRedisDistributedSemaphore(...)`.
- Registers `IDistributedLockProvider` through `Headless.DistributedLocks.Core`.
- Registers `IDistributedReaderWriterLockProvider` through `Headless.DistributedLocks.Core` when `AddRedisDistributedReaderWriterLock(...)` is called.
- Registers `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core` when `AddRedisDistributedSemaphore(...)` is called.
