# Headless.DistributedLocks.Core

Provides the distributed-lock provider implementation and setup extensions.

## Problem Solved

Implements lock acquisition, renewal, release, inspection, timeout handling, and optional messaging wake-ups over an `IDistributedLockStorage`.

## Key Features

- `DistributedLockProvider` implements `IDistributedLockProvider`.
- `DisposableDistributedLock` releases on dispose by default.
- `DistributedLockOptions` configures key prefix, resource name length, and waiter limits.
- `AddDistributedLock(...)` overloads wire storage, options, time provider, ID generator, and optional release consumers.

## Design Notes

- `IOutboxBus` is optional. Without it, release notifications fall back to polling backoff and a warning is logged once when the provider is constructed.
- `TryAcquireAsync(..., acquireTimeout: TimeSpan.Zero)` performs a single storage attempt with an internal safety deadline.

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
```

## Configuration

```csharp
options.KeyPrefix = "distributed-lock:";
options.MaxResourceNameLength = 512;
options.MaxConcurrentWaitingResources = 10_000;
options.MaxWaitersPerResource = 1_000;
```

Default lock expiration is 20 minutes and default acquire timeout is 30 seconds; override those per `AcquireAsync(...)` or `TryAcquireAsync(...)` call.

## Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Core`

## Side Effects

- Registers `IDistributedLockProvider` as singleton.
- Registers `TimeProvider.System` and `ILongIdGenerator` when absent.
- Registers a `DistributedLockReleased` consumer only when an `IOutboxBus` registration exists.
