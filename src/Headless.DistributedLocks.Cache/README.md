# Headless.DistributedLocks.Cache

Cache-backed storage for distributed locks.

## Problem Solved

Stores lock records through `ICache` so applications can reuse an existing cache provider.

## Key Features

- `CacheDistributedLockStorage` implements `IDistributedLockStorage`.
- Uses cache TTL for lock expiration.
- Works with memory, Redis, or custom cache providers through `ICache`.
- Do not use `HybridCache` for monitored or auto-extending leases; local L1 reads can outlive the distributed lock TTL and hide lease loss.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Cache
```

## Quick Start

```csharp
builder.Services.AddInMemoryCache();

builder.Services.AddDistributedLock<CacheDistributedLockStorage>(options =>
{
    options.KeyPrefix = "distributed-lock:";
});
```

## Configuration

No storage-specific configuration. Configure the selected `ICache` provider and `DistributedLockOptions`. `LockMonitoringMode` (lease monitoring and auto-extension) is a storage-agnostic provider feature configured through `Headless.DistributedLocks.Core`.

Avoid `HybridCache` for `LockMonitoringMode.Monitor` and `LockMonitoringMode.AutoExtend`. Lease validation reads the current lock id through `ICache`; `HybridCache` may satisfy that read from its local L1 cache even after the distributed TTL has expired. Use `Headless.DistributedLocks.Redis` for monitored Redis-backed locks, or use a cache provider whose reads are distributed and TTL-accurate.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Core`

## Side Effects

None. The package only provides storage; registration is done through `Headless.DistributedLocks.Core`.
