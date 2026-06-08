# Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

## Problem Solved

Provides process-local caching through the unified `ICache` abstraction, suitable for development, single-instance deployments, or an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation.
- Can serve as default `ICache` or alongside a distributed cache.
- Supports strongly typed `ICache<T>`.
- Automatic memory management with configurable limits (`MaxItems` plus LRU eviction).
- Can act as an `IRemoteCache` adapter for single-instance scenarios.
- Optional value cloning for isolation.
- Shared `GetOrAddAsync` fail-safe, factory timeout, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Memory cache stores entries in an internal envelope with logical expiration and physical expiration. Direct writes set both timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`.

Long `FailSafeMaxDuration` values can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments. Soft-timeout background refreshes also hold values in process while the detached factory runs; `BackgroundFactoryCeiling` bounds how long a cooperative refresh keeps the per-key lock.

`Memory` in Headless caching docs means this package, `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.

## Installation

```bash
dotnet add package Headless.Caching.InMemory
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInMemoryCache();

builder.Services.AddInMemoryCache(options =>
{
    options.MaxItems = 10000;
    options.CloneValues = true;
});

builder.Services.AddInMemoryCache(isDefault: false);
```

## Configuration

```csharp
options.MaxItems = 10000;
options.CloneValues = false;
options.KeyPrefix = "myapp:";
```

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`

## Side Effects

- Registers `IInMemoryCache` as singleton.
- Registers `ICache` as singleton when `isDefault: true`.
- Registers `IRemoteCache` adapter when `isDefault: true`.
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons.
