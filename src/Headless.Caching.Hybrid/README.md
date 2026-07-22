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
- O(1) logical tag invalidation across both tiers plus a `Tag` invalidation message on the backplane so peers bump their own L1 tag marker.
- O(1) logical `ClearAsync` across both tiers plus a `Clear` invalidation message on the backplane so peers bump their L1 clear-generation marker (reserves preserved); distinct from `FlushAll` (drops reserves — physical L1 wipe on the receiver plus a logical L2 remove-generation marker seeded from the origin timestamp).
- `ExpireAsync` across both tiers plus an `Expire` invalidation message on the backplane so peers logically expire their L1 copy (fail-safe reserve preserved) instead of removing it.
- Named tier selection (`LocalCacheName`/`RemoteCacheName`) and named hybrid instances via `setup.AddNamed(name, i => i.UseHybrid(...))`.
- Opt-in auto-recovery: transient L2/backplane outages queue failed single-key operations and replay them on recovery.
- L2 read soft/hard timeouts and a simple L2 circuit breaker keep slow or failing distributed reads from holding callers.
- Implements `IBufferCache` — an L1 hit slices straight into the caller's `IBufferWriter<byte>` (single copy on the hot path); an L1 miss falls through to the same wrapped L2 read the generic path uses and seeds L1 (two copies on the cold path, inherent to populating both tiers). Raw upsert stamps both tiers plus the backplane identically to `UpsertEntryAsync`.
- `cache.Events` event surface (`ICacheEvents`): aggregate get-or-add and direct-op signals on the root hub (`Tier=hybrid`), low-level per-tier L1/L2 reads under `cache.Events.Memory` / `cache.Events.Distributed`, and `Invalidation` events (kind = tag/clear/flush, direction = publish/receive). L1 evictions surface on the composed L1 cache's own `Events.Eviction`, not the hybrid.
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

A default hybrid composes role-keyed tiers registered in the same `AddHeadlessCaching` setup: `setup.AddMemoryTier()` registers the L1 (`IInMemoryCache` plus the `CacheConstants.MemoryCacheProvider` role key) and `setup.AddRedisTier(...)` the L2 (`IRemoteCache` plus the `CacheConstants.RemoteCacheProvider` role key) without touching the default unkeyed `ICache`; `setup.UseHybrid()` then becomes the default `ICache`. Prefer the tier recipe for the common one-hybrid host — no instance names to invent, and the role keys stay reachable through `ICacheProvider`; set `LocalCacheName`/`RemoteCacheName` to bind `AddNamed` instances instead when a tier needs an identity of its own (for example a second hybrid, or tiers shared with other named consumers). Incoming invalidations are handled by `HybridCacheInvalidationConsumer` (`IConsume<CacheInvalidationMessage>`), which `UseHybrid` auto-registers unconditionally — so cross-node L1 invalidation is correct by default rather than a silent opt-in. A single consumer serves every hybrid (default and named): it resolves the default hybrid by the `CacheConstants.HybridCacheProvider` role key and named hybrids by `CacheInvalidationMessage.CacheName` through `ICacheProvider`, so a named hybrid receives only the invalidations published for its own cache name. Auto-registration is idempotent — an explicit `ForMessage<CacheInvalidationMessage>(msg => msg.OnBus<HybridCacheInvalidationConsumer>())` (or assembly scanning) still works and is merged rather than double-registered; an explicit registration wins outright when it precedes `AddHeadlessCaching`, and one added after caching merges when it matches the documented shape (a diverging same-group customization fails fast at messaging bootstrap — register it before caching to defer the default). Registration is order-independent: the emitted `ForMessage` descriptors are inert until messaging bootstrap drains them from the built provider, so caching and messaging may be registered in either order, and a host that never adds messaging pays nothing for the inert descriptors (a functioning hybrid resolves a required `IBus` anyway).

Hybrid fail-safe and factory timeouts use the same coordinator semantics as the other providers. A stale reserve can come from L1 or L2. On factory soft timeout, the stale value is returned to the caller and the detached background factory writes through the composite store on success, so both tiers are refreshed. Eager refresh and conditional (`NotModified`) refresh likewise run through the composite store, so a refresh extends or replaces the entry in both tiers. `DefaultLocalExpiration` still caps L1 physical retention independently of the L2 duration.

