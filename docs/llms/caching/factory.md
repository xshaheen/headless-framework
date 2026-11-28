---
domain: Caching
packages: Caching.DistributedLocks
---

# Factory-backed reads

> Part of [Caching](../caching.md). How `GetOrAddAsync` and `CacheEntryOptions` behave: fail-safe, factory timeouts, lock waits, eager refresh, sliding expiration, jitter, conditional refresh, and cross-node single-flight.

Every provider (InMemory, Redis, Hybrid) runs `GetOrAddAsync` through the same engine, so everything on this page applies to all of them. Hybrid adds its own L2 read timeouts on top; see [Hybrid cache](hybrid.md#headlesscachinghybrid).

## Pick the mechanism for the failure

The mechanisms combine freely, except that sliding expiration excludes both fail-safe and eager refresh.

| Failure mode | Mechanism | Option or API |
| --- | --- | --- |
| Concurrent misses on one node | Per-key single-flight factory | Always on |
| Concurrent misses across nodes sharing one L2 | Cross-node single-flight | `UseDistributedFactoryLock` plus `Headless.Caching.DistributedLocks` |
| Origin down or throwing | Fail-safe: serve the last good value | `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration` |
| Origin slow, stale data acceptable | Soft timeout: return stale, finish in the background | `FactorySoftTimeout` (needs fail-safe and a stale reserve) |
| Origin slow, cold cache | Hard timeout: bound the cold wait | `FactoryHardTimeout` |
| Latency spike when a hot key expires | Eager refresh: renew before expiry | `EagerRefreshThreshold` |
| Entries created together expire together | Jitter | `JitterMaxDuration` |
| Re-fetching an unchanged payload | Conditional refresh (HTTP 304) | The `GetOrAddAsync` overload with a `CacheFactoryContext<T>` factory |
| L2 slow or failing (Hybrid) | L2 read timeouts and circuit breaker | `DistributedCacheSoftTimeout`, `DistributedCacheHardTimeout`, `DistributedCacheCircuitBreakerDuration` |
| Transient L2 or backplane outage (Hybrid) | Queue and replay | `EnableAutoRecovery` |

## Rules

- Enable fail-safe per entry with `CacheEntryOptions.IsFailSafeEnabled = true`. Only `GetOrAddAsync` reads a fail-safe reserve. `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `GetAllSetMembersAsync`, and `ExistsAsync` see only logical expiration.
- Set `FactorySoftTimeout` only together with fail-safe. Without fail-safe a soft timeout does nothing, and the cache logs that once per key.
- Set `FactoryHardTimeout` to bound a cold-cache wait. With no stale value to serve, it throws `CacheFactoryTimeoutException`.
- Set a finite `BackgroundFactoryCeiling` whenever fail-safe is on together with a finite `FactorySoftTimeout`. That combination detaches the factory, and options validation throws when the ceiling is left infinite.
- Do not capture request-scoped disposables in a factory that can run in the background (soft timeout or eager refresh). The background run can outlive the request. Create a fresh scope inside the factory when it needs scoped services.
- Do not combine `SlidingExpiration` with `IsFailSafeEnabled`, or `SlidingExpiration` with `EagerRefreshThreshold`. Options validation rejects both.
- Caller cancellation never serves stale. When the token passed to `GetOrAddAsync` is cancelled, the `OperationCanceledException` propagates and fail-safe does not activate. An `OperationCanceledException` from any other token, such as a downstream HTTP timeout, counts as a factory failure and does activate fail-safe.
- Same-key re-entrant calls (the factory calling `GetOrAddAsync` for its own key) are supported only with fail-safe, a stale reserve, and a finite `FactorySoftTimeout`. Otherwise they can deadlock.

## Expiration timeline

Every entry carries two clocks. **Logical expiration** ends the fresh window that ordinary reads see. **Physical expiration** ends the fail-safe reserve that only `GetOrAddAsync` reads.

```
  write     logical expiry           physical expiry
  │                 │                        │
  ▼                 ▼                        ▼
  ├──── fresh ─────┼──── stale reserve ─────┼──── gone ──▶ t
```

- **fresh:** reads hit, and `GetOrAddAsync` returns the live value.
- **stale reserve:** reads miss. `GetOrAddAsync` runs the factory and serves this value only if the factory fails, with `CacheValue<T>.IsStale = true`. It then throttles retries for `FailSafeThrottleDuration`.
- **gone:** reads miss, and `GetOrAddAsync` runs the factory with no fallback.

Physical expiration is `max(Duration, FailSafeMaxDuration)` from entry creation. With fail-safe off, the two clocks coincide. Direct writes (`UpsertAsync`, `TryInsertAsync`, and the other `TimeSpan?` writes) always set them equal, so they never create a reserve. `UpsertEntryAsync` is the one direct write that takes `CacheEntryOptions` and creates a reserve.

On Redis the key TTL follows physical expiration, not logical expiration, when fail-safe is on.

## Read path

```
 GetOrAddAsync(key)
   │
   ▼
 ┌──────┐  fresh hit
 │  L1  │ ─────────────────────────────────▶ value
 └──────┘
   │ miss / logically stale
   ▼
 ┌──────┐  fresh hit
 │  L2  │ ──▶ promote to L1 ────────────────▶ value
 └──────┘     (Hybrid only — single-tier providers have no L2)
   │ miss / logically stale
   ▼
 acquire per-key lock   (+ distributed lock if UseDistributedFactoryLock)
   │ acquired                          │ busy / LockTimeout
   ▼                                   ▼
 run factory                    stale reserve? ─yes─▶ serve stale (IsStale=true)
   │                                   │ no
   ├─ success ──▶ write tier(s) ──▶ value
   │
   └─ fails or hard-timeout ──▶ stale reserve? ─yes─▶ serve stale (IsStale=true)
                                       │ no
                                       ▼
                                  miss (NoValue) / throw
```

A store read that fails counts as a miss, so a cached value can still come back while the store is unhealthy. Fail-safe restamps and sliding re-arms are best-effort writes for the same reason.

## Fail-safe

`CacheValue<T>.IsStale` is `true` only for the call that activated fail-safe or served stale because a timeout fired. After activation, the cache restamps the entry fresh for `FailSafeThrottleDuration` (default 30 seconds), so reads during that window are logical hits with `IsStale = false`. The throttle never extends physical retention. `FailSafeMaxDuration` defaults to 1 day.

The throttle also holds for a reserve that `RemoveByTagAsync` or `ClearAsync` invalidated. When the factory fails on such a reserve, `GetOrAddAsync` serves the reserve without calling the factory until the window ends, so a failing factory runs once per `FailSafeThrottleDuration` instead of on every call. Direct reads still miss that entry, on this instance and on every peer that reads the shared store. A caller with fail-safe off ignores the window and runs the factory. A tag removal or clear issued while the window is open does not shorten it.

## Factory timeouts

The cache picks one timeout per call:

| Condition | Effective timeout | Result when it fires |
| --- | --- | --- |
| Fail-safe on, a stale reserve exists, finite `FactorySoftTimeout` | Soft | Return stale now; the factory continues in the background and writes its result. |
| No soft fallback, finite `FactoryHardTimeout` | Hard | Cancel or abandon the factory; serve stale if a reserve exists, else throw `CacheFactoryTimeoutException`. |
| Neither applies | None | The factory runs unbounded, except for caller cancellation. |

```
 soft timeout  (fail-safe on + a stale reserve exists)
   factory start ──soft──▶ serve stale now (IsStale=true)
                           factory continues on a detached token ──▶ writes result

 hard timeout  (no stale fallback)
   factory start ──hard──▶ abandon factory ──▶ serve stale if a reserve exists,
                                               else throw CacheFactoryTimeoutException
```

`CacheFactoryTimeoutException` derives from `TimeoutException`. All three factory timeouts default to `Timeout.InfiniteTimeSpan`.

- **Background completion.** A soft-timed-out factory keeps running on a token the cache owns, not on the caller's token, so it can finish after the request is cancelled. A hard timeout never lets the factory complete in the background. This differs from FusionCache, which ties background completion to the caller.
- **`BackgroundFactoryCeiling`.** Infinite by default: a background factory runs to completion and holds the per-key lock meanwhile. A finite, positive value cancels the background token and releases the lock when it elapses. A factory that ignores its token may keep running untracked, but its late write is dropped, so it cannot overwrite a newer value.
- **Per-key, not global.** The lock prevents two background refreshes of one key. It does not limit refreshes across different keys.
- **Late writes.** A factory write derived from an earlier entry carries that entry's concurrency stamp, so a slow factory cannot resurrect a key that was removed meanwhile or overwrite a newer explicit write. On Redis the stamp has millisecond resolution: two writes of one key with identical options in the same millisecond can compare equal. Set `JitterMaxDuration` above zero to close that window.

## Lock waits

- **With a stale reserve and a finite `FactorySoftTimeout`.** A caller that cannot take the per-key lock within the soft timeout returns stale instead of waiting behind the running factory.
- **Without a stale reserve.** `LockTimeout` bounds the wait. It defaults to `Timeout.InfiniteTimeSpan`: the caller waits until the running factory releases the lock. A finite, positive value makes the caller return a miss (`CacheValue<T>.NoValue`) instead.

## Eager refresh

`EagerRefreshThreshold` (exclusive between 0 and 1) stamps an eager point of `createdAt + Duration × threshold` on the entry. A fresh `GetOrAddAsync` hit past that point returns the cached value at once and starts one background refresh with the caller's factory and options. The first reader past the point wins; the others return the cached value untouched. Because the stamp is on the entry, a reader on another node that shares the store can also trigger it.

- A failed eager refresh only loses the early renewal. The entry stays fresh until it expires naturally, and fail-safe (when on) takes over after that. An eager failure never activates fail-safe.
- An entry restamped by the fail-safe throttle carries no eager point, so it cannot trigger an eager refresh during the throttle window.

## Sliding expiration

`SlidingExpiration` is an idle window for factory-backed entries. `Duration` stays the absolute cap from entry creation. Each value read re-arms the logical deadline to `min(now + SlidingExpiration, createdAt + Duration)`.

- The re-arm is throttled: a store writes it only when it moves the deadline out by more than `min(1 s, SlidingExpiration / 10)`. After its last value read, an entry stays readable for at least 90% of the idle window, and never more than one second short of it.
- Metadata reads, such as `GetExpirationAsync`, `ExistsAsync`, key listing, and counts, do not re-arm.
- Call `ICache.RefreshAsync(key)` to re-arm a sliding entry without reading its value. It does nothing for a miss or a non-sliding entry. On Redis an untagged entry re-arms without transferring the value; a tagged entry falls back to a full read.
- On Redis the key TTL follows the idle deadline.

## Jitter

`JitterMaxDuration` adds a random `[0, JitterMaxDuration)` offset to `Duration` on each write, so entries created together do not expire together. The offset applies to the logical, physical, and eager points alike. It defaults to `TimeSpan.Zero` (off).

## Conditional refresh

Use the overload that takes a `CacheFactoryContext<T>` factory when the origin can answer "changed since?" more cheaply than resending the value, as with HTTP `ETag` or `If-Modified-Since`, or a database row version. The context carries the last-known value and its validators (`ETag`, `LastModifiedAt`).

- Return `context.NotModified()` to restamp the cached value as fresh without transferring it again. With no last-known value (`context.HasStaleValue` is `false`) it throws `InvalidOperationException`, so return `context.Modified(value)` on a cold cache.
- Return `context.Modified(value, eTag, lastModifiedAt)` to replace the value.
- The factory may replace `context.Options` and `context.Tags` before it returns (adaptive caching). The write re-validates the options: an invalid change throws after the factory ran, and nothing is written. Changes to `FactorySoftTimeout`, `FactoryHardTimeout`, and `LockTimeout` do nothing for the current call, because the cache reads them before the factory runs.
- Conditional factories get the same timeout, fail-safe, soft-timeout, and eager-refresh behavior as the plain factory.

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

## Tier control per call

These flags affect Hybrid only; single-tier providers ignore them.

| Flag | Effect on `GetOrAddAsync` |
| --- | --- |
| `SkipCacheRead` | Force refresh: reads neither tier and always runs the factory. No reserve is read, so a factory failure propagates. It overrides the two per-tier read flags. |
| `SkipMemoryCacheRead` | Skips the L1 read and serves from, or refreshes against, L2. An L2 reserve stays available for fail-safe, and a fresh L2 value still fills L1 unless `SkipMemoryCacheWrite` is set. |
| `SkipDistributedCacheRead` | Skips the L2 read: a fresh L1 value serves without an L2 round trip, and an L1 miss goes straight to the factory. Setting both per-tier flags equals `SkipCacheRead`. |
| `SkipMemoryCacheWrite`, `SkipDistributedCacheWrite` | Suppress the L1 or L2 write of the factory result and of `UpsertEntryAsync`. The backplane still publishes, so peers drop their stale L1 copy. |

`GetAsync` and `GetAllAsync` take no `CacheEntryOptions` and always read both tiers.

---

## Headless.Caching.DistributedLocks

Adapter that backs the cache's cross-node factory lock with the application's `IDistributedLock`, for entries that set `CacheEntryOptions.UseDistributedFactoryLock`.

### Setup

```bash
dotnet add package Headless.Caching.DistributedLocks
```

Register any `Headless.DistributedLocks.*` provider, then call `setup.UseDistributedFactoryLock()` inside `AddHeadlessCaching`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Any Headless.DistributedLocks provider works; the adapter resolves IDistributedLock.
builder.Services.AddHeadlessDistributedLocks(locks => locks.UseRedis());
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.UseDistributedFactoryLock();
});
```

Opt in per entry:

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

**Verify:** an entry with `UseDistributedFactoryLock = true` on a host without `setup.UseDistributedFactoryLock()` (or another `ICacheFactoryLockProvider`) fails its first `GetOrAddAsync` with `InvalidOperationException` naming the adapter package. A host that calls `UseDistributedFactoryLock()` with no `IDistributedLock` registered fails when the lock provider is first resolved.

### Configuration

`UseDistributedFactoryLock` has three shapes: parameterless, `Action<CacheFactoryLockOptions>`, and `Action<CacheFactoryLockOptions, IServiceProvider>`.

| Option | Default | Description |
| --- | --- | --- |
| `ResourcePrefix` | `"cache:factory:"` | Prefix of the lock resource name, `{ResourcePrefix}{cacheName}:{key}`. A name over 255 characters becomes `{ResourcePrefix}sha256:{digest}` of `{cacheName}:{key}`, so a long key stays within every lock provider's name limit. At most 184 characters. The cache name is `default` for the unnamed cache, so two cache instances lock the same key separately. |
| `TimeUntilExpires` | `null` | Lease TTL of each factory lock. `null` uses the lock provider's default lease. When set, it must be finite and positive; zero, negative, and `Timeout.InfiniteTimeSpan` fail at startup, because a lease that cannot expire would block every node forever after its holder crashes. |

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.UseDistributedFactoryLock(options =>
    {
        options.ResourcePrefix = "myapp:cache:factory:";
        options.TimeUntilExpires = TimeSpan.FromMinutes(2);
    });
});
```

### Runtime behavior

- **When to use it.** Reserve it for expensive factories, such as a slow query or a paid API call. Per-node single-flight already exists without it, and every cold refresh with it pays one lock round trip. Entries that do not set the flag pay nothing.
- **Order.** The cache takes the local per-key lock first, then the distributed lock with the same wait budget: the soft timeout when a stale reserve can absorb it, else `LockTimeout`. On elapse it serves stale when a reserve exists and returns a miss otherwise.
- **Re-check.** After taking the distributed lock, the cache reads the shared store again before running the factory, so the node that lost the race serves the winner's fresh value.
- **Detached refreshes.** A soft-timeout background completion and an eager refresh keep the lease until their write lands.
- **Eager refresh** makes one non-blocking attempt (`TimeSpan.Zero`). When another node holds the lock, this node skips the refresh and leaves the fresh entry alone. A throwing acquire on this path is logged and ignored.
- **Wait mapping.** The wait maps onto `DistributedLockAcquireOptions.AcquireTimeout`: `TimeSpan.Zero` tries once, `Timeout.InfiniteTimeSpan` waits without bound, and a finite value bounds the wait.
- **Lock backend down.** An acquire that throws (as opposed to returning `null` because another node holds the lock) degrades through fail-safe: with `IsFailSafeEnabled` and a usable reserve the cache serves stale, restamps the throttle, and logs a warning. Without a reserve the exception propagates. Caller cancellation always propagates as cancellation.
- **Release** is best-effort: a failed release is logged and the lease TTL frees the key. Keep `TimeUntilExpires`, or the lock provider's default lease, well above the slowest factory run.
- Registers `ICacheFactoryLockProvider` (`TryAdd`, so an existing registration wins) and `CacheFactoryLockOptions` as singletons. It registers no `IDistributedLock`.
