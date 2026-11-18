---
domain: Caching
packages: Caching.InMemory, Caching.Redis
---

# InMemory and Redis providers

> Part of [Caching](../caching.md). Setup, options, and provider-specific behavior of `Headless.Caching.InMemory` and `Headless.Caching.Redis`, including the Redis backplane for hybrid caches.

## Headless.Caching.InMemory

In-process cache for single-instance applications, tests, and the L1 of a hybrid cache.

### Setup

```bash
dotnet add package Headless.Caching.InMemory
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
```

Pick one shape: `AddHeadlessCaching` may run only once per service collection. With options:

```csharp
builder.Services.AddHeadlessCaching(setup =>
    setup.UseInMemory(options =>
    {
        options.MaxItems = 10000;
        options.CloneValues = true;
        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
    })
);
```

As the memory tier of a default hybrid instead of the default `ICache` (see [Hybrid cache](hybrid.md#headlesscachinghybrid)):

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.AddMemoryTier();
    setup.AddRedisTier(options => options.ConnectionMultiplexer = redis); // redis: ConnectionMultiplexer.Connect(...)
    setup.UseHybrid();
    setup.UseRedisBackplane();
});
```

Named instances, each with its own options; the setup still needs exactly one default `Use*`:

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

**Verify:** no startup signal and no health check; an in-process cache has nothing to reach. In a test, resolve `ICache` from the built provider and assert that `UpsertAsync` followed by `GetAsync` returns `HasValue = true`.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` overloads; `null` makes them throw. |
| `MaxItems` | 10000 | Most items before LRU eviction. |
| `CloneValues` | `false` | Clone values on read and write, so callers cannot mutate a cached instance. Without it the cache stores and returns object references. |
| `MaxMemorySize` | `null` | Most total bytes. Requires `SizeCalculator`. |
| `SizeCalculator` | `null` | Computes an entry's size in bytes. Required for `MaxMemorySize` and `MaxEntrySize`. |
| `MaxEntrySize` | `null` | Most bytes for one entry. Requires `SizeCalculator`. |
| `ShouldThrowOnMaxEntrySizeExceeded` | `false` | Throw for an entry over `MaxEntrySize` instead of logging and skipping it. |
| `ShouldThrowOnSerializationError` | `true` | Throw when cloning fails. |
| `MaintenanceInterval` | 250 ms | Interval between background maintenance sweeps. |
| `MaxEvictionsPerCompaction` | 10 | Most live items evicted per compaction. Expired entries are reaped first and do not count. |
| `EvictionSampleSize` | 5 | Entries sampled to find eviction candidates. |

### Runtime behavior

- **Memory.** Long `FailSafeMaxDuration` values and long sliding caps keep more entries in process memory, and so do background refreshes while they run. Bound memory with `MaxItems`, `MaxMemorySize`, and LRU compaction. Each sweep reaps every expired entry it finds, and a compaction reaps expired entries before it evicts live ones.
- **Lazy eviction.** An entry past its physical expiry stays in memory until a value read or the next sweep reaps it. Every mutation that inspects the current value (`IncrementAsync`, `SetIfHigherAsync`, `SetIfLowerAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `RemoveIfEqualAsync`, `SetAddAsync`, `SetRemoveAsync`) treats such an entry as absent and starts over, as Redis does.
- **Null expiration on counters.** A `null` expiration keeps a live counter's expiration on `IncrementAsync`, as Redis `INCRBY` does. On `SetIfHigherAsync` and `SetIfLowerAsync` it stores the new value with no expiration, as Redis `SET` does.
- **What counts and lists see.** `GetCountAsync` and key listing follow physical expiration, so they include entries inside their fail-safe reserve and entries a tag or clear invalidated. `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync` follow logical expiration.
- **Markers.** Tag and clear markers are pruned once no entry they could invalidate can still exist. A tagged entry written with no physical expiry turns that pruning off. `FlushAsync` resets the markers with the keyspace; `ClearAsync` keeps them.
- **Events.** `Hit`, `Miss`, `Set`, and `Remove` (`Tier = l1`), `Eviction` with its reason (`expired`, `capacity`, `removed`, or `flushed`, lazy read-path reaps included), and the bulk `RemoveAll`, `RemoveByPrefix`, `RemoveByTag`, `Clear`, and `Flush`. See [Observability](observability.md#events).
- Implements `IBufferCache`. See [Byte payloads](adapters.md#byte-payloads).
- **Registrations.** `IInMemoryCache` as singleton (both `setup.UseInMemory(...)` and `setup.AddMemoryTier(...)`), and a keyed `ICache` under the role key `CacheConstants.MemoryCacheProvider` (`Headless.Caching:Memory`). As the default provider it also registers `ICache` and `ICache<T>`. `setup.AddNamed(name, i => i.UseInMemory(...))` registers a keyed `ICache` under `name`.

---

## Headless.Caching.Redis

Distributed cache on StackExchange.Redis for multi-instance applications, and the L2 of a hybrid cache.

### Setup

```bash
dotnet add package Headless.Caching.Redis
```

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// ConnectionMultiplexer is left unset, so the cache uses the registered IConnectionMultiplexer, the same one
// the Redis distributed-lock provider resolves.
builder.Services.AddHeadlessCaching(setup => setup.UseRedis(options => options.KeyPrefix = "myapp:"));
```

`UseRedis` has no parameterless shape. Every shape, the `IConfiguration` one included, uses the registered `IConnectionMultiplexer` when `ConnectionMultiplexer` is unset. Set it to give one cache its own connection. Options validation fails at startup when neither exists. The setup never creates a multiplexer.

**Verify:** the readiness health check `cache-redis` (tags `ready`, `headless`, `redis`) reports Redis reachable; a named instance adds `cache-redis-{name}`. See [Health checks](../utilities.md#health-checks).

Named instances, each with its own multiplexer and prefix; the setup still needs exactly one default `Use*`:

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.AddNamed(
        "sessions",
        i =>
            i.UseRedis(options =>
            {
                options.ConnectionMultiplexer = sessionsRedis;
                options.KeyPrefix = "sessions:";
            })
    );
});

