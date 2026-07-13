# Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

## Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

## Key Features

- `IDistributedLock` with single-resource `TryAcquireAsync(...)` / `AcquireAsync(...)` and multi-resource `TryAcquireAllAsync(...)` / `AcquireAllAsync(...)` extensions.
- `IDistributedReadWriteLock` with read/write acquire methods returning `IDistributedLease`.
- `IDistributedSemaphoreProvider` and `IDistributedSemaphore` for creation-time `maxCount` concurrency control.
- `IDistributedLease` handle with `LeaseId`, nullable `FencingToken`, `LostToken`, `CanObserveLoss`, `IsLost`, `ThrowIfLost()`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `TryUsingAsync(resource, work, ...)` convenience that acquires, executes work, and releases — prefer this over manual try/finally for simple guarded execution.
- `LockAcquisitionTimeoutException`, `LockHandleLostException`, and `DistributedLockException` for lock-specific failures.
- Lock inspection methods for current lease id, expiration, active count, active list, and lock info. `GetLeaseIdAsync` does not renew a lease; monitored holders should use `LostToken` for lease-loss observation. Some backends can observe that a resource is locked without being able to surface the current holder id, so inspection `LeaseId` values may be null and provider-wide list/count APIs are limited to what the backend can enumerate.

## Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- Multi-resource acquisition validates, deduplicates, and ordinal-sorts the complete input before the first provider call, then applies one acquire timeout across the canonical set. A zero timeout gives every canonical resource one non-blocking attempt. Partial acquisition is compensated by exhaustive reverse-order release and disposal; it is not transactional.
- Composite resource identity is ordinal: two names are the same only when `StringComparer.Ordinal` considers them equal. Custom providers whose backend aliases ordinal-distinct names, for example through case folding, must reject non-canonical names or require callers to canonicalize them before invoking the composite helpers. Normalizing only inside the provider is too late and can make one composite contend with itself.
- A canonical set of one returns the provider's original child lease, preserving its `LeaseId` and `FencingToken`. A true multi-resource lease has a synthetic `LeaseId`, a joined diagnostic `Resource`, and a `null` scalar `FencingToken`; its loss signal links the child signals, and renew/release operate on every child. The synthetic `Resource` is not a storage key — never pass it to `IsLockedAsync`, `GetLockInfoAsync`, `GetExpirationAsync`, or `GetLeaseIdAsync`, which would report a genuinely held set as unlocked.
- During composite formation, finite-TTL children are renewed at half the TTL, capped at one minute, unless `LockMonitoringMode.AutoExtend` already owns renewal. Composite deadlines and waits use `IDistributedLock.TimeProvider`; custom providers must expose the clock used by their own acquisition logic. That clock schedules the check-in; it never arbitrates expiry — only the backend's own answer decides whether a lease still holds.
- Cleanup failures raise `LockCleanupFailedException` (derives from `DistributedLockException`, carries every failure on `Failures`); a resource whose release failed may still be held until its TTL expires. `DisposeAsync` is the exception: it never throws and logs through `IDistributedLock.Logger` instead, because a throw from disposal inside `await using` would replace the exception the caller's body was already throwing. Call `ReleaseAsync()` explicitly to observe the outcome.
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

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.Extensions`

## Side Effects

None.
