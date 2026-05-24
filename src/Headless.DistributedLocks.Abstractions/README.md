# Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

## Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

## Key Features

- `IDistributedLockProvider` with `TryAcquireAsync(...)` and `AcquireAsync(...)`.
- `IDistributedReaderWriterLockProvider` with read/write acquire methods returning `IDistributedLock`.
- `IDistributedLock` handle with `LockId`, `HandleLostToken`, `IsMonitored`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `TryUsingAsync(resource, work, ...)` convenience that acquires, executes work, and releases — prefer this over manual try/finally for simple guarded execution.
- `LockAcquisitionTimeoutException`, `LockHandleLostException`, and `DistributedLockException` for lock-specific failures.
- Lock inspection methods for current lock id, expiration, active count, active list, and lock info. `GetLockIdAsync` does not renew a lease; monitored holders should use `HandleLostToken` for lease-loss observation.

## Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- Per-call configuration (`TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, `Monitoring`) is bundled into `DistributedLockAcquireOptions`. Omit the argument to use defaults; use `with` expressions to derive variants.
- `ReleaseOnDispose = false` prevents dispose-time release but does not disable explicit `ReleaseAsync(...)`.
- `HandleLostToken` is `CancellationToken.None` unless the acquire call enables monitoring (check `IsMonitored` to disambiguate). It is an observability signal; fence protected writes with `LockId` when correctness matters. A faulted monitor is surfaced as cancellation here as a fail-safe so a silently dead monitor cannot keep appearing healthy.
- `TimeUntilExpires = null` uses the provider default. Built-in providers use a finite 20-minute default, so `null` is valid with `LockMonitoringMode.AutoExtend`; `Timeout.InfiniteTimeSpan` is not.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Abstractions
```

## Quick Start

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

## Configuration

None.

## Dependencies

- `Headless.Checks`
- `Headless.Extensions`

## Side Effects

None.
