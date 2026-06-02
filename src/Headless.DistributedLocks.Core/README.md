# Headless.DistributedLocks.Core

Provides the distributed-lock, reader-writer lock, and semaphore provider implementations and setup extensions.

## Problem Solved

Implements lock/semaphore acquisition, renewal, release, inspection, timeout handling, lease monitoring, and optional messaging wake-ups over storage contracts.

## Key Features

- `DistributedLockProvider` implements `IDistributedLockProvider`.
- `DistributedReaderWriterLockProvider` implements `IDistributedReaderWriterLockProvider`.
- `DistributedSemaphoreProvider` implements `IDistributedSemaphoreProvider`.
- `DisposableDistributedLock` releases on dispose by default.
- `IDistributedReaderWriterLockStorage` defines read/write acquire, extend, release, and validation operations.
- `IDistributedSemaphoreStorage` defines acquire, extend, validate, release, and holder-count operations.
- `DistributedLockOptions` configures key prefix, resource name length, waiter limits, and lease-monitor cadence fractions.
- `AddDistributedLock(...)` overloads wire storage, options, time provider, and ID generator.
- `setup.UseDistributedLockReleaseWakeups()` registers the optional `DistributedLockReleased` consumer from `AddHeadlessMessaging(...)`.
- `AddDistributedReaderWriterLock(...)` overloads wire reader-writer storage, options, time provider, and ID generator.
- `AddDistributedSemaphore(...)` overloads wire semaphore storage, options, time provider, and ID generator.

## Design Notes

- `IOutboxBus` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
- When messaging is present, call `setup.UseDistributedLockReleaseWakeups()` inside `AddHeadlessMessaging(...)` so release messages wake lock waiters instead of waiting for polling.
- `TryAcquireAsync(..., new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero })` performs a single storage attempt with an internal safety deadline.
- Lease monitors are opt-in per acquire call through `Monitoring = LockMonitoringMode.Monitor` (validate only) or `Monitoring = LockMonitoringMode.AutoExtend` (validate + renew) on `DistributedLockAcquireOptions`. Both require a finite `TimeUntilExpires`; combining with `Timeout.InfiniteTimeSpan` throws `ArgumentException`.
- Release messages also nudge active monitors so lost-handle detection can happen before the next polling cadence. Self-release deregisters the monitor before publishing so direct `ReleaseAsync` does not produce a spurious lost signal.
- Intermediate monitor states are surfaced via the `LeaseMonitorStateChanged` log event (`EventId = 30`) for programmatic log filtering. Structured fields are `Resource`, `LockId`, `PreviousState`, and `NextState`. `GetActiveMonitorCount` is `internal` and intended for tests only.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core
```

## Quick Start

```csharp
builder.Services.AddDistributedLock(
    sp => sp.GetRequiredService<IDistributedLockStorage>(),
    options =>
    {
        options.KeyPrefix = "distributed-lock:";
        options.MaxResourceNameLength = 512;
    }
);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseDistributedLockReleaseWakeups();
    // setup.Use... storage and transport providers
});

```

## Configuration

```csharp
options.KeyPrefix = "distributed-lock:";
options.MaxResourceNameLength = 512;
options.MaxConcurrentWaitingResources = 10_000;
options.MaxWaitersPerResource = 1_000;
options.PollingCadenceFraction = 0.5;
options.AutoExtensionCadenceFraction = 1.0 / 3.0;
```

Default lock expiration is 20 minutes and default acquire timeout is 30 seconds; override those per call by passing a `DistributedLockAcquireOptions` instance to `AcquireAsync(...)` or `TryAcquireAsync(...)`. Set `Monitoring = LockMonitoringMode.Monitor` for `HandleLostToken` loss detection and `Monitoring = LockMonitoringMode.AutoExtend` for background renewal.

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

## Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Core`

## Side Effects

- Registers `IDistributedLockProvider` as singleton.
- Registers `IDistributedReaderWriterLockProvider` as singleton when `AddDistributedReaderWriterLock(...)` is called.
- Registers `IDistributedSemaphoreProvider` as singleton when `AddDistributedSemaphore(...)` is called.
- Registers `TimeProvider.System` and `ILongIdGenerator` when absent.
- Does not register messaging consumers by itself; call `setup.UseDistributedLockReleaseWakeups()` from `AddHeadlessMessaging(...)` when release-message wake-ups are needed.
