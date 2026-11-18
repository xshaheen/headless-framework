---
domain: Caching
packages: Caching.Abstractions, Caching
---

# Caching

> One `ICache` abstraction over in-memory, Redis, and hybrid L1+L2 providers, with fail-safe, factory timeouts, refresh, tag invalidation, and cross-node stampede protection on the factory path.

## Orientation

Install `Headless.Caching.Abstractions` and one provider package. Register every cache in one `services.AddHeadlessCaching(setup => ...)` call (`Headless.Caching`). Provider packages add `Use*`, `Add*Tier`, and `AddNamed` members to the setup builder. Application code injects `ICache`.

- **Single instance or development:** `Headless.Caching.InMemory` with `setup.UseInMemory()`.
- **Several instances sharing a cache:** `Headless.Caching.Redis` with `setup.UseRedis(...)`.
- **Local read speed plus a shared L2:** `Headless.Caching.Hybrid` with `setup.AddMemoryTier()`, `setup.AddRedisTier(...)`, `setup.UseHybrid()`, and one backplane: `setup.UseRedisBackplane()`, `setup.UseMessagingBackplane()` (`Headless.Caching.Hybrid.Messaging`), or `setup.UseSingleInstanceBackplane()`.
- **A component that needs BCL `IDistributedCache`**, such as ASP.NET Core Session: `Headless.Caching.Bcl` with `setup.UseBclCache(...)`.
- **ASP.NET Core output caching:** `Headless.Caching.OutputCache` with `setup.UseOutputCache(...)`.
- **One factory run across nodes:** `Headless.Caching.DistributedLocks` with `setup.UseDistributedFactoryLock()`, then `CacheEntryOptions.UseDistributedFactoryLock = true` per entry.

`GetOrAddAsync` is the factory-backed read: it takes `CacheEntryOptions` and gives you fail-safe, factory timeouts, sliding expiration, eager refresh, jitter, tags, and the distributed lock. Everything else on `ICache` is a direct operation: scalar and bulk reads and writes, prefix operations, atomic compare-and-replace and numeric operations, set operations, and invalidation.

## Agent Rules

