# Caching Resilience Program — Full Design (FusionCache-informed)

**Date:** 2026-06-10 · **Status:** Draft for review · **Roadmap:** #369 (M1–M4) + two new capabilities
**Reference implementation studied:** FusionCache (local at `~/Dev/oss/FusionCache`) — design reference only, not a port.

## 1. Goal

Extend `Headless.Caching.*` with the full resilience feature set: fail-safe ✅, factory timeouts,
eager refresh, conditional/adaptive refresh, tagging (RemoveByTag), sliding expiration, local +
distributed stampede protection, auto-recovery, and named caches. The result must feel native to
this framework: explicit options, no hidden global behavior, hot path unaffected unless features
are opted into.

## 2. Decisions locked (owner sign-off 2026-06-10)

| Decision | Choice | Rationale |
| --- | --- | --- |
| Tagging storage | **Redis reverse index** (per roadmap #378/#379), not FusionCache's timestamp barriers | We own the Redis provider (Lua infra exists); real deletion keeps `GetByPrefix`/`GetCount`/`GetAllKeys` ghost-free and frees memory immediately. FusionCache chose barriers because it sits on opaque `IDistributedCache` backends — that constraint doesn't bind us. |
| Distributed stampede | **Opt-in seam** (`ICacheFactoryLockProvider` in Caching.Core + adapter package) | Zero hot-path cost when off; no hard dependency edge Caching → DistributedLocks; differentiator FusionCache only half-has. |
| Named caches | **Keyed DI + thin `ICacheProvider`** | Native .NET keyed services + named options; provider type only for runtime-name resolution. |
| Sequencing | **Single PR on the working branch**; merge the #426/#431 branches into it | Owner call 2026-06-10. PRs #426/#431 become superseded once the program PR opens. |
| Tag staleness edge | **FusionCache-faithful semantics** — `RemoveByTag` removes only entries that currently carry the tag (version-pinned membership, §7) | Matches FusionCache's lazy-filter correctness: an entry without the tag is never invalidated by that tag. |
| Tagged direct writes | `UpsertAsync(key, value, CacheEntryOptions)` only in v1 | Smallest surface; bulk/atomic/set ops stay untagged. |
| Auto-recovery scope | **Hybrid only** (L2 re-sync from L1 + backplane re-publish) | Standalone Redis has no L1 freshness source to re-sync from. |

## 3. Current state (post-#373, with #426/#431 in flight)

- **Two-timestamp model** shipped: logical expiration governs reads; physical expiration is the
  fail-safe reserve (`max(Duration, FailSafeMaxDuration)`). Redis envelope v1
  (`RedisCacheEntryFrame`: magic `0xFF`, version `0x01`, flags, 2 × Unix-ms timestamps).
- **`FactoryCacheCoordinator`** (Caching.Core) centralizes GetOrAdd: per-key `KeyedAsyncLock`
  single-flight, fail-safe activation/restamp, and (PR #426) soft/hard factory timeouts, detached
  background completion, `LockTimeout`, `BackgroundFactoryCeiling`. Memory, Redis, and Hybrid all
  implement `IFactoryCacheStore` and share it.
- **PR #431** adds `SlidingExpiration` (idle window, clamped by absolute `Duration`;
  rejected when combined with fail-safe).
- **Hybrid** = L1 (`IInMemoryCache`) + L2 (`IRemoteCache`) + messaging backplane invalidation
  (`CacheInvalidationMessage`: key/keys/prefix/flush, self-filtered by `InstanceId`).

## 4. Entry metadata model — envelope v2 (foundation)

Everything below needs richer per-entry metadata. One version bump covers all of it.

New optional fields on the stored entry (memory `CacheEntry`, Redis frame, `CacheStoreEntry<T>`):

| Field | Type | Used by |
| --- | --- | --- |
| `EagerRefreshAt` | UTC timestamp? | eager refresh |
| `ETag` | string? | conditional refresh |
| `LastModifiedAt` | UTC timestamp? | conditional refresh |
| `Tags` | string[]? | tagging (L1 index rebuild on L2→L1 promotion; Hybrid tag invalidation) |

**Redis frame v2:** version byte `0x02`; flags byte gains `0x08` hasEagerRefresh, `0x10` hasETag,
`0x20` hasLastModified, `0x40` hasTags. Fixed timestamps first (existing layout), then optional
var-length sections in flag order: ETag (u16 len + UTF-8), tags (u8 count, each u16 len + UTF-8).
Greenfield posture: the codec reads **only v2**; v1 frames are treated like unframed legacy bytes
(miss). No dual-version support.

**`IFactoryCacheStore` change:** `SetEntryAsync`'s growing parameter list is replaced by a
descriptor struct:

```csharp
public readonly record struct CacheStoreEntryWrite<T>(
    T? Value, bool IsNull,
    DateTime LogicalExpiresAt, DateTime PhysicalExpiresAt,
    DateTime? EagerRefreshAt, string? ETag, DateTime? LastModifiedAt,
    IReadOnlyCollection<string>? Tags);

ValueTask SetEntryAsync<T>(string key, in CacheStoreEntryWrite<T> entry, CancellationToken ct);
```

`CacheStoreEntry<T>` (read side) gains the same four fields. Pure plumbing PR — no behavior change.

## 5. Eager refresh (#375)

**Option:** `CacheEntryOptions.EagerRefreshThreshold` (`float?`, exclusive range 0–1; validated at
entry creation like `Duration`).

**Stamp at write:** `EagerRefreshAt = now + Duration × threshold`, persisted in the entry (survives
restarts via L2; visible to all Hybrid nodes). Recomputed on every fresh write, fail-safe restamp
clears it (stale reserve must not eager-refresh — matches FusionCache, which nulls
`EagerExpirationTimestamp` on fail-safe activation).

**Trigger (access-driven, no timers):** in the coordinator's fresh-hit path, when
`now >= EagerRefreshAt` and the entry is still logically fresh:

1. `TryAcquire` the per-key local lock with **zero timeout** — if held, someone is already
   refreshing; return the fresh value.
2. If the distributed lock seam is configured for the entry, `TryAcquire` it with zero timeout —
   if held elsewhere, release local, return.
3. Re-stamp `EagerRefreshAt = null` on the stored entry **before** starting the factory (write
   gate: other readers stop seeing the trigger), then run the factory **detached** under the
   coordinator-owned token, reusing the existing background-completion machinery —
   `BackgroundFactoryCeiling` applies unchanged.
4. Success → normal fresh write (new `EagerRefreshAt`). Failure → Warning log only; the entry
   rides to natural expiry where the existing fail-safe path takes over. No early fail-safe
   restamp — the value is still fresh.

**Interactions:**
- `SlidingExpiration` + `EagerRefreshThreshold` → rejected (both re-arm logical lifetime;
  ambiguous refresh point). FusionCache sidesteps this by not having sliding at all.
- Works with fail-safe, soft/hard timeouts (timeouts don't apply to the detached eager run — the
  caller already has a fresh value; the ceiling is the only bound).
- Conditional-refresh factories make eager refresh cheap (revalidate, often `NotModified`).

## 6. Conditional refresh + adaptive caching (#376)

New factory shape (additive overload on `ICache`/`ICache<T>`; the existing simple overload remains
and is internally adapted to it):

```csharp
ValueTask<CacheValue<T>> GetOrAddAsync<T>(
    string key,
    Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
    CacheEntryOptions options,
    CancellationToken cancellationToken = default);

public sealed class CacheFactoryContext<T>   // one instance per factory execution (miss path only)
{
    public string Key { get; }
    public bool HasStaleValue { get; }
    public CacheValue<T> StaleValue { get; }          // last-known-good, possibly logically expired
    public string? ETag { get; }
    public DateTime? LastModifiedAt { get; }
    public CacheEntryOptions Options { get; set; }     // adaptive: replace before returning
    public IReadOnlyCollection<string>? Tags { get; set; }

    public CacheFactoryResult<T> NotModified();        // throws if !HasStaleValue
    public CacheFactoryResult<T> Modified(T? value, string? eTag = null, DateTime? lastModifiedAt = null);
}
```

**Semantics:**
- `NotModified()` → coordinator re-stamps the **existing value**: logical `= now + Duration`,
  physical `= now + max(Duration, FailSafeMaxDuration)`, `EagerRefreshAt` recomputed; ETag/
  LastModified preserved; entry rewritten via `SetEntryAsync` (value already in hand — no
  metadata-only store op needed in v1).
- `Modified(value, etag, lastModified)` → normal fresh write carrying the new validators.
- Factory throws → exact same fail-safe / timeout handling as the simple overload (one shared
  coordinator path).
- **Adaptive:** `ctx.Options` is applied at save time after validation. Duration, fail-safe knobs,
  eager threshold, and tags are honored; the factory timeout family is consumed *before* the
  factory runs, so mutating it inside the factory has no effect — documented, and mirrored by
  FusionCache's `EnsureIsSafeForAdaptiveCaching` constraints.

## 7. Tagging — reverse index (#378/#379/#380)

**API:**
- `CacheEntryOptions.Tags` (`IReadOnlyCollection<string>?`) + `CacheFactoryContext.Tags`.
- New `UpsertAsync(key, value, CacheEntryOptions, ct)` overload so direct writes can carry tags
  (bulk/atomic/set ops stay untagged in v1).
- `ICache.RemoveByTagAsync(string tag, CancellationToken ct = default)`.

**Memory provider:** `tag → key set` in-process index, maintained on write/remove/expire/evict
(the eviction path already runs per-entry cleanup; index removal hooks there). `RemoveByTag` =
snapshot keys → `RemoveAll`.

**Redis provider:** reverse index per tag, stored as a **hash** for version-pinned membership:
`{prefix}t:{tag}` → `HSET tagHash <key> <physicalExpiresAtMs>`.
- Tagged write = Lua: `SET` framed value (+TTL=physical) · `HSET` key→physical-stamp into each tag
  hash · `HDEL` key from *removed* tag hashes (diff vs the entry's previous tags — see below) ·
  `EXPIRE` each tag hash to the member's physical TTL with the `GT` flag (only ever extends).
- `RemoveByTag` = Lua: `HGETALL` tag hash → for each `(key, recordedStamp)`: `GETRANGE key 0 18`
  (header-only read — magic, version, flags, both timestamps; never the value payload), parse the
  physical timestamp, `UNLINK` the key **only when it equals the recorded stamp** → `UNLINK` the
  tag hash. A re-created entry has a different physical stamp, so it is never removed —
  **FusionCache-faithful semantics** (an entry that doesn't carry the tag can't be invalidated by
  it) at O(1) extra bytes per member instead of O(value) reads.
- **Old-tag diff:** the GetOrAdd path already reads the prior entry (envelope v2 carries its tags),
  so the coordinator passes the removed-tags diff to the write. The tagged `UpsertAsync` overload
  does one read-before-write for the same purpose — tagged direct writes are not hot-path.
- Residual stale hash fields (member expired and never re-created) are reaped by the tag hash's
  own TTL and by the `HDEL`-on-mismatch performed during `RemoveByTag`.

**Memory provider verification:** the in-process index holds entry references; `RemoveByTag`
checks the live entry's current `Tags` before removal — same semantics, trivially.

**Hybrid (#380):** `RemoveByTagAsync` → L2 `RemoveByTag` → backplane `CacheInvalidationMessage`
gains a `Tag` field → each node (including originator) removes matching L1 entries through the
memory tag index. Consistency: eventual, same as existing key invalidation.

## 8. Distributed stampede protection — opt-in seam (new issue)

**Seam (Caching.Core):**

```csharp
public interface ICacheFactoryLockProvider
{
    /// <returns>A releaser, or null when the lock is held elsewhere / not acquired in time.</returns>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan timeout, CancellationToken ct);
}
```

**Opt-in:** `CacheEntryOptions.UseDistributedFactoryLock` (bool, default false). Enabled without a
registered provider → `InvalidOperationException` on first use (explicit, no silent no-op — repo
non-goal "hidden global behavior").

**Coordinator flow when enabled:** local lock → distributed `TryAcquireAsync` (timeout follows the
existing selection: soft timeout when a stale reserve exists, else `LockTimeout`) → **re-check the
store** (the lock owner on another node may have already written a fresh value → fresh hit, no
factory) → factory → write → release both. Timeout acquiring the distributed lock degrades exactly
like a local lock timeout: serve stale when available, else miss. Eager refresh uses zero-timeout
`TryAcquireAsync` (§5).

**Adapter package:** `Headless.Caching.DistributedLocks` — bridges
`Headless.DistributedLocks.Abstractions.IDistributedLockProvider` to the seam; registered via
`services.AddCacheDistributedFactoryLock(...)`. Caching packages gain no direct dependency on the
DistributedLocks domain.

## 9. Auto-recovery (#386, scope extended)

Hybrid-scoped, opt-in (`HybridCacheOptions.EnableAutoRecovery`, default false), modeled on
FusionCache's `AutoRecoveryService` but bounded and explicit:

- **Queue:** bounded `ConcurrentDictionary<key, RecoveryItem>` (`AutoRecoveryMaxItems`, evict the
  earliest-expiring on overflow). Items: failed **L2 writes** (re-sync from L1's current value, only
  if the L1 entry still exists and its write timestamp matches) and failed **backplane publishes**
  (re-publish the invalidation).
- **Barrier:** after any processing failure, set `barrier = now + AutoRecoveryDelay` (default 5 s);
  no retries before it — prevents retry storms during a sustained L2 outage (FusionCache's key
  insight).
- **Conflict check:** an incoming backplane message newer than a queued item for the same key
  drops the queued item — another node already won.
- **Retry cap:** `AutoRecoveryMaxRetries` (default 8); exhausted items are dropped with a Warning.
- **Execution:** a background loop owned by the Hybrid package (registered as a hosted service by
  `Setup` only when enabled). Degraded mode is observable: Warning on enqueue, Information on
  successful replay; meters arrive with #384.
- Memory-layer failures and factory failures are *not* queued — fail-safe + throttle already cover
  those. No infinite loops by construction (cap + barrier + bounded queue).

## 10. Named caches (new issue)

- **Registration:** name-taking overloads on each provider's Setup class —
  `AddInMemoryCache("reports", o => …)`, `AddRedisCache("sessions", …)`, `AddHybridCache(name, …)`.
  Each registers `ICache` as a **keyed singleton** (key = name) + named options
  (`services.Configure<TOptions>(name, …)`), its own coordinator, and (Redis) its own serializer
  selection via the named options. Existing nameless overloads keep today's behavior; the
  role keys `"memory"`/`"remote"`/`"hybrid"` (`CacheConstants`) remain reserved.
- **Resolution:** `[FromKeyedServices("reports")] ICache cache` natively, plus a thin provider in
  Abstractions for runtime names:

```csharp
public interface ICacheProvider
{
    ICache GetCache(string name);        // throws CacheNotFoundException-style on unknown name
    ICache? GetCacheOrNull(string name);
}
```

- **Per-cache defaults:** provider options gain `CacheEntryOptions? DefaultEntryOptions`. A new
  `GetOrAddAsync(key, factory, ct)` overload (no options) uses it and throws
  `InvalidOperationException` when unset — defaults are explicit-at-registration, never magic.

## 11. What we deliberately do NOT take from FusionCache

- **Barrier tagging / `Clear()` via barriers** — replaced by the reverse index (§2).
- **210-bucket hashed `SemaphoreSlim` pool** — `KeyedAsyncLock` is already truly per-key.
- **Options-duplication-per-call** (`Duplicate()`/`Setup` chains) — our `CacheEntryOptions` is a
  small immutable record struct; adaptive mutation happens on a context property instead.
- **No sliding expiration** — we ship it (#431); we add the eager-refresh exclusivity rule instead.
- **`FailSafeDefaultValue` parameter** — caller can layer that trivially; keeps the contract lean.
- **Sync API surface** — the framework is async-only.

## 12. Fit assessment for in-flight PRs

- **PR #426 (factory timeouts)** — **fits as-is.** The coordinator it builds is the substrate every
  feature above plugs into (eager refresh reuses its detached-completion machinery; the
  distributed-lock seam slots into its lock-acquisition phase; envelope v2 extends its store
  contract). CI red is the new repo-wide format gate (landed with #434 after the branch was cut),
  not a code defect. **Action: merge its branch into the working branch.**
- **PR #431 (sliding expiration)** — **fits with one addition:** the
  `SlidingExpiration × EagerRefreshThreshold` rejection rule (added when eager lands).
  **Action: merge its branch into the working branch after the timeouts branch, resolving the
  `CacheEntryOptions`/coordinator overlap.**

## 13. Delivery plan — single PR, ordered commit batches

All work lands as **one PR** on the working branch (owner call). Batches below are commit groups
within it; each batch leaves the branch green (build + targeted tests).

| Batch | Issue | Contents |
| --- | --- | --- |
| 0 | — | merge `origin/main`, then `xshaheen/cache-factory-timeouts` (#426), then `xshaheen/feat-cache-sliding-expiration` (#431); resolve options/coordinator overlap; `make format` |
| 1 | #375/#376/#378 foundation | envelope v2 + `CacheStoreEntryWrite` descriptor; memory entry fields; no behavior change |
| 2 | #375 | eager refresh: option + stamp + trigger + dedup + conformance tests |
| 3 | #376 | conditional/adaptive refresh: context overload, NotModified/Modified, adaptive save |
| 4 | #378 | tagging abstractions + Memory index + `RemoveByTagAsync` + tagged Upsert overload |
| 5 | #379 | tagging Redis reverse index: Lua write/remove, version-pinned membership, TTL (GT) |
| 6 | #380 | tagging Hybrid: Tag field on invalidation message, L1 index invalidation |
| 7 | new | distributed factory lock seam + `Headless.Caching.DistributedLocks` adapter package |
| 8 | new | named caches: keyed registration, `ICacheProvider`, `DefaultEntryOptions` |
| 9 | #386 | auto-recovery: queue + barrier + conflict check + hosted loop (Hybrid only) |
| 10 | — | docs sync: `docs/llms/caching.md` + package READMEs per AUTHORING.md |

The PR closes #374, #375, #376, #377, #378, #379, #380, #386 and supersedes PRs #426/#431
(close those when this PR opens).

## 14. Test matrix (acceptance scenarios)

| Scenario | PR | FusionCache analogue |
| --- | --- | --- |
| Fail-safe on factory failure / on L2 failure | shipped | L1Tests/L1L2Tests fail-safe suites |
| Soft timeout returns stale, background completes & saves | #426 | `AllowTimedOutFactoryBackgroundCompletion` tests |
| Hard timeout: cold throws, warm serves stale, no state corruption | #426 | hard-timeout suites |
| Eager refresh fires past threshold, before expiry | 3 | EagerRefresh tests |
| Eager refresh: N concurrent readers → 1 refresh (zero-timeout dedup) | 3 | eager dedup tests |
| Eager refresh × distributed lock: 2 nodes → 1 refresh | 8 | distributed-locker eager tests |
| `NotModified` extends entry (logical+physical+eagerAt restamped, value kept) | 4 | ConditionalRefresh tests |
| `Modified` replaces value + validators | 4 | ConditionalRefresh tests |
| Conditional factory failure → fail-safe fallback | 4 | — |
| Adaptive: factory shortens Duration → saved entry honors it | 4 | AdaptiveCaching tests |
| RemoveByTag removes all members; multi-tag entry removed via any tag | 5–7 | Tagging suites |
| Tag invalidation propagates across Hybrid nodes (L1 cleared) | 7 | backplane tag tests |
| Sliding read re-arms idle window, clamped by absolute Duration | #431 | — (FusionCache lacks sliding) |
| Sliding does not rewrite L2 on every read (re-arm throttling per #431 design) | #431 | — |
| Local stampede: 100 parallel callers → 1 factory call | shipped | CacheStampedeTests (ClassData parallelism) |
| Distributed stampede: 2 nodes, lock seam on → 1 factory; loser re-checks store and gets fresh hit | 8 | distributed-locker tests |
| Lock-seam timeout → stale fallback / miss, never deadlock; releaser always disposed | 8 | — |
| Auto-recovery: L2 write fails → queued → replayed after barrier; newer backplane msg drops item; retry cap drops with warning | 10 | AutoRecoveryTests_Async |
| Named caches: two instances, different Duration/fail-safe, isolated keys | 9 | DependencyInjectionTests |
| `GetOrAddAsync` without options uses `DefaultEntryOptions`, throws when unset | 9 | — |

## 15. Resolved questions (owner answers 2026-06-10)

1. **Tag staleness window (§7):** FusionCache-faithful semantics — version-pinned membership
   (hash index + header-only `GETRANGE` verification on remove).
2. **Tagged direct writes:** `UpsertAsync(key, value, CacheEntryOptions)` overload only in v1.
3. **Auto-recovery scope:** Hybrid only.
4. **Delivery:** single PR on the working branch; merge the #426/#431 branches into it.
