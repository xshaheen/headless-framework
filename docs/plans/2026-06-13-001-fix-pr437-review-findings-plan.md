# Plan: PR #437 review-finding fixes

Branch: `xshaheen/feat-caching-resilience-program`. Source: x-code-review run `20260613-052815-31755db4`.
All decisions made interactively with the author (2026-06-13). Greenfield — breaking changes OK.

## Already applied (committed-pending; build-verified)
- **#19** `HybridCache.cs:324` — `throw exception` -> `ExceptionDispatchInfo.Capture(exception).Throw()`.
- **#24** `DistributedLocks/Setup.cs` — deleted private dup, reuse `DelegatingCacheProviderOptionsExtension`.
- **#27** `CacheConstants.cs` — added `[PublicAPI]`.

## Decisions (what to do)

| # | Sev | Decision | Files |
|---|-----|----------|-------|
| 1 | P1 | Derive local TTL from `DefaultLocalExpiration` in `GetAllAsync` (kill N GetExpirationAsync round-trips). Document the asymmetry in XML (bulk L1 TTL = default, not exact remaining L2 TTL; single-key stays exact). Follow-up: enrich `IRemoteCache.GetAllAsync<T>` to carry frame expiration (FusionCache-faithful, zero extra reads) — not in this pass. | `HybridCache.cs` |
| 2 | P2 | Mirror the timeout branch on caller-cancel path: `CancelAsync()` -> `_DisposeAfter(operationCts, operationTask)` -> null before rethrow; attach fault observer to abandoned task. | `HybridCache.DistributedResilience.cs` |
| 3 | P2 | Trip the circuit breaker whenever `DistributedCacheCircuitBreakerDuration > Zero`, independent of `RecoveryQueue`; rethrow when queue is null. Apply to `RemoveAsync` + `ExpireAsync`, matching `UpsertAllAsync`. | `HybridCache.cs` |
| 4/5 | P2 | Static non-capturing factory adapter for the simple `GetOrAddAsync` overload (#4); pass `context`/`factory` as params into `_RunFactoryWithTimeoutAsync` instead of pre-binding a closure (#5). | `FactoryCacheCoordinator.cs` |
| 6 | P2 | Track min-expiry item as a field (update on add, re-scan only on removal) instead of O(N) scan under admission lock. | `HybridCacheRecoveryQueue.cs` |
| 7 | P2 | Stripe `KeyedAsyncLock` into N shards (N = pow2 ~= ProcessorCount), index by key hash; preserve exact refcount/removal semantics per shard. Mirror ConcurrentDictionary internal striping. | `KeyedAsyncLock.cs` |
| 8 | P2 | `ArrayPool<byte>.Shared` for the main + `EncodeTags` buffers in `Encode`, returned after `StringSetAsync`. | `RedisCacheEntryFrame.cs` |
| 9 | P2 | Extract shared `_WriteL2EntryAsync<T>` helper used by `_SetEntryCoreAsync` + `_SetEntryL2TailAsync`. | `HybridCache.StoreLayer.cs` |
| 10 | P2 | Hoist `_DisposeAfter` to a shared internal static in Core; unify on `TaskScheduler.Default` (drop the Hybrid copy's `ExecuteSynchronously`). | Core helper + `HybridCache.DistributedResilience.cs` |
| 11 | P2 | Recovery-aware Tag gate: incoming Tag invalidation skips keys with a surviving newer recovery item (reuse the recovery-queue timestamp/conflict check the Key/Keys branches use). Keep eager deletion. 2-node test. | `HybridCache.cs` + recovery queue |
| 12/13 | P2 | Unify `ICache<T>` with `ICache`: `TimeSpan?` expiration on Upsert/UpsertAll; rename `TryAddAsync` -> `TryInsertAsync`. | `ICache\`T.cs`, `ScopedCache.cs`, `Cache<T>`, impls |
| 14 | P2 | Add `CancellationToken cancellationToken = default` to `ICache<T>.RemoveIfEqualAsync` + `GetSetAsync`; thread through `ScopedCache<T>`/`Cache<T>`/impls. | `ICache\`T.cs` + impls |
| 15 | P2 | Advisor warns at startup when messaging is configured but no `CacheInvalidationMessage` consumer is registered (warn, never throw). | `HybridCacheBestPracticesAdvisor.cs` |
| 16 | P3 | Leave as-is (bespoke ArgumentException messages > generic `Argument.IsOneOf`); add a brief note in code why. | `HeadlessCachingSetupBuilder.cs` |
| 17 | P2 | Test `_QueueExpireRecovery` replay: assert enqueue (`GetKind==Expire`), replay calls `l2.ExpireAsync`, published msg carries `Expire==true`. | `HybridCacheExpireTests.cs` |
| 18 | P2 | Redis integration test: sliding entry + `ExpireAsync` -> key gone, returns true. | `RedisCacheExpireTests.cs` |
| 20 | P3 | Attach `ContinueWith(OnlyOnFaulted)` observer to `_OnTimerTick` ProcessAsync. | `HybridCacheRecoveryQueue.cs` |
| 25 | P3 | Collapse `ICacheProviderOptionsExtension` + `DelegatingCacheProviderOptionsExtension` -> builder stores `Action<IServiceCollection>` directly; update ~24 call sites. | Core builder + all provider `Setup.cs` |
| 26 | P3 | Leave as-is (benchmark non-shipping, transitive ref only); document reasoning. | benchmarks |
| 28 | P3 | Drop reflection in `RedisCacheEntryFrameTests` -> direct `InternalsVisibleTo` access; remove `!`. | `RedisCacheEntryFrameTests.cs` |
| 29 | P3 | `<exception>` doc on `EagerRefreshThreshold` (0,1). | `CacheEntryOptions.cs` |
| 30 | P3 | Document envelope-v2 unknown-version->miss contract in Redis README. | Redis README |
| 31 | P3 | XML summary: `SkipCacheRead` + `IsFailSafeEnabled` -> no reserve, factory failure propagates. | `CacheEntryOptions.cs` |
| 32 | P3 | XML: `CacheFactoryContext<T>.Tags` initialized from existing entry; null discards. | `CacheFactoryContext.cs` |
| 33 | P3 | Rename misleading `BackgroundCompletionFinished` hook in EagerRefresh partial. | `FactoryCacheCoordinator.EagerRefresh.cs` |
| 34 | P3 | Split `HybridCache.cs` (1825 lines) into write-ops + recovery-helper partials — LAST, after all behavioral edits. | `HybridCache.cs` |

## Sequencing (respect one-editor-per-file; HybridCache.cs is the hot file)
1. **Wave A — isolated, parallelizable:** #4/5 (coordinator), #6+#20 (recovery queue), #7 (KeyedAsyncLock), #8 (frame) — note #8 and #30 both touch RedisCacheEntryFrame.cs/README, keep separate; #15 (advisor); #25 (collapse: builder + provider Setups); #29/#31/#32/#33 (docs); #16/#26 (note-only); #28 (frame tests).
2. **Wave B — HybridCache.cs cluster (sequential, me):** #3, #11, #1, #9; #2+#10 in DistResilience.cs/Core.
3. **Wave C — contract:** #12/#13/#14 across ICache`T + ScopedCache + Cache + impls (touches HybridCache.cs -> after Wave B).
4. **Wave D — tests:** #17, #18, #28, #11 2-node test, allocation/round-trip benchmark asserts.
5. **Wave E — #34 split** (last).

## Verify
`make build` (warnings-as-errors), `make test-project` for caching suites, targeted Redis integration for #18. Commit logically per wave; do not include the unrelated `docs/brainstorms/*` change.

## Follow-ups (not in this pass)
- Enrich `IRemoteCache.GetAllAsync<T>` (and single-key) to carry frame expiration -> exact L1 TTL with zero extra reads (FusionCache-faithful). Supersedes #1's approximation.
- KeyedAsyncLock: consider AsyncKeyedLock-style ConcurrentDictionary+per-entry-lock if striping proves insufficient.
