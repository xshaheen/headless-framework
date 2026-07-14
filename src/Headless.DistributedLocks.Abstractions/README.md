# Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

## Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

## Key Features

- `IDistributedLock` with single-resource `TryAcquireAsync(...)` / `AcquireAsync(...)` and multi-resource `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` extensions over `IEnumerable<string>`.
- `IDistributedReadWriteLock` with read/write acquire methods returning `IDistributedLease`, plus composite `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` over a mixed `IEnumerable<DistributedReadWriteLockRequest>` set and the uniform-mode sugar `TryAcquireAllReadAsync(...)` / `AcquireAllReadAsync(...)` / `TryAcquireAllWriteAsync(...)` / `AcquireAllWriteAsync(...)`.
- `DistributedReadWriteLockRequest(string Resource, DistributedLockMode Mode)` and `DistributedLockMode` (`None = 0`, `Read = 1`, `Write = 2`) describe one entry of a reader-writer composite.
- `IDistributedSemaphoreProvider` and `IDistributedSemaphore` for creation-time `maxCount` concurrency control, with composite `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` over `IEnumerable<DistributedSemaphoreRequest>` on the provider.
- `DistributedSemaphoreRequest(string Resource, int MaxCount)` names one semaphore in a composite and the capacity it is created with; it carries no permit count.
- `IDistributedLease` handle with `LeaseId`, nullable `FencingToken`, `LostToken`, `CanObserveLoss`, `IsLost`, `ThrowIfLost()`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `TryUsingAsync(resource, work, ...)` convenience that acquires, executes work, and releases — prefer this over manual try/finally for simple guarded execution.
- `LockAcquisitionTimeoutException`, `LockHandleLostException`, and `DistributedLockException` for lock-specific failures.
- Lock inspection methods for current lease id, expiration, active count, active list, and lock info. `GetLeaseIdAsync` does not renew a lease; monitored holders should use `LostToken` for lease-loss observation. Some backends can observe that a resource is locked without being able to surface the current holder id, so inspection `LeaseId` values may be null and provider-wide list/count APIs are limited to what the backend can enumerate.

## Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- Multi-resource acquisition validates, deduplicates, and ordinal-sorts the complete input before the first provider call, then applies one acquire timeout across the canonical set. A zero timeout gives every canonical resource one non-blocking attempt. Partial acquisition is compensated by exhaustive reverse-order release and disposal; it is not transactional. The same coordinator backs all three primitives.
- Composite resource identity is ordinal: two names are the same only when `StringComparer.Ordinal` considers them equal. The canonical set is ordered by *resource*, never by a composed key such as `"a:write"` — a composed sort would place that after `"a:x:read"` for the resources `a` and `a:x`, breaking the single global resource order that keeps a mutex composite and a reader-writer composite over overlapping names from deadlocking against each other. Custom providers whose backend aliases ordinal-distinct names, for example through case folding, must reject non-canonical names or require callers to canonicalize them before invoking the composite helpers. Normalizing only inside the provider is too late and can make one composite contend with itself.
- The ordering guarantee holds only when the caller passes the whole set through one call. Nesting one composite inside another, or acquiring a composite while already holding an unrelated lock, reintroduces the circular-wait risk composites exist to prevent, because neither call sees the complete set.
- A reader-writer set may mix `Read` and `Write` freely, and should. Separate read-set and write-set calls cannot see the whole set: a caller taking read-`a` then write-`b`, racing a caller taking read-`b` then write-`a`, deadlocks, and ordinal sorting cannot fix it because neither call ever sees both resources. One mixed set canonicalizes to `a` then `b` for both callers, and there is no cycle. A resource requested in both modes collapses to a single `Write` child (a write lock subsumes a read lock), which leaves every resource in the canonical set exactly once and makes resource-only ordering total.
- A semaphore request's `MaxCount` is the semaphore's capacity, not a permit count. One resource named twice with conflicting capacities throws `ArgumentException` (naming the resource) before any `CreateSemaphore` call; identical duplicates dedupe. A composite takes exactly one slot per named semaphore, and there is deliberately no permit-count field — a composite cannot make N permits of a *single* semaphore atomic, because ordering cannot resolve same-resource contention. Callers needing that have no safe primitive today: repeated `AcquireAsync(...)` calls can leave two contending callers each holding part of what they need, and only atomic multi-permit acquisition inside the storage backend can fix it.
- Composite acquisition deliberately excludes: cross-primitive sets (mutex + reader-writer + semaphore in one call — the three are unrelated interfaces with no unified provider surface, and PostgreSQL and SQL Server ship no semaphore); upgradeable-read composites (a composite must be able to roll back every child, and a one-way upgrade cannot); and a scalar fencing token for a multi-resource result (independent per-resource fences cannot be represented safely as one number).
- A canonical set of one returns the provider's original child lease, preserving its `LeaseId` and `FencingToken`. A true multi-resource lease has a synthetic `LeaseId`, a joined diagnostic `Resource` (mode-encoded as `r:a+w:b` for reader-writer, a plain `a+b` join otherwise), and a `null` scalar `FencingToken` — even for semaphores, whose individual slots each carry one. Its loss signal links the child signals, and renew/release operate on every child. The synthetic `Resource` and `LeaseId` are not storage keys and were never written to any backend — never pass them to `IsLockedAsync`, `GetLockInfoAsync`, `GetExpirationAsync`, `GetLeaseIdAsync`, `GetHolderCountAsync`, or `RenewAsync(resource, leaseId, ...)`, which would report a genuinely held set as unlocked rather than fail. Inspect the individual resource names instead.
- During composite formation, finite-TTL children are renewed at half the TTL, capped at one minute, unless `LockMonitoringMode.AutoExtend` already owns renewal. A semaphore slot always has a finite TTL, so formation renewal always applies there. Composite deadlines and waits use the provider's `TimeProvider`; custom providers must expose the clock used by their own acquisition logic. That clock schedules the check-in; it never arbitrates expiry — only the backend's own answer decides whether a lease still holds.
- `IDistributedReadWriteLock` and `IDistributedSemaphoreProvider` expose `TimeProvider` and `Logger`, as `IDistributedLock` already did, because the composite coordinator is provider-agnostic and needs a clock for the whole-set deadline and a logger for swallow-and-log disposal. **This is a breaking change for external implementors of those two interfaces**: a custom reader-writer lock or semaphore provider must add both properties.
- Cleanup failures raise `LockCleanupFailedException` (derives from `DistributedLockException`, carries every failure on `Failures`); a resource whose release failed may still be held until its TTL expires. `DisposeAsync` is the exception: it never throws and logs through the provider's `Logger` instead, because a throw from disposal inside `await using` would replace the exception the caller's body was already throwing. Call `ReleaseAsync()` explicitly to observe the outcome.
- Renewing a composite throws `LockHandleLostException` naming the lost child rather than returning `false`. Renewals fan out concurrently, so the lost child's siblings have already been extended and are still held; `false` would mean "nothing to release" and orphan them. `false` is returned only when the composite was already released.
- Per-call configuration (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`) is bundled into `DistributedLockAcquireOptions`. Omit the argument to use defaults; use `with` expressions to derive variants.
- `ReleaseOnDispose = false` prevents dispose-time release but does not disable explicit `ReleaseAsync(...)`, including for composite leases.
- `FencingToken` is a per-resource monotonic grant counter for stale-write rejection. It is distinct from `LeaseId`, which remains the opaque ownership token used for renew/release equality. It is `null` when the backend or lock type does not support fencing.
- `DistributedLockInfo.LeaseId` may be `null` when the backend can prove a resource is locked but cannot expose the current holder identity on the inspection path.
- `LostToken` is `CancellationToken.None` unless the acquire call enables monitoring (check `CanObserveLoss` to disambiguate). It is an observability signal. A faulted monitor is surfaced as cancellation here as a fail-safe so a silently dead monitor cannot keep appearing healthy.
- `ThrowIfLost()` is a self-fencing convenience for hot paths: it throws `LockHandleLostException` when `LostToken` has fired.
- `TimeUntilExpires = null` uses the provider default. Built-in providers use a finite 20-minute default, so `null` is valid with `LockMonitoringMode.AutoExtend`; `Timeout.InfiniteTimeSpan` is not.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Abstractions
```

## Quick Start

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

`IDistributedReadWriteLock` composes over a mixed `(resource, mode)` set — `Task<IDistributedLease?> TryAcquireAllAsync(IEnumerable<DistributedReadWriteLockRequest> requests, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)` and the throwing `AcquireAllAsync(...)`, plus the uniform-mode sugar `TryAcquireAllReadAsync(IEnumerable<string> resources, ...)` / `AcquireAllReadAsync(...)` / `TryAcquireAllWriteAsync(...)` / `AcquireAllWriteAsync(...)`. Pass reads and writes together in one call; taking them as two nested composites can deadlock.

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

`IDistributedSemaphoreProvider` composes over `(resource, maxCount)` descriptors — `Task<IDistributedLease?> TryAcquireAllAsync(IEnumerable<DistributedSemaphoreRequest> requests, DistributedLockAcquireOptions? options = null, CancellationToken cancellationToken = default)` and the throwing `AcquireAllAsync(...)`. One composite can span differently-sized semaphores, and takes exactly one slot of each.

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

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.Extensions`

## Side Effects

None.
