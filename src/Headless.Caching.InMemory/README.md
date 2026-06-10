# Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

## Problem Solved

Provides process-local caching through the unified `ICache` abstraction, suitable for development, single-instance deployments, or an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation.
- Can serve as default `ICache` or alongside a distributed cache.
- Supports strongly typed `ICache<T>`.
- Named cache instances via `AddInMemoryCache(name, ...)`, resolvable as keyed `ICache` services or through `ICacheProvider`.
- Automatic memory management with configurable limits (`MaxItems` plus LRU eviction).
- Tag invalidation through an in-process reverse tag index with live-entry verification.
- Can act as an `IRemoteCache` adapter for single-instance scenarios.
- Optional value cloning for isolation.
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Memory cache stores entries in an internal envelope with logical expiration, physical expiration, and optional sliding expiration. Direct writes set logical and physical timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Sliding `GetOrAddAsync` keeps physical expiration as the absolute cap and re-arms logical expiration on value reads only; `GetExpirationAsync`, `ExistsAsync`, key listing, and count operations do not extend the idle window. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`.

The reverse tag index maps each tag to the set of keys whose entry carried the tag when written. Memberships may be momentarily stale (an untagged overwrite races the index update), so `RemoveByTagAsync` always verifies against the live entry's tags before removing: a key overwritten by an untagged write, or re-created after expiry, has its stale membership cleaned up instead of the new entry being removed. Empty per-tag sets are intentionally not pruned — residue is bounded by the process-lifetime distinct-tag cardinality, and pruning would race concurrent tagged writes.

Long `FailSafeMaxDuration` values and long sliding absolute caps can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments. Soft-timeout and eager background refreshes also hold values in process while the detached factory runs; `BackgroundFactoryCeiling` (infinite by default) optionally bounds how long a cooperative refresh keeps the per-key lock when set to a finite value.

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
    options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
});

builder.Services.AddInMemoryCache(isDefault: false);
```

Named instances (independent options, resolved by name):

```csharp
builder.Services.AddInMemoryCache("orders", options => options.MaxItems = 1000);

public sealed class OrderService(ICacheProvider cacheProvider)
{
    private readonly ICache _cache = cacheProvider.GetCache("orders");
}
```

Names must be non-empty and must not be one of the reserved role keys (`memory`, `remote`, `hybrid` on `CacheConstants`); reserved names are rejected with `ArgumentException`. Named instances never touch the default (unkeyed) `ICache`.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` extension overloads; when `null` those overloads throw. |
| `MaxItems` | `10000` | Maximum number of items before LRU eviction. |
| `CloneValues` | `false` | Clone values on get/set so cached entries are isolated from caller mutations. |
| `MaxMemorySize` | `null` | Maximum total memory in bytes; requires `SizeCalculator`. |
| `SizeCalculator` | `null` | Function computing the byte size of cached objects; required for `MaxMemorySize`/`MaxEntrySize`. |
| `MaxEntrySize` | `null` | Maximum size of a single entry in bytes; requires `SizeCalculator`. |
| `ShouldThrowOnMaxEntrySizeExceeded` | `false` | Throw when an entry exceeds `MaxEntrySize` instead of logging and skipping. |
| `ShouldThrowOnSerializationError` | `true` | Throw on serialization errors during cloning. |
| `MaintenanceInterval` | `250 ms` | Interval between background maintenance runs. |
| `MaxEvictionsPerCompaction` | `10` | Maximum items evicted per compaction cycle. |
| `EvictionSampleSize` | `5` | Entries sampled when finding eviction candidates. |

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`

## Side Effects

- Registers `IInMemoryCache` as singleton.
- Registers `ICache` as singleton when `isDefault: true`.
- Registers a keyed `ICache` under the `memory` role key (`CacheConstants.MemoryCacheProvider`).
- Registers `IRemoteCache` adapter (plus `IRemoteCache<T>` and the `remote` role key) when `isDefault: true`.
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons.
- Registers `ICacheProvider` (shared, `TryAdd`).
- Named overloads register a keyed `ICache` under the instance name with its own options.
