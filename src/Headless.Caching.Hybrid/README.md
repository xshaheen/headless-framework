# Headless.Caching.Hybrid

Two-tier cache combining in-memory L1 with remote L2 and cross-instance invalidation through messaging.

## Problem Solved

Provides one `ICache` implementation that reads from a fast local cache first, falls back to a shared remote cache, and invalidates other instances when writes change cached data.

## Key Features

- L1 + L2 read path: local in-memory first, remote cache second.
- Write path updates L2, updates L1, and publishes invalidation.
- Miss path executes the factory once through the shared `FactoryCacheCoordinator`.
- Supports strongly typed `ICache<T>`.
- Uses `DefaultLocalExpiration` to keep L1 fresher than L2.
- Tag invalidation across both tiers plus a `Tag` invalidation message on the backplane so other instances drop their L1 copies.
- Named tier selection (`LocalCacheName`/`RemoteCacheName`) and named hybrid instances via `AddHybridCache(name, ...)`.
- Opt-in auto-recovery: transient L2/backplane outages queue failed single-key operations and replay them on recovery.
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Register an in-memory cache as non-default, then a remote cache, then the hybrid cache. The hybrid cache becomes the default `ICache` when `isDefault: true`. Incoming invalidations are handled by `HybridCacheInvalidationConsumer` (`IConsume<CacheInvalidationMessage>`); register it with the application's messaging setup (explicit `ForMessage<CacheInvalidationMessage>(...)` registration or assembly scanning) — without it this instance publishes invalidations but never receives them. The consumer routes to the default `HybridCache` only: named hybrid instances publish invalidations but are not wired to the backplane consumer, so their L1 tiers converge only through `DefaultLocalExpiration` TTL (known limitation).

Hybrid fail-safe and factory timeouts use the same coordinator semantics as the other providers. A stale reserve can come from L1 or L2. On soft timeout, the stale value is returned to the caller and the detached background factory writes through the composite store on success, so both tiers are refreshed. Eager refresh and conditional (`NotModified`) refresh likewise run through the composite store, so a refresh extends or replaces the entry in both tiers. `DefaultLocalExpiration` still caps L1 physical retention independently of the L2 duration.

`RemoveByTagAsync` publishes the `Tag` invalidation first (minimizing the window in which another instance re-populates its L1 from a not-yet-invalidated L2), then removes from L2, then from its own L1, and returns the L2 removed count. Receivers apply the tag invalidation to their L1 through the same version-pinned `RemoveByTagAsync` semantics.

On reads, Hybrid promotes L2 entries into L1 only when they are logically fresh. Promoting stale reserves on every read would amplify L1 writes and could overwrite a newer L1 reserve. Fail-safe activation and background success still write through the composite store intentionally.

Publish failures are non-fatal. Other instances may keep their L1 value until TTL or the next successful invalidation, while the local instance still observes the write result.

## Installation

```bash
dotnet add package Headless.Caching.Hybrid
```

## Quick Start

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddInMemoryCache(isDefault: false);
services.AddSingleton<IConnectionMultiplexer>(redis);
services.AddRedisCache(options => options.ConnectionMultiplexer = redis);
services.AddHeadlessMessaging(builder => builder.UseRedis("localhost:6379"));
services.AddHybridCache(options =>
{
    options.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
});
```

```csharp
public sealed record Product(string Id, string Name);

public interface IProductRepository
{
    ValueTask<Product?> GetByIdAsync(string id, CancellationToken cancellationToken);
}