Hybrid also has L2 read-level resilience. `DistributedCacheSoftTimeout` bounds L2 reads that can degrade to a local stale reserve or a miss; in the factory-store path, a timed-out L2 read with an L1 stale reserve serves that reserve immediately and skips the origin factory. `DistributedCacheHardTimeout` bounds L2 reads when no local reserve exists. `DistributedCacheCircuitBreakerDuration` temporarily skips L2 operations after a non-cancellation L2 failure so an unhealthy distributed tier gets relief; reads degrade to L1 or miss, and additive writes can update L1 without waiting on L2 while the circuit is open.

Factory value-writes publish the same key invalidation as explicit upserts: cold-miss fresh writes, soft-timeout background completion writes, eager-refresh writes, and conditional `Modified` writes all broadcast, so peers drop their stale L1 copy instead of serving the old value until its local TTL. Metadata-only restamps do not publish — conditional `NotModified` extensions, fail-safe throttle restamps, and the eager-refresh gate write leave peers' cached bytes identical, so invalidating them would only force pointless L2 re-reads. Publish failures on this path follow the upsert semantics: they never fail the caller, are logged, and with `EnableAutoRecovery` the single-key publish is queued and replayed.

`RemoveByTagAsync` bumps its own L1 tag marker first and unconditionally (in-process and infallible, so the local invalidation always lands even when L2 is unreachable), then publishes the `Tag` invalidation, then bumps the L2 tag marker best-effort under the distributed-cache circuit breaker — an L2 failure trips the circuit and is logged rather than abandoning the local and peer invalidation. With `EnableAutoRecovery`, a skipped (circuit-open) or failed L2 marker bump is queued and replayed on recovery — re-asserting the **original** timestamp (raise-only durable write, so an entry written after the invalidation is not resurrected) and re-broadcasting — so the shared-store marker converges once L2 returns. Without auto-recovery the bump is not replayed, and cross-instance staleness for a node relying solely on the shared marker (a late joiner, or a node once its process-local marker cache expires) is bounded only by each entry's physical TTL (L1 and peers, via the broadcast, are unaffected either way). A single origin timestamp flows through the L1 marker, the broadcast message, and the L2 marker, so every node version-pins the invalidation against the same instant. It returns `ValueTask` (no count — Family-2 invalidation deletes nothing). Receivers seed their L1 tag marker from the notification's origin timestamp (raise-only, via `ISeedableTagMarkerCache`) rather than stamping their own clock, closing the cross-node clock-skew window in which a lagging receiver could record a marker older than a freshly-born entry and miss the invalidation; the previous recovery-aware per-key tag walk is gone — a pending recovery write that landed after the invalidation carries a newer `CreatedAt` and is naturally not invalidated by the older marker. `ClearAsync` follows the same shape with a `Clear` message: bump L1 first, publish, then best-effort L2 clear-generation marker under the circuit breaker; receivers seed their L1 clear marker from the origin timestamp (reserves preserved), distinct from a `FlushAll` physical wipe. Receivers also seed their L2 provider's process-local marker cache from the notification's timestamp (via `ISeedableTagMarkerCache`, which both `RedisCache` and `InMemoryCache` implement), so both the L1 and L2 marker views update immediately — no L2 round-trip and no `TagMarkerRefreshWindow` wait. The window then bounds cross-instance L2 visibility only for no-backplane deployments and for recovery after a missed backplane message; the physical-TTL backstop for a lost marker is unchanged.

`ExpireAsync` carries the logical-expire-keeps-reserve contract across the backplane. It expires L2, expires its own L1, and — only when the key existed — publishes a `CacheInvalidationMessage` with `Expire = true`, so receivers run `LocalCache.ExpireAsync` (logical expiry, reserve preserved) rather than the plain remove a `Key` message would trigger. The local instance's reserve is preserved too, so a `GetOrAddAsync` on either the originating node or a peer can still serve stale through fail-safe after the expiration. Under `EnableAutoRecovery`, a failing L2 expiration follows the same degraded path as `RemoveAsync`: L1 is expired, the L2 expiration is queued for replay, and the call conservatively reports `true` and publishes the `Expire` invalidation because the L2 state is unknown. The `Expire` flag is meaningful only with `Key`/`Keys` set; it is ignored for `Prefix`, `Tag`, and `FlushAll` messages.

