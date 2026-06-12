# Cache L2-level timeouts — design proposal

**Date:** 2026-06-12 · **Status:** Draft for decision · **Reference:** FusionCache `DistributedCacheSoftTimeout` / `DistributedCacheHardTimeout`

## 1. Problem

Headless has **no coordinator-level timeout on L2 (distributed cache) operations**. The only L2 timeouts today are StackExchange.Redis client-level (`ConfigurationOptions.SyncTimeout` / `AsyncTimeout`), which throw on elapse and are unaware of the cache's fail-safe / L1 state. Under an L2 latency spike (slow Redis, network blip), every Hybrid read that misses L1 blocks on L2 up to the client timeout and then fails crudely instead of degrading to L1 / a stale reserve.

FusionCache closes this with a **layered fallback**: an L2 read that *soft*-times-out falls back to L1 / the fail-safe reserve **without running the factory**; a *hard* timeout caps any single L2 op. This is the highest-value gap identified in the FusionCache comparison.

## 2. Proposed scope (v1)

- **Hybrid-only.** Standalone Redis has no L1 to degrade to — an L2 timeout there is just a failure with no better fallback. Scope this to `HybridCache`.
- **Reads only in v1.** Write-side L2 latency is already addressed by `AllowBackgroundDistributedCacheOperations` (writes detach, caller doesn't wait). So v1 targets **L2 read** soft/hard timeouts. Write-side timeouts are a deferred follow-up.
- **Two knobs:** `DistributedCacheSoftTimeout`, `DistributedCacheHardTimeout` (both `TimeSpan`, default `Timeout.InfiniteTimeSpan`).

## 3. The key integration risk (why design-first)

The fail-safe state machine lives in `FactoryCacheCoordinator`, which reads through `IFactoryCacheStore.TryGetEntryAsync`. Hybrid's implementation composes L1 + L2. For the layered fallback to work, **Hybrid's `TryGetEntryAsync` must, on an L2 soft-timeout, return the L1 entry (even a logically-stale one) so the coordinator's existing fail-safe path can serve it.** A hard timeout aborts the L2 read entirely → L1-only view. The coordinator needs no change if the store returns the right view; the work is wrapping the L2 read and shaping the returned `CacheStoreEntry<T>`. Verify this seam holds before coding — it is the crux.

## 4. Decision forks (need owner sign-off)

**Fork A — L2 read soft-timeout during `GetOrAddAsync` (L1 miss / L2 slow):**
- A1: Treat as an L2 miss → run the factory now. Simple, but runs an expensive factory even when the value was in L2.
- **A2 (recommended, FusionCache-faithful):** If an L1 fail-safe reserve exists, serve it and **skip the factory**; otherwise run the factory. Highest value — degrades a slow L2 to stale-but-fast.
- A3: Serve stale if available, else miss (`NoValue`) without factory. Too aggressive (skips factory even with no reserve).

**Fork B — soft-timeout during plain `GetAsync` (no factory):** L1 already missed (that's why we hit L2), and plain reads use logical expiration with no fail-safe reserve, so a soft-timeout → **miss (`NoValue`)**. Confirm this is acceptable (recommended yes).

**Fork C — hard timeout behavior:** degrade (serve stale/miss) vs. throw a typed `CacheDistributedTimeoutException` (FusionCache throws `SyntheticTimeoutException`). **Recommended: degrade on reads** (throwing on a read is hostile); revisit for writes when write-side timeouts land.

**Fork D — options placement:** `HybridCacheOptions` (instance-level, infra tuning) vs. `CacheEntryOptions` (per-call, like the factory timeouts; FusionCache puts them on entry options). **Recommended: `HybridCacheOptions`** for v1 — these are deployment-infra knobs, not per-entry semantics. Per-call override is a later addition if needed.

## 5. Implementation sketch (assuming A2 / B-miss / C-degrade / D-HybridCacheOptions)

1. Add `DistributedCacheSoftTimeout` / `DistributedCacheHardTimeout` to `HybridCacheOptions` (validated `> Zero` or `Infinite`).
2. A read-timeout helper racing the L2 read against a `TimeProvider` timer (reuse the shape of `FactoryCacheCoordinator._RunFactoryWithTimeoutAsync`; honor `CanBeCanceled` to avoid CTS allocation when no timeout is set — the hot path must allocate nothing when both timeouts are infinite).
3. In Hybrid's `IFactoryCacheStore.TryGetEntryAsync` (and `GetAsync`): wrap the L2 read. Soft-timeout → return the L1 view (stale reserve included) so the coordinator's fail-safe serves it; hard-timeout → abort the L2 read, return L1-only. Log a degraded-mode warning (new `[LoggerMessage]`).
4. Bulk reads (`GetAllAsync`): apply the same wrapper or defer to a follow-up.
5. Tests (Hybrid unit): gated/slow L2 double; assert soft-timeout serves stale and **does not** run the factory (A2); plain `GetAsync` soft-timeout returns miss; hard-timeout aborts; both-infinite path is unchanged and allocation-free.

## 6. Out of scope (follow-ups)

- Write-side L2 timeouts (covered for perf by `AllowBackgroundDistributedCacheOperations`).
- Per-call (`CacheEntryOptions`) override of the timeouts.
- Standalone-Redis L2 timeouts (no L1 fallback — low value).

## 7. Open questions

1. Confirm Fork A = A2, B = miss, C = degrade, D = `HybridCacheOptions`.
2. Is the `TryGetEntryAsync`-returns-stale-on-soft-timeout seam (§3) acceptable, or should the coordinator learn an explicit "L2 degraded" signal instead of inferring from the returned entry?
3. Bulk reads in v1 or deferred?
