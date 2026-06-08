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
- Shared `GetOrAddAsync` fail-safe, factory timeout, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Register an in-memory cache as non-default, then a remote cache, then the hybrid cache. The hybrid cache becomes the default `ICache` when `isDefault: true`.

Hybrid fail-safe and factory timeouts use the same coordinator semantics as the other providers. A stale reserve can come from L1 or L2. On soft timeout, the stale value is returned to the caller and the detached background factory writes through the composite store on success, so both tiers are refreshed. `DefaultLocalExpiration` still caps L1 physical retention independently of the L2 duration.

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

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultLocalExpiration` | `5 minutes` | Default L1 TTL; when null, L1 uses the L2 expiration. |
| `InstanceId` | Auto-generated | Unique ID for filtering self-originated invalidation messages. |

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
- Reads configured `HybridCacheOptions`.
- Publishes cache invalidation messages through the registered message bus.
