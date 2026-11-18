---
domain: Caching
packages:
---

# Custom cache providers

> Part of [Caching](../caching.md). What a cache you implement yourself must do so `GetOrAddAsync`, tags, `ClearAsync`, `FlushAsync`, and the hybrid cache work on it as they do on the built-in providers.

Read this page when you write your own `ICache`, such as a provider over another store or a custom L2 for the hybrid. To consume a cache, read [Caching](../caching.md) instead.

## Pick the interfaces

| Interface | Implement it when | What it gives you |
| --- | --- | --- |
| `ICache` | Always. | The cache contract. `Events` has a default implementation that returns a no-op hub. |
| `IFactoryCacheStore` | You want `GetOrAddAsync` with fail-safe, timeouts, eager refresh, sliding expiration, conditional refresh, and tags. | Run `GetOrAddAsync` through a `FactoryCacheCoordinator` over this store. |
| `IRemoteCache` | The store is shared across processes and may serve as a hybrid L2. | The hybrid takes its L2 as the registered `IRemoteCache`, or as the named instance in `RemoteCacheName`, which must implement it. `GetRemoteOrDefaultCache()` returns it. Adds `GetWithExpirationAsync<T>` and `GetAllWithExpirationAsync<T>`. |
| `IInMemoryCache` | The store is process-local and may serve as a hybrid L1. | A marker interface with no members. |
| `ISeedableTagMarkerCache` | The cache keeps a process-local copy of tag, clear, or remove markers, or it is a hybrid L2. | Lets a hybrid seed and write markers with the sender's timestamp. See [Markers](#markers). |
| `IBufferCache` | The store holds bytes and can copy them straight into a caller's buffer. | The zero-intermediate-copy byte path. See [Byte payloads](adapters.md#byte-payloads). |

A cache that implements only `ICache` still works with `BufferCacheExtensions`, which fall back to the `byte[]` path.

## Run the factory path

Construct one `FactoryCacheCoordinator` per cache instance and route `GetOrAddAsync` through it with your `IFactoryCacheStore`:

- Pass your `TimeProvider`, and optionally an `ILogger`, the registered `ICacheFactoryLockProvider` (needed for `UseDistributedFactoryLock`), the instance name for telemetry, and the tier name.
- Surface `cache.Events` from the coordinator's `EventsHub`, so factory and fail-safe events reach subscribers.
- Classify cancellation with `FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)` when a composite store catches exceptions, so caller cancellation propagates and any other `OperationCanceledException` counts as a failure.
- Implement `UpsertEntryAsync(key, value, options)` with `FactoryCacheStoreExtensions.UpsertEntryAsync`. It validates the options with the coordinator's rules, computes the stamps, and writes through your store, so direct writes and factory writes agree.

## Implement `IFactoryCacheStore`

All timestamps the store receives and returns are UTC.

### Reads

`TryGetEntryAsync<T>(key, readOptions, cancellationToken)` returns a `CacheStoreEntry<T>`.

