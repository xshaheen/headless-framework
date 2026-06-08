# Headless.Caching.Abstractions

Defines the unified caching interface for both in-memory and distributed cache implementations.

## Problem Solved

Provides a provider-agnostic caching API, enabling seamless switching between memory and Redis caches without changing application code.

## Key Features

- `ICache` - Core interface for all cache operations:
  - Upsert/Get/Remove with expiration
  - Bulk operations (UpsertAll, GetAll, RemoveAll)
  - Prefix-based operations (GetByPrefix, RemoveByPrefix)
  - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower)
  - Set operations (SetAdd, SetRemove, GetSet)
- `IInMemoryCache` - Marker interface for in-memory implementations
- `IRemoteCache` - Marker interface for remote implementations
- `ICache<T>` - Strongly-typed cache wrapper
- `CacheValue<T>` - Cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - Factory-backed entry options: `Duration`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, and `FailSafeThrottleDuration`.

## Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeout, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

Fail-safe is opt-in and only applies to `GetOrAddAsync`. Direct writes keep the `TimeSpan?` API and write logical expiration equal to physical expiration. A stale value served by fail-safe returns `CacheValue<T>.IsStale = true` only for the activating call; reads during the throttle window are logical hits and return `IsStale = false`.

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