- Inject `ICache` (`Headless.Caching.Abstractions`) for application caching. Inject `IDistributedCache` only for a BCL consumer such as ASP.NET Core Session, backed by `Headless.Caching.Bcl`. Inject `IRemoteCache` only when the code must use the shared tier.
- Back ASP.NET Core output caching with `Headless.Caching.OutputCache`, never with `IDistributedCache`. ASP.NET Core rejects `IDistributedCache` as an output-cache store because it has no atomic tag operations.
- `Memory` in these docs means `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.
- Register every cache instance (default, tiers, named, and cross-cutting options) in one `AddHeadlessCaching` call, with exactly one default `Use{InMemory,Redis,Hybrid}`. A second `AddHeadlessCaching` call on the same service collection throws. A setup that throws registers nothing.
- A hybrid cache needs an L2 tier and a backplane, or the host fails to start. See [Hybrid cache](caching/hybrid.md).
- `ICache<T>` is the only typed facade; it wraps the default `ICache`. `IRemoteCache<T>` and `IInMemoryCache<T>` do not exist. For a specific tier, use `IRemoteCache` or `IInMemoryCache` with method-level generics, or `ICacheProvider.GetCache(name)`.
- Check `CacheValue<T>.HasValue` before you read `.Value`. A miss returns `HasValue = false`; a cached `null` returns `HasValue = true`.
- Pass `CacheEntryOptions` to `GetOrAddAsync`. A `TimeSpan` converts to it implicitly when you need only a duration. Named-argument callers write `options:`, not `expiration:`.
- The option-less `GetOrAddAsync` overloads use the instance's `DefaultEntryOptions` and throw `InvalidOperationException` when it is unset. Set `options.DefaultEntryOptions` at registration to use them; no default duration exists.
- Use `UpsertAsync(key, value, TimeSpan?)` for plain writes. Use `UpsertEntryAsync(key, value, options)` only when a write needs `CacheEntryOptions` (fail-safe reserve, eager point, sliding, tags); it reads before it writes, so keep it off hot paths. The other direct writes (`TryInsertAsync`, set and numeric operations) take `TimeSpan?` and never create a fail-safe reserve.
- A zero or negative `CacheEntryOptions.Duration` means "expire now": the write evicts the key on every provider instead of throwing `ArgumentOutOfRangeException`. `SetAddAsync` is the exception: a zero or negative expiration removes only the named members, through `SetRemoveAsync`, and leaves the rest of the set.
- A `TimeSpan.MaxValue` expiration, or any that jitter, the fail-safe window, or the eager point pushes past `DateTime.MaxValue`, means no expiry on every provider; it never throws.
- Choose `RemoveAsync` or `ExpireAsync` by whether the fail-safe reserve must survive. `RemoveByTagAsync` and `ClearAsync` are logical and keep entries physically present. See [Invalidation](caching/invalidation.md).
- Fail-safe is opt-in per entry (`IsFailSafeEnabled`) and only `GetOrAddAsync` reads the reserve. Caller cancellation never serves stale. See [Factory-backed reads](caching/factory.md).
- Do not capture request-scoped disposables in a `GetOrAddAsync` factory that can run in the background (soft timeout or eager refresh). Create a fresh scope inside the factory.
- `CacheEntryOptions.UseDistributedFactoryLock` needs `setup.UseDistributedFactoryLock()` and a registered `IDistributedLock`; without the adapter the read throws `InvalidOperationException`. Reserve it for expensive factories: single-flight per node is always on.
- Namespace keys per application or module with the provider's `KeyPrefix`.
- For tenant-owned values, register `services.AddTenantScopedCache<T>()` (`Headless.MultiTenancy`) instead of adding the tenant to keys yourself. It registers `ICache<T>` with keys under `t:{tenantId}:`, refuses an operation with no ambient tenant, and evicts one tenant's entries with `RemoveByPrefixAsync("")`. See [Tenant-Scoped Blobs and Caches](multi-tenancy/blobs-and-caches.md#tenant-scoped-blobs-and-caches).
- Do not use a reserved name for a named instance: the role keys `Headless.Caching:Memory`, `Headless.Caching:Remote`, and `Headless.Caching:Hybrid`, or any name under `Headless.Caching:`. `setup.AddNamed` throws `ArgumentException` for a reserved, empty, or duplicate name.
- The cache does not limit key length. Enforce key limits at your own boundaries.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Caching.InMemory` | One process, tests, local development, or the L1 of a hybrid. | Several instances must share cache state. | Fastest, but per process and held in memory. |
| `Headless.Caching.Redis` | Several instances need a shared cache and Redis already runs. | The app cannot depend on Redis. | Shared, with atomic operations, at the cost of network latency and Redis timeout tuning. |
| `Headless.Caching.Hybrid` | Reads are hot enough to gain from L1, and L2 keeps instances coherent. | The hosts share neither Redis nor a messaging transport for the backplane, or L1 staleness is unacceptable. | Fast local reads plus sharing, with a backplane to run and a short L1 staleness window. |

`Headless.Caching.DistributedLocks`, `Headless.Caching.Bcl`, and `Headless.Caching.OutputCache` are not providers. Each sits on top of the provider you choose; Bcl and OutputCache each use their own named instance.

## Register caches

`AddHeadlessCaching` has four kinds of slot:

| Slot | Members | Rule |
| --- | --- | --- |
| Default | `UseInMemory`, `UseRedis`, `UseHybrid` | Exactly one. It becomes the unnamed `ICache` and `ICache<T>`. |
| Tier | `AddMemoryTier`, `AddRedisTier` | At most one per role. The L1 and L2 of a default hybrid; they do not touch the unnamed `ICache`. A tier for a role the default already claims, such as `UseRedis` plus `AddRedisTier`, throws at registration. |
| Named | `AddNamed(name, i => i.Use{InMemory,Redis,Hybrid}(...))` | Any number, each with a unique, non-reserved name and exactly one provider. Each has its own options; a Redis instance can have its own multiplexer. |
| Cross-cutting | `UseDistributedFactoryLock`, `UseBclCache`, `UseOutputCache`, `Use*Backplane` | Optional. |

- `UseInMemory`, `UseRedis`, and `UseHybrid` each take `Action<TOptions>`, `Action<TOptions, IServiceProvider>`, or `IConfiguration`. `UseInMemory` and `UseHybrid` also take no arguments. A named instance binds its options per name, the `IConfiguration` shape included.
- Resolve a named instance with `ICacheProvider.GetCache(name)`, or `GetCacheOrNull(name)`, or as a keyed `ICache`. `ICacheProvider.RegisteredNames` lists the `AddNamed` names only, so validate an outside name against it before you resolve.
- The tiers stay reachable by role through `ICacheProvider` under `CacheConstants.MemoryCacheProvider`, `CacheConstants.RemoteCacheProvider`, and `CacheConstants.HybridCacheProvider`.

