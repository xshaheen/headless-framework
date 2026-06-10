# Headless.Caching.DistributedLocks

Adapter that bridges the caching factory-lock seam (`ICacheFactoryLockProvider`) onto `IDistributedLock`, enabling opt-in multi-node cache stampede protection for entries that set `CacheEntryOptions.UseDistributedFactoryLock`.

## Problem Solved

The per-key factory lock in `Headless.Caching.Core` is process-local: with N app instances sharing one Redis cache, a popular key expiring can still run N concurrent factories — one per node. This package makes the factory single-flight across nodes: the node that wins a distributed lock runs the factory, the others wait on the lock and re-check the shared store, so the losers serve the winner's freshly written value instead of duplicating the work.

## Key Features

- `AddCachingDistributedFactoryLock()` registers `ICacheFactoryLockProvider` backed by the application's `IDistributedLock` registration (any `Headless.DistributedLocks.*` provider).
- Per-entry opt-in through `CacheEntryOptions.UseDistributedFactoryLock`; entries that do not set it pay zero cost.
- Lock resources are namespaced with a configurable prefix (default `cache:factory:`) so cache locks never collide with other lock consumers on the same backend.
- The seam timeout maps directly onto `DistributedLockAcquireOptions.AcquireTimeout`: `TimeSpan.Zero` is a single try-once attempt (used by eager refresh), `Timeout.InfiniteTimeSpan` waits unboundedly, and a finite value bounds the wait.
- Optional lease TTL override (`TimeUntilExpires`) as the backstop that frees the key when a node dies mid-factory.

## Design Notes

- The cross-node lock is a second layer, not a replacement. The coordinator always acquires the local per-key lock first, then the distributed lock, with the same wait budget the local lock used (the soft timeout when a fail-safe stale reserve can absorb the elapse, `LockTimeout` otherwise). Degradation on elapse therefore mirrors the local lock-timeout path exactly: serve stale when a reserve exists, degrade to a miss otherwise.
- After acquiring the distributed lock the coordinator re-checks the shared store before running the factory. The previous owner on another node may have just written a fresh value; the loser of the cross-node race serves the winner's value instead of refreshing again.
- The lease transfers through detached paths. On a soft timeout the lease moves into the background completion together with the local lock releaser, and the eager-refresh path holds it until the detached factory lands — the cross-node guard stays held until the write happens, not just until the caller returns.
- Eager refresh uses a single non-blocking attempt (`TimeSpan.Zero`). When the lock is held elsewhere another node is already refreshing, so the local node skips silently and leaves the still-fresh entry untouched.
- Lock release is best-effort: a failed release is logged and the lease TTL is the backstop, so a release failure never masks the cache operation's outcome. Keep `TimeUntilExpires` (or the lock provider's default lease) comfortably above the slowest expected factory run.
- Use it when the factory is expensive enough (slow query, paid API call) to outweigh a distributed lock round-trip per cold refresh. For cheap factories, per-node single-flight is already enough — N small duplicated calls are cheaper than N lock round-trips on every miss.

## Installation

```bash
dotnet add package Headless.Caching.DistributedLocks
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Any Headless.DistributedLocks provider works; the adapter resolves IDistributedLock.
builder.Services.AddHeadlessDistributedLocks(locks => locks.UseRedis());
builder.Services.AddRedisCache(options => options.ConnectionMultiplexer = redis);
builder.Services.AddCachingDistributedFactoryLock();
```

```csharp
public sealed class ReportService(ICache cache, IReportRepository repository)
{
    public async Task<Report?> GetDailyReportAsync(CancellationToken ct)
    {
        var cached = await cache.GetOrAddAsync(
            "report:daily",
            token => repository.BuildExpensiveReportAsync(token),
            new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(30),
                UseDistributedFactoryLock = true, // one node builds; others re-check the shared store
            },
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

Enabling `UseDistributedFactoryLock` without calling `AddCachingDistributedFactoryLock()` (or registering another `ICacheFactoryLockProvider`) fails the factory-backed read with `InvalidOperationException` instead of silently degrading to single-node behavior.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `ResourcePrefix` | `"cache:factory:"` | Prefix prepended to the cache key to form the distributed lock resource name; override to namespace cache locks away from other lock consumers sharing the backend. |
| `TimeUntilExpires` | `null` | Lease TTL applied to each acquired factory lock; `null` uses the distributed lock provider's default lease duration. The TTL frees the key when a node dies mid-factory. |

```csharp
builder.Services.AddCachingDistributedFactoryLock(options =>
{
    options.ResourcePrefix = "myapp:cache:factory:";
    options.TimeUntilExpires = TimeSpan.FromMinutes(2);
});
```

## Dependencies

- `Headless.Caching.Core`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `ICacheFactoryLockProvider` as singleton (`TryAdd`, so an existing registration wins).
- Registers `CacheFactoryLockOptions` as a singleton option value.
- Requires an `IDistributedLock` registration at resolution time; none is added by this package.
