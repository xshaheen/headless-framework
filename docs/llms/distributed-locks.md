---
domain: Distributed Locks
packages: DistributedLocks.Abstractions, DistributedLocks, DistributedLocks.Database, DistributedLocks.InMemory, DistributedLocks.Messaging, DistributedLocks.PostgreSql, DistributedLocks.Redis, DistributedLocks.SqlServer
---

# Distributed Locks

> Provider-agnostic distributed locking with automatic renewal, expiration, explicit release, and pluggable storage backends.

## Orientation

Use `IDistributedLock` when only one worker should own a named resource at a time. `TryAcquireAsync(...)` returns `null` on timeout; `AcquireAsync(...)` throws `LockAcquisitionTimeoutException` on timeout. Rate limiting is out of scope: for attempt quotas shared across replicas, use `IAttemptLimiter` from [Rate Limiting](rate-limiting.md); for per-replica HTTP load shedding, use `Microsoft.AspNetCore.RateLimiting`.

Use `IDistributedReadWriteLock` when concurrent readers are safe and writers need exclusivity. Use `IDistributedSemaphoreProvider.CreateSemaphore(resource, maxCount)` when up to N holders may work concurrently.

| Provider | Mutex | Reader-writer | Semaphore | Transaction-scoped locks |
| --- | --- | --- | --- | --- |
| Redis | Yes | Yes | Yes | No |
| InMemory (one process only) | Yes | Yes | Yes | No |
| PostgreSQL | Yes | Yes | No | Yes |
| SQL Server | Yes | Yes | No | Yes |

## Agent Rules

