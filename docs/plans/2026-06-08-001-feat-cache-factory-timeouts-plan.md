---
title: "feat: Cache factory soft/hard timeouts + background completion"
type: feat
status: completed
date: 2026-06-08
issue: "#374"
epic: "#369"
depends_on: ["#371", "#372", "#373"]
deepened: 2026-06-08
---

# feat: Cache factory soft/hard timeouts + background completion (#374)

## Summary

Add per-entry **factory timeouts** to `Headless.Caching`, the next edge on the resilience engine
established by fail-safe (#373 / PR #408). Two new `CacheEntryOptions` fields — `FactorySoftTimeout`
and `FactoryHardTimeout` — bound how long `GetOrAddAsync` waits on a slow value factory:

- **Soft timeout** (non-lossy): when fail-safe is enabled *and* a stale value exists, return the stale
  value as soon as the soft timeout elapses, and let the factory **keep running in the background** to
  refresh the entry for future callers.
- **Hard timeout** (bounded): an absolute ceiling. When it fires, **abandon** (cancel) the factory; serve
  the stale value if one exists, otherwise throw a `CacheFactoryTimeoutException`.

All logic lands in `FactoryCacheCoordinator` (engine-first); the three providers (Memory, Redis, Hybrid)
need no behavioral change because they already pass `CacheEntryOptions` straight through.

**Acceptance (from #374):** soft timeout non-lossy; hard timeout bounded; **no duplicate factory runs**.

---

## Problem Frame

`GetOrAddAsync` today runs the value factory to completion inside a per-key `KeyedAsyncLock`
(`src/Headless.Caching.Core/FactoryCacheCoordinator.cs`). A slow or hung dependency therefore stalls the
caller for the *full* factory duration even when a perfectly serviceable stale value sits in the physical
reserve. Fail-safe (#373) only rescues a factory that **throws** — it does nothing for a factory that
simply takes too long.

Factory timeouts close that gap: bound the wait, fall back to the stale reserve, and (for soft timeouts)
refresh the cache off the request path. This is the "FusionCache playbook" the epic (#369) commits to —
mirror its semantics where they're sound, diverge deliberately where they aren't.

**Why now / dependency posture:** the two-timestamp envelope (#371/#372) and the fail-safe coordinator
(#373) are merged. Timeouts ride the *same* `FactoryCacheCoordinator` and reuse the same stale-candidate
detection, throttle restamp, and cancellation-identity classifier. This plan does not touch the envelope
format or the provider stores.

---

## Requirements

Traceable to issue #374 and the epic's cross-cutting quality bar.

- **R1 — Soft timeout, non-lossy.** When fail-safe is on and a stale value exists, a factory exceeding
  `FactorySoftTimeout` returns the stale value immediately; the originating caller never sees an error.
- **R2 — Background completion.** After a soft-timeout stale return, the factory continues running; on
  success it writes the fresh value back (L1+L2 for Hybrid); on failure it is swallowed and logged.
- **R3 — Hard timeout, bounded.** A factory exceeding `FactoryHardTimeout` is cancelled. If a stale value
  exists it is served (fail-safe); otherwise `CacheFactoryTimeoutException` is thrown.
- **R4 — No duplicate factory runs.** While a background factory is in flight for a key, no second factory
  for that key runs concurrently. Stampede protection holds across the background window.
- **R5 — FusionCache-aligned selection.** Soft timeout applies only when `IsFailSafeEnabled` *and* a stale
  candidate exists; otherwise it is inert and the hard timeout (if set) governs. Default for both is
  "no timeout" (`Timeout.InfiniteTimeSpan`), so existing behavior is preserved.
- **R6 — Caller cancellation still wins.** A caller cancelling its own token propagates as cancellation and
  never activates fail-safe or background completion (preserves KTD-7 from #373).
- **R7 — Determinism under test.** The timeout race uses the injected `TimeProvider`, so `FakeTimeProvider`
  drives timeout firing deterministically in unit tests.
- **R8 — Quality bar.** Memory + Redis + Hybrid conformance coverage; docs sync
  (`docs/llms/caching.md` + per-package READMEs); breaking changes documented (greenfield).
- **R9 — Waiters are bounded too (FusionCache parity).** When fail-safe is on and a stale value exists, a
  concurrent caller that cannot acquire the per-key lock within `FactorySoftTimeout` returns the stale value
  rather than blocking behind the in-flight/background-completing factory. Re-entrant same-key calls under
  those conditions get the stale value instead of deadlocking.

---

## Key Technical Decisions

### KTD-1 — Engine-first; providers untouched
All timeout + background-completion logic lives in `FactoryCacheCoordinator`. Providers implement the
unchanged `IFactoryCacheStore` primitive and pass `CacheEntryOptions` through. *Rationale:* the keystone
learnings doc (`docs/solutions/architecture-patterns/caching-fail-safe-coordinator-design.md`, §2) mandates
extending the coordinator, not three providers. Confirmed by repo recon: only the coordinator owns the
read→lock→double-check→factory→upsert state machine.

### KTD-2 — Single effective-timeout selection (FusionCache model)
The coordinator computes **one** effective timeout, not two sequential races:

```
hasFallback = options.IsFailSafeEnabled && staleCandidate is a valid stale reserve
effective =
    soft set AND hasFallback           -> soft   (timed-out factory continues in background)
    else hard set                      -> hard   (timed-out factory is cancelled/abandoned)
    else                               -> infinite (no timeout; today's behavior)
```

*Rationale:* mirrors FusionCache `GetAppropriateFactoryTimeout` (verified against source). Soft without a
fallback is meaningless — there's nothing to return early — so it degrades to the hard ceiling. Keeps the
race to a single `Task.WhenAny`. The soft-vs-hard distinction is known *at selection time*, which is what
lets the coordinator decide cancel-vs-background on timeout.

### KTD-3 — Lock hand-off for background completion (Fork A — decided)
On a soft-timeout stale return, ownership of the `KeyedAsyncLock` releaser transfers to the background
continuation, which disposes it only after writing back. New callers for the same key block on the lock,
then double-check and find the fresh value → **no duplicate factory runs** (R4). *Rationale:* minimal
extension of the existing keyed-lock engine; directly satisfies R4; matches the FusionCache lock-handoff
prior art the issue points at. *Trade-off softened by KTD-11:* a slow/hung background factory pins that key's
semaphore, but under fail-safe + stale, concurrent waiters fall back to the stale value via the
lock-acquisition timeout instead of blocking — the full hot-key fix is still **eager refresh (#375)**.
Rejected alternative (in-flight task registry)
adds a second stampede primitive layered on the lock — more state, more races, more test surface — for
responsiveness #374 doesn't need.

### KTD-4 — Detached background-factory token (Fork C — decided)
The factory runs under an **internal coordinator-owned `CancellationTokenSource` that is NOT linked to the
caller token**. The background factory therefore survives caller cancellation. *Rationale:* background
completion exists to warm the cache for *future* callers; under FusionCache's caller-token parity the
ASP.NET request token cancels the instant the response flushes, so the background factory dies mid-flight
and the cache is rarely refreshed — inert in the dominant host. This is a deliberate divergence from
FusionCache.

**Critical correction (review finding #2):** an *earlier* sketch ran the factory under a CTS **linked to the
caller token**, which would cancel the "detached" background factory the moment the caller's request ended —
silently defeating KTD-4. The factory token must be a standalone internal token, never linked to the caller.
Caller cancellation is observed at the **race level** (KTD-5), not by binding the factory to the caller's
token. The internal token is cancelled on: (a) **hard timeout** (abandon), or (b) **caller cancellation that
arrives *before* the background hand-off** (normal-path cleanup — don't orphan a factory the caller no longer
wants). After hand-off, caller cancellation is ignored and the background factory runs to completion or its
own ceiling. *Bonus:* an OCE from the internal token can never match the caller's token identity, so
`IsCallerCancellation` (KTD-7 from #373) returns false and fail-safe/swallow semantics apply with no
special-casing.

### KTD-4b — Background completion is bounded by a *raced* ceiling (review round 2: adversarial F1)

> **Post-review change (shipped default supersedes the 2 min references below).** `BackgroundFactoryCeiling`
> ships **opt-in**, defaulting to `Timeout.InfiniteTimeSpan` (no ceiling) — matching FusionCache/Caffeine, where
> a detached factory runs to completion unless the operator configures a finite guard. Consequently
> `Timeout.InfiniteTimeSpan` **is** an accepted value (it means "no ceiling"); a *finite* `BackgroundFactoryCeiling`
> must still be `> TimeSpan.Zero`. Wherever this section and §10/§Deferred say "default 2min" or
> "`Timeout.InfiniteTimeSpan` is not an accepted value", read the shipped behavior instead. `CacheEntryOptions`
> XML docs, `docs/llms/caching.md`, and the solution doc reflect the final behavior.

Because the lock is handed off (KTD-3), a background factory that never completes would pin that key's
semaphore — a permanent per-key availability leak. The fix is **not** "cancel the internal token and `await`
the factory": a CPU-bound or otherwise **non-cooperative** factory ignores its token, so `await runningTask`
would block forever and the lock would never release — re-introducing the exact leak. Instead the background
continuation **races** the factory against the ceiling and releases the lock on whichever wins:

```
ceiling = FactoryHardTimeout != Infinite ? FactoryHardTimeout : BackgroundFactoryCeiling   // default 2min, configurable (F2)
winner  = await Task.WhenAny(runningTask, Task.Delay(ceiling, _timeProvider, bgCts.Token))
if (winner == runningTask) { bgCts.Cancel(); /* result path below */ }
else { internalCts.Cancel();   // signal the factory (cooperative ones stop)
       /* ABANDON: do NOT await runningTask; restamp throttle; release lock NOW */ }
```

Two consequences that must be designed together (they are coupled — adversarial F1):
- **Abandon, don't await** on the ceiling branch, so a non-cooperative factory cannot hold the lock past the
  ceiling. The factory may still be running untracked after abandonment (see Risks).
- **Gate the success write on the token.** The result/success path (KTD-7) must write **only when
  `!internalCts.IsCancellationRequested`**. After the ceiling abandons and releases the lock, a *new* caller
  can acquire the key and write a newer value; the abandoned factory, completing later, must **not** overwrite
  it. The lock hand-off prevents factory-vs-factory races *only* for cooperative factories; the token-gate is
  what prevents the post-abandon clobber.

**Precondition (state in docs):** the "no duplicate factory runs" (R4) and "ceiling releases the lock"
guarantees hold cleanly for **cooperative** factories. For non-cooperative factories the lock is still
released at the ceiling, but the abandoned factory keeps running untracked (resource retention) and is
prevented from clobbering only by the token-gate — not by the lock. Background spawning is bounded **per key**
(lock hand-off → one in-flight cooperative factory; new callers block then read fresh — R4) and **per factory
lifetime** (the raced ceiling). It is **not** bounded across distinct keys (see Risks: brownout fan-out).
`BackgroundFactoryCeiling` is a **configurable per-entry option, default 2min** (review round 2: product F2) —
a runaway guard, not a normal-operation cap, so a legitimately-slow factory completes while a stuck one is
abandoned.

### KTD-5 — Timeout race via `Task.WhenAny` + `Task.Delay(TimeProvider)`, caller cancellation at race level
Start the factory under the internal token (KTD-4). Race three things:
`Task.WhenAny(factoryTask, Task.Delay(effective, _timeProvider, delayCts.Token), callerCancellationTask)`
where `callerCancellationTask` completes when the caller's token signals. *Rationale:* using the **injected**
`TimeProvider` for the delay makes timeout firing deterministic under `FakeTimeProvider` (R7) — `Advance(soft)`
fires the timeout without wall-clock sleeps. Outcomes:
- **factory wins** → cancel the delay, dispose the delay/internal CTS, return `Completed`.
- **caller-cancellation wins** (before any hand-off) → cancel the internal token (stop the orphaned factory),
  dispose CTSs, propagate `OperationCanceledException(caller)`.
- **delay wins (timeout)** → if **hard** selected, cancel the internal token (abandon); if **soft** selected,
  leave it running and return `TimedOut(factoryTask)` for the background continuation, **transferring CTS
  ownership** to that continuation (it disposes them — review finding #6).
All `CancellationTokenSource` instances are explicitly disposed on every path; the soft-timeout path transfers
disposal responsibility to the background continuation rather than disposing in the foreground `finally`.

### KTD-6 — Timed-out factory signalled via a synthetic timeout, classified by selection
When the delay wins the race, the helper surfaces a synthetic-timeout outcome (mirrors FusionCache's
`SyntheticTimeoutException` internal signal). The coordinator then branches on which timeout was selected:
- **soft selected** → return stale (`IsStale = true`), hand off lock + still-running factory task to the
  background continuation. Do **not** restamp the throttle on the way out (the background factory is the
  refresh; restamping is redundant).
- **hard selected** → the factory was cancelled. If a stale candidate exists → serve stale (fail-safe
  activation, including best-effort throttle restamp per #373 §3/§6). Else → throw
  `CacheFactoryTimeoutException`.

### KTD-7 — Background success writes fresh (outside any fail-safe catch); failure is best-effort throttled
On background success, write the fresh value with freshly computed logical/physical expirations via
`store.SetEntryAsync(..., CancellationToken.None)` — outside any fail-safe catch (keystone §6: never discard
a freshly-computed value). On background **failure**, swallow + log at Warning, and best-effort restamp the
throttle (reuse `_TryRestampStaleAsync`, `CancellationToken.None`) so a persistently-slow factory doesn't
spawn a fresh background task on every subsequent caller. *Rationale:* the fire-and-forget checklist in
`docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` — never leave a background
task's exception unobserved; never leak the lock if the continuation faults.

### KTD-8 — `CacheFactoryTimeoutException : TimeoutException`, thrown only on the no-fallback hard path
New `[PublicAPI]` exception in `Headless.Caching.Abstractions`, deriving from **`System.TimeoutException`**
(review deferred-question) so it integrates with standard .NET timeout handling/`catch (TimeoutException)`
while still carrying the cache key + elapsed/limit for diagnostics. Thrown only when a hard timeout fires and
no stale value is available. *Rationale:* a typed signal distinct from the factory's own exceptions and from
`OperationCanceledException`. A soft timeout never throws (R1); a hard timeout with a fallback never throws
(serves stale).

### KTD-9 — Two-tier test strategy with a coordinator test seam
Deterministic engine behavior is proven **white-box** in `Headless.Caching.Core.Tests.Unit` against
`FactoryCacheCoordinator` with `FakeTimeProvider` + a `TaskCompletionSource`-gated factory, using an
internal seam (see U4) to await background completion. Cross-provider parity is proven **black-box** in the
conformance harness via a bounded **eventually-poll** (Redis cannot share the in-process `TimeProvider`
seam). *Rationale:* the keystone meta-lesson (#373 §4) — a behavior-changing branch and the test that
exercises it must land together; detached background work needs a deterministic await, not a `Task.Delay`
race.

### KTD-10 — Options validation (location pinned; infinite-safe)
Validation lives at **coordinator entry** (`FactoryCacheCoordinator.GetOrAddAsync`), alongside the existing
`Argument.IsPositive(options.Duration)` and the fail-safe guards — **one home, where the options are consumed**
(review finding #7). `CacheEntryOptions` does not carry its own validator; validation tests therefore live in
`Core.Tests.Unit`, not `Abstractions.Tests.Unit` (finding #7, test-project fix).

Rules, **infinite-safe** (review finding #8 — `Timeout.InfiniteTimeSpan` is `-1ms`, i.e. negative, so a naive
`soft < hard` comparison breaks): treat `Timeout.InfiniteTimeSpan` as "unset / no ceiling" and **skip it
before any positivity or ordering check**. Only *finite* values are validated:
- a finite timeout must be `> TimeSpan.Zero`;
- when **both** are finite, require `soft < hard` (equal/inverted is a config error);
- a finite hard with an infinite soft, or vice-versa, is valid;
- **`BackgroundFactoryCeiling` must be `> TimeSpan.Zero`** (it is never infinite — it is the safety bound;
  `Timeout.InfiniteTimeSpan` is not an accepted value). This guard lives in the background continuation's setup,
  not at coordinator entry, since the ceiling only applies once a soft timeout has spawned a background factory.

*Note:* a soft timeout set without `IsFailSafeEnabled` is **not** an error — it is silently inert per KTD-2
(matches FusionCache). To avoid the "I set a soft timeout and nothing happened" adoption trap (review round 2:
product F4), emit a **one-time Warning** (`CacheSoftTimeoutInert`, deduped per key or once-per-options-shape —
not per call) when a finite `FactorySoftTimeout` is configured with `IsFailSafeEnabled = false`. This keeps
FusionCache-aligned semantics (no throw, no behavior change) while turning a silent dead-end into a
self-correcting nudge.

---

### KTD-11 — Soft timeout also bounds lock *acquisition* (FusionCache parity; review round 2 follow-up)
Soft timeout must bound not only the factory we run, but every **concurrent waiter** for the same key.
Verified against FusionCache `GetAppropriateMemoryLockTimeout`: the soft timeout is applied to **lock
acquisition** too — a caller that can't acquire the per-key lock within the soft timeout returns the stale
value instead of queueing behind the in-flight (possibly background-completing) factory. Without this, our
soft timeout helps exactly **one** caller and every other concurrent caller for that key still serializes on
`_keyedLock.LockAsync` with no timeout — soft timeout would be half-implemented relative to the prior art.

**Rule (mirrors FusionCache):** the lock-acquisition timeout is finite **only** when
`IsFailSafeEnabled && a stale candidate exists && FactorySoftTimeout is finite` — exactly the case where a
stale fallback is available to return. In every other case lock acquisition stays unbounded (today's
behavior, bounded only by the caller token). On lock-acquisition timeout → return the stale value
(`IsStale = true`); the lock holder refreshes the entry. No separate `LockTimeout` option in v1 (the value is
derived from `FactorySoftTimeout`); a configurable `LockTimeout` is deferred.

*Two payoffs for one mechanism:* (a) partially softens the KTD-3 hot-key trade-off — waiters fall back to
stale rather than blocking behind a slow background factory (the full fix is still eager refresh #375); and
(b) gives a **re-entrancy escape hatch** — a factory that re-enters `GetOrAddAsync` for the same key under
fail-safe + stale + finite soft timeout times out acquiring the lock and gets the stale value instead of
self-deadlocking. (Re-entrancy with no stale fallback still deadlocks — the lock timeout is infinite there;
documented as unsupported in U6.)

## High-Level Technical Design

### Timeout selection (decision matrix)

| `IsFailSafeEnabled` | stale candidate? | soft set | hard set | Effective timeout | On timeout |
|---|---|---|---|---|---|
| true | yes | yes | any | **soft** | return stale, factory → background |
| true | yes | no | yes | **hard** | cancel factory, serve stale |
| any | no | yes | yes | **hard** | cancel factory, throw `CacheFactoryTimeoutException` |
| any | no | yes | no | **infinite** | (soft inert, no hard) run unbounded |
| false | n/a | yes | no | **infinite** | (soft inert) run unbounded |
| any | any | no | no | **infinite** | today's behavior |

### Factory execution + background completion (sequence)

```mermaid
sequenceDiagram
    participant C as Caller
    participant K as KeyedAsyncLock
    participant F as Factory
    participant S as IFactoryCacheStore
    participant B as Background continuation

    C->>K: LockAsync(key)  (acquire releaser)
    C->>S: TryGetEntry (double-check)
    Note over C: compute effective timeout (KTD-2)
    C->>F: start factory (internal detached CTS)
    par race (Task.WhenAny)
        F-->>C: completes first
    and
        C->>C: Task.Delay(effective, TimeProvider) wins
    end
    alt factory won
        C->>S: SetEntry(fresh, logical, physical)
        C->>K: releaser.Dispose()
        C-->>C: return fresh
    else soft timeout (stale exists)
        C->>B: hand off (releaser + factory task + internal CTS)
        C-->>C: return STALE (IsStale=true)
        B->>F: WhenAny(factory, Task.Delay(ceiling, TimeProvider))   (race, do not bare-await)
        alt factory wins + token not cancelled
            B->>S: SetEntry(fresh, None)  (gated on !internalCts.IsCancellationRequested)
        else ceiling wins
            B->>F: internalCts.Cancel() + ABANDON (do not await)
            B->>S: best-effort throttle restamp
        end
        B->>K: dispose internal CTS + releaser   (finally; lock released only now)
    else hard timeout
        C->>F: cancel (abandon)
        alt stale exists
            C->>S: throttle restamp (best-effort)
            C->>K: releaser.Dispose()
            C-->>C: return STALE
        else no fallback
            C->>K: releaser.Dispose()
            C-->>C: throw CacheFactoryTimeoutException
        end
    end
```

The single trickiest invariant: the releaser must be disposed **exactly once on exactly one path**. The
synchronous path disposes it on every exit *except* the soft-timeout hand-off, where ownership moves to the
background continuation. Implement as explicit ownership transfer (a local "owned" flag / try-finally), not
a `using`.

---

## Implementation Units

### U1. Add timeout options + `CacheFactoryTimeoutException`

**Goal:** Surface the two timeout knobs and the typed timeout exception on the public contract.
**Requirements:** R5, R8, KTD-2, KTD-8, KTD-10.
**Dependencies:** none.
**Files:**
- `src/Headless.Caching.Abstractions/Contracts/CacheEntryOptions.cs` (modify — add `FactorySoftTimeout`,
  `FactoryHardTimeout`, `BackgroundFactoryCeiling`, defaults)
- `src/Headless.Caching.Abstractions/CacheFactoryTimeoutException.cs` (create)
- `tests/Headless.Caching.Core.Tests.Unit/FactoryCacheCoordinatorTests.cs` (extend — validation lives at
  coordinator entry per KTD-10, so its tests live here, **not** in `Abstractions.Tests.Unit`)

**Approach:** Add `TimeSpan FactorySoftTimeout` / `TimeSpan FactoryHardTimeout`, both defaulting to
`Timeout.InfiniteTimeSpan`. Add **`TimeSpan BackgroundFactoryCeiling`** (review round 2: product F2 — shipped
configurable in v1) defaulting to a **runaway-guard** value `DefaultBackgroundFactoryCeiling = 2min` (generous
enough that only a genuinely-stuck factory hits it, not a normally-slow one). **No validator type on the
options** — validation is inline `Argument.*` guards at coordinator entry (KTD-10), matching the existing
`Argument.IsPositive(options.Duration)`. The guards are infinite-safe: skip `Timeout.InfiniteTimeSpan` before
positivity/ordering checks. `CacheFactoryTimeoutException` is `[PublicAPI]`, derives from
**`System.TimeoutException`** (KTD-8), carries the key + elapsed/limit.

**Patterns to follow:** `CacheEntryOptions` fail-safe fields + their defaults; `Argument.*` guard usage in
`FactoryCacheCoordinator.GetOrAddAsync`. File header copyright line.

**Test suite design:** Options-default assertions are trivial; the validation *behavior* is exercised through
the coordinator in `Core.Tests.Unit` (where the guards live). No provider involvement.

**Test scenarios:**
- Default options expose `Timeout.InfiniteTimeSpan` for both timeouts (no behavior change).
- `FactorySoftTimeout = 0` or negative-finite → coordinator throws at entry.
- `FactoryHardTimeout` finite and `<= FactorySoftTimeout` → throws (inverted/equal).
- **`FactoryHardTimeout = Timeout.InfiniteTimeSpan` with a finite soft → valid, no throw** (infinite-safe, #8).
- Soft finite, hard unset (infinite) → valid (hard inert).
- Soft finite, `IsFailSafeEnabled = false` → valid, **no throw** (inert per KTD-2/KTD-10).
- **`BackgroundFactoryCeiling` default is 2min**; a zero/negative/infinite value → throws (it is the safety
  bound, never infinite — F2).
- `CacheFactoryTimeoutException` is `catch (TimeoutException)`-compatible; carries the key + limit; is `[PublicAPI]`.

**Verification:** New validation + exception tests pass; default-options round-trip unchanged.

---

### U2. Foreground timeout-race method in the coordinator

**Goal:** Race a factory against the effective timeout using the injected `TimeProvider`, and expose the
still-running factory task on timeout — as a **private method on `FactoryCacheCoordinator`**, not a separate
class (review round 2: scope F3 — single call site; the coordinator is already the white-box test target).
**Requirements:** R3, R5, R7, KTD-5, KTD-6.
**Dependencies:** U1.
**Files:**
- `src/Headless.Caching.Core/FactoryCacheCoordinator.cs` (modify — add the private `_RunFactoryWithTimeoutAsync`
  method + its small private result type; **no separate `FactoryTimeoutRunner` class/file**)
- (tests fold into U4's `FactoryCacheCoordinatorTests.cs` — no separate `FactoryTimeoutRunnerTests.cs`)

**Approach:** A private method shaped like
`_RunFactoryWithTimeoutAsync<T>(Func<CancellationToken,ValueTask<T?>> factory, TimeSpan effective, bool cancelOnTimeout, TimeProvider, CancellationToken caller)`
returning a discriminated result: `(Completed value)` | `(TimedOut runningTask, internalCts)`. The factory
runs under an **internal CTS that is NOT linked to the caller** (KTD-4); caller cancellation is handled at the
race level, never by binding the factory to the caller token. `Timeout.InfiniteTimeSpan` does **not**
short-circuit to a bare `await caller` — even with no timeout the caller-cancellation-at-race-level + internal
token still apply so the contract is uniform. **Every CTS is disposed** (review finding #6); on a soft timeout
the still-running factory's CTS ownership is *returned to the caller* (the coordinator hands it to the
background continuation, which disposes it), so it is **not** disposed in this method's `finally`. Frame this
design as directional — exact result shape is the implementer's call.

**Technical design (directional):**
```
internalCts = new CTS();                          // NOT linked to caller (KTD-4)
delayCts    = new CTS();
factoryTask = factory(internalCts.Token).AsTask();
delayTask   = (effective == Timeout.InfiniteTimeSpan)
                ? InfiniteDelay(delayCts.Token)
                : Task.Delay(effective, timeProvider, delayCts.Token);
cancelTask  = caller.WhenCanceled();              // completes if caller signals
winner = await Task.WhenAny(factoryTask, delayTask, cancelTask);

if (winner == factoryTask) { delayCts.Cancel(); dispose(delayCts, internalCts); return Completed(await factoryTask); }
if (winner == cancelTask)  { internalCts.Cancel(); dispose(delayCts, internalCts); throw new OperationCanceledException(caller); }
// timeout:
if (cancelOnTimeout)       { internalCts.Cancel(); dispose(delayCts, internalCts); return TimedOut(factoryTask, ownedCts: null); }  // hard: abandon
delayCts.Cancel(); dispose(delayCts);             // soft: factory keeps running; hand internalCts to caller
return TimedOut(factoryTask, ownedCts: internalCts);
```

**No built-in `WhenCanceled` (review round 2: feasibility F10/F11).** `caller.WhenCanceled()` in the sketch is
**not** a BCL/Headless primitive — build it from a `TaskCompletionSource` + `caller.Register(...)` (mirror the
existing `_WithCancellationSlow` idiom in `src/Headless.Extensions/Threading/TaskExtensions.cs`). The
`CancellationTokenRegistration` it returns **must be disposed on every exit path** (factory-win, caller-cancel,
hard timeout, soft timeout) — the caller token typically outlives the call (a request token), so an
un-disposed registration roots the TCS + closure on it until the request ends, accumulating per call under
load. Extend the "every CTS disposed" discipline to cover this registration (it is not a CTS the helper owns,
so it's easy to miss).

**Patterns to follow:** `_GetUtcNow()` already reads `_timeProvider`; reuse the same injected provider.
ValueTask→Task materialization only where a task handle must outlive the call (background completion).

**Test suite design:** White-box unit in `Core.Tests.Unit`, in U4's `FactoryCacheCoordinatorTests.cs` (the
method is private; exercise it through the coordinator with `FakeTimeProvider` + TCS-gated factory). No
separate helper test file.

**Test scenarios (implemented in U4's file):**
- Factory completes before timeout → `Completed(value)`; delay + internal CTS disposed (no timer/CTS leak, #6).
- `FakeTimeProvider.Advance(effective)` with factory pending + `cancelOnTimeout=true` → internal token signals,
  CTSs disposed, `TimedOut(ownedCts: null)`.
- Same with `cancelOnTimeout=false` (soft) → factory keeps running, `TimedOut(ownedCts: internalCts)`; the
  returned CTS is *not yet* disposed (caller owns it).
- **Caller cancels its token mid-wait → `OperationCanceledException(caller)`; the internal factory token is
  cancelled (factory not orphaned); CTSs disposed.** Confirms KTD-4 detachment is at race level, not binding.
- `Timeout.InfiniteTimeSpan` → never times out; caller cancellation still wins the race.
- Factory throws before timeout → exception surfaces from `Completed` await (caller classifies it); CTSs disposed.

**Verification:** Helper tests pass deterministically with `FakeTimeProvider` (no `Thread.Sleep`/wall-clock);
no CTS/timer leaks (assert via disposal hooks or that repeated runs don't accumulate registrations).

---

### U3. Wire timeouts + lock hand-off + background completion into the coordinator

**Goal:** The heart — integrate effective-timeout selection, the soft/hard branches, lock hand-off, and
detached background completion into `GetOrAddAsync`.
**Requirements:** R1, R2, R3, R4, R5, R6, R9, KTD-2..KTD-7, KTD-11.
**Dependencies:** U1, U2, U7.
**Files:**
- `src/Headless.Caching.Core/FactoryCacheCoordinator.cs` (modify — selection, branches, background
  continuation, lock ownership transfer, lock-acquisition timeout, log events)
**Execution note:** Land each new branch (soft return, hard-cancel-serve-stale, hard-cancel-throw,
background success, background failure) **together with its test** (U4) — the #373 regression shipped
because a new branch landed without a test exercising it.

**Approach:**
0. **Lock-acquisition timeout (KTD-11/R9).** Before acquiring the keyed lock, compute the lock timeout from
   the first-read stale candidate: `lockTimeout = (IsFailSafeEnabled && staleCandidate valid && soft finite)
   ? FactorySoftTimeout : Timeout.InfiniteTimeSpan`. Acquire via the U7 overload
   `_keyedLock.LockAsync(key, lockTimeout, _timeProvider, ct)`. If it returns **null** (timed out) → return
   the stale value (`IsStale = true`) without running the factory; the in-flight holder refreshes. Only when
   acquired does the double-check + factory path run.
1. After the post-lock double-check + stale-candidate computation, compute `hasFallback` and the effective
   timeout (KTD-2).
2. Replace the bare `value = await factory(ct)` with the U2 helper, `cancelOnTimeout = (selected == hard)`.
3. On `Completed` → existing fresh-write path (unchanged: write fresh, return fresh).
4. On `TimedOut` + **soft** selected → return stale (`IsStale = true`); transfer the releaser + the running
   factory task into a background continuation (KTD-3/KTD-4/KTD-7). Suppress the throttle restamp on this
   exit.
5. On `TimedOut` + **hard** selected → factory already cancelled; if `hasFallback` → fail-safe activation
   (throttle restamp + return stale); else → throw `CacheFactoryTimeoutException`.
6. Background continuation: **race** the factory against the ceiling (KTD-4b) —
   `Task.WhenAny(runningTask, Task.Delay(ceiling, _timeProvider, bgCts.Token))` — do **not** bare-`await` it.
   - **factory wins** → cancel `bgCts`; if `!internalCts.IsCancellationRequested` and it succeeded → fresh
     write (outside fail-safe catch); on factory throw → swallow+log + best-effort throttle restamp.
   - **ceiling wins** → `internalCts.Cancel()` (signal cooperative factories), **abandon** the still-running
     task (do not await it), best-effort throttle restamp, release the lock now.
   - **success-write token-gate (KTD-4b):** the fresh write fires only when `!internalCts.IsCancellationRequested`,
     so an abandoned factory completing after the ceiling cannot clobber a newer value.
   The best-effort restamp and fresh write run **inside the try**; the **`finally` disposes the owned CTS
   (handed off from U2) AND the releaser** — exactly once, here, since the foreground surrendered both. The
   restamp must not be able to hang the `finally`: it already uses `CancellationToken.None`; bound it (or
   observe-don't-await it) so a stuck store call cannot delay lock release past the ceiling (adversarial F12).
   Observe the task (no unawaited fire-and-forget; the continuation is the observation point and logs faults).
7. Restructure the `using (await _keyedLock.LockAsync(...))` into explicit acquire + ownership flag so the
   releaser disposes exactly once on the correct path.
8. Add `[LoggerMessage]` events (bottom of file, per convention): `CacheFactoryTimedOut` (soft/hard,
   key, limit), `CacheBackgroundCompletionSucceeded`, `CacheBackgroundCompletionFailed`, and
   `CacheSoftTimeoutInert` (Warning, deduped — soft timeout set with fail-safe off; KTD-10/F4).

**Patterns to follow:** existing `_TryRestampStaleAsync` (best-effort, `CancellationToken.None`, swallow);
`IsCallerCancellation` (KTD-7 token identity + `CanBeCanceled` guard); `_Max`/expiry computation for the
fresh write; the `FactoryCacheCoordinatorLog` partial class at file bottom (memory: log classes go at the
bottom).

**Test suite design:** Behavior proven in U4 (white-box) + U5 (cross-provider). This unit ships no tests of
its own beyond what U4 adds.

**Test scenarios:** (enumerated under U4 — they target this unit's branches)

**Verification:** All U4 + U5 scenarios pass; no `using`-scope double-dispose; `make format-check` +
warnings-as-errors clean.

---

### U4. Coordinator white-box tests + background-completion seam

**Goal:** Deterministically prove every new branch against `FactoryCacheCoordinator`, including detached
background completion.
**Requirements:** R1, R2, R3, R4, R6, R7, KTD-9.
**Dependencies:** U3.
**Files:**
- `src/Headless.Caching.Core/FactoryCacheCoordinator.cs` (modify — add an internal test seam to signal
  background-completion finished AND background-ceiling-timer-registered; gated by `InternalsVisibleTo`)
- `src/Headless.Caching.Core/Headless.Caching.Core.csproj` (modify — add
  `<InternalsVisibleTo Include="Headless.Caching.Core.Tests.Unit" />`; none exists today — review round 2:
  feasibility F9)
- `tests/Headless.Caching.Core.Tests.Unit/FactoryCacheCoordinatorTests.cs` (extend)
- `tests/Headless.Caching.Core.Tests.Unit/FakeFactoryCacheStore.cs` (extend if a blocking/gated factory
  helper is needed)

**Approach:** Add a minimal internal seam — e.g. an `internal event Action? BackgroundCompletionFinished`
or an injectable `internal Action<Task>? _onBackgroundCompletion` — so a test can await the detached task
deterministically instead of polling. Keep the seam internal and `[EditorBrowsable(Never)]`; it must not
leak into the public contract. Use a `TaskCompletionSource`-gated factory to hold the factory open, advance
`FakeTimeProvider` past the soft/hard timeout, assert the synchronous return, then release the gate and
await the seam.

**Ceiling-timer registration barrier (review round 2: adversarial F7).** The ceiling `Task.Delay` is created
*inside* the detached background continuation, which is scheduled asynchronously after the synchronous stale
return. If the test calls `FakeTimeProvider.Advance(ceiling)` **before** that continuation has registered its
timer, the advance is a no-op for it and the ceiling never fires → the test hangs/flakes. The seam must
therefore expose a **second** signal — "ceiling timer registered / continuation reached its race" — that the
test awaits **before** `Advance(ceiling)`. `BackgroundCompletionFinished` alone is insufficient; it only
fires *after* completion. Without the registration barrier the determinism goal (R7/KTD-9) is reintroduced
one level deeper.

**Patterns to follow:** existing `FactoryCacheCoordinatorTests` given/when/then shape with `FakeTimeProvider`
+ `FakeFactoryCacheStore`; `should_serve_stale_when_factory_throws_within_physical_window` as the closest
sibling.

**Test scenarios:**
- **Soft, stale exists, fail-safe on:** factory gated open; `Advance(soft)` → returns stale (`IsStale=true`)
  without `SetEntry`; release gate → background completes → store now holds the fresh value (await seam).
- **Soft success before timeout:** factory completes < soft → fresh returned, single `SetEntry`, no
  background task.
- **No duplicate factory runs (R4):** during the background window a second `GetOrAddAsync(key)` blocks on
  the lock; after background write it returns the **fresh** value; factory invocation count == 1.
- **Background failure:** gated factory throws after the soft return → stale already returned;
  background swallows+logs; throttle restamped (next call within throttle reads as a hit, factory not
  re-run); `BackgroundCompletionFailed` logged.
- **Hard, no fallback:** cold cache, factory gated; `Advance(hard)` → factory cancelled →
  `CacheFactoryTimeoutException` thrown; no stale returned.
- **Hard, stale exists:** `Advance(hard)` → factory cancelled → stale served (`IsStale=true`) + throttle
  restamped (fail-safe activation).
- **Soft inert without fail-safe:** soft set, `IsFailSafeEnabled=false`, factory gated; `Advance(soft)` does
  **not** return early — only the hard ceiling (or infinite) governs.
- **Soft inert without stale:** soft set, fail-safe on, cold cache → soft ignored; hard (if set) governs.
- **Caller cancellation (R6):** caller cancels its token mid-factory → `OperationCanceledException`
  propagates; fail-safe/background **not** activated.
- **Detached token (KTD-4):** caller token cancels right after the soft return → background factory (running
  under the internal token, not linked to the caller) still completes and writes fresh.
- **Background ceiling releases the lock (KTD-4b):** await the ceiling-timer-registered barrier, then
  `Advance(ceiling)`; assert the lock is released (a fresh `LockAsync(key)` acquires) and the entry was
  best-effort throttle-restamped, not left to pin the semaphore.
- **Non-cooperative factory still releases the lock (adversarial F1):** a factory that **ignores** its token
  (never observes cancellation); at the ceiling the continuation abandons it (does not await) and releases the
  lock — assert a fresh `LockAsync(key)` acquires even though the factory task is still running.
- **Post-abandon write is token-gated (adversarial F1):** after the ceiling abandons factory A and a second
  caller runs factory B that writes value `B`, release factory A to complete with value `A`; assert the store
  still holds `B` (A's write was suppressed by `!internalCts.IsCancellationRequested`), not clobbered to `A`.
- **CTS disposed:** assert no `CancellationTokenSource`/timer leak across repeated soft-timeout cycles
  (disposal hook count or no accumulation).
- **Lock-acquisition timeout returns stale (KTD-11/R9):** holder A is inside the lock running a gated factory;
  caller B (fail-safe on, stale present, finite soft) cannot acquire within the soft timeout → `Advance(soft)`
  → B returns the stale value (`IsStale = true`) **without** invoking its factory (factory-call count for B is
  0); A is undisturbed.
- **Lock timeout inert without fallback:** caller B with no stale candidate (cold) or fail-safe off → lock
  acquisition is unbounded; B waits for A (bounded only by its own token), no early null-return.
- **Re-entrancy escape hatch (KTD-11):** a factory that calls `GetOrAddAsync` for the **same key** under
  fail-safe + stale + finite soft → the inner call times out acquiring the lock and returns stale instead of
  deadlocking; assert the outer factory completes with the inner stale value (no hang).
- **Releaser disposed once:** assert the key's semaphore is fully released (a fresh `LockAsync` acquires
  immediately) after each exit path.

**Verification:** All scenarios green deterministically; no wall-clock sleeps; lock never leaked.

---

### U5. Cross-provider conformance (Memory + Redis + Hybrid)

**Goal:** Prove the observable timeout/background contract holds identically across all three providers.
**Requirements:** R1, R2, R3, R4, R8, R9, KTD-9.
**Dependencies:** U3.
**Files:**
- `tests/Headless.Caching.Tests.Harness/CacheConformanceTestsBase.cs` (extend — new virtual scenarios)
- `tests/Headless.Caching.InMemory.Tests.Unit/*` (override/enable new scenarios; Memory drives
  `FakeTimeProvider` via the existing `AdvanceAsync`/`AdvancePastExpirationAsync` seam)
- `tests/Headless.Caching.Redis.Tests.Integration/*` (enable scenarios; real TTL + bounded poll)
- `tests/Headless.Caching.Hybrid.Tests.Unit/*` (enable scenarios; assert L1+L2 both refreshed by background)

**Approach:** Add conformance scenarios that go through the public `ICache.GetOrAddAsync`. Because the
detached background task cannot share an in-process `TimeProvider` seam on Redis, assert background results
with a **bounded eventually-poll** helper (poll `GetAsync` until fresh or a generous deadline, then assert).
For Memory/Hybrid, the harness already advances a `FakeTimeProvider`; for Redis, use short real timeouts +
poll. Hybrid additionally asserts the background write lands in **both** L1 and L2 (mirrors the fail-safe
Hybrid tests' L1/L2 assertions).

**Patterns to follow:** existing fail-safe conformance scenarios
(`should_serve_stale_when_failsafe_factory_throws_within_window`, `should_throttle_failsafe_factory_retries`)
and the `AdvanceAsync` override seam; `HybridCacheFailSafeTests` L1/L2 assertion style; add an "eventually"
poll helper if none exists in `TestBase`.

**Test suite design:** Harness base owns the portable scenarios; Memory/Hybrid run them as unit, Redis as
integration (Testcontainers, Docker). No new harness *package* needed — extend the existing
`Headless.Caching.Tests.Harness`.

**Test scenarios (per provider):**
- Soft timeout returns stale; after background completion a subsequent read returns fresh (poll).
- No duplicate factory runs: concurrent callers during the background window → factory count == 1, all see
  fresh after.
- Hard timeout, cold cache → `CacheFactoryTimeoutException`.
- Hard timeout, stale present → stale served.
- **Lock-acquisition timeout (R9/KTD-11):** two concurrent callers, fail-safe + stale + finite soft; the
  second returns stale within the soft window instead of blocking for the full factory duration (poll/timing
  per provider).
- Hybrid only: background completion refreshes both L1 and L2.

**Verification:** Conformance green on Memory + Hybrid (unit) and Redis (integration, Docker); `make
test-integration` passes for the Redis project.

---

### U6. Docs + learnings sync

**Goal:** Keep the agent-facing doc surfaces and institutional learnings in lockstep with the new contract.
**Requirements:** R8.
**Dependencies:** U3.
**Files:**
- `docs/llms/caching.md` (modify — timeout options, soft/hard semantics, background completion, the
  detached-token divergence from FusionCache, the soft-requires-fail-safe rule)
- `src/Headless.Caching.Abstractions/README.md` (modify — options surface)
- `src/Headless.Caching.Core/README.md` (modify — coordinator behavior)
- `src/Headless.Caching.Hybrid/README.md`, `src/Headless.Caching.Redis/README.md`,
  `src/Headless.Caching.InMemory/README.md` (modify if provider-visible behavior notes apply)
- `docs/solutions/architecture-patterns/caching-fail-safe-coordinator-design.md` (modify — add a timeout +
  background-completion section, or note to spin a sibling learnings doc via `/x-compound` after merge)

**Approach:** Follow `docs/authoring/AUTHORING.md` (read it first). Explain the *why* — why soft requires a
fallback, why the background token is detached (and how that diverges from FusionCache and why), the lock
hand-off + background ceiling and the hot-key trade-off, and the relationship to eager refresh (#375).
**Preconditions to document (factories used with soft timeout + background completion):**
- **Request-scoped disposal (round 1 #5):** the factory must not capture request-scoped disposables (scoped
  `DbContext`, `HttpContext`) — the background run can outlive the request; resolve a fresh scope instead.
- **Re-entrancy is conditionally supported (round 2: adversarial F8 + KTD-11):** a factory that calls
  `GetOrAddAsync` for the **same key** uses the non-reentrant per-key semaphore. **With** fail-safe + a stale
  value + a finite soft timeout, the inner call times out acquiring the lock and returns the stale value
  (KTD-11) — no deadlock. **Without** that combination the lock acquisition is unbounded and the inner call
  **self-deadlocks** (recoverable only by the caller token in the foreground, or the ceiling in the
  background). Document re-entrant same-key factories as supported only under fail-safe + stale; otherwise
  unsupported.
- **Cooperative cancellation (round 2: adversarial F1):** non-cooperative (token-ignoring) factories still get
  their lock released at the ceiling, but keep running untracked and rely on the token-gate (not the lock) to
  avoid clobbering a newer write. Cooperative factories get the clean R4 guarantee.

**Differences from FusionCache (round 2: product F5) — add a dedicated subsection** for developers porting a
FusionCache config/mental-model, listing each divergence as `FusionCache behavior → Headless behavior →
user-visible consequence`: (1) **background token** — FusionCache keeps the caller token; Headless detaches
(survives request end, but must not capture request-scoped state); (2) **hard timeout** — FusionCache can
background-complete on hard timeout; Headless **abandons** (cancels) the factory. This is inbound-adoption
friction, not an upgrade-path concern (greenfield), so it belongs in the docs as a porting note, not buried in
KTD rationale.

Also document the **clobbering limitation** (round 1 #10): a slow background write can overwrite a concurrent
explicit `Set`/`Upsert`/`Remove`; versioned write-back is deferred. Document the breaking change to
`CacheEntryOptions` (greenfield — fields additive, but the engine path changed). **Naming note (round 1 #11):**
these docs/tests use "Memory" as shorthand for the `Headless.Caching.InMemory` provider (matching the keystone
learnings doc); state this once so the shorthand isn't read as a separate provider.

**Test suite design:** Docs — no automated tests. `Test expectation: none -- documentation unit.`

**Verification:** `docs/llms/caching.md` and the per-package READMEs describe the new options and semantics;
drift checks in `AUTHORING.md` pass; no stale claims about "factory always runs to completion."

---

### U7. `KeyedAsyncLock` timeout-bounded acquisition (FusionCache-parity lock timeout)

**Goal:** Give `KeyedAsyncLock` a deterministic, timeout-bounded acquisition overload so the coordinator can
bound waiters by the soft timeout (KTD-11). *Dependency of U3* (placed last in the doc; U-IDs are stable, not
ordered).
**Requirements:** R9, KTD-11.
**Dependencies:** none (extends the existing primitive).
**Files:**
- `src/Headless.Extensions/Threading/KeyedAsyncLock.cs` (modify — add a `TimeProvider`-aware,
  timeout-bounded `LockAsync` overload returning `IDisposable?` (null on timeout))
- `tests/Headless.Extensions.Tests.Unit/Threading/KeyedAsyncLockTests.cs` (extend — find/confirm the existing
  test file; add timeout scenarios)

**Approach:** Add `Task<IDisposable?> LockAsync(string key, TimeSpan timeout, TimeProvider timeProvider,
CancellationToken)`. `SemaphoreSlim.WaitAsync` has no `TimeProvider` overload, so for **deterministic tests**
race the wait against the injected clock rather than using the wall-clock `WaitAsync(TimeSpan)`:
`Task.WhenAny(semaphore.WaitAsync(linked.Token), Task.Delay(timeout, timeProvider, linked.Token))`. On
**acquired** → return a `Releaser` (existing type). On **timeout** → **decrement the refcount** (do not leave
the semaphore acquired or the ref dangling — mirror the existing `_DecrementRefCount` on the `WaitAsync`-throws
path) and return `null`. `Timeout.InfiniteTimeSpan` delegates to the existing unbounded overload (no race,
no behavior change). The atomic acquire/timeout lives **inside** `KeyedAsyncLock` (it owns the semaphore
lifecycle) — do **not** race at the coordinator call site, which would leak a lock the pending `WaitAsync`
later acquires.

**Patterns to follow:** the existing `LockAsync` / `Releaser` / `_GetOrCreate` / `_DecrementRefCount` /
`_Release` ref-counting in `KeyedAsyncLock.cs`; `[MustDisposeResource]` on the non-null return. The
`Task.Delay(timeout, timeProvider, ct)` determinism pattern from U2.

**Test suite design:** Unit in `Headless.Extensions.Tests.Unit` with `FakeTimeProvider`. This is a
general-utility change, tested in its own project — independent of the cache.

**Test scenarios:**
- Uncontended key → acquires immediately; returns non-null releaser; refcount/semaphore released on dispose.
- Contended key, holder releases before timeout → second caller acquires (non-null).
- Contended key, `FakeTimeProvider.Advance(timeout)` before release → second caller gets **null**; assert the
  refcount was decremented (a later uncontended `LockAsync(key)` still works; no leaked ref/semaphore).
- `Timeout.InfiniteTimeSpan` → delegates to the unbounded overload (waits until released or token-cancelled).
- Caller cancels its token while waiting → `OperationCanceledException` (not a null return); refcount
  decremented.
- No semaphore/CTS leak across repeated timed acquisitions on the same key.

**Verification:** New timeout-overload tests pass deterministically with `FakeTimeProvider`; the existing
unbounded `LockAsync` behavior and ref-count invariants are unchanged.

---

## Scope Boundaries

**In scope:** soft/hard timeout options + validation; the typed timeout exception; effective-timeout
selection; the timeout race; lock hand-off; detached background completion (success + failure handling);
cross-provider conformance; docs.

**Out of scope (true non-goals):**
- Distributed/cross-node coordination of background completion — the engine stays best-effort single-node
  (consistent with `redlock-multi-instance-not-adopted-2026-05-19.md`).
- Changing the entry-envelope frame format or `IFactoryCacheStore` contract.

### Deferred to Follow-Up Work
- **Eager (proactive) refresh (#375)** — the real fix for "hot key blocks on a slow background factory"
  (KTD-3 trade-off). Explicitly the next edge on the same engine. *Sequencing rationale (review round 2:
  product F13):* timeouts ship before eager refresh because timeouts are the primitive #375 extends, and the
  KTD-4b ceiling bounds the interim lock-pin — so this order is a deliberate "resilience-primitive first,
  hot-key optimization second" bet, not a deferral of the real fix.
- **Configurable `LockTimeout`** — v1 derives the lock-acquisition timeout from `FactorySoftTimeout`
  (KTD-11). Exposing a separate per-entry `LockTimeout` option (as FusionCache does) is deferred; the derived
  value covers the parity behavior.
- **Global background-completion concurrency cap** — a `SemaphoreSlim` admission gate bounding total
  in-flight background factories across keys (mitigates the brownout fan-out in Risks). Deferred to the #375
  follow-up; v1 bounds per-key + per-factory only.
- *(Resolved — now in v1)* `BackgroundFactoryCeiling` ships as a configurable per-entry option (default 2min)
  per review round 2 / product F2. No longer deferred.
- **`AllowTimedOutFactoryBackgroundCompletion` toggle** — v1 makes soft-timeout background completion
  intrinsic (issue framing: soft → background, hard → abandon). A per-entry opt-out can be added later if a
  consumer needs it.
- **OTel meters/traces + public events for timeout/background activation** — belongs to M4 (#384/#385);
  this plan emits structured logs only.

---

## Risks & Dependencies

- **Request-scoped dependency disposal in the background (review finding #5 — HIGH).** A detached background
  factory (KTD-4) outlives the originating request, so a factory closure that captures a **request-scoped
  disposable** (e.g. a scoped `DbContext`, an `HttpContext`, a scoped `HttpClient`) will hit
  `ObjectDisposedException` when the scope is torn down mid-flight. FusionCache has the identical hazard.
  *Mitigation:* document loudly in U6 — soft-timeout + background-completion factories must **not** capture
  request-scoped disposables; they should resolve their own scope (`IServiceScopeFactory`) or use singleton/
  transient-safe dependencies. This is guidance, not something the engine can enforce; the docs must call it
  out as a precondition of enabling soft timeouts.
- **Long background write-back clobbering a newer value (review finding #10 — MEDIUM).** The lock hand-off
  prevents *factory* writes from racing, but explicit `SetAsync`/`UpsertAsync`/`RemoveAsync` (and tag
  invalidation, M2) **bypass the coordinator lock** entirely. A slow background factory that started at T can
  overwrite a newer explicit write that landed at T+Δ. The entry envelope (#371) has no version/etag slot, so
  compare-and-swap write-back is out of scope here. *Mitigation:* document as a known limitation; **defer
  versioned/CAS background write-back** to a follow-up (ties into eager #375 / adaptive #376). Accept the
  small window for v1 — background completion is a best-effort refresh, not a linearizable write.
- **Hung factory pins the per-key lock (round 1 #1/#3; corrected round 2: adversarial F1).** Addressed by the
  **raced** background ceiling (KTD-4b): the continuation races the factory against a ceiling delay and
  **abandons** (does not await) the factory on the ceiling branch, releasing the lock even for a
  token-ignoring factory. *Mitigation:* KTD-4b + U4's "background ceiling releases the lock" scenario. Waiting
  callers are **not** deadlocked — they block on `LockAsync(key, theirToken)` and can cancel via their own
  token.
- **Post-abandon factory-vs-factory clobber (round 2: adversarial F1, coupled).** After the ceiling abandons a
  non-cooperative factory and releases the lock, a new caller can run a second factory and write; the
  abandoned factory completing later must not overwrite it. *Mitigation:* the background success write is gated
  on `!internalCts.IsCancellationRequested` (KTD-4b) — the lock alone does not prevent this; the token-gate
  does. This extends round 1's clobber risk (which covered only explicit `Set`/`Upsert`/`Remove`).
- **Cross-key background fan-out under a dependency brownout (round 2: adversarial F6).** The "bounded"
  framing in KTD-4b is **per key**; there is no cap on the number of *distinct* keys with a live background
  factory. Under a dependency-wide brownout, every hot key soft-times-out and spawns its own detached factory
  + pinned semaphore + captured store for up to the ceiling — bounded only by key cardinality, an
  amplification (retry-storm-shaped) load against an already-failing dependency. The detached tasks *await*
  (continuations, not blocked threads), so thread-pool starvation is unlikely, but outbound call volume is
  not bounded. *Mitigation (v1):* none beyond the per-factory ceiling; a **global concurrency cap** on
  background completions (a `SemaphoreSlim` admission gate) is a candidate for the #375 follow-up. Documented,
  not solved here — the plan must not claim background spawning is globally bounded.
- **Background-task lifecycle bugs** (fire-and-forget): unobserved exceptions, leaked locks/CTSs on
  continuation faults. *Mitigation:* KTD-7 + the concurrency learnings checklist; U4's "releaser disposed
  once" + "CTS disposed" + "background failure" scenarios; the continuation is the observation point.
- **Lock-ownership transfer correctness:** the releaser must dispose exactly once across the exit paths.
  *Mitigation:* explicit ownership flag (not `using`); a dedicated U4 assertion per path.
- **Cross-provider timing flakiness on Redis:** no shared `TimeProvider` seam. *Mitigation:* KTD-9 bounded
  eventually-poll + short real timeouts; never assert background results synchronously on Redis.
- **Cancellation misclassification:** a hard-timeout OCE must activate fail-safe, not propagate as caller
  cancellation. *Mitigation:* the U2 helper cancels an *internal* token (not the caller's), so
  `IsCallerCancellation` returns false by identity — covered by U4's caller-cancellation + hard-timeout
  scenarios.
- **Hybrid L1/L2 split-write on background failure (residual).** If the background fresh write succeeds on L1
  but fails on L2 (Redis down), L1 and L2 diverge until the next write. *Mitigation:* this mirrors the
  existing fail-safe write path's posture; covered by the Hybrid background scenario asserting both tiers, and
  bounded by the backplane auto-recovery work (M4 #386). Documented, not separately solved here.
- **Dependencies:** #371/#372/#373 (merged). No new NuGet packages.

---

## Sources & Research

- **FusionCache prior art (explicit request):** `GetAppropriateFactoryTimeout` confirms soft fires only
  under `IsFailSafeEnabled && hasFallbackValue` (default both `Timeout.InfiniteTimeSpan`);
  `RunUtils.RunAsyncFuncWithTimeoutAsync` = `Task.WhenAny` + dual CTS;
  `AllowTimedOutFactoryBackgroundCompletion` default `true`; background success writes L1 then L2,
  background failure logged + swallowed; lock handed off to the background task. **Deliberate divergences:**
  (1) detached background token vs FusionCache's caller token (KTD-4); (2) hard timeout cancels the factory
  ("abandon" per #374) rather than allowing background completion on both.
  Source: github.com/ZiggyCreatures/FusionCache — `docs/Timeouts.md`, `docs/FailSafe.md`,
  `FusionCacheEntryOptions.cs`, `Internals/RunUtils.cs`, `FusionCache_Async.cs`.
- **FusionCache round-2 verification (source-confirmed):** (a) **No background ceiling** — once a factory goes
  to background it runs under the caller's *original* token with no secondary bound; with `None` the per-key
  `SemaphoreSlim` is pinned indefinitely ("no safety valve"). This *justifies* our KTD-4b ceiling as the price
  of our token detachment (KTD-4). (b) **Lock-acquisition timeout** — `GetAppropriateMemoryLockTimeout` applies
  `FactorySoftTimeout` to lock acquisition (when fail-safe + stale + finite soft), so waiters fall back to
  stale instead of blocking; we adopt this (KTD-11/U7/R9). (c) **Re-entrancy** — default config deadlocks on
  the non-reentrant `SemaphoreSlim`; undocumented/unsupported in FusionCache. Source: `Internals/RunUtils.cs`,
  `Locking/StandardMemoryLocker.cs`, `FusionCacheEntryOptions.cs` (`GetAppropriateMemoryLockTimeout`),
  `FusionCacheGlobalDefaults.cs` (`EntryOptionsLockTimeout = Timeout.InfiniteTimeSpan`).
- **Keystone learnings:** `docs/solutions/architecture-patterns/caching-fail-safe-coordinator-design.md`
  (§2 engine-first, §4 cancellation identity, §6 fresh-write-outside-catch + best-effort restamp).
- **Fire-and-forget hazards:** `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`.
- **Cancellation vs timeout classification:**
  `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md`.
- **Repo recon:** `FactoryCacheCoordinator.cs`, `IFactoryCacheStore.cs`, `CacheEntryOptions.cs`,
  `KeyedAsyncLock.cs`, `CacheConformanceTestsBase.cs`, `FactoryCacheCoordinatorTests.cs`.