public sealed class SessionService(ICacheProvider cacheProvider)
{
    private readonly ICache _cache = cacheProvider.GetCache("sessions");
}
```

A named Redis instance can use its own value serializer without changing the default cache. `WithSerializer` and `UseRedis` chain in either order. `WithSerializer` has instance, factory, and generic `<TSerializer>()` overloads; an instance without it uses the global `ISerializer`. On a named hybrid it governs L2 only. InMemory never serializes, so it has no `WithSerializer`.

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.AddNamed(
        "binary-values",
        instance =>
        {
            instance.WithSerializer<MyBinarySerializer>();
            instance.UseRedis(options => options.ConnectionMultiplexer = redis);
        }
    );
});
```

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `ConnectionMultiplexer` | The registered `IConnectionMultiplexer` | The StackExchange.Redis multiplexer the cache uses. |
| `KeyPrefix` | `""` | Prefix for all cache keys and for the tag and clear marker keys. |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` overloads; `null` makes them throw. |
| `ReadMode` | `CommandFlags.None` | StackExchange.Redis flags for read operations, such as `PreferReplica`. |
| `TagMarkerRefreshWindow` | 2 seconds | How long a tag or clear marker read from Redis is reused before the next read fetches it again. A longer window saves round trips but delays when this instance sees another instance's invalidation; see [How fast other instances see it](invalidation.md#how-fast-other-instances-see-it). Must be greater than zero. |

### Runtime behavior

- **Timeouts.** StackExchange.Redis takes no `CancellationToken`. Bound Redis calls with `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`. `FactorySoftTimeout` and `FactoryHardTimeout` bound the factory, not Redis.
- **Stored format.** Scalar writes (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `UpsertAllAsync`, `UpsertEntryAsync`) store a versioned binary envelope around the serialized value. Do not read a Redis string written by the cache as your payload; only atomic counters (`IncrementAsync`, `SetIfHigherAsync`, `SetIfLowerAsync`) are plain Redis numeric strings. `byte[]` and `string` values skip the serializer.
- **Mixed versions.** An entry in an older or newer envelope version, or a key with no envelope, reads as a miss, not an error, and `GetOrAddAsync` writes it again. A rolling upgrade needs no migration and no deploy order.
- **Null values.** A cached `null` is stored as a flag. The string `"@@NULL"` is an ordinary cached string; only a legacy unframed key holding `"@@NULL"` reads as `null`.
- **TTL.** The key TTL follows physical expiration, so with fail-safe on Redis keeps the key after logical expiry. With sliding expiration the TTL follows the idle deadline. A positive expiration under 1 ms rounds up to 1 ms. An expiration past `DateTime.MaxValue` (`TimeSpan.MaxValue`) stores no expiry.
- **`SetIfHigherAsync` and `SetIfLowerAsync`** on `long` compare and subtract as Lua doubles, exact only up to 2^53.
- **Prefixes are literal.** `RemoveByPrefixAsync`, `GetCountAsync`, `GetAllKeysByPrefixAsync`, and `GetByPrefixAsync` escape `* ? [ ] \` in the `KeyPrefix`, the caller's prefix, and a tenant id, so a prefix such as `a*` never matches `abc:1`.
- **Conditional writes** agree with reads: when `SET NX` is refused because the key holds a logically dead entry, `TryInsertAsync` overwrites it only while the key still holds those bytes, so a live entry another writer stored in between wins. `TryReplaceAsync`, `TryReplaceIfEqualAsync`, and `RemoveIfEqualAsync` check that the entry is logically present first. Each check costs one extra read.
- **Redis Cluster.** Every operation works on a cluster, tags and `ClearAsync` included. The cache detects a cluster from the first connected server and keeps that answer; while no endpoint is connected it decides again on each call. A bulk call splits into one command per hash slot and keeps at most 16 in flight, so a very large call goes in waves instead of hitting command timeouts.
- **Scripts.** Lua scripts load in the background after the host starts, onto every writable node of each Redis cache. The warm-up does not hold readiness: a script not yet loaded loads on its first use, which costs that call one extra round trip. A Redis outage at start does not stop the host; the `cache-redis` check reports it.
- **`IRemoteCache` extras.** `GetWithExpirationAsync<T>` and `GetAllWithExpirationAsync<T>` return values with their remaining TTL in one round trip.
- **Events.** `Hit`, `Miss`, `Set`, and `Remove` (`Tier = l2`), and the bulk `RemoveAll`, `RemoveByPrefix`, `RemoveByTag`, `Clear`, and `Flush`. Redis evictions happen on the server, so there is no `Eviction` event. See [Observability](observability.md#events).
- Implements `IBufferCache`; a read copies the value once into the caller's buffer. See [Byte payloads](adapters.md#byte-payloads).
- **Registrations.** `IRemoteCache` as singleton (both `setup.UseRedis(...)` and `setup.AddRedisTier(...)`), and a keyed `ICache` under the role key `CacheConstants.RemoteCacheProvider` (`Headless.Caching:Remote`). As the default provider it also registers `ICache` and `ICache<T>`; `IRemoteCache<T>` does not exist. `setup.AddNamed(name, i => i.UseRedis(...))` registers a keyed `ICache` under `name`, with its own script loading bound to its own multiplexer.

### Redis backplane

`setup.UseRedisBackplane()` sends every hybrid invalidation on one Redis pub/sub channel and subscribes every host to it. Use it when the hosts share Redis and run no message broker; see [Choose a backplane](hybrid.md#choose-a-backplane).

- **Connection and channel.** It uses the default Redis cache's `RedisCacheOptions.ConnectionMultiplexer` (from `UseRedis` or `AddRedisTier`), else the registered `IConnectionMultiplexer`. Building the first hybrid throws `InvalidOperationException` when neither exists. The channel is that cache's `KeyPrefix` followed by `headless.caching.hybrid.invalidation`, so applications sharing one Redis server under different prefixes do not flush each other's L1. Each message is the `CacheInvalidationMessage` as camel-case JSON.
- **Publish.** A publish waits for Redis to accept the `PUBLISH`, so a failure reaches the hybrid's logging, auto-recovery, and `ReThrowBackplaneExceptions`. The caller's token bounds the wait, but the Redis command itself runs on.
- **Subscription.** It starts in the background when the first hybrid is built and retries with a jittered back-off while Redis is unreachable, logging `RedisBackplaneSubscribeRetrying` (`EventId = 20`). The host never waits for it. Messages apply one at a time, in delivery order.
- **Gap flush.** Pub/sub delivers at most once, and only to connected subscribers. The backplane flushes every hybrid's L1 once the first subscription is confirmed. After every `ConnectionRestored` event of the multiplexer it subscribes again, waits for Redis to confirm, and then flushes, logging `RedisBackplaneConnectionRestored` (`EventId = 24`). It reacts to every connection type, because on RESP3 the subscription shares the interactive connection; a spare flush costs one L1 refill from L2.
- **Shutdown.** Disposing the host removes this backplane's subscription only; the shared multiplexer stays open.
- Replaces the `IHybridCacheBackplane` registration (`services.Replace`); a later `Use*Backplane` call replaces it in turn.

**Verify:** at startup with Redis unreachable, the log shows `RedisBackplaneSubscribeRetrying` (`EventId = 20`) until the subscription succeeds.