- Code against `IDistributedLock` from `Headless.DistributedLocks.Abstractions`. Do not inject Redis storage types into application services.
- Use `Headless.DistributedLocks.InMemory` only for tests, local development, or deliberately single-instance apps. It is not a cross-process lock.
- When environments share one Redis, give each host a `Headless:Prefix`. It goes in front of `DistributedLockOptions.KeyPrefix`: a lock on `orders` uses `wt1:distributed-lock:orders`, and the Redis release channel becomes `wt1:distributed-lock:released`. The PostgreSQL and SQL Server providers and the static `SqlServerDistributedLock` helpers ignore it, because their database already separates environments. See [Isolation prefix](utilities.md#isolation-prefix).
- On PostgreSQL and SQL Server, create the fence sequence at deploy time with `resources provision`. A host start only verifies it and fails when it is missing. Code that builds a service provider without starting a host (a console tool, a test) calls `await provider.GetRequiredService<SchemaRunner>().ApplyAsync()` before its first fenced acquire.
- Use `TryAcquireAsync(...)` when timeout is an expected branch. Use `AcquireAsync(...)` when timeout should fail the workflow.
- Use `TryAcquireAllAsync(...)` or `AcquireAllAsync(...)` when one operation must hold several resources. Pass the complete set in one call, so the framework can sort it ordinally, deduplicate it, enforce one timeout budget, and undo a partial acquisition. `IDistributedLock` takes `IEnumerable<string>`, `IDistributedReadWriteLock` takes `IEnumerable<DistributedReadWriteLockRequest>`, and `IDistributedSemaphoreProvider` takes `IEnumerable<DistributedSemaphoreRequest>`.
- Inside a unit of work on PostgreSQL or SQL Server, take every row lock the operation needs in one `unit.TransactionLocks.AcquireAllAsync(...)` or `TryAcquireAllAsync(...)` call. Never sort keys by hand or hand-write `FOR UPDATE` with a timeout. Build each name with `LockKey.For<T>(id)` (for example `LockKey.For<Wallet>(walletId)`). See [Locks inside a unit of work](#locks-inside-a-unit-of-work).
- When a caller needs both read and write locks, pass one mixed `DistributedReadWriteLockRequest` set through a single `AcquireAllAsync(...)`. Never nest `AcquireAllReadAsync(...)` inside `AcquireAllWriteAsync(...)` or the reverse: two such callers can deadlock. Never nest one composite inside another, or acquire one while you already hold an unrelated lock.
- Never pass a composite lease's `Resource` or `LeaseId` to a by-resource API (`IsLockedAsync`, `GetLeaseIdAsync`, `GetLockInfoAsync`, `GetHolderCountAsync`, `RenewAsync(resource, leaseId, ...)`). Those calls report a held set as unlocked instead of failing. Renew or release the composite through its handle.
- A `DistributedSemaphoreRequest`'s `MaxCount` is the semaphore's capacity, not a permit count. There is no all-or-nothing way to take N permits of one semaphore; see [Composite Acquisition](#composite-acquisition).
- On PostgreSQL and SQL Server, a composite over N resources pins N connections for the whole hold. Size the connection pool for your largest composite.
- Bundle per-call settings in `DistributedLockAcquireOptions` (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`, `NotifyWaitersOnRelease`). Omit the argument to use defaults: 20-minute expiry and 30-second acquire timeout. Derive variants with `with` expressions.
- Always `await using` the returned lock while `ReleaseOnDispose` is `true` (the default). Set `ReleaseOnDispose = false` only when ownership is deliberately transferred and the caller releases explicitly.
- Set `Monitoring = LockMonitoringMode.Monitor` when work should observe lease loss through `IDistributedLease.LostToken`. Set `Monitoring = LockMonitoringMode.AutoExtend` only when long work should renew its lease in the background; it implies `Monitor`.
- For a resource no caller ever waits on (every acquirer tries once with `AcquireTimeout = TimeSpan.Zero`), set `NotifyWaitersOnRelease = false` to skip the release wake-up. Any other `AcquireTimeout` with `false` throws `ArgumentException`.
- To wake waiters on other hosts at once on the in-memory or Redis provider, call `setup.UseRedisReleaseSignal()` (`Headless.DistributedLocks.Redis`, no broker) or `setup.UseMessagingReleaseSignal()` (`Headless.DistributedLocks.Messaging`). Without one, waiters on other hosts wake on their polling backoff, even when the host registers `Headless.Messaging`. See [Release Wake-ups](#release-wake-ups).
- Use `GetLeaseIdAsync(resource)` for operational inspection only. It does not renew the lease, and it may return `null` while `IsLockedAsync(resource)` reports a lock. A monitored holder observes `LostToken` or calls `ThrowIfLost()` instead of polling it.
- Synchronous `TryUsingAsync(..., Action ...)` overloads force `LockMonitoringMode.None`, because a synchronous delegate cannot observe a cancellation token.
- Do not use locks or semaphores as rate limiters. A semaphore caps concurrent holders; a rate limiter caps throughput per time window. Use `IAttemptLimiter` ([Rate Limiting](rate-limiting.md)) for exact per-subject quotas across replicas, or `Microsoft.AspNetCore.RateLimiting` for per-replica HTTP load shedding.
- For stale-write rejection, use `IDistributedLease.FencingToken` (a `LockFencingToken`) when the backend supplies it. The protected resource persists `token.Value` as its high-water mark and rejects any write whose token is less than or equal to it. Never compare tokens for equality, never compare one with a Fencing lease generation, and never use `LeaseId` as the fence. See [Locks and Fencing Tokens](#locks-and-fencing-tokens).
- Classify the use case as efficiency or correctness before you choose a backend. Redis locks are efficiency locks, not transaction-coupled correctness locks.
- Use `Headless.DistributedLocks.PostgreSql` when the protected resource is already in PostgreSQL or when transaction-coupled advisory locks are required. Session-scoped PostgreSQL locks need direct connections or PgBouncer session pooling.
- Dispose every PostgreSQL and SQL Server lock handle (`await using`) or call `ReleaseAsync()`. These locks have no TTL and no finalizer: an abandoned handle holds its connection and lock until the provider is disposed.
- `Headless.Messaging` resolves a keyed `IDistributedLock` registered under `"headless.messaging"`. An un-keyed app lock provider is not used by message retry processors.
- The Tus file-lock adapter (`Headless.Tus.DistributedLocks`) auto-extends its lease but cannot observe `LostToken`, so a request whose lease is lost keeps writing until it ends. See [Tus](tus.md#concurrent-patch-safety).

## How this differs from fenced leases and idempotency

A distributed lock answers "may this process run now?" for a live in-process handle. Use [Fencing](fencing.md) when an external executor must carry ownership or the lease must outlive a process. Use [Idempotency](idempotency.md) when a retry must replay a stored result. Use [Coordination](coordination.md) to learn which nodes are alive. See [Choosing a coordination primitive](fencing.md#choosing-a-coordination-primitive).

## Core Concepts

A lock coordinates ownership of a string resource such as `order:123`. The lock store owns acquisition and release; the protected resource still owns data integrity. Treat a lock handle as a lease that can expire.

### Efficiency Locks

Efficiency locks avoid duplicate work, such as two nodes generating the same report. An occasional violation costs compute or a duplicate side effect, not corrupted state. Redis-backed locks fit this category.

### Correctness Locks

Correctness locks protect invariants where a stale owner could corrupt data. TTL-based Redis locks cannot prove correctness through process pauses, partitions, or clock skew. For correctness, use a transaction-coupled backend, or make the protected resource reject stale writes.

### Locks and Fencing Tokens

- **The lock** is the lock store's claim that one holder may run now. The holder can lose it without knowing: a GC pause, a network partition, or clock skew can outlast the lease while the lock store hands the resource to someone else. A lock alone therefore never protects data. `LostToken` and `RenewAsync` narrow that window, but a paused process cannot observe either before its next write.
- **The fencing token** is a `LockFencingToken` issued with each grant that increases for the resource. The protected resource (the row, file, or downstream service being written) checks it on every write, records the highest token it accepted, and rejects any write whose token is less than or equal to that mark.

1. Client 1 acquires the lock and receives token 33.
2. Client 1 pauses (for example a long GC), and its lease expires.
3. Client 2 acquires the lock, receives token 34, and writes. The protected resource records 34.
4. Client 1 wakes, still believing it holds the lock, and writes with token 33.
5. The protected resource sees 33 ≤ 34 and rejects the write.

Tokens increase per resource but are not consecutive: PostgreSQL and SQL Server draw them from one shared sequence, so a resource can see 12, then 57.

| Situation | Use |
| --- | --- |
| A duplicate run only costs compute (a cache rebuild, a report, a startup scan that converges) | A lock alone. The work must be safe to run twice. |
| The protected state lives in the same database as the lock, and the write happens in one transaction | A transaction-coupled lock (`unit.TransactionLocks`, or the PostgreSQL and SQL Server transaction-scoped APIs). The lock ends with the transaction that writes. |
| The protected state is elsewhere, or the work spans transactions, and a stale write would corrupt it | A lock plus a fencing check at the protected resource. |

`LeaseId` is the opaque ownership token for renew and release. Never use it as a fence.

Lock tokens and [Fencing](fencing.md) lease generations (`FencedLease.Generation`) come from unrelated sequences. `LockFencingToken` compares only with another `LockFencingToken` and has no conversion to or from `long`, so `lease.FencingToken > fencedLease.Generation` does not compile. Read `.Value` to persist a token and `new LockFencingToken(value)` to read it back. A resource guarded by both must record the highest token and the highest generation in separate fields.

| Provider | Tokens issued for | Guarantee |
| --- | --- | --- |
| Redis | Mutex handles and semaphore slots | Increasing while Redis keeps the fence key. Eviction, data loss, or failover to a replica that missed recent writes can reset it or issue a value again, so fencing is best-effort. |
| PostgreSQL, SQL Server | Mutex handles and reader-writer write handles | Durable and increasing per resource, with gaps. The token is drawn in the statement that grants the lock, so a session that dies before the grant returns never holds a token. |
| InMemory | Mutex handles and semaphore slots | Increasing per resource within the process, with gaps; resets on restart. |

Shared (read) handles never carry a token, and Redis and InMemory reader-writer handles carry none in either mode. A multi-resource composite lease reports `null`.

A failed Redis acquire does not advance the counter. The fence key has no TTL; avoid `allkeys-*` eviction policies when you rely on Redis fencing. Redis replicates asynchronously, so a failover to a replica that missed a recent acquire can let a second caller acquire the same lock while the first still holds it, and receive a fence value already issued.

### Composite Acquisition

All three primitives support all-or-nothing multi-resource acquisition:

| Primitive | Entry points | Request element | Composite `Resource` |
| --- | --- | --- | --- |
| Mutex (`IDistributedLock`) | `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` | `string` | `a+b` |
| Reader-writer (`IDistributedReadWriteLock`) | `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)`, plus the uniform-mode `TryAcquireAllReadAsync(...)` / `AcquireAllReadAsync(...)` / `TryAcquireAllWriteAsync(...)` / `AcquireAllWriteAsync(...)` | `DistributedReadWriteLockRequest(string Resource, DistributedLockMode Mode)`; the uniform-mode overloads take `string` | `r:a+w:b` |
| Semaphore (`IDistributedSemaphoreProvider`) | `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` | `DistributedSemaphoreRequest(string Resource, int MaxCount)` | `a+b` |

**Ordering.** Each call validates and canonicalizes the complete input before the first provider call, then acquires in `StringComparer.Ordinal` order of the resource name alone. Mode and capacity never take part in the sort, so mutex, reader-writer, and semaphore composites over overlapping names order consistently against each other. The ordering holds only when the whole set goes through one call. Sequential `AcquireAsync` calls, nested composites, or a composite taken while you hold another lock can still deadlock.

**Canonicalization.**

- **Mutex:** duplicates are removed.
- **Reader-writer:** a resource requested as both `Read` and `Write` collapses to one `Write` child. `DistributedLockMode.None` (the `default`) is rejected.
- **Semaphore:** naming one resource twice with different `MaxCount` values throws `ArgumentException` naming the resource, before any `CreateSemaphore` call. Identical duplicates are removed, and a composite takes exactly one slot of each named semaphore.

**Timeout.** The acquire timeout is one budget for the whole set. `TimeSpan.Zero` still gives every child one non-blocking attempt. A composite timeout's `LockAcquisitionTimeoutException.Resource` is the joined set, not the resource that blocked; do not split it back into names.

**Failure.** This is compensating coordination, not a transaction. If a later child cannot be acquired, is lost, or faults, every child already held is released in reverse order. While a later child is pending, finite-TTL children are renewed at half their TTL, capped at one minute, unless `AutoExtend` owns renewal. A reader-writer read child cannot hold an infinite lease (its TTL is clamped to `DefaultTimeUntilExpires`), so a set with any read child renews during formation even when `TimeUntilExpires` is infinite.

**Cleanup errors.** A cleanup failure throws `LockCleanupFailedException`, which derives from `DistributedLockException` and carries every failure on `Failures`. Treat a resource whose release failed as possibly still held until its TTL ends. `DisposeAsync` never throws; it logs through the provider's `Logger`. Call `ReleaseAsync()` when you must observe the cleanup outcome.

**Renewal.** Renewing a composite throws `LockHandleLostException` naming the lost child instead of returning `false`, because its siblings are still held and must be released. A composite returns `false` only when it was already released.

**Result.** When canonicalization leaves one resource, you get the provider's own lease, with its real `LeaseId` and `FencingToken`. A multi-resource lease has a synthetic `LeaseId`, a joined diagnostic `Resource`, and a `null` `FencingToken`. Its `LostToken` fires when any child is lost, and renewal and release cover every child. `ReleaseOnDispose = false` suppresses dispose-time release for the whole set but still permits `ReleaseAsync()`. The joined `Resource` and synthetic `LeaseId` exist in no backend.

**Not supported.**

- Sets that mix a mutex, a reader-writer lock, and a semaphore in one call.
- Upgradeable reads.
- A scalar fencing token for a multi-resource lease. Fence per resource, or use a single-resource set, which keeps the child's token.
- **N permits of one semaphore, all or nothing.** Repeated `AcquireAsync(...)` calls are not a safe workaround: two callers that each want 2 permits of a capacity-2 semaphore can each take 1 and stall. A composite cannot fix it either, because ordering cannot separate callers contending for one resource. No safe primitive exists today. Model each permit as its own named resource and compose over those names, or use a mutex.

### Lease Lifecycle Monitoring

Monitoring is opt-in per acquire through `DistributedLockAcquireOptions.Monitoring` (`LockMonitoringMode`). With `None` (the default), `LostToken` is `CancellationToken.None` and `IDistributedLease.CanObserveLoss` is `false`. With `Monitor`, a background monitor cancels `LostToken` when validation finds the stored lease id changed or gone, or when the lease's loss deadline passes. With `AutoExtend`, it also renews at `DistributedLockOptions.AutoExtensionCadenceFraction` of the TTL. `Monitor` alone validates at `PollingCadenceFraction` and never renews.

- **Loss deadline.** `LostToken` cancels at `send + ttl - margin`, where `send` is when the winning acquire (or the last successful renew) was sent, and `margin` is the smaller of a tenth of the TTL and the per-probe storage deadline (at most 5 seconds). A validation that finds the lease held does not move the deadline, so in `Monitor` mode a lease that is never renewed is reported lost one margin before its TTL ends. A store that stays unreachable surfaces as loss before the lease can have expired. A renew that makes the lease non-expiring turns the deadline off. A mutex renew with `Timeout.InfiniteTimeSpan` keeps the current expiry, so it leaves the deadline where it was.
- **Extend rules.** On every provider, a shorter extend never moves a live lease's expiry earlier, an infinite reader stays infinite, and extending a lease whose expiry has passed returns `false`. A positive TTL under 1 ms is rounded up to 1 ms. Manual and automatic renews on one handle run one at a time.
- If the monitor loop faults, `LostToken` is cancelled.
- `LostToken` reports loss only. An explicit `ReleaseAsync()` or dispose stops monitoring without cancelling it, so callbacks registered on it do not fire on the success path of `await using`.
- Monitoring or `AutoExtend` with `TimeUntilExpires = Timeout.InfiniteTimeSpan` throws `ArgumentException` (`ParamName = "timeUntilExpires"`).
- Monitor state transitions (`Held`, `Renewed`, `Lost`, `Unknown`) appear only as the `LeaseMonitorStateChanged` log event (`EventId = 30`, fields `Resource`, `LeaseId`, `PreviousState`, `NextState`).

These signals narrow stale-work windows. They do not turn Redis locks into correctness locks; fence protected writes with `FencingToken`.

### Connection-Scoped Locks (Database Engine)

The PostgreSQL and SQL Server providers store no lease record with a TTL. The lock exists exactly as long as the holding database session.

- **Disposal.** The lock is released only when its handle is disposed or `ReleaseAsync()` is called. No timeout or finalizer frees it, so an abandoned handle holds its connection and lock until the provider is disposed. `GetExpirationAsync(...)` returns `null`. `RenewAsync(...)` extends nothing: it returns `true` while the lock is held and `false` after release, after an out-of-band `ReleaseAsync(resource, leaseId)`, for a lease id this provider instance does not hold, or once connection loss has been observed. It answers from local state, so without `Monitor` a connection that died silently can still renew as `true`.
- **Connection-death detection.** On a monitored handle, a periodic bounded-timeout server-side probe (PostgreSQL: about once a minute with a 10-second command timeout; SQL Server: `ConnectionProbeInterval`) plus the connection's `StateChange` event cancel `LostToken` when the session dies, including a silent half-open connection. TCP keepalive (`PostgreSqlDistributedLockOptions.KeepAlive`) is complementary. `Monitoring = None` runs no probe.
- **Connection sharing.** PostgreSQL shares one physical connection among uncontended locks on distinct keys and uses a dedicated connection on contention or an advisory-key collision. Lock semantics do not change, and a composite still pins one connection per contended child.
- **A failed release ends the session.** When an unlock fails, or an acquire fails and its cleanup throws `LockCleanupFailedException`, the lock may still be held on that session. The provider then clears the driver pool and disposes the connection so the session closes and frees the lock. The failure still surfaces from `ReleaseAsync()`. Clearing the pool drops its idle connections too: on PostgreSQL the whole data source, including a `DataSource` you shared with the application or the shared connection-string pool under the parameterless `UsePostgreSql()`; on SQL Server the connection-string pool. On a shared PostgreSQL connection, the failed lock stays held until the last other lock on that connection releases, and the connection takes no new locks.

### Release Wake-ups

Every release on the in-memory and Redis providers is announced through the registered `IDistributedLockReleaseSignal`, so waiters retry at once instead of on their next backoff poll. The signal is a best-effort hint; polling and lease expiry remain the correctness floor. A release with `NotifyWaitersOnRelease = false` announces nothing.

- **Default.** `Headless.DistributedLocks` registers an in-process signal (with `TryAdd`) that wakes waiters in the releasing process only. On the Redis provider it logs `CrossHostReleaseSignalAbsent` (`EventId = 16`, Information) once.
- **Bus.** `setup.UseMessagingReleaseSignal()` from `Headless.DistributedLocks.Messaging` publishes `DistributedLockReleased` through `IBus` with `DeliveryMode.Direct`. See [Headless.DistributedLocks.Messaging](#headlessdistributedlocksmessaging).
- **Redis pub/sub.** `setup.UseRedisReleaseSignal()` from `Headless.DistributedLocks.Redis` publishes each release on the channel `{KeyPrefix}released` (`DistributedLockOptions.KeyPrefix`) and subscribes every host. It needs a registered `IConnectionMultiplexer` and no messaging, so it works on transports without every-instance delivery, such as AWS SNS/SQS. A publish failure logs `RedisLockReleasePublishFailed` (`EventId = 40`). The subscription starts in the background, retries with a jittered back-off while Redis is unreachable (`RedisLockReleaseSubscribeRetrying`, `EventId = 41`), and is re-established after a reconnect; the host never waits for it. Hosts with different key prefixes on one Redis server do not wake each other.
- Whichever `Use*ReleaseSignal()` call runs last wins. A signal registered before `AddHeadlessDistributedLocks(...)` wins over the default. A replacement signal also owns local wake-ups: the Redis and bus transports deliver a release back to the host that sent it.
- A release never waits for the transport, and a publish failure never fails the release.
- Delivery is at most once and reaches only connected subscribers. A waiter that misses a release wakes on its next backoff poll (capped at 3 seconds plus up to 25% jitter).
- A mutex release wakes one waiter and nudges that resource's lease monitors to validate early. A semaphore release wakes one slot waiter. A reader-writer release wakes one blocked acquirer.

To use another transport, implement `IDistributedLockReleaseSignal` and register it before `AddHeadlessDistributedLocks(...)`, or from `setup.RegisterCrossCuttingExtension(...)` with `services.Replace(...)`. Its `Announce(resource, leaseId)` runs on the release path, so it must not throw or wait for I/O. If it throws anyway, the release still completes: the provider logs `ReleaseSignalAnnounceFailed` (`EventId = 25`, Warning) with the exception, and waiters wake on their polling backoff. On the receiving side, resolve `IEnumerable<IDistributedLockReleaseListener>` and call `OnReleased(resource, leaseId)` on each; it only pulses in-process events, so the transport may call it on its delivery thread.

### Observability

Metrics and traces use one instrumentation name, `Headless.DistributedLocks` (`DistributedLocksDiagnostics.SourceName`). Register with `tracing.AddDistributedLocksInstrumentation()` and `metrics.AddDistributedLocksInstrumentation()` (they need only `OpenTelemetry.Api`), or with `AddMeter(DistributedLocksDiagnostics.SourceName)` and `AddSource(DistributedLocksDiagnostics.SourceName)`. Acquire paths start activities on that source.

| Instrument | Kind | Unit | Meaning |
| --- | --- | --- | --- |
| `headless.lock.failed` | Counter (`int`) | count | A mutex or reader-writer acquire failed or timed out. Tagged `headless.lock.reason`. |
| `headless.lock.wait.time` | Histogram (`double`) | milliseconds | Time spent waiting to acquire a lock, once per attempt, success or failure. |
| `headless.semaphore.failed` | Counter (`int`) | count | A semaphore slot acquire failed or timed out. Tagged `headless.semaphore.reason`. |
| `headless.semaphore.wait.time` | Histogram (`double`) | milliseconds | Time spent waiting for a semaphore slot, once per attempt, success or failure. |

The reason tag has two values, exposed as `DistributedLockFailureReasons.Contended` and `DistributedLockFailureReasons.Stalled`:

- `contended`: every expected not-acquired outcome (held by another holder, acquire timeout elapsed, a swallowed transient storage error). After a transient error during a mutex acquire, the lock deletes its own lease id if the write landed. If every cleanup also fails, the key stays held until its TTL, or until removed by hand with `TimeUntilExpires = Timeout.InfiniteTimeSpan`.
- `stalled`: a try-once acquire (`AcquireTimeout = TimeSpan.Zero`) whose single storage attempt hit the internal safety deadline, even when the caller's token never fires. The acquire returns `null` and logs `TryOnceSafetyDeadlineFired` (`EventId = 24`, Warning, fields `Resource`, `LeaseId`, `Duration`). Alert on `rate(headless.lock.failed{headless.lock.reason="stalled"})` to detect lock-store degradation.
- A try-once acquire that finds the resource held logs no Warning: the mutex logs `FailedToAcquireLock` (`EventId = 7`) at Debug, and the reader-writer and semaphore log nothing. A mutex try-once whose storage call throws logs `FailedToAcquireLockAfter` (`EventId = 13`) at Warning, as a blocking acquire does when its `AcquireTimeout` elapses.

## Reader-Writer Locks

Use `IDistributedReadWriteLock` for read-heavy resources where readers may proceed concurrently and writers run exclusively. Read and write acquires return the same `IDistributedLease` handle as mutex locks.

- **Writer preference (Redis and InMemory).** When a writer queues behind active readers, new readers are blocked until it acquires, so steady read traffic cannot starve the writer. The writer-waiting marker expires after `DistributedLockOptions.WriterWaitingMarkerTtl` (default 30 seconds, valid `0 < ttl <= 5 min`), so an abandoned writer cannot block readers for the whole lease window. Set it above your worst-case writer acquire wait. A writer that times out or is cancelled clears its marker.
- **AutoExtend readers lose their lease when a writer queues.** The reader's extend is refused while a writer waits, and its `LostToken` fires. A reader that must continue reacquires after the writer drains.
- On Redis, resource names containing `{` or `}` are rejected. On InMemory, reader-writer lease ids containing `:` are rejected.
- A reader entry carries its own expiry, so a crashed reader never strands a queued writer past its lease.
- An acquire that was cancelled or failed after the store may have committed it is released, so a lost reply never holds the resource.

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

**Verify:** see [Headless.DistributedLocks.Redis](#headlessdistributedlocksredis).

### Reader-Writer Composite Acquisition

```csharp
Task<IDistributedLease?> TryAcquireAllAsync(IEnumerable<DistributedReadWriteLockRequest> requests, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)
Task<IDistributedLease>  AcquireAllAsync(IEnumerable<DistributedReadWriteLockRequest> requests, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)
```

`DistributedReadWriteLockRequest(string Resource, DistributedLockMode Mode)` takes `DistributedLockMode.Read` or `Write`. `None` (the `default`) is rejected, so an uninitialized request never becomes a silent read lock. The uniform-mode overloads `TryAcquireAllReadAsync(IEnumerable<string>, ...)`, `AcquireAllReadAsync(...)`, `TryAcquireAllWriteAsync(...)`, and `AcquireAllWriteAsync(...)` take plain names.

```csharp
await using var lease = await readerWriterLocks.AcquireAllAsync(
    [
        new DistributedReadWriteLockRequest("catalog:prices", DistributedLockMode.Read),
        new DistributedReadWriteLockRequest("catalog:inventory", DistributedLockMode.Write),
    ],
    new DistributedLockAcquireOptions
    {
        TimeUntilExpires = TimeSpan.FromMinutes(2),
        AcquireTimeout = TimeSpan.FromSeconds(10),
    },
    ct
);
```

**Pass reads and writes in one mixed set.** Two nested composites deadlock:

- Caller X: `AcquireAllReadAsync(["a"])`, then `AcquireAllWriteAsync(["b"])`. It holds read-`a` and waits on write-`b`.
- Caller Y: `AcquireAllReadAsync(["b"])`, then `AcquireAllWriteAsync(["a"])`. It holds read-`b` and waits on write-`a`.

With one mixed set each, both callers order `a` before `b`, and there is no cycle. Use the uniform-mode overloads only when the whole set is one mode.

The composite `Resource` encodes the mode (`r:a+w:b`). Never pass it or the composite `LeaseId` to `IsReadLockedAsync(resource)`, `IsWriteLockedAsync(resource)`, `GetReaderCountAsync(resource)`, or another by-resource API.

## Semaphores

Use `IDistributedSemaphoreProvider` when a resource may have N concurrent holders. `CreateSemaphore(resource, maxCount)` binds the capacity to the returned semaphore. Every caller must use the same `maxCount` for one distributed resource; mixed counts are undefined. A slot returns the same `IDistributedLease` handle as a mutex: `ReleaseAsync()`, `RenewAsync(...)`, `LostToken`, `LockMonitoringMode.Monitor`, `LockMonitoringMode.AutoExtend`, and `FencingToken` all work.

On Redis, holders expire by the Redis server clock, and each slot grant issues a fencing token as a mutex grant does. A semaphore release wakes waiters through the same release signal as a mutex release.

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

### Semaphore Composite Acquisition

Composite acquisition is on `IDistributedSemaphoreProvider`, not on `IDistributedSemaphore`, because a semaphore instance binds one resource at creation:

```csharp
Task<IDistributedLease?> TryAcquireAllAsync(IEnumerable<DistributedSemaphoreRequest> requests, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)
Task<IDistributedLease>  AcquireAllAsync(IEnumerable<DistributedSemaphoreRequest> requests, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)
```

Each `DistributedSemaphoreRequest(string Resource, int MaxCount)` carries its own capacity, so one composite can span differently sized semaphores.

```csharp
await using var lease = await semaphoreProvider.AcquireAllAsync(
    [
        new DistributedSemaphoreRequest("downstream:billing-api", MaxCount: 5),
        new DistributedSemaphoreRequest("downstream:ledger-api", MaxCount: 2),
    ],
    new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(2) },
    ct
);
```

- A composite takes exactly one slot of each named semaphore. Duplicate requests collapse to one; they do not take two permits. See the N-permit limit in [Composite Acquisition](#composite-acquisition).
- A slot always has a finite expiry, so `TimeUntilExpires = Timeout.InfiniteTimeSpan` is rejected whatever the `Monitoring` mode, and held slots are renewed during formation unless `AutoExtend` owns renewal.
- The composite's `FencingToken` is `null` even though each slot carries one. A set that canonicalizes to one resource returns the slot lease with its real token.
- Semaphore composites exist wherever semaphores do: Redis and InMemory.

## Choosing a Provider

Use InMemory when all contenders are inside one process. Use Redis for efficiency locks (mutex, reader-writer, or semaphore). Use PostgreSQL or SQL Server when the protected state already lives there, or when transaction-coupled locks are the right primitive. Never use a lock for a correctness invariant without stale-write rejection through `FencingToken` or transaction-coupled locking.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.DistributedLocks.InMemory` | Tests, local development, or single-instance apps need the real lock abstractions without Redis. | More than one process, node, container, or app instance can contend for the same resource. | No infrastructure; lock and fencing state disappear with the process. |
| `Headless.DistributedLocks.PostgreSql` | You want PostgreSQL advisory mutexes or reader-writer locks, durable sequence fencing, or transaction-coupled locks. | You need semaphores, PgBouncer transaction or statement pooling for session-scoped locks, or no PostgreSQL dependency. | No TTL: the handle must be disposed, and a composite over N resources pins N connections. Connection death is detected actively when monitored. |
| `Headless.DistributedLocks.Redis` | You want Redis-backed efficiency locks, reader-writer locks, or N-holder semaphores. | You need durable transaction-coupled fencing. | Requires `IConnectionMultiplexer`; fencing is best-effort. |
| `Headless.DistributedLocks.SqlServer` | You want SQL Server application locks, durable sequence fencing, or transaction-coupled locks. | You need semaphores, upgradeable reader-writer locks, or no SQL Server dependency. | No TTL; a composite over N resources pins N connections; a contended session-scoped acquire polls at about 100 ms instead of blocking inside SQL Server. |

---

## Headless.DistributedLocks.Abstractions

Defines the public distributed-lock contracts.

### API and behavior

- `IDistributedLock` with `TryAcquireAsync(...)` and `AcquireAsync(...)`. The composites (`TryAcquireAllAsync(...)`, `AcquireAllAsync(...)`) and `TryUsingAsync(...)` ship in `Headless.DistributedLocks`.
- `IDistributedReadWriteLock` with `AcquireReadLockAsync(...)`, `TryAcquireReadLockAsync(...)`, `AcquireWriteLockAsync(...)`, and `TryAcquireWriteLockAsync(...)`.
- `DistributedReadWriteLockRequest(string Resource, DistributedLockMode Mode)` and `DistributedLockMode` (`None = 0`, `Read = 1`, `Write = 2`).
- `IDistributedSemaphoreProvider` and `IDistributedSemaphore`, and `DistributedSemaphoreRequest(string Resource, int MaxCount)`, which carries no permit count.
- `IDistributedLease` with `LeaseId`, nullable `FencingToken` (`LockFencingToken?`), `LostToken`, `CanObserveLoss`, `IsLost`, `ThrowIfLost()`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `DistributedLockException` and its subtypes `LockAcquisitionTimeoutException`, `LockHandleLostException`, `LockCleanupFailedException`, and `DistributedLockDeadlockException`. None derives from `TimeoutException`.
- `LockKey.For<T>(id)` builds `"{typeof(T).Name}:{id}"` from an entity type and a `string`, `Guid` (`"D"` format), or `long` id (an `int` widens; invariant culture). It returns a plain `string` for `IDistributedLock`, the composites, and `unit.TransactionLocks`. Two same-named types in different namespaces share names, which only adds contention. Renaming the type changes the name, so during a rolling deployment old and new nodes do not exclude each other on that entity.
- `unit.TransactionLocks` (`UnitOfWorkTransactionLocks`) and its feature contract `IUnitOfWorkTransactionLocks`, implemented by the PostgreSQL and SQL Server providers. See [Locks inside a unit of work](#locks-inside-a-unit-of-work).
- Inspection: `GetLeaseIdAsync(resource)`, `GetLockInfoAsync(resource)`, `ListActiveLocksAsync()`, `GetActiveLocksCountAsync()`, and `GetExpirationAsync(resource)`. A `LeaseId` may be `null` when the backend sees the lock but not the holder, and list and count results are limited to what the backend can enumerate.

### Design constraints

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`, with no stronger guarantee.
- A custom `IDistributedLock`, `IDistributedReadWriteLock`, or `IDistributedSemaphoreProvider` implements `IDistributedLockEnvironment`: `TimeProvider`, `Logger`, `DefaultTimeUntilExpires`, and `DefaultAcquireTimeout`. The composites read all four, and use that `TimeProvider` for deadlines, so expose the clock your own acquisition uses. Preserve ordinal resource identity: if the backend aliases ordinal-distinct names (for example by case folding), reject non-canonical names or require callers to canonicalize them before a composite call.
- `ReleaseOnDispose = false` prevents dispose-time release but not explicit `ReleaseAsync(...)`, composites included.
- `LostToken` is a signal. Your code decides whether to stop, compensate, or throw `LockHandleLostException`; `ThrowIfLost()` is the common fail-stop check.
- `TimeUntilExpires = null` uses the provider default, 20 minutes on the built-in providers, so `null` is valid with `AutoExtend`. `Timeout.InfiniteTimeSpan` is not.

### Install

```bash
dotnet add package Headless.DistributedLocks.Abstractions
```

### Setup and use

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

```csharp
await using var lease = await readerWriterLocks.AcquireAllAsync(
    [
        new DistributedReadWriteLockRequest("catalog:prices", DistributedLockMode.Read),
        new DistributedReadWriteLockRequest("catalog:inventory", DistributedLockMode.Write),
    ],
    new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(10) },
    ct
);
```

```csharp
await using var slots = await semaphoreProvider.AcquireAllAsync(
    [
        new DistributedSemaphoreRequest("downstream:billing-api", MaxCount: 5),
        new DistributedSemaphoreRequest("downstream:ledger-api", MaxCount: 2),
    ],
    new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(2) },
    ct
);
```

**Verify:** in a test, a second `TryAcquireAsync` on a held resource with `AcquireTimeout = TimeSpan.Zero` returns `null`, and succeeds after the first lease is disposed.

### Configuration

None.

### Runtime behavior

None.

---

## Headless.DistributedLocks

The lock implementations, the composites, and the registration entry point.

### API and behavior

- `AddHeadlessDistributedLocks(...)` is the single registration entry point and returns the `IServiceCollection`. Provider packages add `Use...` methods on `HeadlessDistributedLocksSetupBuilder`.
- Composite acquisition over any provider, as extension methods on the Abstractions contracts; reference this package to use them. See [Composite Acquisition](#composite-acquisition).
- `TryUsingAsync(resource, work, ...)` acquires, runs the work, and releases. Prefer it to a manual try/finally for simple guarded execution.
- `DistributedLockOptions` configures the key prefix, resource name length, waiter limits, and lease-monitor cadence.
- `IDistributedLockReleaseSignal` and `IDistributedLockReleaseListener`. See [Release Wake-ups](#release-wake-ups).
- `HeadlessDistributedLocksSetupBuilder.RegisterCrossCuttingExtension(Action<IServiceCollection>)` runs a registration after the provider's, without counting as a provider.
- For a custom storage provider: `IDistributedLockStorage`, `IDistributedReadWriteLockStorage`, `IDistributedSemaphoreStorage`, and the `IDistributedLocksOptionsExtension` setup hook. See [For store authors](#for-store-authors).

### Design constraints

- The package references no messaging assembly. Without a release transport, a release wakes waiters in its own process only.
- A try-once acquire (`AcquireTimeout = TimeSpan.Zero`) makes one storage attempt under an internal safety deadline; see [Observability](#observability) for the `stalled` signal. This applies to mutex, reader-writer, and semaphore acquires.
- Dispose waits up to `DisposeTimeout` for the release to finish when storage is unreachable, then returns while the release keeps trying in the background; the lease's TTL is the backstop.

### Install

```bash
dotnet add package Headless.DistributedLocks
```

### Setup and use

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
        options.MaxResourceNameLength = 512;
    });

    setup.UseRedis(); // from Headless.DistributedLocks.Redis
    setup.UseMessagingReleaseSignal(); // optional, from Headless.DistributedLocks.Messaging
});

builder.Services.AddHeadlessMessaging(setup =>
{
    // setup.Use... storage and transport providers
});
```

**Verify:** see the provider's Verify step. An invalid `DistributedLockOptions` value fails options validation at host start.

### Configuration

| `DistributedLockOptions` | Default | Notes |
| --- | --- | --- |
| `KeyPrefix` | `distributed-lock:` | Prefix of every lock key and of the Redis release channel. |
| `MaxResourceNameLength` | 1024 | |
| `MaxConcurrentWaitingResources` | 10,000 | Distinct resources with waiters. |
| `MaxWaitersPerResource` | 1,000 | |
| `PollingCadenceFraction` | 0.5 | Validation cadence under `Monitor`, as a fraction of the TTL. Tune down to 0.1 for faster loss detection at the cost of more storage polls. |
| `AutoExtensionCadenceFraction` | 1/3 | Renewal cadence under `AutoExtend`. At most 0.5, so at least two renewals fit in a lease. |
| `WriterWaitingMarkerTtl` | 30 seconds | Reader-writer only. Valid `0 < ttl <= 5 min`. |
| `DisposeTimeout` | 10 seconds | How long dispose waits for a release when storage is unreachable. Valid up to 5 minutes. |

```csharp
options.KeyPrefix = "distributed-lock:";
options.MaxResourceNameLength = 512;
options.MaxConcurrentWaitingResources = 10_000;
options.MaxWaitersPerResource = 1_000;
options.PollingCadenceFraction = 0.5;
options.AutoExtensionCadenceFraction = 1.0 / 3.0;
```

`DistributedLocksStorageOptions.Schema` (default `"headless"`, the schema every Headless feature shares) names the database schema that holds the fence sequence. Set it on the setup builder, not on a provider:

```csharp
services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "app_locks");
    setup.UsePostgreSql(connectionString); // or UseSqlServer(...)
});
```

`setup.ConfigureStorage(configuration)` binds the same options; pass the `Headless:DistributedLocks:Storage` section to bind `Headless:DistributedLocks:Storage:Schema`. The provider validates the schema at startup against its own identifier rules, so a name valid on one engine can fail on the other. Redis and InMemory ignore it.

Override expiration, acquire timeout, monitoring, and dispose behavior per call with `DistributedLockAcquireOptions`:

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

Use `AutoExtend` when the protected work can outlast the initial TTL:

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

The Messaging retry processor uses this shape: a try-once acquire with a lease equal to its polling interval and `AutoExtend`, treating `LostToken` as "stop picking up new work", not "abort work in flight". See [Distributed Lock Integration](messaging/retries.md#distributed-lock-integration).

### Runtime behavior

- Registers exactly one provider, chosen on the builder.
- Redis and InMemory register `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider`. PostgreSQL and SQL Server register `IDistributedLock` and `IDistributedReadWriteLock`.
- Registers `TimeProvider.System` and `IGuidGenerator` when absent.
- Registers the in-process `IDistributedLockReleaseSignal` when none is registered, and each provider as an `IDistributedLockReleaseListener`.
- Applies cross-cutting extensions after the provider's registration.

### For store authors

Every storage method must be safe to call concurrently. Keys and resources arrive already scoped by `KeyPrefix`. Cancellation does not prove a write did not land, so the provider cleans up after a cancelled acquire; your store only has to make each operation atomic. A provider-agnostic lock also implements `IDistributedLockEnvironment`; see [Design constraints](#design-constraints).

**Mutex storage: `IDistributedLockStorage`.**

| Member | What it must do |
| --- | --- |
| `InsertAsync(key, leaseId, ttl, ct)` | Atomically create `key` with `leaseId` only when it does not exist (set-if-absent). A `null` or `Timeout.InfiniteTimeSpan` `ttl` stores no expiry. Return `DistributedLockAcquireResult(Acquired, FencingToken)`; issue a token only when it increases per key, and never advance it on a failed insert. |
| `ReplaceIfEqualAsync(key, expectedId, newId, newTtl, ct)` | Atomically replace the value only when it equals `expectedId` (renewal). A `null` `newTtl` keeps the current expiry. Return `false` when the value differs. |
| `RemoveIfEqualAsync(key, expectedId, ct)` | Atomically delete `key` only when its value equals `expectedId` (release). Return `false` when it differs. |
| `GetExpirationAsync(key, ct)` | Remaining TTL, or `null` when the key is absent or has no expiry. |
| `ExistsAsync(key, ct)`, `GetAsync(key, ct)` | Whether the key exists; its lease id or `null`. |
| `GetAllByPrefixAsync(prefix, ct)`, `GetAllWithExpirationByPrefixAsync(prefix, ct)`, `GetCountAsync(prefix, ct)` | Inspection by key prefix; an empty prefix means every key in scope. |

To wire it, build the public `DistributedLock` over your storage. Its constructor takes `IDistributedLockStorage`, `IDistributedLockReleaseSignal`, `DistributedLockOptions`, `IGuidGenerator`, `TimeProvider`, and `ILogger<DistributedLock>`. Register it as a singleton for both `IDistributedLock` and `IDistributedLockReleaseListener` from your own `IDistributedLocksOptionsExtension`, and call `setup.RegisterExtension(...)` on `HeadlessDistributedLocksSetupBuilder` from a `Use…` extension. `AddHeadlessDistributedLocks` registers the release signal, `TimeProvider`, and `IGuidGenerator`.

**Reader-writer storage: `IDistributedReadWriteLockStorage`.**

- `TryAcquireReadAsync` grants a shared lease only when no writer holds the resource and no writer-waiting marker exists, atomically, so a reader never bypasses a queued writer. The provider always passes a finite `ttl` for readers.
- `TryAcquireWriteAsync(resource, leaseId, waitingId, ttl, markerTtl, ct)` grants an exclusive lease when no reader and no other writer holds the resource. While readers hold it, plant or refresh the writer-waiting marker derived from `waitingId`, with `markerTtl`, not the lease TTL. Return `false` when queued or held.
- `TryExtendReadAsync` and `TryExtendWriteAsync` never shorten an existing TTL and return `false` once the lease is lost. A `null` read `ttl` keeps the existing finite expiry. A `null` write `ttl` makes the write lease non-expiring.
- `ReleaseReadAsync` and `ReleaseWriteAsync` are idempotent and never throw for a missing or mismatched lease. A write release also clears the waiting marker derived from the same lease id.
- `ValidateReadAsync` and `ValidateWriteAsync` report whether the lease id still holds; a waiting marker never satisfies `ValidateWriteAsync`. `IsReadLockedAsync`, `IsWriteLockedAsync`, and `GetReaderCountAsync` are point-in-time diagnostics.

**Semaphore storage: `IDistributedSemaphoreStorage`.**

- `TryAcquireAsync(resource, leaseId, maxCount, ttl, ct)` prunes expired slots, then grants one slot only while live slots are below `maxCount`, atomically. `ttl` is always finite and positive. Return `DistributedLockAcquireResult`.
- `TryExtendAsync`, `ValidateAsync`, and `ReleaseAsync` act on the slot `leaseId` holds. `ReleaseAsync` is idempotent. `GetCountAsync` prunes expired slots before it counts.

The reader-writer and semaphore providers that consume these two storages are not public, so a third-party package cannot yet wire them; only the mutex storage is wirable today.

**Verify:** in a test against your store, a second `TryAcquireAsync` on a held resource with `AcquireTimeout = TimeSpan.Zero` returns `null`, a lease renewed with `RenewAsync` stays held past its first TTL, and a disposed lease frees the resource.

---

## Headless.DistributedLocks.Database

The shared connection-scoped engine behind the PostgreSQL and SQL Server providers.

### API and behavior

- No public surface for applications. A custom backend implements `IDistributedLock` and `IDistributedReadWriteLock` directly.
- The engine carries the fencing token the provider draws in its grant statement onto mutex and write handles.

### Design constraints

The behavior every connection-scoped provider shares is in [Connection-Scoped Locks](#connection-scoped-locks-database-engine): no TTL or finalizer, active connection-death detection on monitored handles, connection sharing among uncontended locks, and one pinned connection per composite child. Read handles never carry a fencing token.

### Install

```bash
dotnet add package Headless.DistributedLocks.Database
```

### Setup and use

Install `Headless.DistributedLocks.PostgreSql` or `Headless.DistributedLocks.SqlServer` instead; they reference this package.

**Verify:** see the provider's Verify step.

### Configuration

None. The providers own options.

### Runtime behavior

None by itself.

---

## Headless.DistributedLocks.InMemory

In-process storage for the three primitives.

### API and behavior

- `UseInMemory()` registers in-process mutex, reader-writer, and semaphore providers through `AddHeadlessDistributedLocks(...)`.
- Expiry uses the registered `TimeProvider`'s monotonic timestamp, not its wall clock, so a clock step neither expires a lease early nor keeps it alive.

### Design constraints

This package is process-local. It does not coordinate across app instances, machines, containers, or processes. Fencing tokens increase only within the process lifetime. A resource's entry is removed once it has no holder and no waiting writer, so high-cardinality resource names (one per request or entity id) do not accumulate. A lease left to expire without release keeps its entry until its key is touched again.

### Install

```bash
dotnet add package Headless.DistributedLocks.InMemory
```

### Setup and use

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

**Verify:** no startup signal. In a test, register a `FakeTimeProvider`, acquire a lock with a 1-minute `TimeUntilExpires`, advance the clock past it, and assert a second `TryAcquireAsync` succeeds.

### Configuration

No InMemory-specific options. Configure `DistributedLockOptions`.

TTL checks use the registered `TimeProvider`, so a test can advance a fake clock. The back-off between retries of a transient storage failure runs on the system clock, so a fake clock the test never advances does not stall a release.

### Runtime behavior

- Registers `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider` over process-local singleton storage.

---

## Headless.DistributedLocks.Messaging

Pushes lock-release wake-ups to every host over the messaging bus.

### API and behavior

- `setup.UseMessagingReleaseSignal()` on `HeadlessDistributedLocksSetupBuilder` replaces the in-process release signal with one that publishes `DistributedLockReleased` through `IBus`, and contributes the package's messaging module.
- `DistributedLockReleased` (`Resource`, `LeaseId`) is the message: `headless.locks.released`, contract version `1` (`DistributedLockReleased.MessageName`), whatever naming conventions the host configures.
- An [every-instance Bus consumer](messaging/consumers.md#every-instance-bus-delivery) with the identity `headless.distributed-locks.release` (concurrency 1) hands each release to every `IDistributedLockReleaseListener` in the host.

### Design constraints

- Requires `AddHeadlessMessaging(...)` in the host, in either order. Without messaging, resolving the lock fails because no `IBus` is registered.
- The transport must support every-instance delivery. AWS SNS/SQS does not: a host that combines it with `UseMessagingReleaseSignal()` fails at startup naming the consumer and the provider. Use `UseRedisReleaseSignal()` there. `ConsumeOnly` never filters every-instance consumers.
- The publish is `DeliveryMode.Direct`, a system message with no tenant, sent with `CancellationToken.None`. The release does not wait for the broker. A publish failure logs `LockReleasePublishFailed` (`EventId = 17`, Warning). Each delivered release logs `ProcessingLockReleased` (`EventId = 14`, Debug).
- A release published while a process is not subscribed never arrives there; its waiters wake on their backoff poll.

### Install

```bash
dotnet add package Headless.DistributedLocks.Messaging
```

### Setup and use

```csharp
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis().UseMessagingReleaseSignal());

builder.Services.AddHeadlessMessaging(setup =>
{
    // setup.Use... storage and transport providers
});
```

**Verify:** the `describe` verb lists the `headless.distributed-locks.release` consumer. A host on a transport without every-instance delivery fails at startup.

### Configuration

No options. The lock provider's `DistributedLockOptions` and the host's messaging options apply.

### Runtime behavior

- Registers the messaging `IDistributedLockReleaseSignal`, the `DistributedLockReleased` contract, and the every-instance release consumer.
- Changes nothing for the PostgreSQL and SQL Server providers: they wake their own waiters (`pg_notify` on PostgreSQL) and do not announce through `IDistributedLockReleaseSignal`.

---

## Headless.DistributedLocks.PostgreSql

PostgreSQL advisory-lock provider for mutex and reader-writer locks.

### API and behavior

- `UsePostgreSql(...)` registers `IDistributedLock` and `IDistributedReadWriteLock` through `AddHeadlessDistributedLocks(...)`, and the `unit.TransactionLocks` feature. See [Locks inside a unit of work](#locks-inside-a-unit-of-work).
- Session-scoped mutex locks use `pg_try_advisory_lock` and release with `pg_advisory_unlock`. Reader-writer locks use shared and exclusive advisory locks.
- `PostgreSqlAdvisoryLockKey` maps strings, `long`, and `(int, int)` keys onto advisory key spaces.
- Mutex and write handles carry durable sequence-backed `FencingToken` values.
- `PostgreSqlDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` take transaction-scoped `pg_advisory_xact_lock`. Each has a `DbTransaction` overload and a synchronous form (`AcquireWithTransaction`, `TryAcquireWithTransaction`); see [EF Core transaction-scoped locks](#ef-core-transaction-scoped-locks).
- A release wakes waiters through `pg_notify`, with polling as the fallback.

### Design constraints

- Session-scoped locks need a stable backend session from acquire through release. Use direct PostgreSQL connections or PgBouncer session pooling. Under PgBouncer transaction or statement pooling, use the transaction-scoped API with a caller-owned transaction.
- Session-scoped locks follow [Connection-Scoped Locks](#connection-scoped-locks-database-engine): no TTL, dispose every handle, and a composite over N resources pins N connections.
- A failed session-scoped acquire holds nothing afterwards, even when the server granted the lock just before the client gave up (a cancellation, surfaced as `OperationCanceledException`, or a command timeout). If that cleanup fails too, the acquire throws `LockCleanupFailedException` carrying both failures; treat the lock as possibly held until its connection ends.
- `IsLockedAsync(resource)` and `GetLockInfoAsync(resource)` see holders in other processes. `ListActiveLocksAsync()` and `GetActiveLocksCountAsync()` see only this provider's handles. Reader counts are exact across processes.
- PostgreSQL has no semaphore here. Use Redis semaphores, or a slot table, for N-holder concurrency.
- **Which pool the locks use.** The parameterless `UsePostgreSql()` takes the container's `NpgsqlDataSource` when the application registered one (for example with `AddNpgsqlDataSource`), otherwise the `AddPostgreSqlSql` connection string on Npgsql's shared pool for that string. Neither opens a second pool, and neither changes `KeepAlive`, so set `Keepalive` on that data source or connection string yourself. An explicit `UsePostgreSql(connectionString)` (or `options.ConnectionString`) builds a provider-owned data source with its own `Max Pool Size` and the `KeepAlive` default; choose it to isolate lock-pinned connections from application queries. On a shared pool, lock holders count against its `Max Pool Size`, and a failed release clears it.
- **No Npgsql multiplexing.** Npgsql 11 removed multiplexing. A connection string that still carries the `Multiplexing` keyword throws `ArgumentException` ("Couldn't set multiplexing") when Npgsql parses it, before any lock runs. Remove the keyword.

### Install

```bash
dotnet add package Headless.DistributedLocks.PostgreSql
```

### Setup and use

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("Postgres");
        options.KeyPrefix = "distributed-lock:";
    })
);
// or reuse the application's NpgsqlDataSource, else the AddPostgreSqlSql connection: setup.UsePostgreSql()

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

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `PostgreSql/headless/DistributedLocks` resource. A host started before that fails, naming the resource and `resources provision`. A lease from `AcquireAsync` has a non-null `FencingToken`.

Transaction-coupled locking:

```csharp
await using var connection = await dataSource.OpenConnectionAsync(ct);
await using var transaction = await connection.BeginTransactionAsync(ct);

await PostgreSqlDistributedLock.AcquireWithTransactionAsync(
    PostgreSqlAdvisoryLockKey.FromString("orders:123"),
    transaction,
    ct
);

// mutate protected rows, then commit or rollback to release the lock
await transaction.CommitAsync(ct);
```

#### EF Core transaction-scoped locks

An EF Core caller gets the transaction as a `DbTransaction` from `db.Database.CurrentTransaction.GetDbTransaction()`. Both providers' helpers accept it and refuse a transaction from another provider with `ArgumentException`, so the calling project needs no Npgsql or SqlClient reference. A `SavingChanges` interceptor runs synchronously; the synchronous forms exist for that path and block the thread until the server answers.

```csharp
// Inside RunAsync(db, …) or after db.Database.BeginTransactionAsync(ct):
var transaction = db.Database.CurrentTransaction!.GetDbTransaction();
await PostgreSqlDistributedLock.AcquireWithTransactionAsync(
    PostgreSqlAdvisoryLockKey.FromString("orders:123"),
    transaction,
    ct
);

// Inside a synchronous SaveChangesInterceptor.SavingChanges override:
PostgreSqlDistributedLock.AcquireWithTransaction(
    PostgreSqlAdvisoryLockKey.FromString("audit:chain"),
    eventData.Context!.Database.CurrentTransaction!.GetDbTransaction()
);
```

The SQL Server helper has the same four shapes with a string resource name.

When the save runs inside a unit of work, use the unit instead of the static helpers. An interceptor reads it with `db.UnitOfWork()`, and the unit applies the provider's `KeyPrefix`, so you build no advisory key and cast no transaction. The context carries a unit inside `RunAsync(db, …)`, `BeginAsync(db)`, or `Enlist(db, tx)`, and when a `HeadlessDbContext` save opens its own transaction because it carries audit entries or domain or integration events. A plain save outside all of these runs `SavingChanges` before any transaction exists, so `db.UnitOfWork()` returns `null`: run that save inside `RunAsync(db, …)`. `db.UnitOfWork()` also returns `null` for a transaction no unit owns, such as one begun with `db.Database.BeginTransactionAsync(ct)` alone; use the static helpers there.

On both engines the transaction owns the lock: asking again for a lock the transaction holds succeeds, so an interceptor that runs on each `SaveChanges` of one transaction can take its lock every time. A failed or cancelled acquire leaves the transaction usable and holding nothing new.

#### Locks inside a unit of work

`unit.TransactionLocks` takes a lock owned by the unit's transaction. `Headless.DistributedLocks.Abstractions` adds the accessor to `IUnitOfWork`, and `UsePostgreSql` and `UseSqlServer` register the feature behind it, so the calling code needs no driver reference.

With `Headless.UnitOfWork.Analyzers` referenced, [HF2003](unit-of-work.md#hf2003) reports an `IDistributedLock.AcquireAsync`, `TryAcquireAsync`, `AcquireAllAsync`, or `TryAcquireAllAsync` call made while a unit of work is in scope and names `unit.TransactionLocks`. It has no code fix: keep the autonomous lock when it must outlive the transaction.

```csharp
await factory.RunAsync(
    connection, // or a DbContext through Headless.UnitOfWork.EntityFramework
    async (unit, ct) =>
    {
        // Waits up to 30 s by default; released by the unit's commit or rollback.
        TransactionLockHandle held = await unit.TransactionLocks.AcquireAsync("orders:123", cancellationToken: ct);

        // One attempt; null while another session holds it.
        TransactionLockHandle? mine = await unit.TransactionLocks.TryAcquireAsync("orders:124", cancellationToken: ct);

        // Several rows: one call, sorted and deduplicated, one 5 s budget, all-or-nothing.
        IReadOnlyList<TransactionLockHandle> both = await unit.TransactionLocks.AcquireAllAsync(
            [LockKey.For<Wallet>(toWalletId), LockKey.For<Wallet>(fromWalletId)],
            TimeSpan.FromSeconds(5),
            ct
        );

        // mutate protected rows
    },
    cancellationToken: ct
);
```

Choose the wait shape by the outcome you want. A `TryAcquire` form returns `null` on contention, for a short wait that refuses the request. An `Acquire` form throws `LockAcquisitionTimeoutException`, for a longer wait whose expiry fails the operation. When that exception reaches the Headless API exception handler, it answers 503 with `error.code: g:resource_busy` and no `Retry-After` ([API surfaces](api.md#api-surfaces), exception mapping). Map at the call site when the endpoint needs another status or knows a retry delay.

```csharp
// 500 ms, then 409 Conflict through the Headless API exception handler.
_ = await unit.TransactionLocks.TryAcquireAllAsync(
        [LockKey.For<Wallet>(fromWalletId), LockKey.For<Wallet>(toWalletId)],
        TimeSpan.FromMilliseconds(500),
        ct
    ) ?? throw new ConflictException("The wallet is being updated by another request.");

// 5 s, then 503 g:resource_busy from the Headless API exception handler.
await unit.TransactionLocks.AcquireAsync(LockKey.For<Ledger>(ledgerId), TimeSpan.FromSeconds(5), ct);

// The same 503 with a Retry-After the application knows, mapped at the call site (here a minimal-API handler).
try
{
    await unit.TransactionLocks.AcquireAsync(LockKey.For<Ledger>(ledgerId), TimeSpan.FromSeconds(5), ct);
}
catch (LockAcquisitionTimeoutException)
{
    httpContext.Response.Headers.RetryAfter = "5";

    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
}
```

A `SavingChanges` interceptor cannot await, so it uses the synchronous forms (`Acquire`, `TryAcquire`, `AcquireAll`, `TryAcquireAll`) on the unit it reads from the context. They block the thread for up to the wait and take no cancellation token. This interceptor refuses a save that runs outside a unit rather than lock nothing:

```csharp
public sealed class AuditChainInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var unit = eventData.Context!.UnitOfWork()
            ?? throw new InvalidOperationException("Save the audit chain inside RunAsync(db, …).");

        unit.TransactionLocks.Acquire("audit:chain");

        return result;
    }
}
```

- The resource encodes as `KeyPrefix + resource`, as the provider's session locks do, so a session lock from `IDistributedLock` and a transaction lock on one name exclude each other.
- It refuses before any command runs. A unit that is no longer active, a unit with no relational resource (`IUnitOfWorkFactory.BeginAsync()` with no connection), a completed transaction, or a transaction from another provider each throw `InvalidOperationException` naming the condition. A host whose only lock provider is Redis or InMemory throws on the accessor. A null, blank, or empty set, or a negative wait other than `Timeout.InfiniteTimeSpan`, throws `ArgumentException`.
- `Acquire*` defaults to a 30-second wait and throws `LockAcquisitionTimeoutException` when it elapses. `TryAcquire*` defaults to one attempt and returns `null`. `Timeout.InfiniteTimeSpan` waits without bound. On PostgreSQL the wait never leaks a `lock_timeout` setting to the rest of the unit.
- `AcquireAll` and `TryAcquireAll` take the set in ordinal order after removing duplicates, the same order `IDistributedLock.AcquireAllAsync` uses, so two units locking overlapping sets wait instead of deadlocking. Two calls are two sets, and the order between them is yours. The returned handles follow the canonical order.
- One budget covers the whole set. When it elapses, `LockAcquisitionTimeoutException.Resource` is the joined set (`"a+b"`), which is diagnostic only. A set of one names its own resource.
- A set is all-or-nothing. When one resource times out, is cancelled, or fails, the set's new locks are gone and the locks the unit held before the call stay held.
- Acquiring a resource the unit already holds succeeds at once. Nothing stacks: the commit or rollback releases it once.
- Besides `LockAcquisitionTimeoutException` and `OperationCanceledException`, an acquire can throw `LockCleanupFailedException` when undoing its partial work failed: the transaction may still hold part of the set, so roll the unit back. SQL Server also throws `DistributedLockDeadlockException` when it picks the acquire as a deadlock victim.
- A failed acquire leaves the unit usable and holding nothing new, even when the server granted the lock just before the client gave up. On PostgreSQL it also lifts the abort a failed statement would put on the transaction. On SQL Server a timed-out acquire never dooms the transaction, but a cancel under `SET XACT_ABORT ON` makes SQL Server roll the whole transaction back.
- `TransactionLockHandle` is identity only and not disposable. Two handles for one resource compare equal.
- A replayed `RunAsync` block takes the lock again in its fresh transaction; the feature never prevents a retry.
- TTL leases from `IDistributedLock` never enlist. A lease taken inside the block is released by its own TTL or `DisposeAsync`, not by the unit.
- A custom provider implements `IUnitOfWorkTransactionLocks`: `TryAcquireAsync(unit, canonicalResources, acquireTimeout, cancellationToken)` and `TryAcquire(unit, canonicalResources, acquireTimeout)`. It receives the set already sorted and deduplicated and the wait already resolved, acquires in that order under one budget, returns `false` on contention, and leaves nothing of the set held when it returns `false` or throws.

### Configuration

```csharp
options.ConnectionString = "..."; // required unless DataSource is set
options.DataSource = dataSource; // preferred when already registered
options.KeyPrefix = "distributed-lock:";
options.PollingFallback = TimeSpan.FromMilliseconds(100);
options.EnablePushWakeup = true;
options.KeepAlive = TimeSpan.FromSeconds(30); // applied only to a provider-built DataSource (explicit ConnectionString)
```

`CommandTimeout` defaults to 30 seconds. The provider never registers its data source as the container's `NpgsqlDataSource`, so an application or EF `UseNpgsql()` that resolves that service gets its own. To share the application's pool explicitly, pass it as `DataSource`.

Set the fence sequence's schema on the feature with `setup.ConfigureStorage(storage => storage.Schema = "app_locks")` (default `"headless"`). The sequence is `headless_distributed_locks_fence`, a schema step (`DistributedLocks/1`) that `resources provision` creates in that schema, creating the schema when absent. The provider names it schema-qualified, so `search_path` does not matter, and validates the schema against PostgreSQL's unquoted-identifier rules at startup.

### Runtime behavior

- Registers `IDistributedLock` and `IDistributedReadWriteLock` as singletons, plus `TimeProvider.System` and `IGuidGenerator` when absent.
- The release signal's `LISTEN` connection reconnects after a lost backend with a jittered back-off from 1 second up to 30 seconds, on the system clock, so a host with a fake `TimeProvider` still reconnects.

---

## Headless.DistributedLocks.Redis

Redis-backed storage for locks, reader-writer locks, and semaphores.

### API and behavior

- `UseRedis()` registers Redis-backed mutex, reader-writer, and semaphore providers through `AddHeadlessDistributedLocks(...)`.
- `UseRedisReleaseSignal()` replaces the default release signal with Redis pub/sub, so waiters on every host wake on release. Opt-in. See [Release Wake-ups](#release-wake-ups).
- Every acquire, extend, and release is one atomic Lua script on the Redis server clock.
- Contributes the `distributed-locks-redis` readiness health check (tags `ready`, `headless`, `redis`), which pings every configured endpoint through the registered `IConnectionMultiplexer`. See [Health checks](utilities.md#health-checks).

### Install

```bash
dotnet add package Headless.DistributedLocks.Redis
```

### Setup and use

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
    setup.UseRedisReleaseSignal(); // optional: push wake-ups over Redis pub/sub
});
```

**Verify:** the `check` verb (`dotnet MyApp.dll check`) and the readiness endpoint report `distributed-locks-redis` as `Healthy`. A Redis outage at start does not stop the host; that check reports it.

### Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `DistributedLockOptions`.

Mutex and semaphore resource names containing `{` or `}` are rejected, because the provider wraps each name in a Redis Cluster hash tag so its lock key and fence key share a slot. Every fence key has no TTL.

### Runtime behavior

- Registers a keyed `HeadlessRedisScriptsLoader` bound to the app's `IConnectionMultiplexer`. Each script loads on the first operation that runs it, which costs that operation one extra round trip.
- Registers `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider` through `Headless.DistributedLocks`.

---

## Headless.DistributedLocks.SqlServer

SQL Server `sp_getapplock` provider for mutex and reader-writer locks.

### API and behavior

- `UseSqlServer(...)` registers `IDistributedLock` and `IDistributedReadWriteLock` through `AddHeadlessDistributedLocks(...)`, and the `unit.TransactionLocks` feature. See [Locks inside a unit of work](#locks-inside-a-unit-of-work).
- Session-scoped mutex locks use `sp_getapplock` with `@LockMode = 'Exclusive'` and release with `sp_releaseapplock`. Reader-writer locks use the `Shared` and `Exclusive` modes.
- Mutex and write handles carry durable `SEQUENCE`-backed `FencingToken` values when `EnableFencing` is on.
- `SqlServerDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` take transaction-owned application locks. Each has a `DbTransaction` overload and a synchronous form (`AcquireWithTransaction`, `TryAcquireWithTransaction`); see [EF Core transaction-scoped locks](#ef-core-transaction-scoped-locks). They take a `string` resource and encode `KeyPrefix + resource` like the session provider, so the two exclude each other on one name.
- Resource names longer than SQL Server's 255-character limit are encoded as `sha256:<lowercase-hex>`.

### Design constraints

- Session-scoped locks need the holding `SqlConnection` open until release. Do not return that connection to pooling code while the lock is held.
- A contended session-scoped acquire does not block inside SQL Server. It retries at a jittered cadence of about 100 ms until the acquire timeout elapses. A release wakes waiters in the same process at once; waiters in other processes wait for their next poll. The transaction-scoped API does block server-side, for its acquire timeout.
- Session-scoped locks follow [Connection-Scoped Locks](#connection-scoped-locks-database-engine): no TTL, dispose every handle, and a composite over N resources pins N connections. A leaked handle also keeps its liveness probe running until the provider is disposed.
- A monitored handle's `LostToken` fires on a clean disconnect or when a `SELECT 1` probe every `ConnectionProbeInterval` (default 30 seconds) finds the session dead or unresponsive. Lower the interval to notice sooner, at one extra query per held lock per interval. An explicit release never cancels the token.
- Disposing the provider while it still holds locks cancels each handle's `LostToken`, clears the connection-string pool, and disposes each lock's connection, so the server frees the locks at once.
- `IsLockedAsync(...)`, `IsReadLockedAsync(...)`, `IsWriteLockedAsync(...)`, and reader counts inspect server lock state for a resource. `GetLeaseIdAsync(...)`, `GetLockInfoAsync(...)`, `ListActiveLocksAsync(...)`, and `GetActiveLocksCountAsync(...)` report only this provider instance's handles.
- Reader counts are exact for local holders, but any number of remote shared readers counts as `1`. Treat the remote value as held or not held.
- Transaction-coupled locking is the safest choice for SQL Server data changes: commit or rollback releases the lock.
- SQL Server has no semaphore here. Use Redis semaphores for N-holder concurrency.

### Install

```bash
dotnet add package Headless.DistributedLocks.SqlServer
```

### Setup and use

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("SqlServer");
        options.KeyPrefix = "distributed-lock:";
    })
);
// or reuse the connection from services.AddSqlServerSql(connectionString): setup.UseSqlServer()

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

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

**Verify:** with `EnableFencing` on, `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `SqlServer/headless/DistributedLocks` resource (a non-default `KeyPrefix` adds the sequence name), and a host started before that fails naming it. A lease from `AcquireAsync` has a non-null `FencingToken`.

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
options.KeyPrefix = "distributed-lock:";
options.CommandTimeout = TimeSpan.FromSeconds(30);
options.ConnectionProbeInterval = TimeSpan.FromSeconds(30); // how soon a monitored lock notices a silently-dead session
options.EnableFencing = true;
```

Set the fence sequence's schema on the feature with `setup.ConfigureStorage(storage => storage.Schema = "app_locks")`. The default is `"headless"`, not `dbo`, and `resources provision` creates the schema when absent. The provider validates it against SQL Server's regular-identifier rules at startup. The sequence is `DistributedLocksFence_{KeyPrefix}`, with every run of characters outside `A-Za-z0-9_` in the prefix collapsed to `_` and the name truncated to 128 characters, so the default prefix `distributed-lock:` gives `DistributedLocksFence_distributed_lock`. Replicas that share a key prefix share a fence.

### Runtime behavior

- Registers `IDistributedLock` and `IDistributedReadWriteLock` as singletons, plus `TimeProvider.System` and `IGuidGenerator` when absent.
- Registers the fence-sequence schema step only when `EnableFencing` is `true`; it is recorded as `DistributedLocks/1`, or `DistributedLocks:DistributedLocksFence_<prefix>/1` for a non-default key prefix.
