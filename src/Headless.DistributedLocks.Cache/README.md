# Headless.DistributedLocks.Cache

Cache-backed storage for distributed locks.

## Problem Solved

Stores lock records through `ICache` so applications can reuse an existing cache provider.

## Key Features

- `CacheDistributedLockStorage` implements `IDistributedLockStorage`.
- Uses cache TTL for lock expiration.
- Works with memory, Redis, hybrid, or custom cache providers through `ICache`.

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

No storage-specific configuration. Configure the selected `ICache` provider and `DistributedLockOptions`. Lease monitoring and `autoExtend` are storage-agnostic provider features configured through `Headless.DistributedLocks.Core`.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Core`

## Side Effects

None. The package only provides storage; registration is done through `Headless.DistributedLocks.Core`.
