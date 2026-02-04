# Headless.Caching.Hybrid

Two-tier hybrid cache combining fast in-memory L1 cache with distributed L2 cache, featuring automatic cross-instance cache invalidation via messaging.

## Installation

```bash
dotnet add package Headless.Caching.Hybrid
```

## Prerequisites

- In-memory cache: `Headless.Caching.Memory`
- Distributed cache: `Headless.Caching.Redis`
- Messaging: Any messaging provider (e.g., `Headless.Messaging.Redis`)

## Usage

### Basic Setup

```csharp
services.AddInMemoryCache(isDefault: false);
services.AddRedisCache(options => options.ConnectionString = "localhost:6379");
services.AddMessaging(builder => builder.UseRedis("localhost:6379"));
services.AddHybridCache(options =>
{
    options.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
});
```

### Using the Cache

```csharp
public class ProductService(ICache cache)
{
    public async Task<Product?> GetProductAsync(string id, CancellationToken ct)
    {
        return (await cache.GetOrAddAsync(
            $"product:{id}",
            async token => await _repository.GetByIdAsync(id, token),
            TimeSpan.FromHours(1),
            ct
        )).Value;
    }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         HybridCache                              │
│  ┌─────────────┐    ┌─────────────┐    ┌──────────────────────┐ │
│  │ L1 Cache    │    │ L2 Cache    │    │ Message Bus          │ │
│  │ (InMemory)  │    │ (Redis)     │    │ (Pub/Sub)            │ │
│  │             │    │             │    │                      │ │
│  │ - Fast      │    │ - Shared    │    │ - Invalidation       │ │
│  │ - Per-inst. │    │ - Durable   │    │ - Cross-instance     │ │
│  └─────────────┘    └─────────────┘    └──────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Read Path

1. Check L1 (local in-memory) - fastest, per-instance
2. On L1 miss, check L2 (distributed) - slower but shared
3. On L2 miss, execute factory, populate both caches

### Write/Invalidation Path

1. Update L2 (distributed cache)
2. Update L1 (local cache)
3. Publish invalidation message
4. Other instances receive message and invalidate their L1

## Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `KeyPrefix` | `""` | Prefix for all cache keys |
| `DefaultLocalExpiration` | `5 minutes` | Default L1 TTL (uses L2 TTL if null) |
| `InstanceId` | Auto-generated | Unique ID for filtering self-originated messages |

## Exception Handling

| Scenario | Behavior |
|----------|----------|
| L2 write fails | Log warning, continue to populate L1 |
| Publish fails | Log warning, other instances serve stale until TTL |
| L1 write fails | Propagate exception (indicates serious issue) |
| L2 read fails | Propagate exception |
| `OperationCanceledException` | Always propagate |

## Metrics

The `HybridCache` exposes metrics:

```csharp
var cache = services.GetRequiredService<HybridCache>();
Console.WriteLine($"L1 hits: {cache.LocalCacheHits}");
Console.WriteLine($"Invalidation calls: {cache.InvalidateCacheCalls}");
```