On reads, Hybrid promotes L2 entries into L1 only when they are logically fresh. Promoting stale reserves on every read would amplify L1 writes and could overwrite a newer L1 reserve. Fail-safe activation and background success still write through the composite store intentionally. Cold set reads are the exception to promotion: an L1-miss `GetSetAsync` serves straight from L2 without seeding L1 — InMemory stores sets as per-member dictionaries (the `SetAddAsync` shape), so seeding the bare returned collection would poison the key for local set read-back, and a paged read returns one page, never the whole set; sets enter L1 only through the hybrid `SetAddAsync` write path (this also makes the cold set read a single L2 round-trip). Separately, when the configured L2 is a third-party `IRemoteCache` that does **not** implement `IFactoryCacheStore`, the non-framed cold-read fallback seeds L1 entries without `Tags`/`CreatedAt`: such entries cannot be invalidated by `RemoveByTagAsync` and serve until their physical TTL (key-based invalidation and `ClearAsync` still reach them). The shipped Redis L2 implements the framed contract, so this limitation applies only to custom remote providers — implement `IFactoryCacheStore` on a custom L2 to restore tag-reachable seeding.

Per-tier read skip on a factory-backed `GetOrAddAsync` (mirrors FusionCache's `SkipMemoryCacheRead` / `SkipDistributedCacheRead`): `CacheEntryOptions.SkipMemoryCacheRead` bypasses the L1 read so the value is served from — or refreshed against — L2, and `CacheEntryOptions.SkipDistributedCacheRead` bypasses the L2 read so a fresh L1 value serves without an L2 round-trip and an L1 miss falls straight through to the factory. A value read from L2 under `SkipMemoryCacheRead` is still promoted into L1 (promotion is a write, governed by `SkipMemoryCacheWrite`). Setting both is a miss, equivalent to the coarse `SkipCacheRead` (which itself skips the read on both tiers outright). Single-tier providers ignore all three flags — there is only one tier to read.

Publish failures are non-fatal. Other instances may keep their L1 value until TTL or the next successful invalidation, while the local instance still observes the write result.

## Installation

```bash
dotnet add package Headless.Caching.Hybrid
```

## Quick Start

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddSingleton<IConnectionMultiplexer>(redis);
services.AddHeadlessMessaging(builder => builder.UseRedis("localhost:6379"));
services.AddHeadlessCaching(setup =>
{
    setup.AddMemoryTier();
    setup.AddRedisTier(options => options.ConnectionMultiplexer = redis);
    setup.UseHybrid(options => options.DefaultLocalExpiration = TimeSpan.FromMinutes(5));
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
services.AddHeadlessCaching(setup =>
{
    setup.AddNamed("hot-l1", i => i.UseInMemory(options => options.MaxItems = 5000));
    setup.AddNamed("hot-l2", i => i.UseRedis(options => options.ConnectionMultiplexer = redis));
    setup.UseHybrid(options =>
    {
        options.LocalCacheName = "hot-l1"; // must implement IInMemoryCache
        options.RemoteCacheName = "hot-l2"; // must implement IRemoteCache
    });
});
```

A named hybrid instance binds named tiers the same way — `setup.AddNamed("hot", i => i.UseHybrid(options => { options.LocalCacheName = "hot-l1"; options.RemoteCacheName = "hot-l2"; }))`. The setup builder stamps that name into invalidation messages so the backplane consumer can route peer invalidations to the matching named hybrid.

Cache events — the hybrid adds per-tier `Events.Memory` / `Events.Distributed` reads and `Invalidation` (publish/receive):

```csharp
cache.Events.Hit += (sender, e) => logger.LogDebug("cache hit {Key} tier={Tier}", e.Key, e.Tier);
cache.Events.Invalidation += (sender, e) => logger.LogDebug("invalidation {Kind} {Direction}", e.Kind, e.Direction);
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
| `EnableAutoRecovery` | `false` | Opt-in self-healing for transient L2/backplane outages: failed single-key L2 writes/removes, failed tag/clear/flush marker bumps (replayed at their original timestamp, raise-only), and failed invalidation publishes are queued and replayed on recovery instead of surfacing. |
| `AutoRecoveryMaxItems` | `128` | Max pending recovery items (one per key); on overflow the earliest-expiring item is evicted (or the incoming item is rejected when it expires soonest). |
| `AutoRecoveryMaxRetries` | `8` | Failed replay attempts before a pending item is dropped with a warning. |
| `AutoRecoveryDelay` | `5 seconds` | Recovery loop cadence and the back-off barrier armed after a failed replay. |
| `AllowBackgroundDistributedCacheOperations` | `false` | Fire-and-forget the L2 write and backplane publish on **additive writes** (`GetOrAddAsync` factory write-through, `UpsertAsync`, `UpsertAllAsync`): the caller returns after the L1 write without awaiting L2. Throughput win, weaker consistency — L2 (and peers) briefly lag L1. A failed background write is queued for replay when `EnableAutoRecovery` is on, otherwise logged best-effort. Removes, `Try*`, atomic/set ops, and reads stay synchronous (their result depends on L2). |
| `DistributedCacheSoftTimeout` | `Timeout.InfiniteTimeSpan` | Max time for L2 reads that can degrade to an L1 stale reserve or a miss. |
| `DistributedCacheHardTimeout` | `Timeout.InfiniteTimeSpan` | Max time for L2 reads when no local reserve exists. Must be greater than `DistributedCacheSoftTimeout` when both are finite. |
| `DistributedCacheCircuitBreakerDuration` | `TimeSpan.Zero` | How long to skip L2 operations after a non-cancellation L2 failure. Zero disables the breaker. |
| `ReThrowDistributedCacheExceptions` | `false` | Re-throw (instead of degrade) a non-cancellation L2 read or factory-write failure. Direct reads (`GetAsync`, `GetAllAsync`, `ExistsAsync`, `GetExpirationAsync`, `GetSetAsync`) and the factory/`UpsertEntryAsync` store-write surface the exception. Timeouts and an open circuit still degrade (no exception to re-throw), sliding re-arm stays best-effort, and a `GetOrAddAsync` store-read fault still falls through to the factory (cache-aside). |
| `ReThrowBackplaneExceptions` | `false` | Re-throw a non-cancellation failure publishing an invalidation message to the backplane, after the failure is logged and (when `EnableAutoRecovery` is on) queued for replay. Surfaces on synchronous write paths; on detached background publishes it is observed and logged instead. |

Auto-recovery (design reference: FusionCache's auto-recovery, adapted) keeps one pending operation per key with kind-aware coalescing: a newer value operation (set/remove) replaces any queued item, a publish refreshes a queued publish, but a publish never displaces a queued value operation — the value operation subsumes it, because a successful set/remove replay republishes the key invalidation itself, stamped with the original write time so receivers order it correctly against newer writes. If that post-replay publish fails, a residual publish is queued in its place (the value already landed in L2) and inherits the normal retry cap, so the failure path cannot loop. Any successful L2 write for a key clears its pending item, and a queued set is only replayed while the L1 entry still carries the exact stamp the write produced (L1 is the source of truth; otherwise the item is dropped as obsolete). Incoming invalidations from other instances drop older queued items so a replay cannot resurrect stale data, and a single-key invalidation older than a surviving pending item is ignored instead of wiping the newer local L1 state — together these make concurrent-writer divergence under an outage converge on the last writer's value once every node has replayed (a message without a timestamp is treated as newer — conservative drop; tag invalidations are not conflict-matched because queued items are not indexed by tag). With auto-recovery enabled, a failing single-key L2 write no longer propagates to the caller: the call succeeds against L1 in degraded mode (logged as a warning), so callers must tolerate L2 lagging L1 until replay. Items without a natural expiry (removes, publishes) are retained for `AutoRecoveryDelay × AutoRecoveryMaxRetries`; replay passes run oldest-first and stop at the first failure, arming the back-off barrier so a sustained outage does not become a retry storm. Bulk, atomic (increment/set-if), and set operations are never captured.

For factory-backed sliding entries, `DefaultLocalExpiration` caps the L1 copy only. Hybrid revalidates sliding L1 hits against L2 before re-arm so L2 keeps the original `Duration` as the absolute cap. If L2 is unavailable, a fresh L1 sliding value can still be returned, but the read is not re-armed.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Bus.Abstractions`

## Side Effects

- `setup.UseHybrid(...)` (default) registers `HybridCache` as singleton, the default `ICache` over it, a keyed `ICache` under `CacheConstants.HybridCacheProvider`, and `ICache<T>`.
- Registers `ICacheProvider` (shared, `TryAdd`).
- `setup.AddNamed(name, i => i.UseHybrid(...))` registers a keyed `ICache` under the instance name with its own options and tier resolution.
- Reads configured `HybridCacheOptions`.
- Publishes cache invalidation messages through the registered message bus.
- Runs a `TimeProvider`-driven recovery timer when `EnableAutoRecovery` is set.