public sealed class ProductService(ICache cache, IProductRepository repository)
{
    public async Task<Product?> GetProductAsync(string id, CancellationToken ct)
    {
        var cached = await cache.GetOrAddAsync(
            $"product:{id}",
            token => repository.GetByIdAsync(id, token),
            new CacheEntryOptions
            {
                Duration = TimeSpan.FromHours(1),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(6),
                FactorySoftTimeout = TimeSpan.FromMilliseconds(250),
                FactoryHardTimeout = TimeSpan.FromSeconds(3),
            },
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

Named tiers — point the hybrid at named L1/L2 registrations instead of the default `IInMemoryCache`/`IRemoteCache`:

```csharp
services.AddInMemoryCache("hot-l1", options => options.MaxItems = 5000);
services.AddRedisCache("hot-l2", options => options.ConnectionMultiplexer = redis);
services.AddHybridCache(options =>
{
    options.LocalCacheName = "hot-l1";   // must implement IInMemoryCache
    options.RemoteCacheName = "hot-l2";  // must implement IRemoteCache
});
```

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` extension overloads; when `null` those overloads throw. |
| `DefaultLocalExpiration` | `5 minutes` | Default L1 TTL; when null, L1 uses the L2 expiration. |
| `InstanceId` | Auto-generated | Unique ID for filtering self-originated invalidation messages. |
| `LocalCacheName` | `null` | Keyed `ICache` registration to use as the L1 tier; must implement `IInMemoryCache` or resolution throws. `null` uses the default `IInMemoryCache`. |
| `RemoteCacheName` | `null` | Keyed `ICache` registration to use as the L2 tier; must implement `IRemoteCache` or resolution throws. `null` uses the default `IRemoteCache`. |
| `EnableAutoRecovery` | `false` | Opt-in self-healing for transient L2/backplane outages: failed single-key L2 writes/removes and failed invalidation publishes are queued and replayed on recovery instead of surfacing. |
| `AutoRecoveryMaxItems` | `128` | Max pending recovery items (one per key); on overflow the earliest-expiring item is evicted (or the incoming item is rejected when it expires soonest). |
| `AutoRecoveryMaxRetries` | `8` | Failed replay attempts before a pending item is dropped with a warning. |
| `AutoRecoveryDelay` | `5 seconds` | Recovery loop cadence and the back-off barrier armed after a failed replay. |

Auto-recovery (design reference: FusionCache's auto-recovery, adapted) keeps one pending operation per key — newer operations replace older ones, and any successful L2 write for a key clears its pending item. A queued set is only replayed while the L1 entry still carries the exact stamp the write produced (L1 is the source of truth; otherwise the item is dropped as obsolete), and incoming invalidations from other instances drop older queued items so a replay cannot resurrect stale data (a message without a timestamp is treated as newer — conservative drop; tag invalidations are not conflict-matched because queued items are not indexed by tag). With auto-recovery enabled, a failing single-key L2 write no longer propagates to the caller: the call succeeds against L1 in degraded mode (logged as a warning), so callers must tolerate L2 lagging L1 until replay. Items without a natural expiry (removes, publishes) are retained for `AutoRecoveryDelay × AutoRecoveryMaxRetries`; replay passes run oldest-first and stop at the first failure, arming the back-off barrier so a sustained outage does not become a retry storm. Bulk, atomic (increment/set-if), and set operations are never captured.

For factory-backed sliding entries, `DefaultLocalExpiration` caps the L1 copy only. Hybrid revalidates sliding L1 hits against L2 before re-arm so L2 keeps the original `Duration` as the absolute cap. If L2 is unavailable, a fresh L1 sliding value can still be returned, but the read is not re-armed.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Bus.Abstractions`

## Side Effects

- Registers `HybridCache` as singleton.
- Registers `ICache` as singleton when `isDefault: true`.
- Registers keyed `ICache` under `CacheConstants.HybridCacheProvider`.
- Registers `ICache<T>` as singleton.
- Registers `ICacheProvider` (shared, `TryAdd`).
- Named overloads register a keyed `ICache` under the instance name with its own options and tier resolution.
- Reads configured `HybridCacheOptions`.
- Publishes cache invalidation messages through the registered message bus.
- Runs a `TimeProvider`-driven recovery timer when `EnableAutoRecovery` is set.