- Return an entry that is physically present even when its logical expiration has passed. That entry is the fail-safe reserve. Return `CacheStoreEntry<T>.NotFound` only when the entry is missing or physically expired.
- An expiry is reached at its exact tick: `expiresAt <= now` is expired. `CacheStoreEntryExtensions.IsFresh(now)` and `IsPhysicallyPresent(now)` apply this rule; use them instead of writing your own comparison.
- Report an entry a tag, clear, or remove marker invalidated as logically expired, and keep it physically present when its reserve allows. See [Markers](#markers).
- Return every stored stamp unchanged: `CreatedAt`, `ThrottledUntil`, `EagerRefreshAt`, `ETag`, `LastModifiedAt`, `Tags`, `SlidingExpiration`, and the logical and physical expirations. A cached `null` is `IsNull = true`; `Value` is then ignored.
- Return a `ConcurrencyStamp` that identifies the exact physical snapshot read. Treat it only as an equality token. It decides whether a later conditional write commits, so its collision resistance is yours to choose.
- `readOptions` is a `FactoryCacheReadOptions`. A single-tier store ignores `SkipMemoryRead` and `SkipDistributedRead`. `IsRecheck` marks a read that repeats an earlier read of the same `GetOrAddAsync` call, after a lock wait; record no per-tier hit or miss metric or event for it, so per-tier counters count one read per call.
- `ServeStaleImmediately = true` asks the coordinator to serve a physically present stale entry without running the factory, because a lower tier degraded during the read. The coordinator honors it only when fail-safe is on. Only a multi-tier store needs it.

`TryGetAllEntriesAsync<T>(keys, readOptions, cancellationToken)` returns an array aligned by position with `keys`: element `i` is the snapshot of `keys[i]`, a miss is `NotFound`, and a duplicate key gets its own element. Resolve the markers the batch needs in one prefetch for the whole batch, never once per key.

### Writes

`SetEntryAsync<T>(key, in CacheStoreEntryWrite<T> entry, cancellationToken)` stores a value with its expirations and metadata.

- **Conditional write.** When `ExpectedConcurrencyStamp` is set, commit only while the live entry still has that stamp, and return `false` otherwise. A `null` stamp is an unconditional write. This compare stops a late factory from bringing back a key that was removed meanwhile or overwriting a newer write.
- **Persist every stamp** the write carries and return it on the next read: `CreatedAt`, `ThrottledUntil`, `EagerRefreshAt`, `ETag`, `LastModifiedAt`, `Tags`, and `SlidingExpiration`. Apply no throttle logic of your own: the coordinator reads `ThrottledUntil` and decides.
- **Restamps.** `IsRestamp = true` marks a write that only restamps the current value (a conditional `NotModified`, a fail-safe throttle, an eager-refresh gate). `CreatedAt` then carries the original birth time. A multi-tier store skips cross-instance invalidation for it.
- **Per-tier skips.** A single-tier store ignores `SkipMemoryCacheWrite` and `SkipDistributedCacheWrite`.
- **Expiry.** Map physical expiration to the store's own TTL, and keep logical expiration in the stored entry, so reads can miss while the reserve stays.

### Sliding re-arm

`TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, cancellationToken)` extends a sliding entry's logical deadline after a fresh value read.

- Extend the existing entry in place. Do not rewrite the value.
- Only extend, never shorten, and cap the new deadline at `physicalExpiresAt`.
- Re-arm only when `CacheEntryStamps.ShouldRearmSliding(current, rearmed, slidingExpiration)` returns `true`, which throttles a hot key to one write per `CacheEntryStamps.GetSlidingRearmTolerance(slidingExpiration)`.
- Treat it as best-effort: never surface a failure, because the value read already succeeded. Do nothing for a missing entry or one past its physical cap.

## Markers

`RemoveByTagAsync`, `ClearAsync`, and a distributed `FlushAsync` are logical: each writes a timestamp marker and deletes nothing. Your store owns the markers.

- **Tag marker.** One per tag. `RemoveByTagAsync(tag)` raises it to now.
- **Clear marker.** One per cache. `ClearAsync()` raises it to now. An entry it invalidates keeps its fail-safe reserve.
- **Remove marker.** One per cache, for a logical `FlushAsync`. An entry it invalidates reads as a hard miss with no reserve. A store whose `FlushAsync` deletes physically has no remove marker.

**Read rule.** The newest marker that applies to an entry is the larger of the clear marker and the marker of every tag the entry carries. The entry is invalidated when that marker is newer than its `CreatedAt`: report it as logically expired and keep it as a reserve. An entry with no `CreatedAt` is invalidated whenever any marker applies. An entry born before the remove marker is a hard miss with no reserve. A key written after a marker has a newer `CreatedAt`, so the marker does not invalidate it.

**Marker writes are raise-only.** A write never lowers a marker the store already holds. A replay after an outage can carry an older timestamp than a marker that already landed; lowering the marker would bring back entries written after the invalidation. On a shared store, make the raise atomic.

**Keep marker keys apart from cache keys.** Marker names must not collide with any key a caller can write.

**Cost.** A store may cache markers in process to save round trips, as Redis does with `TagMarkerRefreshWindow`. The window delays when this instance sees another instance's invalidation; the physical TTL still bounds staleness if a marker is lost.

### `ISeedableTagMarkerCache`

Implement it on any cache that keeps a process-local copy of its markers, and on a cache meant to be a hybrid L2.

| Member | Contract |
| --- | --- |
| `SeedTagMarker(tag, invalidatedAt)`, `SeedClearMarker(invalidatedAt)`, `SeedRemoveMarker(invalidatedAt)` | Raise only the process-local copy, from a timestamp learned elsewhere, such as a backplane message. Write nothing to the shared store. |
| `WriteTagMarkerAsync(tag, invalidatedAt, ct)`, `WriteClearMarkerAsync(invalidatedAt, ct)`, `WriteRemoveMarkerAsync(invalidatedAt, ct)` | Raise the marker in the shared store at the given timestamp, raise-only, then raise the local copy. `WriteTagMarkerAsync` throws `ArgumentNullException` for a `null` tag and `ArgumentException` for an empty one. |

- A store whose `FlushAsync` deletes physically implements `SeedRemoveMarker` and `WriteRemoveMarkerAsync` as no-ops, as `Headless.Caching.InMemory` does.
- Never implement `Write*` as `Seed*`. A hybrid uses `Write*` for live invalidation and for auto-recovery replay, so a local-only write leaves the shared marker unwritten and breaks recovery.

**How a hybrid uses it.**

- **Receiving.** On a tag, clear, or flush message from another host, the hybrid seeds its L1 and its L2 with the sender's timestamp. Then an L1 miss that falls through to L2 sees the invalidation at once instead of after L2's refresh window. An L1 that does not implement it gets a local `RemoveByTagAsync` or `ClearAsync` instead, which stamps the receiver's own clock.
- **Sending.** `RemoveByTagAsync`, `ClearAsync`, and `FlushAsync` on the hybrid write the L2 marker through `Write*` with the original timestamp. Under `EnableAutoRecovery` a failed write is replayed later with that same timestamp. An L2 that does not implement it gets `RemoveByTagAsync` or `ClearAsync` instead, stamped with its own clock.

## Direct writes must agree with reads

A logically dead entry (expired inside its reserve, or invalidated by a tag, clear, or remove marker) is still physically present. Every conditional write must treat it as absent, as every read does:

- `TryInsertAsync` inserts when the key is absent or holds a logically dead entry, and returns `false` when a live entry exists. Overwrite the dead entry only while it is still the entry you judged dead, so a live entry another writer stored in between wins.
- `TryReplaceAsync`, `TryReplaceIfEqualAsync`, and `RemoveIfEqualAsync` return `false` for a logically dead entry and leave it in place.
- `TryReplaceIfEqualAsync` and `RemoveIfEqualAsync` compare atomically: the whole compare and write is one step, with no write landing between them.
- Mutations that read the current value (`IncrementAsync`, `SetIfHigherAsync`, `SetIfLowerAsync`, `SetAddAsync`, `SetRemoveAsync`) treat a physically expired entry that has not been reaped yet as absent and start over.

`SyncWithSourceAsync` relies on these rules. It reads the entry, reads the source, then swaps with `TryReplaceIfEqualAsync`, or `TryInsertAsync` when the entry is absent, and retries when another write landed in between. A store whose `TryReplaceIfEqualAsync` is not atomic, or whose `TryInsertAsync` overwrites a live entry, lets an older value win.

## Stored format

Version the format you store entries in, and read any version you do not recognize, including bytes written without your format, as a miss rather than an error. `GetOrAddAsync` then writes the entry again in the current format, so a rolling upgrade needs no migration and no deploy order. Read truncated or corrupt data as a miss too.

## Verify

Run the same assertions against your provider that hold for the built-in ones:

- `RemoveByTagAsync(tag)` makes `GetAsync` miss an entry written before it, while `GetOrAddAsync` with fail-safe on and a throwing factory still returns that value with `IsStale = true`.
- An entry written after `RemoveByTagAsync` or `ClearAsync` is not invalidated.
- A write with a stale `ExpectedConcurrencyStamp` returns `false` and leaves the entry unchanged.
- A marker write with an older timestamp than the stored marker leaves the stored marker unchanged.
- `TryInsertAsync` succeeds on a key that `ExpireAsync` logically expired, and `TryReplaceIfEqualAsync` returns `false` on it.
