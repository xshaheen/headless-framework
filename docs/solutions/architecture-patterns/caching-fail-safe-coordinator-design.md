---
title: "Fail-safe caching: centralized coordinator, two-timestamp envelope, and the cancellation-identity pitfall"
date: 2026-06-05
category: architecture-patterns
module: Headless.Caching
problem_type: architecture_pattern
component: service_class
severity: medium
applies_when:
  - Adding factory-backed cache resilience (serve stale on factory or store failure)
  - Implementing logical-vs-physical expiration across multiple cache providers
  - Classifying an exception as caller-cancellation versus a downstream failure
  - Comparing nullable expiration timestamps across providers and an engine
tags: [caching, fail-safe, stale-while-revalidate, cancellation, time-provider, hybrid-cache, redis]
related_components: [redis, memory-cache, hybrid-cache]
---

# Fail-safe caching: centralized coordinator, two-timestamp envelope, and the cancellation-identity pitfall

## Context

`Headless.Caching` adds per-entry **fail-safe**: when a factory throws (or the backing store is unavailable), `GetOrAddAsync` serves the last-known-good value instead of propagating the failure, bounded by `FailSafeMaxDuration` and rate-limited by `FailSafeThrottleDuration`. This was the first feature of the caching "resilient core" and it established the shared factory-orchestration engine (`FactoryCacheCoordinator` + `IFactoryCacheStore`) that Memory, Redis, and Hybrid all delegate to. The design and its sharp edges were settled across PR #408 and a two-round code review; several of the edges are non-obvious enough to re-bite anyone extending the engine (timeouts, eager refresh, adaptive), so they are captured here.

## Guidance

### 1. Two-timestamp envelope: logical governs reads, physical is the reserve

Every entry carries `LogicalExpiresAt` and `PhysicalExpiresAt`. Normal reads (`GetAsync`, bulk reads, `ExistsAsync`, `GetExpirationAsync`) treat **logical** expiration as the cutoff; the physically-retained value is reachable **only** through `GetOrAddAsync`'s fail-safe fallback. When fail-safe is disabled (the default), `logical == physical`, so behavior is unchanged. Physical is computed as `now + (IsFailSafeEnabled ? max(Duration, FailSafeMaxDuration) : Duration)` — the longer of the two always wins so physical never precedes logical.

### 2. Centralize the state machine; providers supply a primitive

The read → keyed-lock → double-check → factory → upsert flow lives once in `FactoryCacheCoordinator`. Providers implement `IFactoryCacheStore` (`TryGetEntryAsync` / `SetEntryAsync` with explicit logical + physical) and delegate `GetOrAddAsync` to the coordinator. Hybrid implements the primitive as a **composite** (L1 then L2). Engine-first means the next features extend the coordinator, not three providers.

### 3. Throttle by re-stamping logical forward — never extend physical

On activation the coordinator writes the stale value back with `logical = min(now + FailSafeThrottleDuration, physical)` and **unchanged physical**. This reuses the existing envelope (no frame-format change) and gives distributed throttle coordination for free (logical is persisted, so any node sees it). Consequence: during the throttle window the value reads as a normal hit (`IsStale = false`); only the activating call returns `IsStale = true`.

### 4. Classify cancellation by token IDENTITY — and guard the `None` case

Caller cancellation must propagate and never activate fail-safe; an `OperationCanceledException` from an unrelated/downstream token (a timeout) must activate fail-safe. Classify by token identity, not just `IsCancellationRequested`. **The pitfall:** `oce.CancellationToken == cancellationToken` is `true` for `None == None`, so when the caller uses the default token and the factory throws a token-less OCE, fail-safe is wrongly suppressed in the exact outage it exists for. Guard the identity branch on `cancellationToken.CanBeCanceled`.

### 5. One expiry-boundary convention across every layer: `expiresAt <= now`

An entry is expired at the exact tick. The coordinator predicates (`IsFresh`/`IsPhysicallyPresent` use `> now`), Redis `_IsExpired` (`<= now`), the Memory eviction loop, and `CacheEntry.IsExpired`/`IsLogicallyExpired` must all agree. A lone provider using `<` disagrees at the boundary instant and produces "passes on Memory, fails on Redis" flakiness.

### 6. Best-effort restamp; keep the fresh write out of the fail-safe catch

Two structural rules in the coordinator:
- The throttle restamp is an optimization, not caller work: it uses `CancellationToken.None` and an unconditional catch (log at **Warning** so a persistently failing store is visible), so the stale value is always returned once activation is decided.
- Only the `factory(...)` call sits inside the fail-safe `try`. The fresh `store.SetEntryAsync` must be **outside** that `catch`, so a store-write failure after a successful factory propagates instead of silently discarding the fresh value and returning stale.

### 7. A fail-safe stale candidate must have a non-null physical expiration

`IsPhysicallyPresent` treats a null `PhysicalExpiresAt` as "never expires", but a genuine reserve always has one (the coordinator writes it). Require `PhysicalExpiresAt.HasValue` for stale candidacy; otherwise a legacy null-physical entry is served as stale but the restamp can't write, so every caller re-runs the factory (a throttle hole that hammers the dependency).

### 8. Hybrid: promote only fresh L2 on reads; let activation refresh L1

On a composite read, promote an L2 entry into L1 **only when it is logically fresh** — promoting a stale reserve on every read amplifies L1 writes and can overwrite a newer L1 reserve. Fail-safe activation still re-stamps the (now throttle-fresh) value into L1 via the composite write; that is intentional FusionCache parity so the throttle window is an L1 hit.