## Topics

| Read when | Topic |
| --- | --- |
| You set `CacheEntryOptions` beyond `Duration`: fail-safe, factory timeouts, lock waits, eager refresh, sliding expiration, jitter, conditional (HTTP 304) refresh, per-tier skip flags, or the distributed factory lock | [Factory-backed reads](caching/factory.md) |
| You invalidate entries: remove versus expire, tags, `ClearAsync` versus `FlushAsync`, or how fast other instances see it | [Invalidation](caching/invalidation.md) |
| You run a hybrid L1+L2 cache: backplane choice, L2 timeouts and circuit breaker, auto-recovery, or a custom backplane or L2 | [Hybrid cache](caching/hybrid.md) |
| You configure `Headless.Caching.InMemory` or `Headless.Caching.Redis`, the Redis backplane, or Redis-specific behavior such as timeouts, cluster, and stored format | [InMemory and Redis providers](caching/providers.md) |
| You cache opaque bytes, need `IDistributedCache`, or back ASP.NET Core output caching | [Byte payloads and ASP.NET Core adapters](caching/adapters.md) |
| You export cache metrics or traces, or subscribe to `cache.Events` | [Observability](caching/observability.md) |
| You implement your own cache provider or hybrid L2: the store contract, markers, conditional-write rules, and stored format | [Custom cache providers](caching/custom-provider.md) |

---

## Headless.Caching.Abstractions

The cache contracts: `ICache`, its typed facade, the tier interfaces, and the options and result types.

### Install

```bash
dotnet add package Headless.Caching.Abstractions
```

