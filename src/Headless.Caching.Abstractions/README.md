# Headless.Caching.Abstractions

Defines the unified caching interface for in-memory, distributed, and hybrid cache implementations.

## Problem Solved

Provides a provider-agnostic caching API so applications can switch between memory, Redis, and hybrid caches without changing call sites.

## Key Features

- `ICache` - core interface for cache operations:
  - Upsert/Get/Remove with expiration
  - Bulk operations (UpsertAll, GetAll, RemoveAll)
  - Prefix-based operations (GetByPrefix, RemoveByPrefix)
  - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower)
  - Set operations (SetAdd, SetRemove, GetSet)
- `IInMemoryCache` - marker interface for in-memory implementations.
- `IRemoteCache` - marker interface for remote implementations.
- `ICache<T>` - strongly typed cache wrapper.
- `CacheValue<T>` - cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - factory-backed entry options: `Duration`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration`, `FactorySoftTimeout`, `FactoryHardTimeout`, and `BackgroundFactoryCeiling`.
- `CacheFactoryTimeoutException` - `TimeoutException` subtype thrown when a hard factory timeout fires without a stale fallback.

## Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeouts, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

Fail-safe is opt-in and only applies to `GetOrAddAsync`. Direct writes keep the `TimeSpan?` API and write logical expiration equal to physical expiration. A stale value served by fail-safe returns `CacheValue<T>.IsStale = true` only for the activating call; reads during the throttle window are logical hits and return `IsStale = false`.

Factory soft timeouts are useful only when fail-safe is enabled and a stale reserve exists. In that case the caller gets stale data and the factory continues in the background. Soft timeouts configured without fail-safe are inert and logged once per key. Factory hard timeouts cancel or abandon the factory; they serve stale when possible and throw `CacheFactoryTimeoutException` on a cold cache.

Background completion uses a detached coordinator-owned cancellation token, not the caller token. A request token may be cancelled after the stale response is returned and the background refresh can still finish. Factories used with soft timeouts must not capture request-scoped disposables; create a fresh dependency scope inside the factory when scoped services are required after the request path returns.

`BackgroundFactoryCeiling` defaults to `Timeout.InfiniteTimeSpan` (no ceiling): a detached background factory runs to completion, matching the behavior of comparable caches (FusionCache, Caffeine, sturdyc). Set a finite, positive value to bound how long a detached factory may hold the per-key lock. When the ceiling fires, the coordinator cancels the internal token and releases the lock: cooperative factories stop, while non-cooperative factories may continue running untracked, but the coordinator gates late success writes so an abandoned factory cannot clobber a newer cache value through the timeout path.

## Installation

```bash
dotnet add package Headless.Caching.Abstractions
```

## Quick Start

```csharp
public sealed record Product(int Id, string Name);

public interface IProductRepository
{
    ValueTask<Product?> GetAsync(int id, CancellationToken cancellationToken);
}

public sealed class ProductService(ICache cache, IProductRepository repository)
{
    public async Task<Product?> GetProductAsync(int id, CancellationToken ct)
    {
        var key = $"product:{id}";
        var cached = await cache
            .GetOrAddAsync(key, token => repository.GetAsync(id, token), TimeSpan.FromMinutes(10), ct)
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }

    public async Task<Product?> GetProductWithOptionsAsync(int id, CancellationToken ct)
    {
        var key = $"product:{id}";
        var cached = await cache
            .GetOrAddAsync(
                key,
                token => repository.GetAsync(id, token),
                new CacheEntryOptions
                {
                    Duration = TimeSpan.FromMinutes(10),
                    IsFailSafeEnabled = true,
                    FailSafeMaxDuration = TimeSpan.FromHours(1),
                    FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                    FactorySoftTimeout = TimeSpan.FromMilliseconds(200),
                    FactoryHardTimeout = TimeSpan.FromSeconds(2),
                    BackgroundFactoryCeiling = TimeSpan.FromMinutes(2),
                },
                ct
            )
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None. This is an abstractions package.
