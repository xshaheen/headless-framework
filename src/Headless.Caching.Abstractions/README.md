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
- `CacheValue<T>` - Cache result with HasValue semantics
- `CacheEntryOptions` - Factory-backed entry options. Only `Duration` is active today; future cache resilience knobs grow on this type.

## Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeout, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

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
                new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
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
