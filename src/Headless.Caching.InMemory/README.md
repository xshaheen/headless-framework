# Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

## Problem Solved

Provides process-local caching through the unified `ICache` abstraction, suitable for development, single-instance deployments, or an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation.
- Can serve as the default `ICache` (`setup.UseInMemory(...)`) or as the memory tier of a default hybrid (`setup.AddMemoryTier(...)`).
- Supports strongly typed `ICache<T>`.
- Named cache instances via `setup.AddNamed(name, i => i.UseInMemory(...))`, resolvable as keyed `ICache` services or through `ICacheProvider`.
- Automatic memory management with configurable limits (`MaxItems` plus LRU eviction).
- O(1) logical tag invalidation and `ClearAsync` through per-tag and clear-generation timestamp markers (Family-2), compared against each entry's birth time on read.
- Optional value cloning for isolation.
- Implements `IBufferCache` — stores framed bytes, slices to the caller's `IBufferWriter<byte>` on read, copies the `ReadOnlySequence<byte>` on write, with the same stamping as the generic path (no intermediate `byte[]` on the fast path).
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Memory cache stores entries in an internal envelope with logical expiration, physical expiration, and optional sliding expiration. Direct writes set logical and physical timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Sliding `GetOrAddAsync` keeps physical expiration as the absolute cap and re-arms logical expiration on value reads only; `GetExpirationAsync`, `ExistsAsync`, key listing, and count operations do not extend the idle window. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`. `ExpireAsync` only pulls logical expiration to now when the entry carries a genuine fail-safe reserve (physical outliving logical on a non-sliding entry); a sliding entry's `physical > logical` surplus is its absolute cap, not a reserve, so `ExpireAsync` hard-removes it instead of preserving it.

Tag and clear invalidation are Family-2 logical: `RemoveByTagAsync` stamps a per-tag marker (`tag -> DateTime`) to now in O(1) and `ClearAsync` bumps a global clear-generation marker; neither enumerates entries. On read, an entry is invalidated when its birth time (`CreatedAt`) predates the newest marker it is subject to (the max of the clear marker and every per-tag marker it carries) — direct reads miss, the factory coordinator demotes it to a fail-safe reserve. Markers are never pruned: residue is bounded by the process-lifetime distinct-tag cardinality, and a re-created entry (newer `CreatedAt`) is naturally not invalidated by an older marker. `FlushAsync` resets the markers along with the keyspace (no entry survives to be invalidated); `ClearAsync` keeps them. Because invalidation is logical, `GetCountAsync` and key listing still see logically-invalidated entries until physical eviction.

Long `FailSafeMaxDuration` values and long sliding absolute caps can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments. Soft-timeout and eager background refreshes also hold values in process while the detached factory runs; `BackgroundFactoryCeiling` (infinite by default) optionally bounds how long a cooperative refresh keeps the per-key lock when set to a finite value.

Set members (`SetAddAsync`/`SetRemoveAsync`/`GetSetAsync`) compare strings with ordinal (case-sensitive) equality, matching the distributed providers' byte-exact membership; non-string members compare with default `Equals` here, while Redis compares serialized bytes — custom `Equals` overrides can behave differently across providers. `GetSetAsync` returns `CacheValue.NoValue` (`HasValue == false`, `Value == null`) whenever the requested page has no members — an absent key, a set whose live members are all expired, and a `pageIndex` past the last live member all read as a miss, matching Redis exactly (no non-null empty collection, no key-existence probe). `HasValue` reflects whether the requested page has members, not whether the key exists.

`Memory` in Headless caching docs means this package, `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.

## Installation

```bash
dotnet add package Headless.Caching.InMemory
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Pick one shape — AddHeadlessCaching may be called only once per service collection.

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());

builder.Services.AddHeadlessCaching(setup =>
    setup.UseInMemory(options =>
    {
        options.MaxItems = 10000;
        options.CloneValues = true;
        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
    })
);

// As the memory tier of a default hybrid instead of the default ICache (see Headless.Caching.Hybrid):
builder.Services.AddHeadlessCaching(setup =>
{
    setup.AddMemoryTier();
    setup.AddRedisTier(options => options.ConnectionMultiplexer = redis); // redis: ConnectionMultiplexer.Connect(...)
    setup.UseHybrid();
});
```

Named instances (independent options, resolved by name; the setup still needs exactly one default `Use*`):

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseInMemory();
    setup.AddNamed("orders", i => i.UseInMemory(options => options.MaxItems = 1000));
});

public sealed class OrderService(ICacheProvider cacheProvider)
{
    private readonly ICache _cache = cacheProvider.GetCache("orders");
}
```

Names must be non-empty and must not be reserved: the `CacheConstants` role keys (`Headless.Caching:{Memory,Remote,Hybrid}`) and any name under the `Headless.Caching:` namespace are rejected with `ArgumentException`, and duplicate names throw. Each named instance must select exactly one provider. Named instances never touch the default (unkeyed) `ICache`.

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

- Registers `IInMemoryCache` as singleton (`setup.UseInMemory(...)` and `setup.AddMemoryTier(...)`).
- Registers `ICache` as singleton when used as the default provider (`setup.UseInMemory(...)`).
- Registers a keyed `ICache` under the `CacheConstants.MemoryCacheProvider` role key (`Headless.Caching:Memory`).
- Registers `ICache<T>` as singleton when used as the default provider.
- Registers `ICacheProvider` (shared, `TryAdd`).
- `setup.AddNamed(name, i => i.UseInMemory(...))` registers a keyed `ICache` under the instance name with its own options.