### 9. Factory timeouts ride the same fallback candidate and lock

Factory soft/hard timeouts are not provider features. They extend `FactoryCacheCoordinator` because the coordinator already owns stale-candidate detection, keyed locking, cancellation classification, and fresh writes.

Select exactly one effective factory timeout:

- If fail-safe is enabled, a valid stale reserve exists, and `FactorySoftTimeout` is finite, use the soft timeout. The caller returns stale and the same factory continues in the background.
- Otherwise, if `FactoryHardTimeout` is finite, use the hard timeout. The coordinator cancels or abandons the factory; stale is served when available, and a cold cache throws `CacheFactoryTimeoutException`.
- Otherwise, preserve existing behavior: the factory runs until it completes or the caller cancels.

Soft timeout also bounds per-key lock acquisition for callers that already have a stale fallback. This is what makes waiters and supported same-key re-entrant calls return stale instead of blocking behind the in-flight/background factory. Do not add a separate stampede primitive for this path unless there is a stronger requirement than per-key serialization.

### 10. Detached background completion needs a ceiling and a write gate

After a soft timeout, the background factory runs with a coordinator-owned token that is not linked to the caller token. This is deliberate: ASP.NET request cancellation after the stale response is returned must not kill the refresh intended for future callers. The cost is a contract for factory authors: do not capture request-scoped disposables; create a fresh scope inside the factory if scoped services are needed after the request path returns.

The lock hand-off protects the key only while the background task is still under coordinator ownership. `BackgroundFactoryCeiling` (default 2 minutes) races the factory and releases the per-key lock if a token-ignoring factory does not finish. On the ceiling branch, cancel the internal token and do **not** await the factory. A non-cooperative factory may continue running untracked, but the success write must be gated on the internal token so an abandoned factory cannot clobber a newer timeout-path value after the lock is released.

The guarantee is per key and cooperative-factory clean: while the background refresh is in flight, the key does not run duplicate cooperative factories. It is not a global concurrency bound across distinct keys. It also does not protect explicit user writes (`Set`, `Upsert`, `Remove`) from a slow successful background refresh; versioned/CAS writes are a separate future hardening.

## Why This Matters

Fail-safe trades a hard failure for a bounded stale read; each edge above, gotten wrong, silently breaks that trade in a way tests rarely catch unless they target the branch:
- **#4** suppressed fail-safe under the most common call shape (default token) — a regression that shipped because the behavior change had no test exercising the new branch. The meta-lesson: a behavior-changing fix and a test that exercises the new branch must land together.
- **#6** would return stale and discard a freshly-computed value on a transient cache-write blip.
- **#7** turns the throttle into a no-op and hammers a down dependency.
- **#5** is the dominant cause of cross-provider "works here, not there" bugs.
- **#9/#10** prevent a timeout feature from becoming either ineffective (soft timeout without stale fallback), lossy (hard timeout allowing background writes), or an availability leak (awaiting a non-cooperative background factory while holding the key lock).

## When to Apply

- Building or extending the caching resilience engine (timeouts, eager/adaptive refresh ride the same coordinator).
- Adding factory timeout, background refresh, or waiter lock-timeout behavior.
- Any code that must distinguish "the caller cancelled" from "a dependency failed" — reach for token identity plus the `CanBeCanceled` guard.
- Any multi-provider contract with an expiration/freshness boundary — pick one operator convention and assert it at the exact tick.

## Examples

Cancellation classification (the fix):

```csharp
// WRONG: None == None is true, so a token-less OCE under a default caller token
// is misread as caller cancellation and fail-safe is suppressed.
return exception is OperationCanceledException oce && oce.CancellationToken == cancellationToken;

// RIGHT: only identity-match when the caller actually supplied a cancelable token.
if (cancellationToken.IsCancellationRequested) return true;
return cancellationToken.CanBeCanceled
    && exception is OperationCanceledException oce
    && oce.CancellationToken == cancellationToken;
```

Fresh write must be outside the fail-safe catch:

```csharp
T? value;
try { value = await factory(cancellationToken); }
catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
{
    if (!options.IsFailSafeEnabled || !IsStaleCandidate(staleCandidate, now)) throw;
    await TryRestampStaleAsync(...);          // best-effort, CancellationToken.None
    return ToCacheValue(staleCandidate, isStale: true);
}
// factory succeeded: persist + return the FRESH value; a write failure here propagates.
await store.SetEntryAsync(key, value, value is null, logical, physical, cancellationToken);
return new CacheValue<T>(value, hasValue: true);
```

Exact-tick boundary, asserted with `FakeTimeProvider`:

```csharp
// advance to exactly PhysicalExpiresAt -> entry is expired (expiresAt <= now)
timeProvider.Advance(duration);
(await cache.GetAsync<T>(key)).HasValue.Should().BeFalse();
```

## Related

- `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md` — the cancellation-vs-timeout learning this engine applies (token identity over exception type).
- `docs/solutions/architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md` — why any future cache-owned Redis CAS script must register under the cache package's keyed `ScriptsLoader` (the shared `ReplaceIfEqual`/`RemoveIfEqual` scripts must not be mutated); fail-safe v1 deliberately stays Lua-free.
- `docs/solutions/tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md` — the best-effort single-node lock posture the cross-node restamp race is accepted under.
- PR #408 (`feat(caching): serve stale values on factory failure`) and `docs/llms/caching.md`.
