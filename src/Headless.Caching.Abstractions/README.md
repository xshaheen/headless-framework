# Headless.Caching.Abstractions

Defines the unified caching interface for in-memory, distributed, and hybrid cache implementations.

## Problem Solved

Provides a provider-agnostic caching API so applications can switch between memory, Redis, and hybrid caches without changing call sites.

## Key Features

- `ICache` - core interface for cache operations:
  - Upsert/Get/Remove with expiration
  - `RefreshAsync` to re-arm sliding entries without returning the value
  - Removal (`RemoveAsync` hard-deletes including the fail-safe reserve; `ExpireAsync` logically expires but preserves the reserve)
  - Bulk operations (UpsertAll, GetAll, RemoveAll)
  - Prefix-based operations (GetByPrefix, RemoveByPrefix)
  - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower)
  - Set operations (SetAdd, SetRemove, GetSet). `GetSetAsync` returns `CacheValue.NoValue` (`HasValue == false`, `Value == null`, never a non-null empty collection) whenever the requested page has no members — absent key, empty set, all live members expired, or a `pageIndex` past the last live member all read identically across providers with no extra existence round-trip. `HasValue` reflects whether the requested page has members, not whether the key exists.
  - Tag invalidation (`UpsertEntryAsync` with `CacheEntryOptions.Tags`; `RemoveByTagAsync` — O(1) logical, returns `ValueTask`)
  - Logical whole-cache clear (`ClearAsync` — O(1), reserve-preserving) vs. reserve-dropping flush (`FlushAsync` — physical in-process, logical remove-generation marker on a distributed tier)