### Use the cache

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
                    EagerRefreshThreshold = 0.8f, // refresh in the background after 8 minutes
                    IsFailSafeEnabled = true,
                    FailSafeMaxDuration = TimeSpan.FromHours(1),
                    FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                    FactorySoftTimeout = TimeSpan.FromMilliseconds(200),
                    FactoryHardTimeout = TimeSpan.FromSeconds(2),
                    Tags = ["products", $"product:{id}"],
                },
                ct
            )
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }
}
```

**Verify:** in a test against the registered cache, the first `GetProductAsync` runs the repository and the second returns the cached value without calling it.

### API

| Type | Use |
| --- | --- |
| `ICache` | Every cache operation. See the operation notes below. |
| `ICache<T>` | The same operations bound to one value type, over the default `ICache`. |
| `IInMemoryCache` | The L1 tier. Adds no members. |
| `IRemoteCache` | The L2 tier. Adds `GetWithExpirationAsync<T>` and `GetAllWithExpirationAsync<T>`, which return values with their remaining TTL in one round trip. |
| `ICacheProvider` | Resolves named instances and role keys: `GetCache(name)`, `GetCacheOrNull(name)`, `RegisteredNames`. |
| `CacheValue<T>` | A read result: `HasValue`, `Value`, and `IsStale`, which is `true` only for a call that fail-safe served stale. |
| `CacheEntryOptions` | Options for `GetOrAddAsync` and `UpsertEntryAsync`. See [Factory-backed reads](caching/factory.md). |
| `CacheFactoryContext<T>`, `CacheFactoryResult<T>` | The conditional-factory contract. See [Conditional refresh](caching/factory.md#conditional-refresh). |
| `CacheOptions` | Base provider options: `KeyPrefix`, `DefaultEntryOptions`, and `CacheName`, the telemetry name of the instance. |
| `CacheFactoryTimeoutException` | Thrown when `FactoryHardTimeout` fires with no stale value to serve. Derives from `TimeoutException`. |
| `IBufferCache`, `BufferCacheExtensions` | Byte payloads without intermediate copies. See [Byte payloads](caching/adapters.md#byte-payloads). |
| `ICacheEvents` | The `cache.Events` hooks. See [Events](caching/observability.md#events). |
| `ScopedCache<T>` | An `ICache<T>` that prefixes every key with `{scope}:`, reading the scope from a delegate on each call. See below. |

### Operation notes

- **Sets.** String members compare ordinally (case-sensitive) on every provider. Other member types compare by serialized bytes on Redis and by `Equals` on InMemory, so a custom `Equals` or a non-canonical serializer can behave differently across providers. `GetSetAsync` returns `HasValue = false` with a `null` `Value` whenever the requested page has no members: an absent key, an empty set, a set whose members all expired, and a page past the end all read the same, with no extra round trip. `Value` is never an empty collection.
- **`SetIfHigherAsync` and `SetIfLowerAsync`** return the difference (`new - old` or `old - new`) when they update, `0` when they do not, and the stored value when the key was absent. A `0` is ambiguous when `0` is a valid stored value; call `ExistsAsync` first when that matters.
- **`RefreshAsync(key)`** re-arms a sliding entry without reading its value; see [Sliding expiration](caching/factory.md#sliding-expiration).
- **Prefix operations** (`GetByPrefixAsync`, `RemoveByPrefixAsync`, `GetAllKeysByPrefixAsync`, `GetCountAsync`) see entries a tag or clear invalidated until they physically expire.

### Helpers

- **`SyncWithSourceAsync(keys, readSource, expiration)`** on `ICache<T>` (`CacheSourceSyncExtensions`): call it after a write to the source of truth commits. For each key it reads the entry, then the source, then swaps the entry with `TryReplaceIfEqualAsync` (or `TryInsertAsync` when absent), and retries when another write landed in between. Writers whose cache writes land out of order with their commits still leave the last committed value. Every value `readSource` returns must be new and compare by value, such as a record carrying a fresh random stamp; otherwise a slow writer's swap can match a newer write of an equal value. A key still contended after four rounds is removed, and so is a key the source omits.
- **`IServiceProvider.GetRemoteOrDefaultCache()`** returns the registered `IRemoteCache`, else the default `ICache`, and throws `InvalidOperationException` when neither exists. Use it for state that only round-trips through the shared tier, such as atomic counters or compare-and-swap records: on a hybrid host it skips the L1 write and the invalidation broadcast every hybrid write pays.
- **`ScopedCache<T>`** keeps key, prefix, and count operations inside its scope. `RemoveByTagAsync`, `ClearAsync`, and `FlushAsync` act on the whole underlying cache, and `Events` is a no-op. `AddTenantScopedCache<T>()` registers one keyed by the ambient tenant.

---

## Headless.Caching

The `AddHeadlessCaching` entry point, the setup builder, and the factory engine every provider runs.

### Setup

```bash
dotnet add package Headless.Caching
```

Provider packages reference it, so you rarely add it by hand. The `Use*` and `Add*Tier` members come from the provider packages:

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis); // default slot: exactly one Use* required
    setup.AddNamed(
        "sessions",
        i =>
            i.UseRedis(options =>
            {
                options.ConnectionMultiplexer = redis;
                options.KeyPrefix = "sessions:";
            })
    );
    setup.UseDistributedFactoryLock(); // cross-cutting opt-in (Headless.Caching.DistributedLocks)
});
```

**Verify:** a setup with no default `Use*`, two defaults, a reserved or duplicate name, or a second `AddHeadlessCaching` call throws at registration. With Redis, the readiness checks `cache-redis` and `cache-redis-sessions` report each instance; see [Health checks](utilities.md#health-checks).

### Configuration

Set these on the setup builder:

| Setup option | Default | Description |
| --- | --- | --- |
| `IncludeKeyInTraces` | `false` | Allows raw cache keys on spans. Keys are never metric dimensions. |
| `EventBufferCapacity` | 2,048 | Event signals buffered per cache; a new signal is dropped when the buffer is full. |
| `EventShutdownDrainTimeout` | 2 seconds | Longest wait, on cache disposal, for accepted event signals before they are abandoned. |
| `EventHandlerErrorLogLevel` | `Warning` | Log level for an exception thrown by a `cache.Events` handler. |

### Runtime behavior

- `AddHeadlessCaching` checks every slot rule first, then applies tiers, the default, named instances, and cross-cutting options in that order. A failed setup registers nothing.
- Registers `ICacheProvider` as a singleton (`TryAdd`).
- **Custom providers.** To write your own provider or L2, follow [Custom cache providers](caching/custom-provider.md). `ICacheFactoryLockProvider` is the seam for a custom cross-node factory lock; its `TryAcquireAsync(cacheName, key, timeout, cancellationToken)` receives the instance name (`default` for the unnamed cache) and the unbounded key, so keep instances apart and map long keys into your lock store's name limit.
