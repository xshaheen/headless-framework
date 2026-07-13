---
domain: Distributed Locks
packages: DistributedLocks.Abstractions, DistributedLocks.Core, DistributedLocks.Core.Database, DistributedLocks.InMemory, DistributedLocks.PostgreSql, DistributedLocks.Redis, DistributedLocks.SqlServer
---

# Distributed Locks

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Efficiency Locks](#efficiency-locks)
    - [Correctness Locks](#correctness-locks)
    - [Fencing Tokens](#fencing-tokens)
    - [Composite Acquisition](#composite-acquisition)
    - [Lease Lifecycle Monitoring](#lease-lifecycle-monitoring)
    - [Connection-Scoped Locks (Database Engine)](#connection-scoped-locks-database-engine)
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
- [Headless.DistributedLocks.Core.Database](#headlessdistributedlockscoredatabase)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.DistributedLocks.InMemory](#headlessdistributedlocksinmemory)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.DistributedLocks.PostgreSql](#headlessdistributedlockspostgresql)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.DistributedLocks.Redis](#headlessdistributedlocksredis)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.DistributedLocks.SqlServer](#headlessdistributedlockssqlserver)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-5)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)

> Provider-agnostic distributed locking with automatic renewal, expiration, explicit release, and pluggable storage backends.

## Quick Orientation

Use `IDistributedLock` when only one worker should own a named resource at a time. `TryAcquireAsync(...)` returns `null` on timeout; `AcquireAsync(...)` throws `LockAcquisitionTimeoutException` on timeout. Rate limiting is out of scope for this domain — and the framework does not ship a rate-limiting package. Use `Microsoft.AspNetCore.RateLimiting` (in-process) or `Polly.RateLimiting` + a community Redis-backed `RateLimiter` (distributed) when admission control is needed.

Use `IDistributedReadWriteLock` when concurrent readers are safe and writers need exclusivity. Use `IDistributedSemaphoreProvider.CreateSemaphore(resource, maxCount)` when up to N holders may work concurrently. Redis ships mutex, reader-writer, and semaphore support. Postgres ships mutex and reader-writer support over advisory locks; it does not provide semaphores. In-process scenarios can use `Headless.DistributedLocks.InMemory`, which ships all three primitives but is process-local and not distributed.

## Agent Instructions

- Code against `IDistributedLock` from `Headless.DistributedLocks.Abstractions`; do not inject Redis storage types into application services.
- Use `Headless.DistributedLocks.InMemory` only for tests, local development, or deliberately single-instance apps. It is not a cross-process lock.
- Use `TryAcquireAsync(...)` when timeout is an expected branch; use `AcquireAsync(...)` when timeout should fail the workflow.
- Use `TryAcquireAllAsync(...)` or `AcquireAllAsync(...)` when one operation must hold several resources. Pass the complete set in one call so the framework can sort it ordinally, deduplicate it, enforce one timeout budget, and compensate partial acquisition in reverse order.
- Per-call configuration is bundled into `DistributedLockAcquireOptions` (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`). Omit the argument to use defaults; use `with` expressions to derive variants.
- Always `await using` the returned lock when `ReleaseOnDispose` is `true` (the default); set `ReleaseOnDispose = false` only when ownership is deliberately transferred and the caller will release explicitly.
- Set `Monitoring = LockMonitoringMode.Monitor` when work should observe lease loss through `IDistributedLease.LostToken`; set `Monitoring = LockMonitoringMode.AutoExtend` only when long work should renew its own lease in the background (it implies `Monitor`).
- Use `GetLeaseIdAsync(resource)` for operational inspection only; it reads the current observable lease id and does not renew the lease. Some backends can prove the resource is locked without exposing the current holder identity, so `GetLeaseIdAsync` may return `null` even while `IsLockedAsync(resource)` or `GetLockInfoAsync(resource)` reports an active lock. If you already hold a monitored lease, observe `LostToken` or call `ThrowIfLost()` instead of polling `GetLeaseIdAsync`.
- Synchronous `TryUsingAsync(..., Action ...)` overloads force `LockMonitoringMode.None` because synchronous delegates cannot observe a lease-lost cancellation token.
- Do not use distributed locks (or the semaphore) as rate limiters. A semaphore caps *concurrent holders* (concurrency control); a rate limiter caps *throughput per time window* (rate control). For rate control, delegate to `Microsoft.AspNetCore.RateLimiting` (in-process), `RedisRateLimiting` (distributed), or `Polly.RateLimiting` (composition) — the framework ships no rate-limiting package.
- Use `IDistributedLease.FencingToken` for stale-write rejection when the backend supplies it. Do not repurpose `LeaseId` as the fence; `LeaseId` remains the opaque ownership token used for renew/release equality. Call `ThrowIfLost()` in hot paths that must fail-stop after observed lease loss.
- Before choosing a backend, classify the use case as efficiency or correctness. Redis locks are efficiency locks, not transaction-coupled correctness locks.
- Use `Headless.DistributedLocks.PostgreSql` when the protected resource is already in PostgreSQL or when transaction-coupled advisory locks are required. Standard session-scoped Postgres locks require direct connections or PgBouncer session pooling.
- For connection-scoped (database) locks there is no TTL and no finalizer reclaim: always dispose the handle (`await using`) or call `ReleaseAsync()`. An abandoned handle leaks its connection and advisory lock until the provider is disposed. Connection death is surfaced through `LostToken` only when monitoring is enabled, so observe that token for monitored handles rather than assuming a lease will expire.
- Default lock expiration is 20 minutes and default acquire timeout is 30 seconds. Override them per call via `DistributedLockAcquireOptions`; `DistributedLockOptions` configures key prefix and waiter/resource limits.
- If `Headless.Messaging` is registered, lock release wake-ups are push-based. If no `IOutboxBus` is registered, the provider still works and falls back to polling backoff with a one-time warning.
- `Headless.Messaging.Core` uses a keyed `IDistributedLock` registration under `"headless.messaging"`; an un-keyed app lock provider is not automatically used by message retry processors.
- Use `AddHeadlessDistributedLocks(setup => setup.UseRedis())` for Redis-backed mutex, reader-writer, and semaphore primitives. PostgreSQL and SQL Server providers intentionally register mutex + reader-writer only.

## Core Concepts

Distributed locks coordinate ownership of a string resource such as `order:123`. The lock store owns acquisition and release; the protected resource still owns data integrity. Treat lock handles as leases that can expire.

### Efficiency Locks

Efficiency locks avoid duplicate work, such as two nodes generating the same report. Occasional violations cost compute or duplicate side effects, not corrupted state. Redis-backed locks fit this category.

### Correctness Locks

Correctness locks protect invariants where a stale owner could corrupt data. TTL-based Redis locks cannot prove correctness through process pauses, partitions, or clock skew. For correctness, use a transaction-coupled backend when one exists, or make the protected resource reject stale writes.

### Fencing Tokens

`IDistributedLease.FencingToken` is a nullable per-resource monotonic grant counter. A protected resource can store the last accepted token and reject writes carrying an older token. `LeaseId` is separate: it remains the opaque ownership token used for renew and release equality.

Redis mutex locks and Redis semaphores issue fencing tokens with an atomic Lua acquire path: the lock/slot grant and `INCR` of the per-resource fence key happen in the same script, and failed acquires do not advance the counter. Redis mutex storage maps logical lock names to internal hash-tagged keys so the lock key and fence counter share a Redis Cluster slot. Redis fencing is best-effort: the fence key intentionally has no TTL and monotonicity holds only while Redis retains the key. Avoid `allkeys-*` eviction policies for Redis deployments that rely on fencing. Postgres and SQL Server mutex locks issue durable sequence-backed fencing tokens.

### Composite Acquisition

`TryAcquireAllAsync(resources, options, cancellationToken)` and `AcquireAllAsync(resources, options, cancellationToken)` acquire a complete resource set in ordinal order after validating and deduplicating the input. Canonical ordering prevents two callers that request the same set in different orders from deadlocking each other. The acquire timeout is one budget for the whole set, not a fresh timeout per child; `TimeSpan.Zero` still gives every canonical child one non-blocking attempt.

This is compensating coordination, not a transactional multi-lock primitive. If a later child cannot be acquired, is lost, or faults, every child already held is released and disposed in reverse order. Cleanup is exhaustive and cleanup failures are surfaced rather than hidden. While a later child is pending, finite-TTL children are renewed at half their TTL, capped at one minute; `LockMonitoringMode.AutoExtend` already owns renewal, and infinite-TTL leases need none. The coordinator uses `IDistributedLock.TimeProvider` for deadlines and scheduled waits, so custom providers must expose the same clock used by their own acquisition logic.

When canonicalization leaves one resource, the method returns the provider's child lease unchanged, preserving its real `LeaseId` and `FencingToken`. A true multi-resource result has a synthetic composite `LeaseId`, a joined diagnostic `Resource`, and a `null` scalar `FencingToken` because independent per-resource fences cannot be represented safely as one number. Its loss token links the child loss tokens, renewal covers every child, and release/disposal run in reverse order. `ReleaseOnDispose = false` suppresses dispose-time release for the complete set but still permits explicit `ReleaseAsync()`.

### Lease Lifecycle Monitoring

Lock monitoring is opt-in per acquire call via `DistributedLockAcquireOptions.Monitoring` (a `LockMonitoringMode` enum). `Monitoring = LockMonitoringMode.Monitor` starts a background lease monitor and makes `IDistributedLease.LostToken` cancel when validation detects the stored lease id changed, disappeared, or the lease lifetime exceeds the requested TTL after repeated unknown validation results. With `LockMonitoringMode.None` (default), `LostToken` is `CancellationToken.None` and `IDistributedLease.CanObserveLoss` is `false`.

If the monitor loop faults, `LostToken` is also cancelled as a fail-safe so a silently dead monitor cannot keep appearing healthy.

Intermediate monitor states (`Held`, `Renewed`, `Lost`, `Unknown`) are not exposed as a public API; they are visible through the `LeaseMonitorStateChanged` log event (`EventId = 30`, name `LeaseMonitorStateChanged`) for programmatic log filtering. The structured fields are `Resource`, `LeaseId`, `PreviousState`, and `NextState`. `GetActiveMonitorCount` on the provider is `internal` and intended for test/diagnostic use only.

Combining `LockMonitoringMode.Monitor` or `LockMonitoringMode.AutoExtend` with `Timeout.InfiniteTimeSpan` for `TimeUntilExpires` throws `ArgumentException` (`ParamName = "timeUntilExpires"`): lease monitoring requires a finite lease window.

`LockMonitoringMode.AutoExtend` implies monitoring and renews at `DistributedLockOptions.AutoExtensionCadenceFraction` of the TTL. `LockMonitoringMode.Monitor` (validate only) validates at `PollingCadenceFraction` and never renews the lease. These signals narrow stale-work windows; they do not upgrade Redis or cache locks into correctness locks. Fence protected writes with `FencingToken` when stale owners can corrupt state.

### Connection-Scoped Locks (Database Engine)

Database-backed providers (`Headless.DistributedLocks.PostgreSql` over PostgreSQL advisory locks, and any provider built on `Headless.DistributedLocks.Core.Database`) do not store a lease record with a TTL. The lock exists for exactly as long as the holding database session does: the engine acquires the native primitive (for example `pg_try_advisory_lock`) on a live connection and releases it by unlocking — or by closing the connection, which drops every advisory lock the session held. Three engine behaviors follow from this and are visible to consumers as semantics, not as new API. The public surface (`AddHeadlessDistributedLocks(setup => setup.UsePostgreSql(...))`, `IDistributedLock.TryAcquireAsync(...)`, the returned `IDistributedLease`) is unchanged.

**Disposal contract — there is no TTL and no finalizer reclaim.** A connection-scoped lock is released only when its handle is disposed (or `ReleaseAsync()` is called). There is no lease timeout that eventually frees it and no GC finalizer that reclaims it: the provider holds a strong reference to the backing engine handle for its lifetime, so a handle abandoned without disposal leaks its connection and its advisory lock until the provider itself is disposed. This is the deliberate contract — it mirrors `lock`/`using` discipline, and the reference engine's finalizer queue was intentionally dropped in favor of requiring explicit disposal. Always dispose the handle; `await using` is the intended usage. (`RenewAsync(...)` is a no-op success and `GetExpirationAsync(...)` returns `null`, because there is nothing to renew or expire.)

**Active connection-death detection.** Because the lock lives only while the session does, a consumer needs to know promptly when that session dies — otherwise it keeps running a critical section the database has already released. When the lock is monitored (`CanObserveLoss == true`, exposing `IDistributedLease.LostToken`), an active `ConnectionMonitor` backs the token. It runs a server-side probe (a bounded-timeout sleep query on a roughly one-minute cadence) in addition to the connection's `StateChange` event. The probe carries a bounded command timeout (default 10s), which is what catches a silent half-open connection — a network drop with no RST, where `StateChange` alone never fires until the next real query. When the session dies, `LostToken` is cancelled. The trade-off is a small, periodic query cost on the holding connection in exchange for bounded death-detection latency; it is the database analog of [lease monitoring](#lease-lifecycle-monitoring) for TTL-based providers.

TCP keepalive remains complementary, not redundant. Keepalive (`PostgresDistributedLockOptions.KeepAlive`, default 30s, applied only to a provider-built data source) surfaces a dead socket faster at the transport layer; the monitor is the active query-level check that does not depend on keepalive timing. Keep both for the tightest detection window.

**Optimistic connection multiplexing.** Opening one physical connection per uncontended lock is wasteful, so the engine multiplexes: several uncontended advisory locks on *distinct* keys share a single physical connection. Two situations force a transparent fall back to a *dedicated* connection: (1) the acquire would block (contention — a dedicated connection lets PostgreSQL serialize the waiter correctly without holding up the shared connection's other locks or their release); and (2) two resource strings resolve to the *same* advisory key (a key collision — advisory locks are re-entrant per session, so sharing one connection would let two callers each believe they hold an exclusive lock; routing the colliding acquirer to its own connection makes the database serialize them). The trade-off is shared-connection efficiency in the common uncontended case versus a dedicated connection exactly when correctness or progress demands it. This is internal: callers see only cheaper connection usage, never different lock semantics.

### Messaging Wake-ups

`DistributedLock` can publish `DistributedLockReleased` through `IOutboxBus` so waiters wake quickly. The same message also nudges active lease monitors for that resource so loss validation can happen before the next polling cadence. Messaging is optional: when no outbox bus is registered, lock acquisition and lease monitoring fall back to polling. This keeps distributed locks usable without forcing `Headless.Messaging`.

### Observability

The package emits OpenTelemetry metrics and traces under a single instrumentation name, `Headless.DistributedLocks` (used for both the `Meter` and the `ActivitySource`). Register them with `AddMeter("Headless.DistributedLocks")` and `AddSource("Headless.DistributedLocks")` in your OpenTelemetry setup.

| Instrument | Kind | Unit | Meaning |
| --- | --- | --- | --- |
| `headless.lock.failed` | Counter (`int`) | count | Incremented when a mutex / reader-writer acquire fails or times out. Carries a `reason` dimension (see below). |
| `headless.lock.wait.time` | Histogram (`double`) | milliseconds | Time spent waiting to acquire a lock, recorded once per acquire attempt (success or failure). |
| `headless.semaphore.failed` | Counter (`int`) | count | Incremented when a semaphore slot acquire fails or times out. Carries a `reason` dimension (see below). |
| `headless.semaphore.wait.time` | Histogram (`double`) | milliseconds | Time spent waiting to acquire a semaphore slot, recorded once per acquire attempt (success or failure). |

The `*.failed` counters carry a `reason` dimension so a lock-store stall is distinguishable from routine contention:

- `reason=contended` — every expected not-acquired outcome (lock held by another holder, acquire timeout elapsed, swallowed transient storage error).
- `reason=stalled` — a non-blocking try-once acquire (`AcquireTimeout = TimeSpan.Zero`) whose single storage attempt hit the internal safety deadline (lock-store stall), surfaced even when the caller's token never fires. Alert on `rate(headless.lock.failed{reason="stalled"})` to detect lock-store degradation; the same trip also emits the `TryOnceSafetyDeadlineFired` log event (`EventId = 24`, Warning, fields `Resource`/`LeaseId`/`Duration`) as a per-event breadcrumb. The metric counts toward the same total as before — the tag splits the existing counter, it does not add a new instrument.

The two values are exposed as `public const` on `DistributedLockFailureReasons` (`Contended` / `Stalled`) so alert-rule and dashboard code can reference them at compile time instead of hard-coding the strings.

Acquire paths start activities on the `ActivitySource` for distributed tracing. Lease-monitor state transitions (`Held`, `Renewed`, `Lost`, `Unknown`) are not metrics; they surface through the `LeaseMonitorStateChanged` log event (see [Lease Lifecycle Monitoring](#lease-lifecycle-monitoring)).

## Reader-Writer Locks

Use `IDistributedReadWriteLock` for read-heavy resources where multiple readers can proceed concurrently and writers must run exclusively. Read and write acquires return the same `IDistributedLease` handle shape as mutex locks, so `ReleaseAsync()`, `RenewAsync(...)`, `LostToken`, and `LockMonitoringMode.AutoExtend` work the same way.

Redis reader-writer locks use two keys per resource: `{resource}:writer` for the active writer or writer-waiting marker, and `{resource}:readers` for active reader lease ids. The braces are Redis cluster hash-tags so both keys live on the same slot. Resource names containing `{` or `}` are rejected because storage owns that hash-tag shape.

The reader set is a Redis HASH whose fields are reader lockIds and whose values are per-reader expiry epochs in milliseconds (computed inside Lua via `redis.call('TIME')` so the server clock is authoritative). The hash key itself carries a generous safety-net TTL (2× the lease duration); the per-entry expiry is the source of truth for liveness. Each writer-acquire script run prunes expired reader entries before checking "no live readers" so a crashed reader never strands a queued writer past its own lease.

Writer-preference is intentional. When a writer queues behind active readers, Redis stores a writer-waiting marker. New readers are blocked while that marker exists, preventing steady read traffic from starving the writer. The marker is keyed by the writer's leaseId but the plant/refresh branch fires for any caller observing a `:_WRITERWAITING`-suffixed value, so multiple contending writers collectively keep the marker continuously present even if individual writers cancel. The marker TTL is governed by `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30s) rather than the lease TTL, so an abandoned writer cannot block readers for the full lease window. If the writer times out or is cancelled before acquiring, the provider clears its waiting marker via the release path.

Readers running `Monitoring = LockMonitoringMode.AutoExtend` may see `LostToken` fire when a writer queues — the extend-read script refuses to refresh while a writer-waiting marker is present, which the provider classifies as `Lost`. This is the contract that enforces the writer-preference guarantee at the per-reader level: a reader that wants to keep its lease through a writer queue must reacquire from scratch after the writer drains.

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
    });

    setup.UseRedis();
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

Use `IDistributedSemaphoreProvider` when a resource may have N concurrent holders. `CreateSemaphore(resource, maxCount)` binds capacity to the returned semaphore instance, so its acquire calls cannot disagree about `maxCount`; all callers must use the same `maxCount` for a given distributed resource because mixed counts are undefined. Acquired slots return the same `IDistributedLease` handle used by mutex locks: `ReleaseAsync()`, `RenewAsync(...)`, `LostToken`, `LockMonitoringMode.Monitor`, `LockMonitoringMode.AutoExtend`, and `FencingToken` all flow through the same surface.

Redis semaphores store live holders in a ZSET keyed by lease id with expiration timestamps as scores. Lua uses Redis server `TIME`; acquire prunes expired holders before checking capacity, while count and validate stay read-only and exclude expired scores without mutating the ZSET. The holders key gets a safety TTL of at least `ttl * 2` without shrinking an existing longer key TTL. Each successful slot grant increments the same per-resource fence counter model used by Redis mutex locks. Semaphore release publishes `DistributedLockReleased`, so waiters can wake through the same optional outbox path as mutex waiters; without messaging they fall back to polling backoff.

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

Use InMemory when all contenders are inside one process. Use Redis when you operate Redis and need efficiency locks (mutex, reader-writer, or semaphore) with atomic Lua scripts. Use Postgres when the protected state already lives in PostgreSQL or when session/transaction-coupled advisory locks are the right primitive. Use SQL Server when the protected state already lives in SQL Server or when native `sp_getapplock` server-side blocking is the right primitive. Do not use distributed locks for correctness locks on protected state mutations without stale-write rejection through `FencingToken` or transaction-coupled locking.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.DistributedLocks.InMemory` | Tests, local development, or single-instance apps need the real lock abstractions without Redis. | More than one process, node, container, or app instance can contend for the same resource. | No infrastructure; coordination and fencing state disappear with the process. |
| `Headless.DistributedLocks.PostgreSql` | You want PostgreSQL advisory mutexes or reader-writer locks, durable sequence fencing, or transaction-coupled locks. | You need semaphores, PgBouncer transaction/statement pooling for session-scoped locks, or no PostgreSQL dependency. | No TTL; the lock lives as long as the holding connection, so the handle must be disposed to release it (no finalizer reclaim). Connection death is detected actively (see [Connection-Scoped Locks](#connection-scoped-locks-database-engine)). |
| `Headless.DistributedLocks.Redis` | You want direct Redis-backed efficiency locks, reader-writer locks, or N-holder semaphores. | You need durable transaction-coupled fencing. | Requires `IConnectionMultiplexer`; Redis fencing is best-effort unless the fence key is retained. |
| `Headless.DistributedLocks.SqlServer` | You want SQL Server application locks, native server-side blocking, durable sequence fencing, or transaction-coupled locks. | You need semaphores, upgradeable reader-writer locks, or no SQL Server dependency. | No TTL; session-scoped locks live as long as the holding connection, and waiters block inside SQL Server. |

---

## Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

### Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

### Key Features

- `IDistributedLock` with single-resource `TryAcquireAsync(...)` / `AcquireAsync(...)` and multi-resource `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` extensions.
- `IDistributedReadWriteLock` with `AcquireReadLockAsync(...)`, `TryAcquireReadLockAsync(...)`, `AcquireWriteLockAsync(...)`, and `TryAcquireWriteLockAsync(...)`.
- `IDistributedSemaphoreProvider` and `IDistributedSemaphore` for creation-time `maxCount` concurrency control.
- `IDistributedLease` handle with `LeaseId`, nullable `FencingToken`, `LostToken`, `CanObserveLoss`, `IsLost`, `ThrowIfLost()`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `TryUsingAsync(resource, work, ...)` convenience that acquires, executes work, and releases — prefer this over manual try/finally for simple guarded execution.
- `LockAcquisitionTimeoutException`, `LockHandleLostException`, and `DistributedLockException` for lock-specific failures.
- `GetLeaseIdAsync(resource)`, `GetLockInfoAsync(resource)`, `ListActiveLocksAsync()`, `GetActiveLocksCountAsync()`, `GetExpirationAsync(resource)` for operational inspection and monitoring. `GetLeaseIdAsync` does not renew a lease; monitored holders should use `LostToken` or `ThrowIfLost()` for lease-loss observation. Inspection `LeaseId` values may be null when the backend can observe the locked resource but not the current holder identity, and provider-wide list/count results are limited to what the backend can enumerate.

### Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- Multi-resource acquisition validates, deduplicates, and ordinal-sorts the complete input before the first provider call, then applies one acquire timeout across the canonical set. A zero timeout gives every canonical resource one non-blocking attempt. Partial acquisition is compensated by exhaustive reverse-order release and disposal; it is not transactional.
- A canonical set of one returns the provider's original child lease, preserving its `LeaseId` and `FencingToken`. A true multi-resource lease has a synthetic `LeaseId`, a joined diagnostic `Resource`, and a `null` scalar `FencingToken`; its loss signal links the child signals, and renew/release operate on every child.
- During composite formation, finite-TTL children are renewed at half the TTL, capped at one minute, unless `LockMonitoringMode.AutoExtend` already owns renewal. Composite deadlines and waits use `IDistributedLock.TimeProvider`; custom providers must expose the clock used by their own acquisition logic.
- Per-call configuration (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`) is bundled into `DistributedLockAcquireOptions`. Omit the argument to use defaults; use `with` expressions to derive variants.
- `ReleaseOnDispose = false` prevents dispose-time release but does not disable explicit `ReleaseAsync(...)`, including for composite leases.
- `LostToken` is an observability signal. Consumer code decides whether to stop, compensate, or throw `LockHandleLostException`; `ThrowIfLost()` implements the common fail-stop check.
- `TimeUntilExpires = null` uses the provider default. Built-in providers use a finite 20-minute default, so `null` is valid with `LockMonitoringMode.AutoExtend`; `Timeout.InfiniteTimeSpan` is not.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Abstractions
```

### Quick Start

The multi-resource extension signatures are `Task<IDistributedLease?> TryAcquireAllAsync(IEnumerable<string> resources, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)` and `Task<IDistributedLease> AcquireAllAsync(IEnumerable<string> resources, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)`.

```csharp
public sealed class OrderWorker(IDistributedLock lockProvider)
{
    public async Task ProcessAsync(Guid orderId, CancellationToken ct)
    {
        await using var lease = await lockProvider.AcquireAllAsync(
            [$"order:{orderId}", $"customer:{orderId}"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromMinutes(5),
                AcquireTimeout = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            ct
        );

        using var lostRegistration = lease.LostToken.Register(
            () => { /* stop work */
            }
        );
        lease.ThrowIfLost();
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

Provides the `DistributedLock` implementation and setup extensions.

### Problem Solved

Implements lock acquisition, renewal, release, inspection, timeout handling, and optional messaging wake-ups over an `IDistributedLockStorage`.

### Key Features

- `DistributedLock` implements `IDistributedLock`.
- `DistributedReadWriteLock` implements `IDistributedReadWriteLock`.
- `DistributedSemaphoreProvider` implements `IDistributedSemaphoreProvider`.
- `DisposableDistributedLock` releases on dispose by default.
- `IDistributedReadWriteLockStorage` defines atomic read/write acquire, extend, release, and validation operations for storage providers.
- `IDistributedSemaphoreStorage` defines acquire, extend, validate, release, and holder-count operations for storage providers.
- `DistributedLockOptions` configures key prefix, resource name length, waiter limits, and lease-monitor cadence fractions.
- `AddHeadlessDistributedLocks(...)` is the single root registration entry point (it returns the `IServiceCollection` for chaining); provider packages contribute `Use...` methods on the `HeadlessDistributedLocksSetupBuilder`.
- `AddHeadlessDistributedLocks(...)` auto-registers the optional `DistributedLockReleased` consumer descriptor.
- `IDistributedLocksOptionsExtension` is the setup-time hook used by provider packages to wire supported primitives.

### Design Notes

- `IOutboxBus` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
- When messaging is present, the release consumer is drained at messaging startup whether `AddHeadlessDistributedLocks(...)` runs before or after `AddHeadlessMessaging(...)`; without messaging, waiters fall back to polling.
- `TryAcquireAsync(..., new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero })` performs a single storage attempt with an internal safety deadline. If that deadline fires (lock-store stall, caller token never cancels), the acquire still returns `null` but emits the `TryOnceSafetyDeadlineFired` log event (`EventId = 24`, Warning) and tags the failure metric `reason=stalled`, distinguishing a stall from routine contention (`reason=contended`). Applies to mutex, reader-writer, and semaphore non-blocking acquires.
- Lease monitors drain before dispose-time release, so monitoring does not add release retry latency during shutdown.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Core
```

### Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
        options.MaxResourceNameLength = 512;
    });

    setup.UseRedis(); // from Headless.DistributedLocks.Redis
});

builder.Services.AddHeadlessMessaging(setup =>
{
    // setup.Use... storage and transport providers
});
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

`Headless.Messaging.Core`'s retry processor is the canonical in-repo consumer of this mode. Each retry-pickup tick acquires its coarse pickup lock with `AcquireTimeout = TimeSpan.Zero` (non-blocking try-once), a finite lease window equal to the current polling interval, and `LockMonitoringMode.AutoExtend` so a pickup that outruns the initial TTL keeps the lease without manual per-tick renewal. It treats `LostToken` as a pickup boundary, not a dispatch-cancellation token: a lost lease blocks new pickup but never aborts in-flight dispatch, which stays governed by the per-row `LockedUntil` lease (the correctness primitive). See [Distributed Lock Integration](messaging.md#distributed-lock-integration) for the retry-lock EventIds and the full correctness-vs-coordination split.

### Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Core`

### Side Effects

- Registers exactly one provider selected by the `AddHeadlessDistributedLocks(...)` builder.
- Redis and InMemory providers register `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider`.
- PostgreSQL and SQL Server providers register `IDistributedLock` and `IDistributedReadWriteLock`.
- Registers `TimeProvider.System` and `IGuidGenerator` when absent.
- Auto-registers the shared `DistributedLockReleased` messaging consumer. The descriptor is inert when messaging is absent; waiters still use polling.

---

## Headless.DistributedLocks.Core.Database

Shared connection-scoped engine contracts for database-backed distributed locks.

### Problem Solved

Lets database providers map session-scoped or transaction-scoped lock primitives onto the standard distributed-lock abstractions without adding ADO.NET-specific machinery to Redis or cache providers.

### Key Features

- `IConnectionScopedLockStorage` for non-blocking session-held lock acquisition and release.
- `ConnectionScopedDistributedLock` implements `IDistributedLock` over connection-scoped storage.
- `ConnectionScopedReadWriteLock` implements `IDistributedReadWriteLock` over shared/exclusive storage.
- `IFencingTokenSource` lets database providers stamp mutex handles with durable sequence-backed fencing tokens.
- `IReleaseSignal` provides the wake-up seam for provider push notifications plus polling fallback.

### Design Notes

- Connection-scoped locks have no TTL and no GC finalizer reclaim. `RenewAsync(...)` is a no-op success, `GetExpirationAsync(...)` returns `null`, and the lock is released only when the handle is disposed (or `ReleaseAsync()` is called). The provider holds a strong reference to the engine handle for its lifetime, so an abandoned handle leaks its connection and lock until the provider is disposed. Always `await using` the handle. See [Connection-Scoped Locks](#connection-scoped-locks-database-engine).
- Handle loss is backed by an active `ConnectionMonitor`, not just the connection's `StateChange` event: monitored handles (`CanObserveLoss == true`) run a periodic bounded-timeout server-side probe so a silent half-open connection cancels `LostToken` instead of going unnoticed until the next query. `Monitoring = None` skips that active probe and leaves `LostToken` at `CancellationToken.None`.
- The engine optimistically multiplexes uncontended locks on distinct keys onto a shared physical connection and transparently falls back to a dedicated connection on contention or advisory-key collision. This is a performance characteristic; lock semantics are unchanged.
- Reader-writer locks do not issue fencing tokens; `FencingToken` is `null` for read and write handles.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Core.Database
```

### Quick Start

Use a concrete provider such as `Headless.DistributedLocks.PostgreSql`; application code normally does not register `Core.Database` directly.

### Configuration

None directly. Concrete providers own options and storage configuration.

### Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.DistributedLocks.Core`
- `Headless.Core`
- `Headless.Hosting`

### Side Effects

None by itself. Concrete providers register the public lock providers.

---

## Headless.DistributedLocks.InMemory

In-process storage and setup helpers for distributed-lock abstractions.

### Problem Solved

Provides a no-infrastructure backend for code that depends on `IDistributedLock`, `IDistributedReadWriteLock`, or `IDistributedSemaphoreProvider` in tests, local development, and single-instance applications.

### Key Features

- `InMemoryDistributedLockStorage` implements `IDistributedLockStorage`.
- `InMemoryDistributedReadWriteLockStorage` implements `IDistributedReadWriteLockStorage`.
- `InMemoryDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `UseInMemory()` registers in-process mutex, reader-writer lock, and semaphore providers through `AddHeadlessDistributedLocks(...)`.
- Uses injected `TimeProvider` for deterministic TTL behavior.
- Mutex compare-and-swap preserves the existing absolute expiration when `ReplaceIfEqualAsync(..., newTtl: null)` is used.

### Design Notes

This package is process-local. It does not coordinate across app instances, machines, containers, or processes. Use it when one process owns all contenders, or when tests need a real provider without Redis. Fencing tokens are monotonic inside the process lifetime only.

Reader-writer lease ids must not contain `:` because that character is reserved for the writer-waiting marker suffix; ids containing it are rejected.

### Installation

```bash
dotnet add package Headless.DistributedLocks.InMemory
```

### Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
    });

    setup.UseInMemory();
});
```

### Configuration

No InMemory-specific options. Configure `DistributedLockOptions`.

Reader-writer and semaphore TTL checks use the registered `TimeProvider`, so tests can register a fake clock and advance leases deterministically. `LostToken` is `CancellationToken.None` unless monitoring is enabled through `DistributedLockAcquireOptions`.

### Dependencies

- `Headless.DistributedLocks.Core`

### Side Effects

- Registers `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core`.
- Registers process-local singleton storage instances for all three primitives.

---

## Headless.DistributedLocks.PostgreSql

PostgreSQL advisory-lock provider for mutex and reader-writer distributed locks.

### Problem Solved

Coordinates work across nodes using PostgreSQL advisory locks, with no Redis dependency and with transaction-coupled locking available for data mutations already protected by a PostgreSQL transaction.

### Key Features

- `UsePostgreSql(...)` registers `IDistributedLock` and `IDistributedReadWriteLock` through `AddHeadlessDistributedLocks(...)`.
- `PostgresAdvisoryLockKey` maps strings, `long`, and `(int, int)` keys onto PostgreSQL advisory key spaces.
- Session-scoped mutex locks use `pg_try_advisory_lock` and release with `pg_advisory_unlock`.
- Reader-writer locks use PostgreSQL shared and exclusive advisory locks.
- Mutex handles receive durable sequence-backed `FencingToken` values.
- `PostgresDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` use transaction-scoped `pg_advisory_xact_lock`.

### Design Notes

- Standard provider locks are session-scoped: they require a stable backend session from acquire through release. Use direct PostgreSQL connections or PgBouncer session pooling.
- Under PgBouncer transaction or statement pooling, use the transaction-coupled static API with a caller-owned `NpgsqlTransaction`; do not use session-scoped handles.
- Session-scoped locks have no TTL and no finalizer reclaim. `RenewAsync(...)` returns `true`, `GetExpirationAsync(...)` returns `null`, and the lock is released only when the handle is disposed or `ReleaseAsync()` is called. Always `await using` the handle; an abandoned handle leaks its connection and advisory lock until the provider is disposed. See [Connection-Scoped Locks](#connection-scoped-locks-database-engine).
- `Monitoring = LockMonitoringMode.None` leaves `LostToken` as `CancellationToken.None` and avoids the active connection probe. `Monitor` and `AutoExtend` both opt into connection-death observation; there is no TTL to extend.
- Resource-targeted inspection (`IsLockedAsync(resource)`, `GetLockInfoAsync(resource)`) can see remote holders because the caller supplies the advisory key. Provider-wide enumeration (`ListActiveLocksAsync()`, `GetActiveLocksCountAsync()`) remains local-handle only because `pg_locks` does not expose reversible resource names for the provider namespace once advisory keys are hashed.
- Postgres does not provide an N-holder advisory semaphore; use Redis semaphores or a separate slot-table design when N-holder concurrency is required.
- The provider multiplexes uncontended advisory locks on distinct keys onto a shared physical connection and falls back to a dedicated connection on contention or advisory-key collision. This lowers connection usage in the common case without changing lock semantics.
- Connection-death detection for an idle lock holder is active: monitored handles (`LostToken`) run a periodic bounded-timeout server-side probe whose command timeout catches silently-dropped half-open connections that Npgsql's `StateChange` event alone would miss until the next operation. TCP keepalive is complementary, not redundant: when the provider builds its own data source from `ConnectionString` it defaults `KeepAlive` (30s, see `PostgresDistributedLockOptions.KeepAlive`) unless the connection string already sets one, surfacing dead sockets faster at the transport layer. If you inject your own `DataSource`, set `Keepalive` on it yourself for the tightest detection window; the active monitor still operates regardless.

### Installation

```bash
dotnet add package Headless.DistributedLocks.PostgreSql
```

### Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("Postgres");
        options.KeyPrefix = "distributed-lock:";
    })
);

await using var lease = await lockProvider.AcquireAsync(
    "orders:123",
    new DistributedLockAcquireOptions
    {
        AcquireTimeout = TimeSpan.FromSeconds(10),
        Monitoring = LockMonitoringMode.Monitor,
    },
    ct
);
```

Transaction-coupled locking:

```csharp
await using var connection = await dataSource.OpenConnectionAsync(ct);
await using var transaction = await connection.BeginTransactionAsync(ct);

await PostgresDistributedLock.AcquireWithTransactionAsync(
    PostgresAdvisoryLockKey.FromString("orders:123"),
    transaction,
    ct
);

// mutate protected rows, then commit or rollback to release the lock
await transaction.CommitAsync(ct);
```

### Configuration

```csharp
options.ConnectionString = "..."; // required unless DataSource is set
options.DataSource = dataSource; // preferred when already registered
options.KeyPrefix = "distributed-lock:";
options.PollingFallback = TimeSpan.FromMilliseconds(100);
options.EnablePushWakeup = true;
options.KeepAlive = TimeSpan.FromSeconds(30); // applied only to a provider-built DataSource
```

### Dependencies

- `Headless.DistributedLocks.Core.Database`
- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Npgsql`

### Side Effects

- Registers `IDistributedLock` as singleton.
- Registers `IDistributedReadWriteLock` as singleton.
- Registers Postgres storage, release signal, fencing-token source, `TimeProvider.System`, and `IGuidGenerator` when absent.

---

## Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks, reader-writer locks, and semaphores.

### Problem Solved

Stores lock records directly in Redis with atomic acquire, replace, release, reader-writer transitions, semaphore slots, and fencing-token issuance.

### Key Features

- `RedisDistributedLockStorage` implements `IDistributedLockStorage`.
- `RedisDistributedReadWriteLockStorage` implements `IDistributedReadWriteLockStorage`.
- `RedisDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `UseRedis()` registers Redis-backed mutex, reader-writer lock, and semaphore providers through `AddHeadlessDistributedLocks(...)`.
- Uses `HeadlessRedisScriptsLoader` for atomic Lua script operations.
- Mutex compare-and-swap uses Redis `KEEPTTL`, preserving the existing expiration when `ReplaceIfEqualAsync(..., newTtl: null)` is used.

### Installation

```bash
dotnet add package Headless.DistributedLocks.Redis
```

### Quick Start

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
        options.MaxResourceNameLength = 512;
    });

    setup.UseRedis();
});
```

### Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`.

Redis mutex storage maps each logical lock name to an internal hash-tagged lock key and one no-TTL fence counter key in the same Redis Cluster slot. Redis semaphore storage creates `{resource}:holders` (ZSET of `leaseId → expiry-epoch-ms`) and `fence:{resource}`. Resource names containing `{` or `}` are rejected where storage-owned hash-tags are required.

Reader-writer storage creates `{resource}:writer` (string holding the active writer id or the `:_WRITERWAITING`-suffixed marker) and `{resource}:readers` (HASH of `leaseId → expiry-epoch-ms`) Redis keys internally. Resource names containing `{` or `}` are rejected so the storage-owned Redis cluster hash-tag remains deterministic. The marker TTL is governed by `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30s, validated `0 < ttl <= 5 min`).

### Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `StackExchange.Redis`
- Redis server **6.2+** (semaphore lease extension uses grow-only `ZADD GT`, which never shortens a live holder's TTL).

### Side Effects

- Registers a keyed `HeadlessRedisScriptsLoader` bound to the app's `IConnectionMultiplexer`.
- Registers hosted `IInitializer` warmup for Redis mutex, reader-writer, and semaphore scripts.
- Registers `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core`.

---

## Headless.DistributedLocks.SqlServer

SQL Server `sp_getapplock` provider for mutex and reader-writer distributed locks.

### Problem Solved

Coordinates work across nodes using SQL Server application locks, with native server-side blocking and transaction-coupled locking available for data mutations already protected by a SQL Server transaction.

### Key Features

- `UseSqlServer(...)` registers `IDistributedLock` and `IDistributedReadWriteLock` through `AddHeadlessDistributedLocks(...)`.
- Session-scoped mutex locks use `sp_getapplock` with `@LockMode = 'Exclusive'` and release with `sp_releaseapplock`.
- Reader-writer locks use SQL Server `Shared` and `Exclusive` application-lock modes.
- Mutex handles receive durable SQL `SEQUENCE`-backed `FencingToken` values when fencing is enabled.
- `SqlServerDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` use transaction-owned application locks.
- Resource names longer than SQL Server's 255-character `@Resource` limit are encoded as `sha256:<lowercase-hex>`.

### Design Notes

- Standard provider locks are session-scoped: the holding `SqlConnection` must stay open until release. Do not return that connection to arbitrary pooling code while the lock is held.
- SQL Server blocks waiters inside `sp_getapplock @LockTimeout`; there is no push-notification channel and no provider polling loop for contended acquires. The provider still receives a no-op release signal to satisfy its constructor contract; under server-side blocking that signal is never invoked.
- Session-scoped locks have no TTL. `RenewAsync(...)` returns `true` and `GetExpirationAsync(...)` returns `null`. Consistent with the connection-scoped disposal contract (no GC finalizer reclaim), a leaked, undisposed handle strands its connection, applock, and liveness-probe timer until the provider is disposed — always `await using` the handle.
- Connection-death detection backs the handle's lost token with two signals: the connection's `StateChange` event (clean disconnects) and an active bounded-timeout liveness probe (a periodic `SELECT 1`) that catches a silent half-open connection where `StateChange` alone never fires. This mirrors the intent of the multiplexing-engine providers' `ConnectionMonitor`, which this raw-`SqlConnection` storage cannot reuse directly.
- `IsLockedAsync(...)`, `IsReadLockedAsync(...)`, `IsWriteLockedAsync(...)`, and reader counts inspect SQL Server lock state for a specific resource. `GetLockIdAsync(...)`, `GetLockInfoAsync(...)`, `ListActiveLocksAsync(...)`, and `GetActiveLocksCountAsync(...)` report only handles owned by the current provider instance because SQL Server application locks do not expose Headless lock ids for remote sessions.
- Reader counts are presence-only for remote holders: `APPLOCK_TEST` reports the current lock mode but no holder count, so `GetReaderCountAsync(...)`/`GetLocksCountAsync(...)` count local holders exactly but collapse any number of remote shared readers to `1`. Treat the remote value as held / not-held. (The Postgres provider counts `pg_locks` rows and reports exact cross-process counts — a deliberate per-backend difference.)
- Transaction-coupled locking is the safest primitive for SQL Server data mutations: commit or rollback releases the lock, and no explicit release is issued. The transaction API takes a `string` resource, whereas the Postgres advisory-lock API takes a typed `PostgresAdvisoryLockKey`; the asymmetry is primitive-driven (`sp_getapplock` is string-keyed, `pg_advisory_xact_lock` keys on a `bigint`). Both encode `KeyPrefix + resource` identically to the session provider, so the two APIs mutually exclude on the same logical resource.
- SQL Server does not provide an N-holder semaphore here; use Redis semaphores or a future persistent slot-table design when N-holder concurrency is required.

### Installation

```bash
dotnet add package Headless.DistributedLocks.SqlServer
```

### Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("SqlServer");
        options.KeyPrefix = "distributed-lock:";
    })
);

await using var lease = await lockProvider.AcquireAsync(
    "orders:123",
    new DistributedLockAcquireOptions
    {
        AcquireTimeout = TimeSpan.FromSeconds(10),
        Monitoring = LockMonitoringMode.Monitor,
    },
    ct
);
```

Transaction-coupled locking:

```csharp
await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync(ct);
await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

await SqlServerDistributedLock.AcquireWithTransactionAsync("orders:123", transaction, cancellationToken: ct);

// mutate protected rows, then commit or rollback to release the lock
await transaction.CommitAsync(ct);
```

### Configuration

```csharp
options.ConnectionString = "..."; // required
options.Schema = "dbo"; // fencing sequence schema
options.KeyPrefix = "distributed-lock:";
options.CommandTimeout = TimeSpan.FromSeconds(30);
options.EnableFencing = true;
```

### Dependencies

- `Headless.DistributedLocks.Core.Database`
- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Microsoft.Data.SqlClient`

### Side Effects

- Registers `IDistributedLock` as singleton.
- Registers `IDistributedReadWriteLock` as singleton.
- Registers SQL Server storage, fencing-token source, storage initializer, `TimeProvider.System`, and `IGuidGenerator` when absent. The provider is wired with a no-op release signal (not a polling loop) because SQL Server blocks contended acquires server-side, so the provider's wait loop is unreachable.
- Creates a sanitized SQL `SEQUENCE` for durable fencing when `EnableFencing` is `true`.