- `[PublicAPI] IBufferCache` - capability interface for byte-oriented caches (Redis, InMemory, Hybrid) that read a payload into an `IBufferWriter<byte>` (`TryGetToAsync`) and write it from a `ReadOnlySequence<byte>` (`UpsertRawAsync`) without the intermediate `byte[]` the generic `GetAsync<byte[]>`/`UpsertEntryAsync<byte[]>` path allocates. `byte[]` is the cache's native wire format (stored verbatim, never through a serializer), so the raw path and the typed `byte[]` path are always byte-consistent under any serializer — no special serializer configuration is needed; all expiry/tag/sliding/`CreatedAt` semantics match `UpsertEntryAsync`. The remaining copy per side is the unavoidable network I/O — zero-copy across a distributed cache is impossible.
- `[PublicAPI] BufferCacheExtensions` - `TryGetToOrFallbackAsync` / `UpsertRawOrFallbackAsync` on `ICache`: take the `IBufferCache` fast path when the cache implements it, else fall back to the generic `byte[]` path. Lets a consumer holding opaque bytes avoid re-implementing the feature-detect.
- `IInMemoryCache` - in-memory (L1) tier contract; a marker interface (`: ICache`) with no extra members.
- `IRemoteCache` - remote (L2) tier contract; adds `GetAllWithExpirationAsync<T>` / `GetWithExpirationAsync<T>` for single-round-trip value-plus-TTL reads (a remote store doesn't expose its TTL locally the way an in-memory tier does).
- `[PublicAPI] ISeedableTagMarkerCache` - optional capability a cache implements for Family-2 tag/clear/remove markers, in two families. `Seed{Tag,Clear,Remove}Marker` update only the **process-local** marker copy from knowledge gained out-of-band (e.g. a backplane notification carrying the originator timestamp), avoiding the refresh-window wait. `Write{Tag,Clear,Remove}MarkerAsync` write the marker to the **durable shared store** then update the local copy, used for the live invalidation and for auto-recovery replay — they are **raise-only** (never lower a newer stored marker), which is what lets a replay carry the *original* timestamp without resurrecting entries written after it. `SeedRemoveMarker`/`WriteRemoveMarkerAsync` are the logical-`FlushAsync` counterpart: entries born before the marker read as a hard miss with no fail-safe reserve (unlike clear, which preserves reserves). Both `RedisCache` and `InMemoryCache` implement the interface — `InMemoryCache`'s remove-marker members are no-ops since its `FlushAsync` wipes physically. Providers that do not implement it fall back to window-bounded refresh (and no marker auto-recovery).
- `ICache<T>` - strongly typed convenience facade over the default `ICache`, exposing the full `ICache` surface (scalar reads/writes, bulk, prefix, atomic numeric ops `IncrementAsync`/`SetIfHigherAsync`/`SetIfLowerAsync`, `GetAllKeysByPrefixAsync`, `GetCountAsync`, `ExistsAsync`, `GetExpirationAsync`, `RemoveAllAsync`, `FlushAsync`) bound to a fixed type parameter. For a specific tier use the untyped `IRemoteCache`/`IInMemoryCache` (method-level generics) or `ICacheProvider.GetCache(name)`. Typed tier wrappers (`IRemoteCache<T>`, `IInMemoryCache<T>`) do not exist.
- `ICacheProvider` - resolves named cache instances and the reserved role keys (`CacheConstants.{Memory,Remote,Hybrid}CacheProvider` — `Headless.Caching:{Memory,Remote,Hybrid}`) via `GetCache(name)` / `GetCacheOrNull(name)`, plus `RegisteredNames` (`IReadOnlySet<string>`) for validating an externally supplied name before resolving. `RegisteredNames` lists only the named instances added through `setup.AddNamed(...)`; the default (unnamed) cache and the tier role keys are excluded even though `GetCache` resolves them.
- `[PublicAPI] ICacheEvents` - the typed, in-process event surface returned by `ICache.Events` (`cache.Events.Hit += …`): native .NET events for every cache signal (`Hit`/`Miss`/`Set`/`Remove`/`Eviction`, `FactorySuccess`/`FactoryError`/`FactoryTimeout`/`FailSafeActivation`/`EagerRefresh`/`BackgroundRefresh`, the operation-level `RemoveAll`/`RemoveByPrefix`/`RemoveByTag`/`Clear`/`Flush`, and hybrid `Invalidation`), plus nullable `Memory`/`Distributed` per-tier sub-hubs (non-null only on the hybrid). Event args (`CacheEventArgs` → keyed `CacheKeyEventArgs` and the operation-level variants) carry `CacheName`, `Tier`, and the caller-facing `Key` (never the internally-prefixed store key), with typed enums (`CacheTier`, `CacheEvictionReason`, `CacheFailSafeTrigger`, `CacheFactoryOutcome`, `CacheRefreshKind`, `CacheInvalidationKind`, `CacheInvalidationDirection`). `Events` has a default interface implementation returning the shared allocation-free no-op hub `CacheEvents.NoOp`, so existing `ICache` implementers are unaffected and an unobserved cache allocates nothing. `Cache<T>` forwards the inner hub; `ScopedCache<T>` returns the no-op hub (subscribe on the underlying unscoped cache).
- `CacheValue<T>` - cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - factory-backed entry options: `Duration`, `SlidingExpiration`, `JitterMaxDuration`, `EagerRefreshThreshold`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration`, `FactorySoftTimeout`, `FactoryHardTimeout`, `BackgroundFactoryCeiling`, `LockTimeout`, `UseDistributedFactoryLock`, and `Tags`.
- `CacheFactoryContext<T>` / `CacheFactoryResult<T>` - conditional-factory contract (the HTTP-304 pattern): the factory sees the last-known value and its validators (`ETag`, `LastModifiedAt`) and returns `NotModified()` or `Modified(value, eTag, lastModifiedAt)`; it may also replace `Options` and `Tags` before returning (adaptive caching).
- `CacheOptions` - base provider options carrying `KeyPrefix`, `DefaultEntryOptions`, and `CacheName` (the instance name surfaced on the `headless.cache.name` telemetry dimension; instrumentation metadata only, set automatically for named instances).
- `CacheDefaultEntryExtensions` - option-less `GetOrAddAsync` overloads that apply the cache instance's `DefaultEntryOptions` and throw `InvalidOperationException` when none is configured.
- `CacheFactoryTimeoutException` - `TimeoutException` subtype thrown when a hard factory timeout fires without a stale fallback.

## Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeouts, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

`SlidingExpiration` is an optional idle window for factory-backed entries. `Duration` remains the absolute cap from entry creation, and value-returning reads re-arm the logical deadline to `min(now + SlidingExpiration, createdAt + Duration)`. Metadata reads do not re-arm. Sliding expiration is rejected together with fail-safe and together with eager refresh in this version.

`EagerRefreshThreshold` (exclusive between 0 and 1) stamps an eager point of `createdAt + Duration × threshold` on the entry. A fresh `GetOrAddAsync` hit past that point returns the cached value immediately and starts a non-blocking, deduplicated background refresh, so hot keys are renewed before they expire and callers never block on the refresh. A failed eager refresh only loses the proactive renewal — the entry stays fresh until natural expiry, and fail-safe (when enabled) takes over from there.

`JitterMaxDuration` adds a uniform random offset in `[0, JitterMaxDuration)` to `Duration` on each write, so entries created in the same burst do not all expire at the same instant and trigger a synchronized factory stampede on the next read wave (anti-stampede). The jittered duration drives the logical, physical, and eager spans alike, so it never violates the `physical >= logical` invariant. Defaults to `TimeSpan.Zero` (no jitter, deterministic expiry).

The conditional `GetOrAddAsync` overload exists for origins that can answer "has this changed since?" cheaper than re-sending the value (HTTP `ETag`/`If-Modified-Since`, DB row versions). `NotModified()` re-stamps the existing entry as fresh without re-transferring the payload; it throws `InvalidOperationException` when no last-known value exists (`HasStaleValue` is `false`) — return `Modified(value)` on a cold cache. Mutating `context.Options` is re-validated before the write: an invalid adaptive mutation (e.g. non-positive duration) throws after the factory ran and nothing is written. The factory-timeout family (`FactorySoftTimeout`, `FactoryHardTimeout`, `LockTimeout`) is consumed before the factory runs, so adaptive changes to those fields are inert for the current call.

`Tags` are persisted with the entry for later one-call invalidation through `RemoveByTagAsync`. On a factory-backed read, call-provided tags win over the tags carried by an existing entry; `null` carries the existing tags forward. Each tag must be non-empty, and both the tag count and each tag's UTF-8 byte length must fit in an unsigned 16-bit value (provider envelope limits) — violations throw `ArgumentException` before anything is written. `RemoveByTagAsync` is O(1) logical (Family-2) invalidation: it writes one per-tag timestamp marker and returns `ValueTask`, without enumerating members. On the next read the shared predicate compares the entry's birth time (`CreatedAt`) against the newest applicable marker; an older entry is a miss for direct reads and a fail-safe reserve under the coordinator. Memberships are version-pinned by birth time, so a key re-created after the marker (newer `CreatedAt`) is not invalidated.

`ClearAsync` is the logical, O(1) whole-cache counterpart: it bumps one reserved clear-generation marker compared on every read, so every entry born before the bump reads as a miss while its fail-safe reserve is preserved. `FlushAsync` drops every entry including its fail-safe reserve — physical in-process, a logical remove-generation marker on a distributed (Redis) tier (cluster-safe, no `FLUSHDB`; physical memory reclaimed by each entry's TTL). Prefer `ClearAsync` when fail-safe coverage must outlive the clear; use `FlushAsync` to drop everything including reserves.

`UpsertEntryAsync(key, value, options)` is the direct-write path that honors full `CacheEntryOptions` semantics (fail-safe physical retention, eager stamp, sliding clamp, tags). It performs a read-before-write and stamps a fresh birth time so a prior tag/clear marker does not invalidate the new value, so prefer the plain `UpsertAsync(key, value, TimeSpan?)` on hot paths that need none of the per-entry option semantics. It is named distinctly because the `TimeSpan`-to-options implicit conversion would otherwise make every bare-`TimeSpan` upsert ambiguous.

`RemoveAsync` and `ExpireAsync` are two strengths of the same invalidation, distinguished by what happens to the fail-safe reserve. `RemoveAsync` hard-deletes the entry and its physical reserve — a subsequent `GetOrAddAsync` whose factory throws has nothing to fall back to. `ExpireAsync` only pulls logical expiration forward: ordinary reads (`GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, …) miss immediately, but the physical reserve survives, so a later `GetOrAddAsync` whose factory fails (with `IsFailSafeEnabled`) can still serve the stale value (the fail-safe parachute). Reach for `ExpireAsync` when you want to force a refresh while keeping the staleness safety net; reach for `RemoveAsync` when the cached value must be gone for good. For an entry written without a fail-safe reserve (a plain `TimeSpan?` write, or fail-safe disabled — logical and physical expiration coincide) `ExpireAsync` is equivalent to `RemoveAsync`: there is no reserve to preserve. Both return `true` when an entry was found and expired/removed, `false` when the key is absent.

