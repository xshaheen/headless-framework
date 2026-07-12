# Headless.DistributedLocks.Core

Provides the distributed-lock, reader-writer lock, and semaphore provider implementations and setup extensions.

## Problem Solved

Implements lock/semaphore acquisition, renewal, release, inspection, timeout handling, lease monitoring, and optional messaging wake-ups over storage contracts.

## Key Features

- `DistributedLock` implements `IDistributedLock`.
- `DistributedReadWriteLock` implements `IDistributedReadWriteLock`.
- `DistributedSemaphoreProvider` implements `IDistributedSemaphoreProvider`.
- `DisposableDistributedLock` releases on dispose by default.
- `IDistributedReadWriteLockStorage` defines read/write acquire, extend, release, and validation operations.
- `IDistributedSemaphoreStorage` defines acquire, extend, validate, release, and holder-count operations.
- `DistributedLockOptions` configures key prefix, resource name length, waiter limits, and lease-monitor cadence fractions.
- `AddHeadlessDistributedLocks(...)` is the single root registration entry point (it returns the `IServiceCollection` for chaining); provider packages contribute `Use...` methods on the `HeadlessDistributedLocksSetupBuilder`.
- `AddHeadlessDistributedLocks(...)` auto-registers the optional `DistributedLockReleased` consumer descriptor.
- `IDistributedLocksOptionsExtension` is the setup-time hook used by provider packages to wire supported primitives.

## Design Notes

- `IOutboxBus` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
- When messaging is present, the release consumer is drained at messaging startup whether `AddHeadlessDistributedLocks(...)` runs before or after `AddHeadlessMessaging(...)`; without messaging, waiters fall back to polling.
- `TryAcquireAsync(..., new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero })` performs a single storage attempt with an internal safety deadline. If that deadline fires (lock-store stall, caller token never cancels), the acquire still returns `null` but emits the `TryOnceSafetyDeadlineFired` log event (`EventId = 24`, Warning) and tags the failure metric `reason=stalled`, distinguishing a stall from routine contention (`reason=contended`). Applies to mutex, reader-writer, and semaphore non-blocking acquires. The `reason` values are exposed as `public const` on `DistributedLockFailureReasons` for compile-time use in alert rules.
- Lease monitors are opt-in per acquire call through `Monitoring = LockMonitoringMode.Monitor` (validate only) or `Monitoring = LockMonitoringMode.AutoExtend` (validate + renew) on `DistributedLockAcquireOptions`. Both require a finite `TimeUntilExpires`; combining with `Timeout.InfiniteTimeSpan` throws `ArgumentException`.
- Release messages also nudge active monitors so lost-handle detection can happen before the next polling cadence. Self-release deregisters the monitor before publishing so direct `ReleaseAsync` does not produce a spurious lost signal.
- Intermediate monitor states are surfaced via the `LeaseMonitorStateChanged` log event (`EventId = 30`) for programmatic log filtering. Structured fields are `Resource`, `LeaseId`, `PreviousState`, and `NextState`. `GetActiveMonitorCount` is `internal` and intended for tests only.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core
```

## Quick Start

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

## Configuration

```csharp
options.KeyPrefix = "distributed-lock:";
options.MaxResourceNameLength = 512;
options.MaxConcurrentWaitingResources = 10_000;
options.MaxWaitersPerResource = 1_000;
options.PollingCadenceFraction = 0.5;
options.AutoExtensionCadenceFraction = 1.0 / 3.0;
```

Default lock expiration is 20 minutes and default acquire timeout is 30 seconds; override those per call by passing a `DistributedLockAcquireOptions` instance to `AcquireAsync(...)` or `TryAcquireAsync(...)`. Set `Monitoring = LockMonitoringMode.Monitor` for `LostToken` loss detection and `Monitoring = LockMonitoringMode.AutoExtend` for background renewal.

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

- Registers exactly one provider selected by the `AddHeadlessDistributedLocks(...)` builder.
- Redis and InMemory providers register `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider`.
- PostgreSQL and SQL Server providers register `IDistributedLock` and `IDistributedReadWriteLock`.
- Registers `TimeProvider.System` and `IGuidGenerator` when absent.
- Auto-registers the shared `DistributedLockReleased` messaging consumer. The descriptor is inert when messaging is absent; waiters still use polling.
