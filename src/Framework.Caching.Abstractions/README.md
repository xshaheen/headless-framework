# Framework.Caching.Abstractions

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
- `IDistributedCache` - Marker interface for distributed implementations
- `ICache<T>` - Strongly-typed cache wrapper
- `CacheValue<T>` - Cache result with HasValue semantics

## Installation

```bash
dotnet add package Framework.Caching.Abstractions
```

## Usage

```csharp
public sealed class ProductService(ICache cache)
{
    public async Task<Product?> GetProductAsync(int id, CancellationToken ct)
    {
        var key = $"product:{id}";
        var cached = await cache.GetAsync<Product>(key, ct).AnyContext();

        if (cached.HasValue)
            return cached.Value;

        var product = await _repository.GetAsync(id, ct).AnyContext();
        if (product is not null)
            await cache.UpsertAsync(key, product, TimeSpan.FromMinutes(10), ct).AnyContext();

        return product;
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None. This is an abstractions package.