Fail-safe is opt-in and only applies to `GetOrAddAsync`. Direct `TimeSpan?` writes keep logical expiration equal to physical expiration. A stale value served by fail-safe returns `CacheValue<T>.IsStale = true` only for the activating call; reads during the throttle window are logical hits and return `IsStale = false`.

Factory soft timeouts are useful only when fail-safe is enabled and a stale reserve exists. In that case the caller gets stale data and the factory continues in the background. Soft timeouts configured without fail-safe are inert and logged once per key. Factory hard timeouts cancel or abandon the factory; they serve stale when possible and throw `CacheFactoryTimeoutException` on a cold cache.

Background completion uses a detached coordinator-owned cancellation token, not the caller token. A request token may be cancelled after the stale response is returned and the background refresh can still finish. Factories used with soft timeouts or eager refresh must not capture request-scoped disposables; create a fresh dependency scope inside the factory when scoped services are required after the request path returns.

`BackgroundFactoryCeiling` defaults to `Timeout.InfiniteTimeSpan` (no ceiling): a detached background factory runs to completion, matching the behavior of comparable caches (FusionCache, Caffeine, sturdyc). Set a finite, positive value to bound how long a detached factory may hold the per-key lock. When the ceiling fires, the coordinator cancels the internal token and releases the lock: cooperative factories stop, while non-cooperative factories may continue running untracked, but the coordinator gates late success writes so an abandoned factory cannot clobber a newer cache value through the timeout path.

