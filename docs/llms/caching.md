---
domain: Caching
packages: Caching.Abstractions, Caching.Core, Caching.DistributedLocks, Caching.Hybrid, Caching.InMemory, Caching.Redis, Caching.Bcl, Caching.OutputCache
---

# Caching

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Resilience model](#resilience-model)
    - [Read path](#read-path)
    - [Fail-safe and the expiration timeline](#fail-safe-and-the-expiration-timeline)
    - [Operation semantics and entry model](#operation-semantics-and-entry-model)
    - [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Caching.Abstractions](#headlesscachingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Caching.Core](#headlesscachingcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Caching.DistributedLocks](#headlesscachingdistributedlocks)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Caching.Hybrid](#headlesscachinghybrid)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Caching.InMemory](#headlesscachinginmemory)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Caching.Redis](#headlesscachingredis)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Design Notes](#design-notes-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Caching.Bcl](#headlesscachingbcl)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-6)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Caching.OutputCache](#headlesscachingoutputcache)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Design Notes](#design-notes-7)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)

> Unified cache abstraction with in-memory, Redis, and hybrid L1+L2 implementations, plus fail-safe, refresh, tagging, and multi-node stampede protection on the factory path.

## Quick Orientation

Install `Headless.Caching.Abstractions` plus one provider. All registration flows through a single `services.AddHeadlessCaching(setup => ...)` call (entry point in `Headless.Caching.Core`); provider packages contribute `Use*`/`Add*Tier`/`AddNamed` extensions on the setup builder. Code against `ICache` for application cache operations.

- Single-instance or development: `Headless.Caching.InMemory` with `setup.UseInMemory()`.
- Multi-instance shared cache: `Headless.Caching.Redis` with `setup.UseRedis(...)`.
- Local hot path plus shared L2: `Headless.Caching.Hybrid` with `setup.AddMemoryTier()`, `setup.AddRedisTier(...)`, messaging, then `setup.UseHybrid()`.
- BCL `IDistributedCache` consumers such as ASP.NET Core Session: add `Headless.Caching.Bcl` and call `setup.UseBclCache(...)` inside the same `AddHeadlessCaching` setup.
- Cross-node factory single-flight: add `Headless.Caching.DistributedLocks`, call `setup.UseDistributedFactoryLock()`, and opt in per entry with `CacheEntryOptions.UseDistributedFactoryLock`.

`ICache` supports scalar reads/writes, bulk operations, prefix operations, atomic compare/replace and numeric operations, set operations, tag invalidation (`RemoveByTagAsync`), and a logical whole-cache clear (`ClearAsync`). Invalidation comes in shapes that differ by what survives. `RemoveAsync` hard-deletes a single entry (including any fail-safe reserve); `ExpireAsync` logically expires a single entry — normal reads miss immediately but the fail-safe reserve is preserved. `RemoveByTagAsync` is an O(1) logical tag invalidation (it writes a per-tag timestamp marker; it does not enumerate or delete member keys), and `ClearAsync` is an O(1) logical whole-cache clear (it bumps one reserved generation marker); both preserve fail-safe reserves. `FlushAsync` is the whole-cache flush that drops reserves — physical in-process, a logical remove-generation marker on a distributed (Redis) tier (cluster-safe, no `FLUSHDB`); the reserve-dropping counterpart of `ClearAsync`. `GetOrAddAsync` is the factory-backed path: it is governed by `CacheEntryOptions` (fail-safe, factory timeouts, sliding expiration, eager refresh, tags, distributed lock) and has a conditional overload taking a `CacheFactoryContext<T>` factory for HTTP-304-style refresh. `UpsertEntryAsync` is the only direct-write path that also honors `CacheEntryOptions`. Named cache instances are added with `setup.AddNamed(name, i => i.Use{InMemory,Redis,Hybrid}(...))` and resolved through `ICacheProvider`.

## Agent Instructions

- Use `ICache` from `Headless.Caching.Abstractions` for application cache operations. Use `Microsoft.Extensions.Caching.Distributed.IDistributedCache` only for standard BCL consumers such as ASP.NET Core Session, and back it with `Headless.Caching.Bcl`. Use `IRemoteCache` only when a remote/L2 implementation is required.
- For ASP.NET Core response/output caching, do not back `AddOutputCache()` with `IDistributedCache` — ASP.NET's own guidance rejects it as an output-cache store because it lacks atomic tag operations. Add `Headless.Caching.OutputCache` and call `setup.UseOutputCache(...)`; it registers an `IOutputCacheStore` over a named Headless cache so tag eviction rides the engine's distributed tag index. Still call `AddOutputCache()` and declare tags via `[OutputCache(Tags = "...")]` / `.CacheOutput(p => p.Tag("..."))` — the package supplies only the store, not the policy. In the `configureCache` callback select only the backing provider (`instance.UseRedis(...)` / `UseHybrid(...)`); no serializer configuration is needed because the middleware's payloads are `byte[]`, the cache's native wire format — configuring a serializer on the named instance is rejected at registration. `UseOutputCache` is a consumer of the engine, so `AddHeadlessCaching` still requires a default provider alongside it.
- In Headless caching docs, `Memory` means `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.
- Use `Headless.Caching.InMemory` for development and single-instance deployments. Use `Headless.Caching.Redis` for production multi-instance deployments. Use `Headless.Caching.Hybrid` when the app needs process-local read speed with remote cache sharing.
- Configure every cache instance (default, tiers, named, cross-cutting) in one `services.AddHeadlessCaching(setup => ...)` call. Exactly one default `Use{InMemory,Redis,Hybrid}` is required; a second `AddHeadlessCaching` call on the same service collection throws, and a failed setup leaves the service collection unchanged.
- For hybrid caching, call `setup.AddMemoryTier()`, then `setup.AddRedisTier(...)`, then `setup.UseHybrid()` in the same setup. The hybrid cache becomes the default `ICache`. `UseHybrid()` without a registered `IRemoteCache` (i.e. without `AddRedisTier(...)` or a named remote tier) throws `InvalidOperationException` at DI resolve time; `Headless.Caching.InMemory` does not register `IRemoteCache` and cannot act as L2. Registering a tier for the role the default provider already claims (for example `UseRedis` plus `AddRedisTier`) throws at registration.
- `ICache<T>` is the single typed convenience facade wrapping the default `ICache`; it exposes the full operation surface bound to type `T`. There are no typed tier interfaces (`IRemoteCache<T>` and `IInMemoryCache<T>` do not exist). For tier-specific access use the untyped `IRemoteCache`/`IInMemoryCache` with method-level generics, or `ICacheProvider.GetCache(name)`.
- `setup.UseDistributedFactoryLock()` comes in three overload shapes: parameterless, `Action<CacheFactoryLockOptions>`, and `Action<CacheFactoryLockOptions, IServiceProvider>`. `CacheFactoryLockOptions.TimeUntilExpires`, when set, must be finite and positive — zero, negative, or `Timeout.InfiniteTimeSpan` are rejected at startup.
- Always check `CacheValue<T>.HasValue` before accessing `.Value`; cache misses return `HasValue = false`.
- `GetOrAddAsync` takes `CacheEntryOptions`. Passing a `TimeSpan` still works through implicit conversion when only duration is needed.
- A non-positive `CacheEntryOptions.Duration` (zero or negative — for example a BCL absolute expiration already in the past) means "expire immediately": the write becomes an immediate eviction across every provider (Redis, InMemory, Hybrid) instead of throwing `ArgumentOutOfRangeException`.
- **Set-membership ops are the exception to the rule above:** `SetAddAsync(key, members, TimeSpan.Zero)` (or a negative expiration) does **not** evict the key — it removes only the named members (delegating to `SetRemoveAsync`) and leaves the rest of the set intact. The "non-positive `Duration` = immediate whole-key eviction" statement covers scalar writes and `CacheEntryOptions.Duration`, not set-membership operations; see the `ICache.SetAddAsync` / `SetRemoveAsync` XML remarks for the per-op contract.
- The option-less `GetOrAddAsync` extension overloads use `ICache.DefaultEntryOptions` and throw `InvalidOperationException` when it is unset. Set `options.DefaultEntryOptions` at registration to opt in; never assume a magic default duration exists.
- Set `CacheEntryOptions.SlidingExpiration` when a factory-backed entry should expire after an idle window while still respecting `Duration` as the absolute cap. Value reads re-arm; metadata reads such as `GetExpirationAsync` do not.
- Use `ICache.RefreshAsync(key)` when a caller needs to re-arm a sliding entry without materializing its value. It is a no-op for misses and non-sliding entries. On Redis, an untagged entry re-arms from the frame header only — the value payload is never transferred, a measurable win for large session payloads; tagged entries fall back to a full read.
- Do not combine sliding expiration with fail-safe, and do not combine sliding expiration with eager refresh; the coordinator rejects both combinations.
- Enable fail-safe per factory-backed entry with `CacheEntryOptions.IsFailSafeEnabled = true`. When the factory throws and a logically expired value is still physically retained, `GetOrAddAsync` serves that value and returns `CacheValue<T>.IsStale = true`.
- Fail-safe retention is bounded by `max(Duration, FailSafeMaxDuration)` from entry creation. `FailSafeThrottleDuration` restamps logical expiration to avoid hammering a failing factory, but never extends physical retention.
- Normal value reads (`GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`) use logical expiration. A fail-safe reserve is only consumed by `GetOrAddAsync`.
- Pick `RemoveAsync` vs `ExpireAsync` by whether the fail-safe parachute should survive the invalidation. `RemoveAsync` hard-deletes the entry and its physical reserve, so a subsequent `GetOrAddAsync` whose factory fails has nothing to fall back to. `ExpireAsync` only logically expires the entry: normal reads miss immediately, but the fail-safe reserve is kept, so a later `GetOrAddAsync` whose factory fails (with `IsFailSafeEnabled`) can still serve the stale value. For an entry written without fail-safe (no reserve beyond its logical lifetime) `ExpireAsync` is equivalent to `RemoveAsync`. Both return `true` when an entry was found and `false` when the key is absent. On Hybrid, `ExpireAsync` expires both tiers and propagates an `Expire` invalidation so peers logically expire their L1 copy (reserve preserved) rather than removing it.
- Keep direct write operations (`UpsertAsync`, `TryInsertAsync`, set/increment operations) on `TimeSpan?`; they do not establish a fail-safe reserve because they do not carry `CacheEntryOptions`. Use `UpsertEntryAsync(key, value, options)` only when the write needs option semantics (fail-safe reserve, eager stamp, sliding, tags) — it performs a read-before-write, so prefer plain `UpsertAsync` on hot paths.
- Set `CacheEntryOptions.EagerRefreshThreshold` (exclusive between 0 and 1) to refresh hot entries in the background before they expire; the eager point is `createdAt + Duration × threshold`. A failed eager refresh leaves the entry fresh until natural expiry — it never activates fail-safe by itself.
- Set `CacheEntryOptions.JitterMaxDuration` to add a random `[0, JitterMaxDuration)` offset to `Duration` on each write, desynchronizing the expiry of entries created together (anti-stampede). The jitter flows into the logical, physical, and eager spans alike, so it never violates the `physical >= logical` invariant. Defaults to `TimeSpan.Zero` (off).
- In a conditional factory, `context.NotModified()` requires a last-known value: check `context.HasStaleValue` and return `context.Modified(value)` on a cold cache, or the call throws `InvalidOperationException`.
- Adaptive changes to `context.Options` are re-validated at write time (an invalid mutation throws after the factory ran, nothing is written), and changes to the factory-timeout family (`FactorySoftTimeout`, `FactoryHardTimeout`, `LockTimeout`) are inert for the current call — those fields are consumed before the factory runs.
- Tags must be non-empty, and both the tag count and each tag's UTF-8 byte length must fit in an unsigned 16-bit value; violations throw `ArgumentException` before anything is written.
- `RemoveByTagAsync` is O(1) logical (Family-2) invalidation: it writes a per-tag timestamp marker and returns `ValueTask` (no count) — it does not enumerate or delete member keys. On the next read, an entry whose birth time (`CreatedAt`) predates the newest applicable tag marker is treated as a miss by direct reads and demoted to a fail-safe reserve by the factory coordinator (so a failing factory can still serve it stale). It is version-pinned: a key re-created after the marker (a newer birth time) is NOT invalidated. Do not expect it to physically remove entries — counts, key listing, and prefix reads still see the keys until they physically expire or are evicted.
- `ClearAsync` is the logical, O(1) whole-cache counterpart of `RemoveByTagAsync`: it bumps one reserved generation marker so every entry born before the bump reads as a miss but keeps its fail-safe reserve. Pick `ClearAsync` when fail-safe coverage should outlive the clear; pick `FlushAsync` (drops reserves — physical in-process, a logical remove-generation marker on a distributed tier) to drop everything including reserves.
- Tagging and `ClearAsync` work on Redis Cluster (one marker key per tag, plus one clear marker — no cross-slot enumeration). Marker keys live under a reserved namespace prefixed with a NUL byte (U+0000) (`{KeyPrefix}\0__tag:{tag}` and `{KeyPrefix}\0__clear`); ordinary cache keys never contain a NUL byte, so consumer keys cannot collide with them.
- Logical tag/clear invalidation is lazy and eventually visible on a shared L2: another instance sees an L2 marker bump only after its process-local marker cache refreshes (Redis `TagMarkerRefreshWindow`, default 2s); the physical TTL backstops staleness if a marker is ever lost. On a backplane hybrid the receiver seeds both its L1 and its L2 marker immediately from the notification timestamp (via `ISeedableTagMarkerCache`), so neither is window-bounded; the window bounds only no-backplane (pure-Redis) deployments and recovery after a missed backplane message.
- `CacheEntryOptions.UseDistributedFactoryLock` requires the `Headless.Caching.DistributedLocks` adapter (`setup.UseDistributedFactoryLock()`) plus a registered `IDistributedLock`; enabling it without the provider fails the read with `InvalidOperationException`. Reserve it for expensive factories — per-node single-flight already exists without it.
- Named cache instances must not use a reserved name — the `CacheConstants` role keys (`Headless.Caching:Memory`, `Headless.Caching:Remote`, `Headless.Caching:Hybrid`), or any name under the `Headless.Caching:` namespace; `setup.AddNamed` throws `ArgumentException` for reserved names and rejects duplicates. Resolve named instances through `ICacheProvider.GetCache(name)` / `GetCacheOrNull(name)`.
- Named Redis cache instances can select their serializer with `instance.WithSerializer(...)` (a Redis-provider capability shipped in `Headless.Caching.Redis`) when that instance needs a different value codec from the default cache. InMemory stores object references and never serializes, so `WithSerializer` is not offered there; on a hybrid instance it governs L2 (Redis) only. The `Headless.Caching.Bcl` and `Headless.Caching.OutputCache` adapters need no serializer configuration: `byte[]` is the cache's native wire format, stored verbatim (never passed through a serializer), so their `byte[]` payloads avoid JSON/base64 encoding under any serializer.
- For opaque-byte payloads, prefer the `IBufferCache` fast path over `GetAsync<byte[]>`/`UpsertEntryAsync<byte[]>`: it reads the payload into a caller buffer and writes from a `ReadOnlySequence<byte>` without the 1–2 intermediate `byte[]` materializations the generic path allocates (read drops from 3 payload copies to 1, write from 2 to 1; the one remaining copy is the unavoidable network I/O — zero-copy across a network boundary is impossible). Redis, InMemory, and Hybrid implement it; call it through `BufferCacheExtensions.TryGetToOrFallbackAsync(key, IBufferWriter<byte>)` / `UpsertRawOrFallbackAsync(key, ReadOnlySequence<byte>, options)` so a non-byte-oriented provider transparently falls back to the generic `byte[]` path. `PipeWriter` is an `IBufferWriter<byte>`, so a response/body pipe is a valid destination.
- Named hybrid instances publish invalidations with `CacheInvalidationMessage.CacheName`, and `HybridCacheInvalidationConsumer` routes them through `ICacheProvider` to the matching named hybrid. Default hybrid messages use a null `CacheName` and resolve through the `CacheConstants.HybridCacheProvider` role key.
- Hybrid `AllowBackgroundDistributedCacheOperations = true` fire-and-forgets the L2 write and backplane publish on additive writes (the `GetOrAddAsync` factory write-through, `UpsertAsync`, `UpsertAllAsync`): the caller returns after the L1 write, so L2 and peers briefly lag L1. A failed background write queues for replay when `EnableAutoRecovery` is on, else is logged best-effort. Removes, `Try*`, atomic/set ops, and reads stay synchronous because their result is the L2 answer.
- Hybrid `DistributedCacheSoftTimeout` / `DistributedCacheHardTimeout` bound slow L2 reads. A factory-backed read with an L1 stale reserve serves that reserve on L2 soft timeout and skips the origin factory; a plain read degrades to a miss. `DistributedCacheCircuitBreakerDuration` skips L2 operations for a short window after non-cancellation L2 failures. When a non-replayable write (removes, `Try*` CAS ops, atomic numeric ops, set mutations, prefix sweeps) fails against L2, Hybrid clears — or logically expires, for `ExpireAsync` — the affected L1 entries before rethrowing: the surfaced exception does not mean "nothing changed locally"; this node deliberately stops serving values whose L2 state is unknown.
- Hybrid auto-recovery (`EnableAutoRecovery = true`) is degraded-mode semantics, not a guarantee: a failing single-key L2 write succeeds against L1 and is replayed later, so L2 (and other instances) can lag L1 until recovery. Successful set/remove replays republish their key invalidation, so peers converge once replay lands instead of waiting out their L1 TTL. Bulk, atomic, and set operations are never captured and still surface failures.
- Use `FactorySoftTimeout` only with fail-safe and a stale reserve. When it fires, the caller gets stale data and the factory continues in the background under a detached internal token.
- Do not capture request-scoped disposables in a soft-timeout or eager-refresh factory. The background refresh can outlive the request token; create a fresh scope inside the factory when scoped services are needed.
- Use `FactoryHardTimeout` to bound cold-cache factory waits. When it fires with no stale fallback, `GetOrAddAsync` throws `CacheFactoryTimeoutException`; when stale data exists, it serves stale.
- `BackgroundFactoryCeiling` defaults to `Timeout.InfiniteTimeSpan` (no ceiling); a detached background refresh runs to completion. Set a finite, positive value to bound how long it can hold the per-key lock. It is **required to be finite** whenever fail-safe is enabled together with a finite `FactorySoftTimeout` — that combination is the one that detaches the factory, so an infinite ceiling could let a hung factory hold the per-key lock indefinitely; `ValidateOptions` throws if it is left infinite there.
- `LockTimeout` defaults to `Timeout.InfiniteTimeSpan`: a caller with no stale reserve waits until the in-flight factory releases the per-key lock. Set a finite, positive value so such a caller degrades to a miss (`CacheValue<T>.NoValue`) instead of blocking. When a stale reserve exists and `FactorySoftTimeout` is finite, the soft timeout governs the wait instead and the caller is served stale.
- Per-call tier control (Hybrid; single-tier providers ignore these): `CacheEntryOptions.SkipCacheRead = true` is a force-refresh — `GetOrAddAsync` bypasses the cached read on both tiers and always runs the factory (so no stale fail-safe reserve is read; a factory failure has nothing to fall back to and propagates). `SkipMemoryCacheWrite` / `SkipDistributedCacheWrite` suppress the L1 / L2 write on the factory write-through and `UpsertEntryAsync`; the backplane publish still fires so peers drop stale L1. (Granular per-tier *read*-skip — skip one tier but read the other — is a follow-up; only the both-tier force-refresh is honored today.)
- Hybrid logs startup best-practice warnings for valid-but-questionable configs (e.g. fail-safe with no graveyard window beyond `Duration`, a finite `FactorySoftTimeout` without fail-safe, an over-high `EagerRefreshThreshold`, an over-large `AutoRecoveryDelay`); these are warnings, never failures.
- Caller cancellation never serves stale: if the `CancellationToken` passed to `GetOrAddAsync` is cancelled, the exception propagates and fail-safe/background completion does not activate from that cancellation.
- StackExchange.Redis does not support `CancellationToken` on its operations. Configure Redis operation timeouts via `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`; factory timeouts are separate coordinator behavior.
- Redis scalar entries use a versioned binary envelope. Do not parse Redis string bytes as the application payload directly; strip the envelope first unless the key is a raw counter.
- Redis key TTL follows physical expiration, not logical expiration, when fail-safe is enabled, and follows the sliding idle deadline when sliding expiration is enabled.
- Use `options.KeyPrefix` to namespace cache keys per application or module.
- InMemory cache supports `CloneValues = true` for value isolation between callers.
- Hybrid cache `DefaultLocalExpiration` controls L1 TTL independently of L2. Set it shorter than L2 for freshness; sliding factory entries still use the L2 physical cap as the absolute duration authority.

## Core Concepts

### Resilience model

The factory path (`GetOrAddAsync`) composes several independent mechanisms, each targeting one failure mode. Reach for the option whose row matches the failure you need to survive; the mechanisms combine freely except where Agent Instructions note an exclusion (sliding expiration excludes both fail-safe and eager refresh).

| Failure mode | Mechanism | Option / API |
| --- | --- | --- |
| Concurrent misses on one node (thundering herd) | Per-key single-flight factory | Always on |
| Thundering herd across nodes sharing one L2 | Cross-node single-flight | `UseDistributedFactoryLock` (+ `Headless.Caching.DistributedLocks`) |
| Origin down or throwing | Fail-safe — serve last known-good value | `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration` |
| Origin slow, stale acceptable | Soft timeout — return stale, finish in background | `FactorySoftTimeout` (needs fail-safe + a stale reserve) |
| Origin slow, cold cache | Hard timeout — bound the cold wait | `FactoryHardTimeout` |
| Hot key expiry latency spike | Eager refresh — renew before expiry | `EagerRefreshThreshold` |
| Synchronized expiry of co-created entries | Jitter — desynchronize expiry | `JitterMaxDuration` |
| Re-fetching an unchanged payload | Conditional refresh (HTTP-304) | `GetOrAddAsync` `CacheFactoryContext<T>` overload |
| L2 slow or failing (Hybrid) | L2 read timeouts + circuit breaker | `DistributedCacheSoftTimeout` / `DistributedCacheHardTimeout` / `DistributedCacheCircuitBreakerDuration` |
| Transient L2/backplane outage (Hybrid) | Auto-recovery — queue and replay | `EnableAutoRecovery` |
| Invalidate a group of keys | Logical tag invalidation | `Tags` + `RemoveByTagAsync` |
| Invalidate everything, keep reserves | Logical whole-cache clear | `ClearAsync` |

### Read path

Every `GetOrAddAsync` resolves a value through the tier(s), then a single-flight factory. A logically-stale entry is not a hit for ordinary reads, but it is a fail-safe reserve the factory can fall back to:

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

Single-tier providers (`InMemory`, `Redis`) skip the L2 hop; the lock, factory, and fail-safe rows are identical across all providers because they live in the shared coordinator.

### Fail-safe and the expiration timeline

Every entry carries two clocks. **Logical expiration** ends the fresh window that ordinary reads see; **physical expiration** ends the fail-safe reserve that only `GetOrAddAsync` reads. Fail-safe stretches physical past logical so a failing factory has a parachute:

```
  write     logical expiry           physical expiry
  │                 │                        │
  ▼                 ▼                        ▼
  ├──── fresh ─────┼──── stale reserve ─────┼──── gone ──▶ t
```

- **fresh** — reads hit; `GetOrAddAsync` returns the live value.
- **stale reserve** — reads miss; `GetOrAddAsync` runs the factory and serves this value *only if the factory fails* (`CacheValue<T>.IsStale = true`), then throttles retries for `FailSafeThrottleDuration`.
- **gone** — reads miss; `GetOrAddAsync` runs the factory with no fallback.

`physical expiry = max(Duration, FailSafeMaxDuration)`. With fail-safe off, the two clocks coincide, the stale-reserve window collapses to zero, and `ExpireAsync` becomes equivalent to `RemoveAsync` (no reserve to preserve). A non-positive `Duration` (zero or negative) is a degenerate case meaning "expire immediately": every provider turns such a write into an immediate eviction rather than rejecting it, which is what lets the BCL adapter honor a `Set` whose absolute expiration is already in the past.

### Operation semantics and entry model

`CacheValue<T>` distinguishes misses from cached null values with `HasValue`. `IsStale` is set only when the current `GetOrAddAsync` call activates fail-safe or returns stale because a timeout fired.

Entries can carry two expiration timestamps:

- Logical expiration controls ordinary reads and cache freshness.
- Physical expiration controls how long a fail-safe reserve remains available to `GetOrAddAsync`.

`ExpireAsync` operates on those two clocks independently: it pulls logical expiration to now (ordinary reads miss) while leaving physical expiration intact, so the fail-safe reserve stays available to `GetOrAddAsync`. `RemoveAsync` drops both. When an entry has no physical reserve beyond its logical lifetime (a plain `TimeSpan?` write, or fail-safe disabled) the two clocks coincide and `ExpireAsync` collapses to `RemoveAsync`.

`SlidingExpiration` is an optional idle window for factory-backed entries. `Duration` remains the absolute cap from entry creation, and value-returning reads re-arm the logical deadline to `min(now + SlidingExpiration, createdAt + Duration)`. Metadata reads do not re-arm. Sliding expiration is rejected together with fail-safe and together with eager refresh in this version.

Eager refresh renews hot entries before they expire. `EagerRefreshThreshold` stamps an eager point of `createdAt + Duration × threshold` on the entry itself (`EagerRefreshAt`), so any reader of an eager-stamped entry can refresh it with its current factory and options — including readers on other nodes sharing the store. A fresh `GetOrAddAsync` hit past the eager point returns the cached value immediately and starts a detached refresh, deduplicated by a zero-timeout per-key lock attempt: the first reader past the point wins, everyone else returns untouched. The winner clears the eager stamp with a gate write before running the factory so concurrent readers stop triggering; a failed gate write skips the refresh and leaves the entry fresh and re-triggerable. A failed eager factory only loses the proactive renewal — the entry stays fresh to natural expiry (no fail-safe restamp, because nothing is stale yet), and fail-safe takes over after expiry when enabled.

Conditional refresh (the HTTP-304 pattern) runs the factory with a `CacheFactoryContext<T>` carrying the last-known value and its validators (`ETag`, `LastModifiedAt`). The factory asks the origin "changed since?" and returns `context.NotModified()` to re-stamp the existing value as fresh without re-transferring it, or `context.Modified(value, eTag, lastModifiedAt)` to replace it. The context's `Options` and `Tags` are mutable (adaptive caching): the write uses whatever the factory left there, re-validated before persisting. The simple value-factory overload adapts onto the same engine, so both shapes share identical timeout, fail-safe, and refresh semantics, and conditional factories also work through soft-timeout background completion and eager refresh.

Tag invalidation is Family-2 logical tag-version invalidation, the same model as FusionCache, MS HybridCache, and Symfony's `TagAwareAdapter`. `RemoveByTagAsync(tag)` writes a single per-tag timestamp marker (`\0__tag:<tag> = now`) in O(1) and returns `ValueTask` — it never enumerates or deletes member keys. Each entry carries its birth time (`CreatedAt`); on read, the shared predicate `CacheTagInvalidation.IsInvalidated` treats the entry as invalidated when any tag marker it carries (or the global clear-generation marker) is newer than `CreatedAt`. An invalidated entry is a miss for direct reads and a demoted fail-safe reserve under the factory coordinator (so a failing factory can still serve it stale). Memberships are version-pinned by birth time: a key re-created after a marker has a newer `CreatedAt` and is not invalidated by the old marker. The physical TTL (`PhysicalExpiresAt`) backstops staleness if a marker is lost. The earlier eager reverse-index design (tag → keys, swept by a budgeted loop) was replaced because it forced an iteration budget, blocked Redis Cluster, and raced members re-added mid-sweep; the trade-off is that logically-dead entries linger physically — prefix reads, key listing, and counts still see them until they physically expire or are evicted.

`ClearAsync()` is the whole-cache analogue: it bumps one reserved clear-generation marker checked by every read (tagged or not). Every entry born before the bump reads as a miss but keeps its fail-safe reserve. It is the reserve-preserving counterpart of `FlushAsync` (which drops reserves: physical in-process, a logical remove-generation marker on a distributed tier — cluster-safe, no `FLUSHDB`). On a shared L2, marker bumps are eventually visible: an instance resolves L2 markers from a process-local cache refreshed within `RedisCacheOptions.TagMarkerRefreshWindow` (default 2s), so a peer's bump is observed only after the window elapses (the bumping instance invalidates immediately on its own next read). On a backplane hybrid the receiver seeds both its L1 and its L2 marker immediately from the notification timestamp (via `ISeedableTagMarkerCache`), so neither is window-bounded; the window is the backstop only for no-backplane (pure-Redis) deployments and recovery after a missed backplane message.

Registration uses one builder with four slots. `services.AddHeadlessCaching(setup => ...)` is the single entry point, and provider packages contribute deferred extensions into the default slot (`Use{InMemory,Redis,Hybrid}` — the unkeyed `ICache`), the role-keyed tier slots (`AddMemoryTier`/`AddRedisTier` — the L1/L2 of a default hybrid), the named slot (`AddNamed(name, i => i.Use…(...))` — unlimited independent instances), and the cross-cutting slot (`UseDistributedFactoryLock`). The invariant is per-slot exactly-one rather than a global exactly-one-provider gate: exactly one default is required, at most one tier per reserved role is allowed, named instances need unique non-reserved names and exactly one provider each, and a tier role the default provider already claims (for example `UseRedis` plus `AddRedisTier`, which both claim the remote role) is rejected. The old per-provider extensions let two `isDefault: true` registrations silently race for the default `ICache` (first-wins); the builder turns that into a hard error at registration. All contributions are deferred until the gates pass and applied tiers → default → named → cross-cutting, so a throwing setup leaves the service collection unchanged, and a second `AddHeadlessCaching` call on the same service collection throws. `UseInMemory`, `UseRedis`, and `UseHybrid` each come in `Action<TOptions>`, `Action<TOptions, IServiceProvider>`, and `IConfiguration` shapes (`UseInMemory` and `UseHybrid` also work with no arguments; `UseRedis` does not, because `ConnectionMultiplexer` is required), and named instances bind their options per instance name — including the `IConfiguration` shape.

Named caches are keyed `ICache` registrations created by `setup.AddNamed(name, i => i.Use{InMemory,Redis,Hybrid}(...))`, each with independent options (and for Redis, an independent multiplexer and scripts loader). Three role keys are reserved and registered by every provider setup — `CacheConstants.{Memory,Remote,Hybrid}CacheProvider`, i.e. `Headless.Caching:{Memory,Remote,Hybrid}`, namespaced so they cannot collide with consumer-owned keyed services — so the default tiers are always reachable by role; named instances must pick other names (any name under the `Headless.Caching:` namespace is reserved). `ICacheProvider` resolves both shapes. `DefaultEntryOptions` (per cache instance, from `CacheOptions`) feeds the option-less `GetOrAddAsync` extension overloads and is deliberately explicit-at-registration: unset means those overloads throw rather than invent a duration.

The distributed factory lock is an opt-in second locking layer for multi-node stampede protection. The local per-key lock always comes first; with `UseDistributedFactoryLock` set and the `Headless.Caching.DistributedLocks` adapter registered, the coordinator then acquires a distributed lock with the same wait budget, re-checks the shared store (the loser of the cross-node race serves the winner's fresh value), and only then runs the factory. The lease transfers through soft-timeout background completions and eager refreshes so the cross-node guard holds until the detached write lands.

Factory timeout selection is a single decision:

| Condition | Effective timeout | Timeout result |
| --- | --- | --- |
| Fail-safe enabled, stale reserve exists, finite `FactorySoftTimeout` | Soft | Return stale and continue the factory in the background. |
| No soft fallback, finite `FactoryHardTimeout` | Hard | Cancel or abandon the factory; serve stale if possible, otherwise throw `CacheFactoryTimeoutException`. |
| Neither applies | None | Preserve existing unbounded factory behavior except for caller cancellation. |

```
 soft timeout  (fail-safe on + a stale reserve exists)
   factory start ──soft──▶ serve stale now (IsStale=true)
                           factory continues on a detached token ──▶ writes result

 hard timeout  (no stale fallback)
   factory start ──hard──▶ abandon factory ──▶ serve stale if a reserve exists,
                                               else throw CacheFactoryTimeoutException
```

Soft timeout also bounds acquisition of the per-key lock when fail-safe and a stale reserve exist. A concurrent waiter that cannot acquire the lock within `FactorySoftTimeout` returns stale rather than blocking behind an in-flight or background-completing factory. When no stale reserve exists, `LockTimeout` bounds that wait instead: it defaults to `Timeout.InfiniteTimeSpan` (wait until the in-flight factory releases the lock, matching FusionCache's default), and a finite value makes the waiter degrade to a miss (`CacheValue<T>.NoValue`) on elapse rather than blocking. Same-key re-entrant factory calls are only supported under the fail-safe plus stale plus finite-soft combination; otherwise they can still deadlock and are unsupported.

Background completion is per-key, not global. The keyed lock prevents duplicate cooperative factories for the same key while the background refresh runs, but it does not limit refreshes across distinct keys. If the background ceiling abandons a token-ignoring factory, that factory may continue running untracked; the coordinator gates late success writes from the timeout path so it cannot overwrite a newer timeout-path value after abandonment. Factory writes derived from a prior physical entry also carry the store's opaque `ConcurrencyStamp`, so a slow foreground or background factory cannot resurrect a removed key or overwrite a concurrent explicit writer that changed the entry after the factory read it. On Redis this compare is over the fixed frame header only (a deliberate perf choice), so two same-key writes sharing identical options within a single millisecond can compare equal — set `CacheEntryOptions.JitterMaxDuration > 0` to close that narrow window (see issue #583).

FusionCache alignment is intentional but not exact. Headless uses FusionCache-like factory soft/hard timeout selection, L2 read soft/hard timeouts, waiter lock timeout behavior, eager refresh, conditional/adaptive factories, auto-recovery, a simple L2 circuit breaker, and the same Family-2 logical tag-version invalidation, but Headless detaches the background refresh token from the caller token and abandons the factory path on hard timeout instead of allowing background completion after the hard limit.

### Zero-intermediate-copy buffer path

`IBufferCache` is an opt-in capability interface (in `Headless.Caching.Abstractions`) for consumers that hold opaque byte payloads — the ASP.NET Core output-cache adapter and the BCL distributed-cache adapter — and want to read/write those bytes without the intermediate `byte[]` materializations the generic `ICache.GetAsync<byte[]>`/`UpsertEntryAsync<byte[]>` path allocates. Redis, InMemory, and Hybrid implement it alongside `ICache`.

```csharp
public interface IBufferCache
{
    ValueTask<bool> TryGetToAsync(string key, IBufferWriter<byte> destination, CancellationToken ct = default);
    ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken ct = default
    );
}
```

**The floor is one copy per side, not zero.** A distributed cache must put the payload on the wire and read it back, so one I/O copy per side is unavoidable — "zero-copy across a network boundary" is physically impossible. What `IBufferCache` removes is the *removable* waste: the 1–2 intermediate `byte[]` copies the generic path pays (`ToArray()`, a `MemoryStream`, the serializer round-trip). The read path drops from 3 payload copies (wire → `byte[]` → `MemoryStream` → caller) to 1 (wire slice → caller's `IBufferWriter<byte>`); the write path drops from 2 (`ToArray` → serialize → network) to 1 (sequence → framed network buffer).

`PipeWriter` implements `IBufferWriter<byte>`, which is the lever: a store can stream the decoded payload straight into the caller's response pipe or body buffer. `TryGetToAsync` writes nothing on a miss and returns `false`. `UpsertRawAsync` consumes the `ReadOnlySequence<byte>` synchronously before its first await, so callers may hand in pooled buffers valid only for the duration of the call.

**`byte[]` is the cache's native wire format — stored verbatim, never passed through a serializer.** So the raw `IBufferCache` path and the typed `GetAsync<byte[]>`/`UpsertEntryAsync<byte[]>` path are always byte-consistent under any serializer; no special serializer configuration is needed to mix them. A `byte[]` typed write is not JSON/base64-encoded — it lands as the same raw bytes a `UpsertRawAsync` would write, and a `TryGetToAsync` reads them back identically. Every other entry semantic — expiry, fail-safe physical retention, Family-2 tag invalidation, sliding, `CreatedAt` stamping — is identical to the generic path; `UpsertRawAsync` reuses the same stamping/framing pipeline as `UpsertEntryAsync` and the only difference is that the payload arrives as a `ReadOnlySequence<byte>` rather than a `byte[]`.

Consumers do not feature-detect by hand: `BufferCacheExtensions.TryGetToOrFallbackAsync` / `UpsertRawOrFallbackAsync` take the `IBufferCache` fast path when the cache implements it and fall back to the generic `byte[]` path otherwise (the fallback materializes one `byte[]` — the cost the fast path avoids). On Redis the framing is copy-neutral: the frame header rides in the same allocation as the payload, the read exposes the value as a slice of the received buffer (`DecodedFrame.ValueSegment`), and the write splices the `ReadOnlySequence<byte>` into the single frame buffer at the value offset once, so a buffer write produces byte-identical frames to a generic write.

`byte[]` and `string` are the cache's native wire types — the codec stores and reads them without invoking the configured serializer, so the buffer path is serializer-independent and `IBufferCache` works the same under any serializer choice.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Caching.InMemory` | Single process, tests, local development, or L1 for Hybrid. | Multiple app instances must share cache state. | Fastest path, but data is per process and retained in memory. |
| `Headless.Caching.Redis` | Multiple app instances need a shared cache and Redis is already operational. | The app cannot tolerate Redis operational dependency. | Distributed and script-backed atomic operations, with network latency and Redis timeout tuning. |
| `Headless.Caching.Hybrid` | Reads are hot enough to benefit from L1 while L2 keeps instances coherent. | Invalidation messaging is not configured or L1 staleness is unacceptable. | Fast local reads plus remote sharing, with extra moving parts and invalidation timing. |

`Headless.Caching.DistributedLocks` is not a provider — it is an adapter that any provider's factory path can opt into per entry when the factory is expensive enough to justify a distributed lock round-trip.

`Headless.Caching.Bcl` is also not a provider. It registers a standard `IDistributedCache` adapter over a dedicated named Headless cache for framework integrations that require the BCL contract.

`Headless.Caching.OutputCache` is likewise not a provider. It registers an ASP.NET Core `IOutputCacheStore` over a dedicated named Headless cache, making `AddOutputCache()` distributed and tag-aware. It is separate from `Headless.Caching.Bcl` because an `IOutputCacheStore` references the ASP.NET shared framework, which the framework-agnostic BCL adapter must not pull in. Distribution and cluster-wide tag eviction are a function of the backing tier the consumer composes (Redis or Hybrid), not the adapter.

## Headless.Caching.Abstractions

Defines the unified caching interface for in-memory, distributed, and hybrid cache implementations.

### Problem Solved

Provides a provider-agnostic caching API so applications can switch between memory, Redis, and hybrid caches without changing call sites.

### Key Features

- `ICache` - core interface for cache operations:
    - Upsert/Get/Remove with expiration
    - `RefreshAsync` to re-arm sliding entries without returning the value
    - Removal (`RemoveAsync` hard-deletes including the fail-safe reserve; `ExpireAsync` logically expires but preserves the reserve)
    - Bulk operations (UpsertAll, GetAll, RemoveAll)
    - Prefix-based operations (GetByPrefix, RemoveByPrefix)
    - Atomic operations (TryInsert, TryReplace, Increment, SetIfHigher/Lower). `SetIfHigherAsync`/`SetIfLowerAsync` return the difference (`new - old` / `old - new`) when the store was updated, `0` on a no-op, and the stored value itself when the key was absent (nothing to diff against); a `0` return is therefore ambiguous when `0` is a legitimate stored value — check `ExistsAsync` first when that matters. On Redis the long overloads compare/diff via Lua IEEE-754 doubles, exact only up to 2^53.
    - Set operations (SetAdd, SetRemove, GetSet). String set members compare with ordinal (case-sensitive) equality on every provider (InMemory matches Redis's byte-exact membership); non-string member equality is provider-native (serialized-byte equality on Redis, default `Equals` on InMemory), so custom `Equals` overrides or non-canonical serializers can diverge across providers. `GetSetAsync` returns `CacheValue.NoValue` (`HasValue == false`, `Value == null`) whenever the requested page has no members — an absent key, an empty set, a set whose live members are all expired, and a `pageIndex` past the last live member all read as a miss identically across providers (no extra existence round-trip is issued). `HasValue` is authoritative and reflects whether the requested page has members, not whether the key exists; `Value` is never a non-null empty collection.
    - Tag invalidation (`UpsertEntryAsync` with `CacheEntryOptions.Tags`; `RemoveByTagAsync` — O(1) logical, returns `ValueTask`)
    - Logical whole-cache clear (`ClearAsync` — O(1), reserve-preserving) vs. reserve-dropping flush (`FlushAsync` — physical in-process, logical remove-generation marker on a distributed tier)
- `[PublicAPI] IBufferCache` - capability interface for byte-oriented caches (Redis, InMemory, Hybrid) that read a payload into an `IBufferWriter<byte>` (`TryGetToAsync`) and write it from a `ReadOnlySequence<byte>` (`UpsertRawAsync`) without the intermediate `byte[]` the generic path allocates. `byte[]` is the cache's native wire format (stored verbatim, never through a serializer), so the raw path and the typed `GetAsync<byte[]>`/`UpsertEntryAsync<byte[]>` path are always byte-consistent under any serializer — no special serializer configuration is needed; all expiry/tag/sliding/`CreatedAt` semantics match `UpsertEntryAsync`. See [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path).
- `[PublicAPI] BufferCacheExtensions` - `TryGetToOrFallbackAsync` / `UpsertRawOrFallbackAsync` on `ICache`: take the `IBufferCache` fast path when the cache implements it, else fall back to the generic `byte[]` path. Lets a consumer holding opaque bytes avoid re-implementing the feature-detect.
- `IInMemoryCache` - in-memory (L1) tier contract; a marker interface (`: ICache`) with no extra members.
- `IRemoteCache` - remote (L2) tier contract; adds `GetAllWithExpirationAsync<T>` / `GetWithExpirationAsync<T>` for single-round-trip value-plus-TTL reads (a remote store doesn't expose its TTL locally the way an in-memory tier does).
- `ICache<T>` - strongly typed convenience facade over the default `ICache`, exposing the full `ICache` surface (scalar reads/writes, bulk, prefix, atomic numeric ops `IncrementAsync`/`SetIfHigherAsync`/`SetIfLowerAsync`, `GetAllKeysByPrefixAsync`, `GetCountAsync`, `ExistsAsync`, `GetExpirationAsync`, `RemoveAllAsync`, `FlushAsync`) bound to a fixed type parameter. For a specific tier use the untyped `IRemoteCache`/`IInMemoryCache` (method-level generics) or `ICacheProvider.GetCache(name)`. Typed tier wrappers (`IRemoteCache<T>`, `IInMemoryCache<T>`) do not exist.
- `ICacheProvider` - resolves named cache instances and the reserved role keys (`CacheConstants.{Memory,Remote,Hybrid}CacheProvider` — `Headless.Caching:{Memory,Remote,Hybrid}`).
- `CacheValue<T>` - cache result with `HasValue` semantics and an `IsStale` flag when fail-safe serves a stale value.
- `CacheEntryOptions` - factory-backed entry options: `Duration`, `SlidingExpiration`, `EagerRefreshThreshold`, `IsFailSafeEnabled`, `FailSafeMaxDuration`, `FailSafeThrottleDuration`, `FactorySoftTimeout`, `FactoryHardTimeout`, `BackgroundFactoryCeiling`, `LockTimeout`, `UseDistributedFactoryLock`, and `Tags`.
- `CacheFactoryContext<T>` / `CacheFactoryResult<T>` - conditional-factory contract (the HTTP-304 pattern): the factory sees the last-known value and its validators (`ETag`, `LastModifiedAt`) and returns `NotModified()` or `Modified(value, eTag, lastModifiedAt)`; it may also replace `Options` and `Tags` before returning (adaptive caching).
- `CacheOptions` - base provider options carrying `KeyPrefix` and `DefaultEntryOptions`.
- `CacheDefaultEntryExtensions` - option-less `GetOrAddAsync` overloads that apply the cache instance's `DefaultEntryOptions` and throw `InvalidOperationException` when none is configured.
- `CacheFactoryTimeoutException` - `TimeoutException` subtype thrown when a hard factory timeout fires without a stale fallback.

### Design Notes

`GetOrAddAsync` accepts `CacheEntryOptions` so factory-backed cache entries have a stable extension point for fail-safe, factory timeouts, refresh, and tagging features. A `TimeSpan` converts implicitly to `CacheEntryOptions`, so positional duration-only call sites keep their shorthand while explicit options are available when a caller wants to name the duration. This is a greenfield public API break for named arguments: callers using `expiration: ...` on `GetOrAddAsync` must rename that argument to `options: ...`.

`SlidingExpiration` is an optional idle window for factory-backed entries. `Duration` remains the absolute cap from entry creation, and value-returning reads re-arm the logical deadline to `min(now + SlidingExpiration, createdAt + Duration)`. Metadata reads do not re-arm. Sliding expiration is rejected together with fail-safe and together with eager refresh in this version.

`EagerRefreshThreshold` (exclusive between 0 and 1) stamps an eager point of `createdAt + Duration × threshold` on the entry. A fresh `GetOrAddAsync` hit past that point returns the cached value immediately and starts a non-blocking, deduplicated background refresh, so hot keys are renewed before they expire and callers never block on the refresh. A failed eager refresh only loses the proactive renewal — the entry stays fresh until natural expiry, and fail-safe (when enabled) takes over from there.

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

### Installation

```bash
dotnet add package Headless.Caching.Abstractions
```

### Quick Start

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

### Configuration

No configuration required. This is an abstractions-only package; `CacheOptions.KeyPrefix` and `CacheOptions.DefaultEntryOptions` are configured on the provider packages.

### Dependencies

- `Headless.Extensions`

### Side Effects

None. This is an abstractions package.

---

## Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

### Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, timeout, eager refresh, conditional refresh, and background completion behavior.

### Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine; both the simple value factory and the conditional `CacheFactoryContext<T>` factory run on one state machine with identical timeout, fail-safe, and refresh semantics.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads (single-key `TryGetEntryAsync` and position-aligned bulk `TryGetAllEntriesAsync`, the latter resolving the whole batch's clear/remove/tag invalidation markers in one prefetch for O(1) marker round-trips regardless of key count), conditional writes, and metadata-only sliding re-arm (`TryRearmSlidingAsync`). Factory writes derived from an existing physical entry carry the entry's opaque `ConcurrencyStamp`; stores return `false` when the live entry no longer matches, preventing a late factory from resurrecting a removed key or clobbering a concurrent writer. (Stamp collision-resistance is provider-specific: the Redis stamp is the fixed frame header, so same-key writes with identical options within one millisecond may compare equal — a narrow window, closed by enabling jitter; see #583.)
- `CacheStoreEntry<T>` - entry snapshot with logical, physical, and sliding expiration plus per-entry metadata (`CreatedAt`, `EagerRefreshAt`, `ETag`, `LastModifiedAt`, `Tags`). `CreatedAt` is the birth time the Family-2 read-time predicate compares against tag/clear markers.
- `CacheStoreEntryWrite<T>` - write descriptor carrying the value, expirations, eager stamp, validators, `CreatedAt` (stamped on every fresh write so a prior tag/clear marker cannot invalidate it), `Tags`, and `IsRestamp` (marks metadata-only restamps — `NotModified` extensions, fail-safe throttle restamps, the eager-refresh gate write — so multi-tier stores can skip cross-instance invalidation for them).
- `CacheTagInvalidation` - the single shared read-time predicate `IsInvalidated(createdAt, newestMarker)` so every tier agrees on when an entry is logically tag/clear-invalidated.
- `CacheEntryStamps` - single home of the fresh-write stamp math (`CreatedAt` birth time, fail-safe extends physical retention, eager threshold stamps the eager point, sliding clamps the logical lifetime) and of options/tags validation, so the coordinator and the providers' direct `UpsertEntryAsync` writes always agree.
- `FactoryCacheStoreExtensions.UpsertEntryAsync` - shared direct options-based upsert composed on the store primitive (read-before-write; stamps a fresh `CreatedAt`).
- `ICacheFactoryLockProvider` - optional cross-node coordination seam consumed when `CacheEntryOptions.UseDistributedFactoryLock` is set (adapter: `Headless.Caching.DistributedLocks`).
- `CacheStoreEntryExtensions` - shared `IsFresh`/`IsPhysicallyPresent` predicates so every provider and the coordinator agree on the expiration boundary (an entry is expired at the exact tick, `expiresAt <= now`).
- `FactoryCacheCoordinator.IsCallerCancellation` - shared predicate provider composites use so caller cancellation propagates while an unrelated/downstream `OperationCanceledException` activates fail-safe consistently.
- `SetupCachingCore.AddHeadlessCaching` - the single registration entry point: provider packages contribute deferred extensions through `Use*`/`Add*Tier`/`AddNamed` on the setup builder, and contributions are applied only after the setup gates pass.
- `HeadlessCachingSetupBuilder` / `HeadlessCacheInstanceBuilder` / `ICacheProviderOptionsExtension` - the builder surface provider packages extend: a default slot (exactly one `Use*`), role-keyed tier slots (at most one per reserved role), named instances (unlimited, unique non-reserved names, exactly one provider each), and cross-cutting extensions.
- `ICacheProvider` over the container's keyed `ICache` registrations; `AddHeadlessCaching` registers it automatically.
- Fail-safe, factory timeout, eager refresh, and background completion logs.

### Design Notes

Providers construct the coordinator directly with their `TimeProvider`, logger, and optional `ICacheFactoryLockProvider`; the Core package ships the `AddHeadlessCaching` entry point and the setup builder, not a provider. Provider packages queue deferred `ICacheProviderOptionsExtension` contributions that `AddHeadlessCaching` applies tiers → default → named → cross-cutting only after the per-slot gates pass — exactly one default provider, at most one tier per reserved role, no tier role already claimed by the default provider, unique non-reserved instance names with exactly one provider each, and no repeated `AddHeadlessCaching` call — so a failed setup leaves the service collection unchanged. Store read failures are treated as misses, fail-safe restamp writes are best-effort, and sliding re-arm writes are best-effort so a cached value can still be returned when the backing store is unhealthy. A provider composite can mark a physically-present stale `CacheStoreEntry<T>` with `ServeStaleImmediately` when a lower tier degraded during the read; the coordinator then returns that stale value without running the factory, but only when fail-safe is enabled. Cancellation is classified by token identity: the caller's own cancellation propagates and never activates fail-safe, while an `OperationCanceledException` from an unrelated or downstream token is treated as a failure that activates fail-safe. Sliding expiration is rejected together with fail-safe (one needs value reads to extend the logical deadline while the other needs logical expiration to expose a stale reserve) and together with eager refresh (both re-arm the logical lifetime).

Factory timeout selection is centralized in the coordinator. If fail-safe is enabled, a stale reserve exists, and `FactorySoftTimeout` is finite, the soft timeout governs. Otherwise a finite `FactoryHardTimeout` governs. Otherwise factory execution is unbounded except for caller cancellation. A finite soft timeout also bounds acquisition of the same per-key lock when stale data exists, so waiters and supported same-key re-entrant calls return stale instead of blocking behind an in-flight refresh. When no stale reserve exists, `LockTimeout` (default `Timeout.InfiniteTimeSpan`) bounds that acquisition instead, and a finite value makes the waiter degrade to a miss rather than block.

Eager refresh triggers off the entry's own `EagerRefreshAt` stamp, so any reader of an eager-stamped entry can refresh it with its current factory and options. The first reader past the eager point wins a zero-timeout per-key `TryLock`; everyone else returns the still-fresh value untouched. The winner double-checks the entry under the lock, then performs a gate write that clears the eager stamp before the factory starts, so other readers (including other nodes reading through a shared store) stop triggering while the refresh is in flight. The gate write is best-effort: when it fails, the refresh is skipped and the entry stays fresh and re-triggerable. An eager factory failure is logged and the entry is left untouched — it is still fresh, so there is no fail-safe restamp; natural expiry and fail-safe (when enabled) take over from there.

Conditional refresh and adaptive options run through one write path shared by the foreground factory, the soft-timeout background completion, and the eager refresh. A `NotModified` result re-stamps the existing last-known-good value as fresh with the context's current options, preserving its validators; the factory's adaptive `Options` replacement is re-validated before the write and an invalid mutation throws after the factory ran with nothing written. The throttle restamp applied when fail-safe activates preserves the stale entry's metadata (`ETag`, `LastModifiedAt`, `Tags`) but drops `EagerRefreshAt`, so a restamped stale reserve cannot trigger an eager refresh on top of the throttle.

When `CacheEntryOptions.UseDistributedFactoryLock` is set and a provider is registered, the coordinator layers a cross-node lock over the local per-key lock: acquire local, acquire distributed with the same wait budget, re-check the shared store (the loser of the cross-node race serves the winner's value), then run the factory. The lease transfers into soft-timeout background completions and eager refreshes so the cross-node guard stays held until the detached write lands, and release is best-effort with the lease TTL as the backstop. A throwing acquire (lock backend down, as opposed to `null` = held elsewhere) degrades through fail-safe: with `IsFailSafeEnabled` and a usable stale reserve the coordinator serves the stale value, restamps the throttle so per-call retries stop hammering the down backend, and logs a warning; without a usable reserve the provider's exception propagates, and caller cancellation always propagates as cancellation. Enabling the option without a registered `ICacheFactoryLockProvider` throws `InvalidOperationException` naming the adapter package.

The coordinator deliberately diverges from FusionCache on background cancellation. A soft-timed-out factory uses a detached internal token and can outlive the caller request. Hard timeouts cancel or abandon the factory and never allow background completion. The per-key no-duplicate-factory guarantee holds cleanly for cooperative factories; after the background ceiling abandons a token-ignoring factory, another factory may run for that key while the abandoned task continues untracked, but late timeout-path writes are gated off.

### Installation

```bash
dotnet add package Headless.Caching.Core
```

### Quick Start

`AddHeadlessCaching` is the registration entry point this package owns; the `Use*`/`Add*Tier` extensions come from the provider packages:

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

Beyond the entry point, consumers do not use this package directly. Provider packages reference it to implement `GetOrAddAsync` and the options-based `UpsertEntryAsync`.

### Configuration

None.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Extensions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

- `AddHeadlessCaching` validates the setup gates, then applies provider contributions in tier → default → named → cross-cutting order; a failed setup registers nothing.
- Registers `ICacheProvider` as singleton (`TryAdd`) and a registration sentinel that makes a second `AddHeadlessCaching` call throw.
- Providers own coordinator construction; no other registrations.

---

## Headless.Caching.DistributedLocks

Adapter that bridges the caching factory-lock seam (`ICacheFactoryLockProvider`) onto `IDistributedLock`, enabling opt-in multi-node cache stampede protection for entries that set `CacheEntryOptions.UseDistributedFactoryLock`.

### Problem Solved

The per-key factory lock in `Headless.Caching.Core` is process-local: with N app instances sharing one Redis cache, a popular key expiring can still run N concurrent factories — one per node. This package makes the factory single-flight across nodes: the node that wins a distributed lock runs the factory, the others wait on the lock and re-check the shared store, so the losers serve the winner's freshly written value instead of duplicating the work.

### Key Features

- `setup.UseDistributedFactoryLock()` — a cross-cutting extension on the `AddHeadlessCaching` setup builder — registers `ICacheFactoryLockProvider` backed by the application's `IDistributedLock` registration (any `Headless.DistributedLocks.*` provider). Three overload shapes: parameterless, `Action<CacheFactoryLockOptions>`, and `Action<CacheFactoryLockOptions, IServiceProvider>`.
- Per-entry opt-in through `CacheEntryOptions.UseDistributedFactoryLock`; entries that do not set it pay zero cost.
- Lock resources are namespaced with a configurable prefix (default `cache:factory:`) so cache locks never collide with other lock consumers on the same backend.
- The seam timeout maps directly onto `DistributedLockAcquireOptions.AcquireTimeout`: `TimeSpan.Zero` is a single try-once attempt (used by eager refresh), `Timeout.InfiniteTimeSpan` waits unboundedly, and a finite value bounds the wait.
- Optional lease TTL override (`TimeUntilExpires`) as the backstop that frees the key when a node dies mid-factory; must be a finite positive value when set — the validator rejects zero, negative, or `Timeout.InfiniteTimeSpan` at startup.

### Design Notes

- The cross-node lock is a second layer, not a replacement. The coordinator always acquires the local per-key lock first, then the distributed lock, with the same wait budget the local lock used (the soft timeout when a fail-safe stale reserve can absorb the elapse, `LockTimeout` otherwise). Degradation on elapse therefore mirrors the local lock-timeout path exactly: serve stale when a reserve exists, degrade to a miss otherwise.
- After acquiring the distributed lock the coordinator re-checks the shared store before running the factory. The previous owner on another node may have just written a fresh value; the loser of the cross-node race serves the winner's value instead of refreshing again.
- The lease transfers through detached paths. On a soft timeout the lease moves into the background completion together with the local lock releaser, and the eager-refresh path holds it until the detached factory lands — the cross-node guard stays held until the write happens, not just until the caller returns.
- Eager refresh uses a single non-blocking attempt (`TimeSpan.Zero`). When the lock is held elsewhere another node is already refreshing, so the local node skips silently and leaves the still-fresh entry untouched.
- Lock release is best-effort: a failed release is logged and the lease TTL is the backstop, so a release failure never masks the cache operation's outcome. Keep `TimeUntilExpires` (or the lock provider's default lease) comfortably above the slowest expected factory run.
- A throwing acquire (lock backend down, as opposed to `null` = held elsewhere) degrades through fail-safe: with `IsFailSafeEnabled` and a usable stale reserve the coordinator serves the stale value, restamps the throttle so per-call retries stop hammering the down backend, and logs a warning. Without a usable reserve the exception propagates; caller cancellation always propagates as cancellation. The eager-refresh path's non-blocking acquire is best-effort instead — a throwing acquire there is logged and the still-fresh entry stays untouched.
- Use it when the factory is expensive enough (slow query, paid API call) to outweigh a distributed lock round-trip per cold refresh. For cheap factories, per-node single-flight is already enough — N small duplicated calls are cheaper than N lock round-trips on every miss.
- `TimeUntilExpires`, when set, must be finite and positive. A lease TTL must be able to expire so a crashed holder cannot permanently block all nodes from refreshing the cache entry.

### Installation

```bash
dotnet add package Headless.Caching.DistributedLocks
```

### Quick Start

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

Enabling `CacheEntryOptions.UseDistributedFactoryLock` without calling `setup.UseDistributedFactoryLock()` (or registering another `ICacheFactoryLockProvider`) fails the factory-backed read with `InvalidOperationException` instead of silently degrading to single-node behavior.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `ResourcePrefix` | `"cache:factory:"` | Prefix prepended to the cache key to form the distributed lock resource name; override to namespace cache locks away from other lock consumers sharing the backend. |
| `TimeUntilExpires` | `null` | Lease TTL applied to each acquired factory lock; `null` uses the distributed lock provider's default lease duration. The TTL frees the key when a node dies mid-factory. When set, must be a finite positive value — zero, negative, or `Timeout.InfiniteTimeSpan` are rejected at startup because an infinite or zero-duration lease cannot release a crashed holder. |

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

### Dependencies

- `Headless.Caching.Core`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Hosting`

### Side Effects

- Registers `ICacheFactoryLockProvider` as singleton (`TryAdd`, so an existing registration wins).
- Registers `CacheFactoryLockOptions` as a singleton option value.
- Requires an `IDistributedLock` registration at resolution time; none is added by this package.

---

## Headless.Caching.Hybrid

Two-tier cache combining in-memory L1 with remote L2 and cross-instance invalidation through messaging.

### Problem Solved

Provides one `ICache` implementation that reads from a fast local cache first, falls back to a shared remote cache, and invalidates other instances when writes change cached data.

### Key Features

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
- Implements `IBufferCache` — an L1 hit slices straight into the caller's `IBufferWriter<byte>` (single copy on the hot path); an L1 miss falls through to the same wrapped L2 read the generic path uses and seeds L1 (two copies on the cold path, inherent to populating both tiers). Raw upsert stamps both tiers plus the backplane identically to `UpsertEntryAsync`. See [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path).
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

### Design Notes

A default hybrid composes role-keyed tiers registered in the same `AddHeadlessCaching` setup: `setup.AddMemoryTier()` registers the L1 (`IInMemoryCache` plus the `CacheConstants.MemoryCacheProvider` role key) and `setup.AddRedisTier(...)` the L2 (`IRemoteCache` plus the `CacheConstants.RemoteCacheProvider` role key) without touching the default unkeyed `ICache`; `setup.UseHybrid()` then becomes the default `ICache`. Prefer the tier recipe for the common one-hybrid host — no instance names to invent, and the role keys stay reachable through `ICacheProvider`; set `LocalCacheName`/`RemoteCacheName` to bind `AddNamed` instances instead when a tier needs an identity of its own (for example a second hybrid, or tiers shared with other named consumers). Incoming invalidations are handled by `HybridCacheInvalidationConsumer` (`IConsume<CacheInvalidationMessage>`), which `UseHybrid` auto-registers when a messaging bus (`IBus`) is already present in the container — so cross-node L1 invalidation is correct by default rather than a silent opt-in. A single consumer serves every hybrid (default and named): it resolves the default hybrid by the `CacheConstants.HybridCacheProvider` role key and named hybrids by `CacheInvalidationMessage.CacheName` through `ICacheProvider`, so a named hybrid receives only the invalidations published for its own cache name. Auto-registration is idempotent — an explicit `ForMessage<CacheInvalidationMessage>(msg => msg.OnBus<HybridCacheInvalidationConsumer>())` (or assembly scanning) still works and is merged rather than double-registered, and an explicit registration always wins. The gate reads the container at caching-setup time, so register messaging before caching (as in the quick start). If no bus is present then — for example a single-node host with no backplane — no consumer is wired, so it pays for no idle subscription; and if messaging is added *after* caching, the startup best-practices advisor warns (Check 5) so the missing consumer is loud rather than a silent one-way backplane.

Hybrid fail-safe and factory timeouts use the same coordinator semantics as the other providers. A stale reserve can come from L1 or L2. On factory soft timeout, the stale value is returned to the caller and the detached background factory writes through the composite store on success, so both tiers are refreshed. Eager refresh and conditional (`NotModified`) refresh likewise run through the composite store, so a refresh extends or replaces the entry in both tiers. `DefaultLocalExpiration` still caps L1 physical retention independently of the L2 duration.

Hybrid also has L2 read-level resilience. `DistributedCacheSoftTimeout` bounds L2 reads that can degrade to a local stale reserve or a miss; in the factory-store path, a timed-out L2 read with an L1 stale reserve serves that reserve immediately and skips the origin factory. `DistributedCacheHardTimeout` bounds L2 reads when no local reserve exists. `DistributedCacheCircuitBreakerDuration` temporarily skips L2 operations after a non-cancellation L2 failure so an unhealthy distributed tier gets relief; reads degrade to L1 or miss, and additive writes can update L1 without waiting on L2 while the circuit is open.

Factory value-writes publish the same key invalidation as explicit upserts: cold-miss fresh writes, soft-timeout background completion writes, eager-refresh writes, and conditional `Modified` writes all broadcast, so peers drop their stale L1 copy instead of serving the old value until its local TTL. Metadata-only restamps do not publish — conditional `NotModified` extensions, fail-safe throttle restamps, and the eager-refresh gate write leave peers' cached bytes identical, so invalidating them would only force pointless L2 re-reads. Publish failures on this path follow the upsert semantics: they never fail the caller, are logged, and with `EnableAutoRecovery` the single-key publish is queued and replayed.

`RemoveByTagAsync` bumps its own L1 tag marker first and unconditionally (in-process and infallible, so the local invalidation always lands even when L2 is unreachable), then publishes the `Tag` invalidation, then bumps the L2 tag marker best-effort under the distributed-cache circuit breaker — an L2 failure trips the circuit and is logged rather than abandoning the local and peer invalidation. With `EnableAutoRecovery`, a skipped (circuit-open) or failed L2 marker bump is queued and replayed on recovery — re-asserting the **original** timestamp (raise-only durable write, so an entry written after the invalidation is not resurrected) and re-broadcasting — so the shared-store marker converges once L2 returns. Without auto-recovery the bump is not replayed, and cross-instance staleness for a node relying solely on the shared marker (a late joiner, or a node once its process-local marker cache expires) is bounded only by each entry's physical TTL (L1 and peers, via the broadcast, are unaffected either way). A single origin timestamp flows through the L1 marker, the broadcast message, and the L2 marker, so every node version-pins the invalidation against the same instant. It returns `ValueTask` (no count — Family-2 invalidation deletes nothing). Receivers seed their L1 tag marker from the notification's origin timestamp (raise-only, via `ISeedableTagMarkerCache`) rather than stamping their own clock, closing the cross-node clock-skew window in which a lagging receiver could record a marker older than a freshly-born entry and miss the invalidation; the previous recovery-aware per-key tag walk is gone — a pending recovery write that landed after the invalidation carries a newer `CreatedAt` and is naturally not invalidated by the older marker. `ClearAsync` follows the same shape with a `Clear` message: bump L1 first, publish, then best-effort L2 clear-generation marker under the circuit breaker; receivers seed their L1 clear marker from the origin timestamp (reserves preserved), distinct from a `FlushAll` physical wipe. Receivers also seed their L2 provider's process-local marker cache from the notification's timestamp (via `ISeedableTagMarkerCache`, which both `RedisCache` and `InMemoryCache` implement), so both the L1 and L2 marker views update immediately — no L2 round-trip and no `TagMarkerRefreshWindow` wait. The window then bounds cross-instance L2 visibility only for no-backplane deployments and for recovery after a missed backplane message; the physical-TTL backstop for a lost marker is unchanged.

`ExpireAsync` carries the logical-expire-keeps-reserve contract across the backplane. It expires L2, expires its own L1, and — only when the key existed — publishes a `CacheInvalidationMessage` with `Expire = true`, so receivers run `LocalCache.ExpireAsync` (logical expiry, reserve preserved) rather than the plain remove a `Key` message would trigger. The local instance's reserve is preserved too, so a `GetOrAddAsync` on either the originating node or a peer can still serve stale through fail-safe after the expiration. Under `EnableAutoRecovery`, a failing L2 expiration follows the same degraded path as `RemoveAsync`: L1 is expired, the L2 expiration is queued for replay, and the call conservatively reports `true` and publishes the `Expire` invalidation because the L2 state is unknown. The `Expire` flag is meaningful only with `Key`/`Keys` set; it is ignored for `Prefix`, `Tag`, and `FlushAll` messages.

For factory-backed sliding entries, `DefaultLocalExpiration` caps the L1 copy only. Hybrid revalidates sliding L1 hits against L2 before re-arm so L2 keeps the original `Duration` as the absolute cap. If L2 is unavailable, a fresh L1 sliding value can still be returned, but the read is not re-armed.

On reads, Hybrid promotes L2 entries into L1 only when they are logically fresh. Promoting stale reserves on every read would amplify L1 writes and could overwrite a newer L1 reserve. Fail-safe activation and background success still write through the composite store intentionally.

Publish failures are non-fatal. Other instances may keep their L1 value until TTL or the next successful invalidation, while the local instance still observes the write result.

### Installation

```bash
dotnet add package Headless.Caching.Hybrid
```

### Quick Start

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

### Configuration

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
| `ReThrowDistributedCacheExceptions` | `false` | Re-throw (instead of degrade) a non-cancellation L2 read or factory-write failure. Direct reads and the factory/`UpsertEntryAsync` store-write surface it; timeouts/open-circuit still degrade, sliding re-arm stays best-effort, and a `GetOrAddAsync` store-read fault still falls through to the factory. |
| `ReThrowBackplaneExceptions` | `false` | Re-throw a non-cancellation backplane publish failure after logging and any recovery queueing. Surfaces on synchronous write paths; observed-and-logged on detached background publishes. |

Auto-recovery (design reference: FusionCache's auto-recovery, adapted) keeps one pending operation per key with kind-aware coalescing: a newer value operation (set/remove) replaces any queued item, a publish refreshes a queued publish, but a publish never displaces a queued value operation — the value operation subsumes it, because a successful set/remove replay republishes the key invalidation itself, stamped with the original write time so receivers order it correctly against newer writes. If that post-replay publish fails, a residual publish is queued in its place (the value already landed in L2) and inherits the normal retry cap, so the failure path cannot loop. Any successful L2 write for a key clears its pending item, and a queued set is only replayed while the L1 entry still carries the exact stamp the write produced (L1 is the source of truth; otherwise the item is dropped as obsolete). Incoming invalidations from other instances drop older queued items so a replay cannot resurrect stale data, and a single-key invalidation older than a surviving pending item is ignored instead of wiping the newer local L1 state — together these make concurrent-writer divergence under an outage converge on the last writer's value once every node has replayed (a message without a timestamp is treated as newer — conservative drop; tag invalidations are not conflict-matched because queued items are not indexed by tag). With auto-recovery enabled, a failing single-key L2 write no longer propagates to the caller: the call succeeds against L1 in degraded mode (logged as a warning), so callers must tolerate L2 lagging L1 until replay. Items without a natural expiry (removes, publishes) are retained for `AutoRecoveryDelay × AutoRecoveryMaxRetries`; replay passes run oldest-first and stop at the first failure, arming the back-off barrier so a sustained outage does not become a retry storm. Family-2 tag/clear/flush marker bumps are also captured (stored under synthetic keys — per-tag bumps coalesce, clear/remove are singletons); their replay re-asserts the marker at its original timestamp via a raise-only durable write and re-broadcasts, and because raise-only markers are idempotent they are exempt from the incoming-invalidation conflict drop. Bulk, atomic (increment/set-if), and set operations are never captured.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Bus.Abstractions`

### Side Effects

- `setup.UseHybrid(...)` (default) registers `HybridCache` as singleton, the default `ICache` over it, a keyed `ICache` under `CacheConstants.HybridCacheProvider`, and `ICache<T>`.
- Registers `ICacheProvider` (shared, `TryAdd`).
- `setup.AddNamed(name, i => i.UseHybrid(...))` registers a keyed `ICache` under the instance name with its own options and tier resolution.
- Reads configured `HybridCacheOptions`.
- Publishes cache invalidation messages through the registered message bus.
- Runs a `TimeProvider`-driven recovery timer when `EnableAutoRecovery` is set.

---

## Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

### Problem Solved

Provides process-local caching through the unified `ICache` abstraction, suitable for development, single-instance deployments, or an L1 cache layer.

### Key Features

- Full `IInMemoryCache` implementation.
- Can serve as the default `ICache` (`setup.UseInMemory(...)`) or as the memory tier of a default hybrid (`setup.AddMemoryTier(...)`).
- Supports strongly typed `ICache<T>`.
- Named cache instances via `setup.AddNamed(name, i => i.UseInMemory(...))`, resolvable as keyed `ICache` services or through `ICacheProvider`.
- Automatic memory management with configurable limits (`MaxItems` plus LRU eviction).
- O(1) logical tag invalidation and `ClearAsync` through per-tag and clear-generation timestamp markers (Family-2), compared against each entry's birth time on read.
- Implements `IBufferCache` — stores framed bytes, slices to the caller's `IBufferWriter<byte>` on read, copies the `ReadOnlySequence<byte>` on write, with the same stamping as the generic path. See [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path).
- Optional value cloning for isolation.
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

### Design Notes

Memory cache stores entries in an internal envelope with logical expiration, physical expiration, and optional sliding expiration. Direct writes set logical and physical timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Sliding `GetOrAddAsync` keeps physical expiration as the absolute cap and re-arms logical expiration on value reads only; `GetExpirationAsync`, `ExistsAsync`, key listing, and count operations do not extend the idle window. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`. `ExpireAsync` only pulls logical expiration to now when the entry carries a genuine fail-safe reserve (physical outliving logical on a non-sliding entry); a sliding entry's `physical > logical` surplus is its absolute cap, not a reserve, so `ExpireAsync` hard-removes it instead of preserving it.

Tag and clear invalidation are Family-2 logical: `RemoveByTagAsync` stamps a per-tag marker (`tag -> DateTime`) to now in O(1) and `ClearAsync` bumps a global clear-generation marker; neither enumerates entries. On read, an entry is invalidated when its birth time (`CreatedAt`) predates the newest marker it is subject to (the max of the clear marker and every per-tag marker it carries) — direct reads miss, the factory coordinator demotes it to a fail-safe reserve. Markers are pruned by the background maintenance sweep once every entry a marker could invalidate is guaranteed physically gone — its invalidation instant is older than `now - maxObservedEntryLifetime` (the largest physical lifetime of any tagged entry ever written) — so the store stays bounded without ever resurrecting still-live pre-marker data (a re-created entry with a newer `CreatedAt` is naturally not invalidated by an older marker anyway); a tagged entry with no physical expiry disables pruning, since it could outlive any bound. `FlushAsync` resets the markers along with the keyspace (no entry survives to be invalidated); `ClearAsync` keeps them. Because invalidation is logical, `GetCountAsync` and key listing still see logically-invalidated entries until physical eviction.

Long `FailSafeMaxDuration` values and long sliding absolute caps can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments. Soft-timeout and eager background refreshes also hold values in process while the detached factory runs; `BackgroundFactoryCeiling` (infinite by default) optionally bounds how long a cooperative refresh keeps the per-key lock when set to a finite value.

`Memory` in Headless caching docs means this package, `Headless.Caching.InMemory`, not `Microsoft.Extensions.Caching.Memory`.

### Installation

```bash
dotnet add package Headless.Caching.InMemory
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Pick one shape — AddHeadlessCaching may be called only once per service collection.

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());

builder.Services.AddHeadlessCaching(setup =>
    setup.UseInMemory(options =>
    {
        options.MaxItems = 10000;
        options.CloneValues = true;
        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
    })
);

// As the memory tier of a default hybrid instead of the default ICache (see Headless.Caching.Hybrid):
builder.Services.AddHeadlessCaching(setup =>
{
    setup.AddMemoryTier();
    setup.AddRedisTier(options => options.ConnectionMultiplexer = redis); // redis: ConnectionMultiplexer.Connect(...)
    setup.UseHybrid();
});
```

Named instances (independent options, resolved by name; the setup still needs exactly one default `Use*`):

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

Names must be non-empty and must not be reserved: the `CacheConstants` role keys (`Headless.Caching:{Memory,Remote,Hybrid}`) and any name under the `Headless.Caching:` namespace are rejected with `ArgumentException`, and duplicate names throw. Each named instance must select exactly one provider. Named instances never touch the default (unkeyed) `ICache`.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` extension overloads; when `null` those overloads throw. |
| `MaxItems` | `10000` | Maximum number of items before LRU eviction. |
| `CloneValues` | `false` | Clone values on get/set so cached entries are isolated from caller mutations. |
| `MaxMemorySize` | `null` | Maximum total memory in bytes; requires `SizeCalculator`. |
| `SizeCalculator` | `null` | Function computing the byte size of cached objects; required for `MaxMemorySize`/`MaxEntrySize`. |
| `MaxEntrySize` | `null` | Maximum size of a single entry in bytes; requires `SizeCalculator`. |
| `ShouldThrowOnMaxEntrySizeExceeded` | `false` | Throw when an entry exceeds `MaxEntrySize` instead of logging and skipping. |
| `ShouldThrowOnSerializationError` | `true` | Throw on serialization errors during cloning. |
| `MaintenanceInterval` | `250 ms` | Interval between background maintenance runs. |
| `MaxEvictionsPerCompaction` | `10` | Maximum items evicted per compaction cycle. |
| `EvictionSampleSize` | `5` | Entries sampled when finding eviction candidates. |

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`

### Side Effects

- Registers `IInMemoryCache` as singleton (`setup.UseInMemory(...)` and `setup.AddMemoryTier(...)`).
- Registers `ICache` as singleton when used as the default provider (`setup.UseInMemory(...)`).
- Registers a keyed `ICache` under the `CacheConstants.MemoryCacheProvider` role key (`Headless.Caching:Memory`).
- Registers `ICache<T>` as singleton when used as the default provider.
- Registers `ICacheProvider` (shared, `TryAdd`).
- `setup.AddNamed(name, i => i.UseInMemory(...))` registers a keyed `ICache` under the instance name with its own options.

---

## Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

### Problem Solved

Provides Redis-backed caching through the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

### Key Features

- Full `IRemoteCache` implementation using StackExchange.Redis.
- Can serve as the default `ICache` (`setup.UseRedis(...)`) or as the remote tier of a default hybrid (`setup.AddRedisTier(...)`).
- `GetWithExpirationAsync<T>` returns the cached value and its remaining TTL in one round-trip; used internally by `Headless.Caching.Hybrid` to avoid a double L2 read.
- Supports strongly typed `ICache<T>` (the single typed facade; `IRemoteCache<T>` is not registered).
- Named cache instances via `setup.AddNamed(name, i => i.UseRedis(...))`, each owning its own scripts loader bound to its own multiplexer.
- `HeadlessCacheInstanceBuilder.WithSerializer(...)` - per-named-Redis-instance value-codec selection (instance, factory, and generic `<TSerializer>()` overloads); Redis resolves the keyed serializer by cache name and falls back to the global `ISerializer`. Serialization is a Redis-tier concern, so this lives in the Redis package; InMemory stores object references and never serializes, so it is not offered there. On a hybrid instance it governs L2 (Redis) only.
- Prefix-based key management.
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower).
- Set/list operations with pagination.
- Lua scripts for atomic multi-key operations.
- O(1) logical tag invalidation and `ClearAsync` through timestamp markers (Family-2), compared against each entry's birth time on read — one marker key per tag, so tagging works on Redis Cluster.
- Redis Cluster support for all operations, including tagging and clear.
- Implements `IBufferCache` — `TryGetToAsync` writes the decoded value slice into the caller's `IBufferWriter<byte>` and `UpsertRawAsync` splices a `ReadOnlySequence<byte>` payload into the frame buffer, both reusing the same envelope stamping so expiry/tags/sliding/`CreatedAt` match the generic path; the frame is byte-identical and the read exposes the payload as a slice of the received buffer (one copy). See [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path).
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

### Design Notes

Scalar write operations (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `UpsertAllAsync`, `UpsertEntryAsync`) store entries as a versioned binary envelope: a 27-byte fixed header, optional variable sections, then the raw value segment produced by the cache value codec. Physical expiration is mapped to the Redis key TTL; when fail-safe is enabled, Redis retains the key until physical expiration even after logical expiration has passed. Sliding expiration maps the key TTL to the idle deadline and keeps physical expiration in the envelope as the absolute cap. Logical expiration rides in the payload so normal value reads can miss while `GetOrAddAsync` still has a fail-safe reserve. `ExpireAsync` rewrites the payload's logical stamp to now while keeping the key TTL (the physical reserve) when the entry carries a genuine fail-safe reserve on a non-sliding entry; a sliding entry's surplus TTL is its absolute cap, not a reserve, so `ExpireAsync` deletes the key instead of preserving it. A raw/legacy non-framed key carries no logical metadata, so it has no reserve and is likewise deleted. Atomic counters (`Increment`, `SetIfHigher`, `SetIfLower`) bypass framing and write raw Redis-native numeric strings (see below).

The envelope byte layout (version `0x03`) is:

| Offset | Field | Description |
| --- | --- | --- |
| 0 | Magic | `0xFF` — marks a framed entry |
| 1 | Version | `0x03` — current envelope version; any other version (including the retired `0x01`/`0x02`) reads as unframed legacy bytes, i.e. a framed-semantics miss |
| 2 | Flags | bit0 = `isNull`, bit1 = `hasLogicalExpiresAt`, bit2 = `hasPhysicalExpiresAt`, bit3 = `hasSlidingExpiration`, bit4 = `hasEagerRefreshAt`, bit5 = `hasETag`, bit6 = `hasLastModifiedAt`, bit7 = `hasTags` |
| 3–10 | LogicalExpiresAt | `Int64` little-endian Unix milliseconds; bytes always reserved, meaningful only when bit1 is set |
| 11–18 | PhysicalExpiresAt | `Int64` little-endian Unix milliseconds; bytes always reserved, meaningful only when bit2 is set |
| 19–26 | CreatedAt | `Int64` little-endian Unix milliseconds — the entry's birth time, an always-present v3 fixed field (the flags byte is full at 8 bits, so presence is implied by the version rather than a flag bit). `long.MinValue` is the sentinel meaning "no birth time known"; the Family-2 read-time predicate compares this against tag/clear markers |
| 27+ | Optional sections | In order, each present only when its flag is set: `SlidingExpiration` (`Int64` little-endian milliseconds, bit3), `EagerRefreshAt` (`Int64` little-endian Unix milliseconds, bit4), `LastModifiedAt` (`Int64` little-endian Unix milliseconds, bit6), `ETag` (`UInt16` little-endian byte length + UTF-8 bytes, bit5), `Tags` (`UInt16` little-endian count, then per tag a `UInt16` little-endian byte length + UTF-8 bytes, bit7) |
| rest | ValueSegment | raw codec bytes after the last present section; empty when `isNull` is set |

Note the section order is positional (sliding, eager, last-modified, etag, tags), which differs from the flag bit order. The decoder is defensive: truncated sections, out-of-range timestamps, and non-positive sliding windows all read as unframed legacy bytes rather than throwing, so corrupt or foreign data degrades to a miss.

**Rolling-upgrade contract:** a key written by an older node (version `0x01`/`0x02` or unframed raw bytes) or by a future node carrying a version byte other than `0x03` is decoded as `Unframed` — a cache miss, not an error. The reading node re-populates the entry using the `GetOrAddAsync` factory, so mixed-version deployments self-heal without any explicit migration step. No special deployment ordering is required. A v2 entry has no `CreatedAt`, which is one reason it reads as a miss rather than being re-interpreted.

Tagging is Family-2 logical invalidation: `RemoveByTagAsync(tag)` writes one timestamp marker at `{KeyPrefix}\0__tag:{tag}` (Unix-ms) and `ClearAsync` writes the reserved clear-generation marker at `{KeyPrefix}\0__clear`. Both are O(1) `StringSet` writes that enumerate nothing, and because there is one marker key per tag (plus one clear key) rather than a multi-slot reverse index, tagging and clear work on Redis Cluster. On read, the cache resolves the newest marker applicable to an entry — the max of the clear marker and every per-tag marker the entry's frame carries — and compares it against the frame's `CreatedAt`; an older entry is a miss for direct reads and a fail-safe reserve under the factory coordinator. Tags ride in the entry frame, not a separate index; tagged writes that derive from an existing physical entry use the `CacheTaggedSetScriptDefinition` compare-and-set Lua script (verify the expected `ConcurrencyStamp`, then `SET` with TTL), while plain writes use a direct `SET`. The reserved namespace is prefixed with a NUL byte (U+0000) that ordinary cache keys never contain, so consumer keys cannot collide with the markers; do not embed a NUL byte in your own cache keys.

To avoid a Redis round-trip on every read, each instance keeps a process-local marker cache: a resolved tag/clear marker is reused for `TagMarkerRefreshWindow` (default 2s) before the next read that needs it refreshes it via a single pipelined `MGET`. The instance that issues a bump updates its own cached marker immediately, so it self-invalidates on its next read; another instance observes the bump only after its window elapses — the documented Family-2 cross-instance visibility lag. The physical key TTL still backstops staleness if a marker is ever lost.

Null scalar values are represented by a header flag with an empty value segment. The literal string `"@@NULL"` is a normal cacheable string when written through Redis cache APIs. Raw legacy keys containing `"@@NULL"` still read as null. Atomic counters remain raw Redis-native numeric strings so Redis can perform native atomic arithmetic; their read path falls back to the raw value codec.

Factory timeouts are enforced in the shared coordinator before provider writes. A soft-timeout background refresh writes through Redis on success and Redis TTL still follows physical expiration. StackExchange.Redis operation timeouts remain configured on `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`; they are separate from `CacheEntryOptions.FactorySoftTimeout` and `FactoryHardTimeout`.

### Installation

```bash
dotnet add package Headless.Caching.Redis
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddHeadlessCaching(setup =>
    setup.UseRedis(options =>
    {
        options.ConnectionMultiplexer = redis;
        options.KeyPrefix = "myapp:";
    })
);
```

`UseRedis` has no parameterless shape: `ConnectionMultiplexer` is required, and the `IConfiguration`-binding shape still needs the multiplexer supplied through an additional `Configure` call.

Named instances (independent multiplexer, prefix, and scripts loader per name; the setup still needs exactly one default `Use*`):

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

Names must be non-empty and must not be reserved: the `CacheConstants` role keys (`Headless.Caching:{Memory,Remote,Hybrid}`) and any name under the `Headless.Caching:` namespace are rejected with `ArgumentException`, and duplicate names throw. Each named instance must select exactly one provider. Named instances never touch the default (unkeyed) `ICache`.

A named Redis instance can override its value serializer without affecting the default cache (`WithSerializer` and `UseRedis` chain in either order):

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
| `ConnectionMultiplexer` | required | The StackExchange.Redis multiplexer the cache uses; the setup never creates one. |
| `KeyPrefix` | `""` | Prefix for all cache keys (and the NUL-prefixed `\0__tag:` / `\0__clear` marker namespaces). |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` extension overloads; when `null` those overloads throw. |
| `ReadMode` | `CommandFlags.None` | StackExchange.Redis command flags applied to read operations (e.g. `PreferReplica`). |
| `TagMarkerRefreshWindow` | `2 seconds` | How long a Family-2 tag/clear marker fetched from Redis is reused from the process-local marker cache before the next read that needs it re-fetches it (pipelined `MGET`). A larger window cuts marker round-trips at the cost of a longer cross-instance visibility lag for a marker another instance bumped (the physical TTL still backstops staleness); the bumping instance self-invalidates immediately. Must be greater than zero. |

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `Headless.Serializer.Json`
- `StackExchange.Redis`

### Side Effects

- Registers `IRemoteCache` as singleton (`setup.UseRedis(...)` and `setup.AddRedisTier(...)`).
- Registers `ICache` as singleton when used as the default provider (`setup.UseRedis(...)`).
- Registers a keyed `ICache` under the `CacheConstants.RemoteCacheProvider` role key (`Headless.Caching:Remote`).
- Registers `ICache<T>` as singleton when used as the default provider.
- Registers `ICacheProvider` (shared, `TryAdd`).
- Registers a keyed `HeadlessRedisScriptsLoader` bound to `RedisCacheOptions.ConnectionMultiplexer`, plus a hosted `IInitializer` that warms the cache Lua scripts on host start.
- `setup.AddNamed(name, i => i.UseRedis(...))` registers a keyed `ICache` under the instance name with a per-instance scripts loader and initializer bound to that instance's multiplexer.
- Named Redis instances use the keyed `ISerializer` configured by `HeadlessCacheInstanceBuilder.WithSerializer(...)` when present; otherwise they use the global `ISerializer`.
- `RemoveByTagAsync`/`ClearAsync` write timestamp marker keys in the reserved `{KeyPrefix}\0__tag:{tag}` / `{KeyPrefix}\0__clear` namespaces (one `StringSet` per call; no key enumeration).

---

## Headless.Caching.Bcl

Adapter that exposes a named Headless cache as `Microsoft.Extensions.Caching.Distributed.IDistributedCache`.

### Problem Solved

Provides standard BCL distributed-cache interop for ASP.NET Core Session and third-party libraries that require `IDistributedCache`, while keeping application code on the richer Headless `ICache` API.

### Key Features

- Registers `IDistributedCache` over an internal adapter backed by a named `ICache`; consumers only ever see `IDistributedCache`. The adapter implements the buffer-oriented extension `IBufferDistributedCache`, so callers that take its `TryGet(IBufferWriter<byte>)` / `Set(ReadOnlySequence<byte>)` members stream through the `IBufferCache` fast path without an intermediate `byte[]` (transparent `byte[]` fallback when the backing cache is not byte-oriented). See [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path).
- `setup.UseBclCache(...)` provisions a dedicated named cache and registers it as `IDistributedCache`.
- Maps `DistributedCacheEntryOptions` absolute, relative, and sliding expiration to `CacheEntryOptions`.
- Uses `ICache.RefreshAsync` for `IDistributedCache.Refresh`/`RefreshAsync`, so sliding entries can be re-armed without returning their value.
- Stores `byte[]` payloads in the Redis value segment unchanged rather than JSON/base64 encoded, because `byte[]` is the cache's native wire format (stored verbatim, never through a serializer); no serializer is wired.
- Supports ASP.NET Core Session round-trips when backed by a Redis named cache.

### Design Notes

This package is an interop adapter, not a general application-cache abstraction. Prefer injecting `ICache` for code you own; use `IDistributedCache` only where a framework or third-party component demands the BCL contract.

The adapter targets a dedicated named cache so BCL `byte[]` payloads stay isolated in their own namespace. `byte[]` is the cache's native wire format, stored verbatim (never through a serializer), so the adapter's `byte[]` writes land as raw bytes under any serializer — no serializer is wired, and the `configureCache` callback selects only the backing provider. The named cache still uses the normal Headless provider pipeline, so Redis entries retain the Headless frame header for logical expiration, physical expiration, sliding metadata, tags, and rolling-upgrade behavior; only the value segment is raw bytes.

`DistributedCacheEntryOptions` maps to `CacheEntryOptions.Duration`, so sliding-only or option-less BCL writes use `HeadlessDistributedCacheAdapterOptions.DefaultAbsoluteExpiration` as the absolute cap. The default cap is one day. A `Set` whose absolute expiration is already in the past yields a non-positive duration, which the engine treats as "expire immediately" (an immediate eviction across every provider), matching `Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache` rather than throwing.

The sync BCL methods block on the async implementation with `GetAwaiter().GetResult()`, matching the Microsoft Redis adapter. Prefer the async BCL methods in ASP.NET Core code paths.

### Installation

```bash
dotnet add package Headless.Caching.Bcl
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.UseBclCache(
        options =>
        {
            options.CacheName = "aspnet-session";
            options.DefaultAbsoluteExpiration = TimeSpan.FromHours(8);
        },
        instance =>
            instance.UseRedis(options =>
            {
                options.ConnectionMultiplexer = redis;
                options.KeyPrefix = "session:";
            })
    );
});

builder.Services.AddSession(options => options.IdleTimeout = TimeSpan.FromMinutes(20));
```

Consumers that need the standard contract can inject `IDistributedCache`; application services should still inject `ICache`.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `CacheName` | `"bcl-distributed-cache"` | Named cache instance used by the adapter. Must be non-empty and must not be a reserved Headless cache provider key or under the reserved `Headless.Caching:` namespace. |
| `DefaultAbsoluteExpiration` | `1 day` | Absolute lifetime cap used when BCL callers provide only sliding expiration or no expiration options. Must be greater than zero. |

The `configureCache` callback passed to `UseBclCache(...)` selects only the backing provider for the named cache — exactly one, usually `instance.UseRedis(...)`. `byte[]` is the cache's native wire format, so no serializer configuration is needed — configuring one is rejected at registration.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Microsoft.Extensions.Caching.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

- Adds a named cache instance through `setup.AddNamed(...)`.
- Registers the internal adapter as singleton and `IDistributedCache` as singleton (`TryAdd`).
- Registers `HeadlessDistributedCacheAdapterOptions` with FluentValidation and startup validation.
- Registers `TimeProvider.System` when no `TimeProvider` is already registered.

---

## Headless.Caching.OutputCache

Adapter that backs ASP.NET Core's `IOutputCacheStore` with a named Headless cache, making `services.AddOutputCache()` distributed and tag-aware.

### Problem Solved

ASP.NET Core's output-cache middleware ships only an in-memory store, and ASP.NET's own guidance states that `IDistributedCache` is **not** a valid output-cache store because it lacks the atomic tag operations the middleware needs for `EvictByTagAsync`. This package fills that gap: it backs an `IOutputCacheStore` (and the optional `IOutputCacheBufferStore`) with the Headless cache engine, so output-cache entries become distributed and tag eviction rides the engine's distributed tag index.

### Key Features

- Registers `Microsoft.AspNetCore.OutputCaching.IOutputCacheStore` over a named Headless `ICache`; the same instance also implements the optional `IOutputCacheBufferStore` that the formatter pattern-matches (only `IOutputCacheStore` is registered as a service).
- The `IOutputCacheBufferStore` members stream through the `IBufferCache` fast path (`BufferCacheExtensions`): read slices the entry into the response `PipeWriter` (an `IBufferWriter<byte>`), write frames the response body `ReadOnlySequence<byte>` — no intermediate `byte[]` on the hot path when the backing cache is Redis/InMemory/Hybrid, and a transparent `byte[]` fallback otherwise. See [Zero-intermediate-copy buffer path](#zero-intermediate-copy-buffer-path).
- `setup.UseOutputCache(...)` provisions a dedicated named cache and wires it as the output-cache store.
- `EvictByTagAsync(tag)` delegates to `ICache.RemoveByTagAsync` — O(1) logical (Family-2) tag-marker invalidation. Backed by Redis, the tag marker is a single shared Redis key every instance reads, so eviction is cluster-wide.
- `validFor` maps directly to the entry's `Duration` (a single relative TTL; no sliding/absolute reconciliation); a non-positive `validFor` falls back to `DefaultExpiration`.
- Tags pass straight through to the engine, persisted on the entry via `UpsertEntryAsync`; tag-count/length limits are delegated to the engine's write-time check.
- Uses `services.Replace` for `IOutputCacheStore`, so the Headless store wins regardless of whether `AddOutputCache()` runs before or after `AddHeadlessCaching(...)`.
- Stores the middleware's `byte[]` output-cache entries in the value segment unchanged rather than JSON/base64 encoded, because `byte[]` is the cache's native wire format (stored verbatim, never through a serializer); no serializer is wired.

### Design Notes

This package provides only the **store**. The consumer still calls `services.AddOutputCache()` and declares output-cache policies — vary-by, expiration strategy — and tags via `[OutputCache(Tags = "...")]` on controllers or `.CacheOutput(p => p.Tag("..."))` on minimal APIs. Policy stays ASP.NET's concern; this adapter changes only where entries live and how tag eviction propagates.

It is a separate package from `Headless.Caching.Bcl` because an `IOutputCacheStore` references the ASP.NET shared framework (`Microsoft.AspNetCore.App`). Keeping it out of the framework-agnostic BCL adapter means a non-web consumer of `IDistributedCache` never pulls an ASP.NET dependency.

`EvictByTagAsync` is **logical** eviction, not physical deletion. `RemoveByTagAsync` bumps a per-tag timestamp marker so matching entries read as misses (the marker's timestamp postdates their `CreatedAt`); they are not physically removed until their TTL lapses. This satisfies the ASP.NET output-cache contract and is cluster-safe — one marker key per tag, no key enumeration, works on Redis Cluster. With a Redis-backed store the marker lives in Redis, so a tag evicted on node A becomes a miss on node B on its next read (no L1 to invalidate, no backplane required).

`byte[]` is the cache's native wire format, stored verbatim (never through a serializer), so the middleware's `byte[]` entries land as raw bytes under any serializer — no serializer is wired, and the `configureCache` callback selects **only** the backing provider (for example `instance.UseRedis(...)`). Back the named instance with a single remote provider (Redis) for distributed output caching.

Distribution is a function of the backing provider the consumer composes, not the adapter. With an InMemory-only backing cache (which stores object references and never serializes) eviction is single-node only. Back the named instance with Redis (`instance.UseRedis(...)`) for distributed, cluster-wide output caching: the value blobs and the tag markers both live in Redis, shared by every instance.

### Installation

```bash
dotnet add package Headless.Caching.OutputCache
```

### Quick Start

Redis-backed store — distributed and tag-aware across instances:

```csharp
var builder = WebApplication.CreateBuilder(args);
var mux = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddOutputCache(); // ASP.NET; still declare [OutputCache(Tags = ...)] / .CacheOutput(p => p.Tag(...))
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = mux);
    setup.UseOutputCache(
        options => options.CacheName = "output-cache",
        instance =>
            instance.UseRedis(options =>
            {
                options.ConnectionMultiplexer = mux;
                options.KeyPrefix = "output-cache:";
            })
    );
});
```

Declare tags where output caching is applied, then evict them through the standard ASP.NET API:

```csharp
app.MapGet("/products", GetProducts).CacheOutput(p => p.Tag("products"));

// elsewhere — IOutputCacheStore.EvictByTagAsync delegates to the Headless engine
await outputCacheStore.EvictByTagAsync("products", cancellationToken);
```

Because the Redis-backed store keeps both the value blobs and the tag markers in Redis, eviction is already cluster-wide: a tag evicted through any instance's `IOutputCacheStore` becomes a miss on every instance's next read of a matching entry — no backplane or extra wiring required. Back the named instance with a single remote provider (`instance.UseRedis(...)`); the application's own default cache can still be Hybrid independently.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `CacheName` | `"output-cache"` | Named cache instance used by the store. Must be non-empty and must not be a reserved Headless cache provider key. Output-cache entries live in their own namespace, isolated from the default cache. |
| `DefaultExpiration` | `1 minute` | Duration applied only when ASP.NET hands the store a non-positive `validFor`; a positive `validFor` always passes through unchanged as the entry `Duration`. Must be greater than zero. |

Options are validated through the Hosting FluentValidation pipeline with startup validation. The `configureCache` callback passed to `UseOutputCache(...)` selects only the backing provider for the named cache — exactly one, usually `instance.UseRedis(...)`. `byte[]` is the cache's native wire format, so no serializer configuration is needed.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference)

### Side Effects

- Adds a named cache instance through `setup.AddNamed(...)`.
- Replaces (`services.Replace`) the `IOutputCacheStore` registration with the Headless store as singleton. Only `IOutputCacheStore` is registered; the same instance also implements `IOutputCacheBufferStore`, which the formatter discovers by pattern-matching the resolved store (no separate registration).
- Registers `HeadlessOutputCacheStoreOptions` with FluentValidation and startup validation.
- Registers `TimeProvider.System` when no `TimeProvider` is already registered.
