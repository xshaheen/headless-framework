# Headless.DistributedLocks.Abstractions

Defines public distributed-lock contracts.

## Problem Solved

Lets application and domain code depend on lock interfaces without referencing a concrete storage backend.

## Key Features

- `IDistributedLockProvider` with `TryAcquireAsync(...)` and `AcquireAsync(...)`.
- `IDistributedLock` handle with `LockId`, `RenewAsync(...)`, and `ReleaseAsync(...)`.
- `LockAcquisitionTimeoutException` and `DistributedLockException` for lock-specific failures.
- Lock inspection methods for expiration, active count, active list, and lock info.

## Design Notes

- `AcquireAsync(...)` is a throwing convenience over `TryAcquireAsync(...)`. It does not provide stronger safety guarantees.
- `releaseOnDispose: false` prevents dispose-time release but does not disable explicit `ReleaseAsync(...)`.

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
            timeUntilExpires: TimeSpan.FromMinutes(5),
            acquireTimeout: TimeSpan.FromSeconds(10),
            cancellationToken: ct
        );

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
