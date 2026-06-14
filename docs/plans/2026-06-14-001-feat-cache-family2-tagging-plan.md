# Plan: Family 2 logical tag-version invalidation for Headless caching

- **Date:** 2026-06-14
- **Branch:** `xshaheen/feat-caching-resilience-program` (PR #437) — note: a concurrent actor commits here; keep per-unit commits to bound rebase surface.
- **Status:** approved, implementation in progress.

## Context

Tag invalidation currently uses an **eager reverse-index sweep** (Redis hash `tag -> {member: stamp}` swept by a budgeted Lua loop; InMemory `_tagIndex`). That design is the single source of three problems: an iteration **budget** (livelock guard), **no Redis cluster** support (cross-slot sweep), and a **point-in-time race** (members re-added mid-sweep survive). FusionCache, MS HybridCache, and Symfony `TagAwareAdapter` all avoid these by using **logical version/timestamp invalidation** instead of eager deletion.

## Decisions (locked)

1. Replace eager reverse-index with **Family 2 logical tag-version invalidation**.
2. **Full scope**: both L1 (InMemory) and L2 (Redis) + Hybrid. Remove the reverse index entirely.
3. **Timestamp markers** (`__tag:<tag> = now`) + **physical-TTL backstop** (existing `PhysicalExpiresAt` bounds staleness if a marker is lost).
4. Add **public `ClearAsync`** (logical, O(1), reserve-preserving) alongside `FlushAsync` (physical wipe).
5. **`RemoveByTagAsync` -> `ValueTask`** (no count under O(1) invalidation); delete `CacheTagRemovalIncompleteException`.

## Core mechanism

- `RemoveByTag(tag)` = write one per-tag timestamp marker. O(1). One key per tag -> works on cluster.
- On **read**, a provider's `TryGetEntryAsync` **demotes** a tag-invalidated entry (any tag marker > entry `CreatedAt`) from fresh to "logically-expired-but-physically-present" — i.e. a fail-safe reserve. `FactoryCacheCoordinator` then needs **no change**: fail-safe serves it stale; direct reads treat it as a miss. This structurally eliminates the #16 reserve-destruction class of bug.
- `ClearAsync` = bump one reserved generation marker checked by every tagged read.

## Implementation Units

- **U1 (prerequisite): entry `CreatedAt`.** Add to `CacheStoreEntry`, `CacheStoreEntryWrite`, `CacheEntryStamps.Compute`; set in `FactoryCacheCoordinator._WriteFactoryResultAsync`. Redis frame: new field + bump `Version 0x02 -> 0x03` (legacy frames read as miss). InMemory `CacheEntry`: new `CreatedAt`. `PhysicalExpiresAt` already provides the lost-marker backstop — no extra field.
- **U2: read chokepoint.** Core helper `IsInvalidated(tags, createdAt, markerLookup)`, gated on `Tags is { Count: > 0 }`. Providers demote in `TryGetEntryAsync`; direct value reads return miss.
- **U3: Redis markers + local cache.** `RemoveByTag`/`ClearAsync` = O(1) marker SET; delete `_EnsureTaggingClusterSupported`; process-local marker cache with `TagMarkerRefreshWindow` option (pipelined MGET on refresh). Stop writing the reverse-index hash.
- **U4: InMemory markers.** Replace `_tagIndex` with `_tagMarkers` (`tag -> DateTime`); O(1) invalidate; remove `GetTaggedKeys`/`_UpdateTagIndex`/`_UntagEntry` + all call sites.
- **U5: Hybrid backplane.** Tag/clear bump propagates via existing backplane; **delete** the recovery-aware per-key tag walk in `HandleInvalidationAsync`; peers bump their own L1 markers on broadcast.
- **U6: reserved-tag `ClearAsync`** across abstraction + providers + Hybrid.
- **U7: contract.** `RemoveByTagAsync` -> `ValueTask`; add `ClearAsync`; update `ICache`/`ICache<T>`/`IRemoteCache`/`ScopedCache` + 3 providers + Hybrid + ~12 tests + docs.

## Removal list

`CacheTagRemovalIncompleteException` + throw site + `LogRemoveByTagBudgetExceeded`; the budget loop + `MaxMembersPerTagRemoval`; `CacheRemoveByTagScriptDefinition` (Lua); the tag-index logic in `CacheTaggedSetScriptDefinition`; `_EnsureTaggingClusterSupported` + calls; `IInMemoryCache.GetTaggedKeys` + impl + `_tagIndex`/`_UpdateTagIndex`/`_UntagEntry`; Hybrid recovery-aware tag path + `RecoveryQueue.HasNewerPendingItemThan` (if unused); verify `CacheStoreEntryWrite.RemovedTags`/`ComputeRemovedTags` have no other consumer before removing.

## Test plan

- Rewrite tag conformance (`CacheConformanceTestsBase`) and per-provider tag tests for the new `ValueTask` return + miss/stale-reserve assertions (no counts, no `GetTaggedKeys`, no cluster-not-supported).
- New: read-time tag-expiry (3 tiers); fail-safe serves tag-invalidated reserve; `ClearAsync` reserve-preserving + O(1); cluster now-supported (Redis integration); marker-loss bounded by physical TTL; cross-instance marker propagation (Hybrid); version-pin (re-created entry after bump not invalidated); frame v3 legacy-read codec.

## Risks / open items

- **Read-path perf**: per-read marker lookups — mitigated by the local marker cache + `Tags.Count > 0` gate. Untagged reads pay nothing.
- **L2 visibility lag**: other instances see an L2 marker bump only after their local cache refreshes (`TagMarkerRefreshWindow`); L1 is immediate via backplane. Accepted Family-2 tradeoff; document it.
- **Public break**: `RemoveByTagAsync` return type + new `ClearAsync` — ~7 source + ~12 test files + docs.
- **Frame flags byte** is full at 8 bits — imply `CreatedAt` presence by frame version `0x03` rather than a new flag bit.
- **Concurrent-actor collisions**: hottest files `RedisCache.cs`, `InMemoryCache.cs`, `FactoryCacheCoordinator.cs`, `HybridCache.*`, `RedisCacheEntryFrame.cs`, `CacheEntryStamps.cs`, `CacheStoreEntryWrite.cs`. Land U1 isolated first; one commit per provider.
- **Docs-sync**: `docs/llms/caching.md` + `src/Headless.Caching.{Core,Abstractions,Redis,InMemory,Hybrid}/README.md`.