`LockTimeout` defaults to `Timeout.InfiniteTimeSpan`, matching FusionCache's default: a caller with no stale reserve waits until the in-flight factory releases the per-key lock. Set a finite, positive value so a caller that cannot acquire the lock in time degrades to a miss (`CacheValue<T>.NoValue`) instead of blocking, bounding tail latency when an in-flight factory is slow and no fail-safe reserve exists. When a stale reserve does exist and `FactorySoftTimeout` is finite, that soft timeout governs the wait instead and the caller is served stale on elapse.

`UseDistributedFactoryLock` adds a cross-node single-flight layer on top of the local per-key lock. It is off by default and zero-cost when disabled; enabling it requires a registered `ICacheFactoryLockProvider` (reference `Headless.Caching.DistributedLocks` and call `setup.UseDistributedFactoryLock()` inside `AddHeadlessCaching`), otherwise the factory-backed read throws `InvalidOperationException` rather than silently degrading to single-node behavior.

`DefaultEntryOptions` is explicit-at-registration, never magic: the option-less `GetOrAddAsync` extension overloads throw `InvalidOperationException` when the cache instance has no configured default, so a missing default is a loud configuration error instead of a silent surprise duration.

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

Conditional refresh (the HTTP-304 pattern) — extend the cached entry without re-transferring the value when the origin reports it unchanged:

```csharp
public sealed class FeedService(ICache cache, HttpClient httpClient)
{
    public async Task<string?> GetFeedAsync(CancellationToken ct)
    {
        var cached = await cache.GetOrAddAsync<string>(
            "feed:latest",
            async (context, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/feed");

                if (context.HasStaleValue && context.ETag is not null)
                {
                    request.Headers.IfNoneMatch.ParseAdd(context.ETag);
                }

                using var response = await httpClient.SendAsync(request, token);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return context.NotModified(); // re-stamp the cached value as fresh
                }

                var body = await response.Content.ReadAsStringAsync(token);
                return context.Modified(body, eTag: response.Headers.ETag?.Tag);
            },
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

Tag invalidation:

```csharp
await cache.UpsertEntryAsync(
    "product:42",
    product,
    new CacheEntryOptions { Duration = TimeSpan.FromMinutes(30), Tags = ["products"] },
    ct
);

await cache.RemoveByTagAsync("products", ct); // O(1) logical invalidation: bumps the "products" marker

// Logical whole-cache clear (reserves preserved); FlushAsync drops reserves instead.
await cache.ClearAsync(ct);
```

Cache events (native .NET events; handlers run guarded on a background task by default — keep them synchronous):

```csharp
cache.Events.Hit += (sender, e) => logger.LogDebug("cache hit {Key} (stale={Stale})", e.Key, e.IsStale);
cache.Events.Set += (sender, e) => logger.LogDebug("cache set {Key}", e.Key);
```

## Configuration

No configuration required. This is an abstractions-only package; `CacheOptions.KeyPrefix`, `CacheOptions.DefaultEntryOptions`, and `CacheOptions.CacheName` are configured on the provider packages.

## Dependencies

- `Headless.Extensions`

## Side Effects

None. This is an abstractions package.
