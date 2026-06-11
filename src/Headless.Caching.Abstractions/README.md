# Headless.Caching.Abstractions

Defines the unified caching interface for in-memory, distributed, and hybrid cache implementations.

## Problem Solved

Provides a provider-agnostic caching API so applications can switch between memory, Redis, and hybrid caches without changing call sites.

## Key Features

- `ICache` - core interface for cache operations:
  - Upsert/Get/Remove with expiration
  - Bulk operations (UpsertAll, GetAll, RemoveAll)
  - Prefix-based operations (GetByPrefix, RemoveByPrefix)
  - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower)
  - Set operations (SetAdd, SetRemove, GetSet)
  - Tag invalidation (`UpsertEntryAsync` with `CacheEntryOptions.Tags`, `RemoveByTagAsync` returning the removed count)
- `IInMemoryCache` - marker interface for in-memory implementations.
- `IRemoteCache` - marker interface for remote implementations.
- `ICache<T>` - strongly typed cache wrapper.
- `ICacheProvider` - resolves named cache instances and the reserved role keys (`memory`, `remote`, `hybrid` on `CacheConstants`).
- `CacheValue<T>` - cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - factory-backed entry options: `Duration`, `SlidingExpiration`, `EagerRefreshThreshold`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration`, `FactorySoftTimeout`, `FactoryHardTimeout`, `BackgroundFactoryCeiling`, `LockTimeout`, `UseDistributedFactoryLock`, and `Tags`.
- `CacheFactoryContext<T>` / `CacheFactoryResult<T>` - conditional-factory contract (the HTTP-304 pattern): the factory sees the last-known value and its validators (`ETag`, `LastModifiedAt`) and returns `NotModified()` or `Modified(value, eTag, lastModifiedAt)`; it may also replace `Options` and `Tags` before returning (adaptive caching).
- `CacheOptions` - base provider options carrying `KeyPrefix` and `DefaultEntryOptions`.
- `CacheDefaultEntryExtensions` - option-less `GetOrAddAsync` overloads that apply the cache instance's `DefaultEntryOptions` and throw `InvalidOperationException` when none is configured.
- `CacheFactoryTimeoutException` - `TimeoutException` subtype thrown when a hard factory timeout fires without a stale fallback.

## Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeouts, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

`SlidingExpiration` is an optional idle window for factory-backed entries. `Duration` remains the absolute cap from entry creation, and value-returning reads re-arm the logical deadline to `min(now + SlidingExpiration, createdAt + Duration)`. Metadata reads do not re-arm. Sliding expiration is rejected together with fail-safe and together with eager refresh in this version.

`EagerRefreshThreshold` (exclusive between 0 and 1) stamps an eager point of `createdAt + Duration × threshold` on the entry. A fresh `GetOrAddAsync` hit past that point returns the cached value immediately and starts a non-blocking, deduplicated background refresh, so hot keys are renewed before they expire and callers never block on the refresh. A failed eager refresh only loses the proactive renewal — the entry stays fresh until natural expiry, and fail-safe (when enabled) takes over from there.

The conditional `GetOrAddAsync` overload exists for origins that can answer "has this changed since?" cheaper than re-sending the value (HTTP `ETag`/`If-Modified-Since`, DB row versions). `NotModified()` re-stamps the existing entry as fresh without re-transferring the payload; it throws `InvalidOperationException` when no last-known value exists (`HasStaleValue` is `false`) — return `Modified(value)` on a cold cache. Mutating `context.Options` is re-validated before the write: an invalid adaptive mutation (e.g. non-positive duration) throws after the factory ran and nothing is written. The factory-timeout family (`FactorySoftTimeout`, `FactoryHardTimeout`, `LockTimeout`) is consumed before the factory runs, so adaptive changes to those fields are inert for the current call.

`Tags` are persisted with the entry for later one-call invalidation through `RemoveByTagAsync`. On a factory-backed read, call-provided tags win over the tags carried by an existing entry; `null` carries the existing tags forward. Each tag must be non-empty, and both the tag count and each tag's UTF-8 byte length must fit in an unsigned 16-bit value (provider envelope limits) — violations throw `ArgumentException` before anything is written. `RemoveByTagAsync` removes exactly the entries that currently carry the tag: memberships are pinned to the entry version, so a key that expired or was re-created without the tag is cleaned up from the index instead of removed.

`UpsertEntryAsync(key, value, options)` is the direct-write path that honors full `CacheEntryOptions` semantics (fail-safe physical retention, eager stamp, sliding clamp, tags). It performs a read-before-write to reconcile provider tag indexes, so prefer the plain `UpsertAsync(key, value, TimeSpan?)` on hot paths that need none of the per-entry option semantics. It is named distinctly because the `TimeSpan`-to-options implicit conversion would otherwise make every bare-`TimeSpan` upsert ambiguous.

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

var removed = await cache.RemoveByTagAsync("products", ct); // count of entries removed
```

## Configuration

No configuration required. This is an abstractions-only package; `CacheOptions.KeyPrefix` and `CacheOptions.DefaultEntryOptions` are configured on the provider packages.

## Dependencies

- `Headless.Extensions`

## Side Effects

None. This is an abstractions package.
