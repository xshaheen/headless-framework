---
title: "feat(distributed-locks): Phase 1 — ergonomics + throttling extraction"
type: feat
status: partially-reverted
created: 2026-05-20
depth: deep
origin: https://github.com/xshaheen/headless-framework/issues/288
tracking: https://github.com/xshaheen/headless-framework/issues/287
---

> **2026-05-21 — Rate-limiting requirements reverted.** R1.4 (extraction to
> `Headless.RateLimiting.*`) and R1.5 (period-boundary spin-fix landing in the
> extracted package) have been dropped from the framework. After a post-merge
> architecture review against `Microsoft.AspNetCore.RateLimiting`,
> `System.Threading.RateLimiting`, `Polly.RateLimiting`, and community
> `RedisRateLimiting`, the conclusion was that a framework-shipped distributed
> sliding-window rate limiter has no defensible niche against the existing
> .NET stack: the ecosystem has converged on "BCL primitive + edge / community
> distributed backend", and Headless Framework's breadth-first thesis does not
> include filling narrow distributed-rate-limiting gaps. The `Headless.RateLimiting.*`
> package family and `docs/llms/rate-limiting.md` have been deleted. Lock
> ergonomics (R1.1–R1.3, R1.6) remain shipped. The old throttling code that
> lived under `Headless.DistributedLocks.Core/ThrottlingLocks/*` does not
> return — consumers needing rate limiting should use `Microsoft.AspNetCore.RateLimiting`
> (in-process) or `Polly.RateLimiting` + a community Redis-backed `RateLimiter`
> (distributed).

# feat(distributed-locks): Phase 1 — ergonomics + throttling extraction

## Summary

Phase 1 of the 4-phase `Headless.DistributedLocks.*` enhancement (tracking #287). Two themes shipped together as the foundation for Phases 2-4:

1. **Lock ergonomics** — additive API on `IDistributedLockProvider`:
   - `AcquireAsync(...)` overload that throws `LockAcquisitionTimeoutException` on timeout, alongside existing `TryAcquireAsync(...)` (still returns `null`).
   - New `releaseOnDispose: bool` parameter (default `true`) on both methods.
   - `IOutboxPublisher` ctor parameter becomes nullable. Provider works without `Headless.Messaging` registered; falls back to polling-only wake-up with a one-shot startup warning log.

2. **Throttling extraction** — move the throttling primitive out of `Headless.DistributedLocks.*` into a new `Headless.RateLimiting.*` package family (`Abstractions`, `Core`, `Cache`, `Redis`). No shim, no compatibility layer — greenfield posture. During the move, fix the period-boundary spin-check bug at `src/Headless.DistributedLocks.Core/ThrottlingLocks/ThrottlingDistributedLockProvider.cs:80-103` (Linux/.NET timer wakes 1-4ms early due to `CONFIG_HZ`, causing the loop to double-sleep).

Settled-by-research leans applied: `LockAcquisitionTimeoutException : DistributedLockException : Exception` (OQ1 lean from #287); `IDistributedRateLimiter` naming (OQ2 lean); no shim for the rename (OQ5 lean).

## Problem Frame

- **`TryAcquireAsync`-only surface forces null checks** even when callers want fail-fast semantics. Standard ergonomics in the broader ecosystem (Foundatio, `DistributedLock`) is a throwing variant — Headless lacks it.
- **Caller can't opt out of dispose-time release.** Some callers want a lock that outlives the `using` block (transfer of ownership, manual lifecycle). Today, `DisposableDistributedLock.DisposeAsync` unconditionally releases.
- **`Headless.DistributedLocks.Core` hard-depends on `Headless.Messaging.*`** via `IOutboxPublisher` (constructor) and `AddConsumer<…>` (Setup). Consumers who want distributed locks without messaging cannot register the package — over-coupling.
- **Throttling lives in the wrong package.** It's a rate-limiting primitive, not a lock. Phase 3b will add a true distributed semaphore (N-holder); throttling and semaphore should sit in the same rate-limiting package, not under "DistributedLocks". The current location also forces a hard cache dependency on consumers who only want regular locks.
- **Period-boundary spin-check bug** at `ThrottlingDistributedLockProvider.cs:80-103`: after `timeProvider.Delay(sleepUntil - _Now())`, the next iteration re-computes `cacheKey = _GetCacheKey(resource)` using `_Now()`. The Linux/.NET timer wakes 1-4ms early (`CONFIG_HZ` slop), so `_GetCacheKey` returns the previous period's key and the loop sleeps a second time for the next period's tick. Bug is reproducible with `FakeTimeProvider`.

---

## Requirements Traceability

| R-ID | Requirement | Units |
|---|---|---|
| R1.1 | `AcquireAsync` overload that throws `LockAcquisitionTimeoutException` on linked-token cancellation; never returns null | U2 |
| R1.2 | `LockAcquisitionTimeoutException` (carries `Resource`) inheriting from new `DistributedLockException` base | U1 |
| R1.3 | `releaseOnDispose: bool` parameter (default `true`) on `TryAcquireAsync` + new `AcquireAsync`; `DisposableDistributedLock` honors flag | U3 |
| R1.4 | Extract throttling to `Headless.RateLimiting.*` with renames: `IDistributedRateLimiter`, `IDistributedRateLimiterLease`, `SlidingWindowDistributedRateLimiter`, storage classes renamed in line | U5, U6, U7, U8, U9, U10, U11 |
| R1.5 | Fix period-boundary spin-check bug during the move; capture-key-before-sleep + 1ms spin-wait (cap 100) until `_GetCacheKey` rotates; warn if cap exceeded | U6 |
| R1.6 | `IOutboxPublisher` becomes optional in `DistributedLockProvider` ctor; locks work without `Headless.Messaging` registered; warning log at startup; falls back to polling-only | U4 |

---

## Key Technical Decisions

**Exception hierarchy.** `LockAcquisitionTimeoutException : DistributedLockException : Exception` with `Resource` property. Does **not** inherit from `TimeoutException` — that would conflict with the planned `DistributedLockException` base for future lock-specific exceptions (`LockHandleLostException` etc., Phase 2). 408 routing in `Headless.Api`'s exception handler is deferred (see Deferred section).

**Warning log emission for missing `IOutboxPublisher`.** Source-generated `[LoggerMessage]` partial fired from the `DistributedLockProvider` constructor. Provider is a DI singleton, so constructor runs once per process at first resolution — effectively "at startup" for any warm app. No separate `IHostedService` needed. Mirrors the EventId-pegged style of `Headless.Messaging`'s `_WarnIfNoOpProvider` (EventId 77). (See origin: tracking-issue/#287 OQ1; see learning: `docs/solutions/architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md` for the EventId-pegged style.)

**Auto-detect messaging registration in Setup.** `AddDistributedLockExtensions._AddDistributedLockCore` already calls `services.AddConsumer<LockReleasedConsumer, DistributedLockReleased>(...)`. Change: probe `IServiceCollection` for an existing `IOutboxPublisher` registration; if absent, skip both the consumer registration and `IOutboxPublisher` resolution (use `GetService<>` instead of `GetRequiredService<>`). Keeps the existing hard `ProjectReference` to `Headless.Messaging.Core` in `Headless.DistributedLocks.Core.csproj` — splitting messaging into a separate `Headless.DistributedLocks.Messaging` package is deferred.

**Spin-wait implementation for R1.5.** Async via `timeProvider.Delay(1.Milliseconds(), ct)` in a `for (int i = 0; i < 100; i++)` loop, breaking when `_GetCacheKey(resource) != previousCacheKey`. Warn via new source-gen log event (e.g., `LogThrottlingClockFrozen`) when the cap is exceeded. `TimeProvider` (already framework standard) is mandatory so `FakeTimeProvider` drives the regression test deterministically. (See learning: `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` patterns #1, #4.)

**Characterization-first for the spin-bug fix.** The FakeTimeProvider regression test is written before/with the fix in the same change. Test asserts the desired post-fix behavior (acquire completes within one period plus spin cap); fails against the current code, passes once the fix lands. Test moves to `Headless.RateLimiting.Tests.Unit` alongside the moved provider.

**Cache rate-limiter has no Setup class.** Mirrors current `Headless.DistributedLocks.Cache` convention (consumers wire via Core's typed-storage overload). `Headless.RateLimiting.Cache` ships only `CacheDistributedRateLimiterStorage`; registration goes through `Headless.RateLimiting.Core`'s `AddRateLimiter<TStorage>(...)` overloads.

**EventIds reset in the new package.** `Headless.RateLimiting.Core.LoggerExtensions` numbers EventIds from 1 in its own source-gen partial class. The current throttling EventIds (15-25 in `Headless.DistributedLocks.Core/ThrottlingLocks/LoggerExtensions.cs`) do not need to be preserved across the package boundary.

**XML doc wording for `AcquireAsync`.** Documented as a convenience-throwing variant of `TryAcquireAsync`, **not** a stronger-safety variant. No language that could be misread as multi-instance / RedLock semantics. (See learning: `docs/solutions/tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md`.)

---

## High-Level Technical Design

*This illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce.*

### `AcquireAsync` → `TryAcquireAsync` delegation

```text
AcquireAsync(resource, timeUntilExpires, acquireTimeout, releaseOnDispose, ct):
  lock = await TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, releaseOnDispose, ct)
  if lock is null:
    throw LockAcquisitionTimeoutException(resource)
  return lock
```

Single delegation point. All acquisition logic stays in `TryAcquireAsync`; `AcquireAsync` is a thin shell that flips the null-return into an exception. `Resource` flows onto the exception verbatim.

### Spin-check bug fix shape

```text
Inside the do-while at ThrottlingDistributedLockProvider:45 (post-move: SlidingWindowDistributedRateLimiter.cs):
  ...
  previousCacheKey = cacheKey                         # NEW — capture before sleep
  sleepUntil = _DatePeriodEnded().AddMilliseconds(1)
  if sleepUntil > _Now():
    await timeProvider.Delay(sleepUntil - _Now(), ct)
  else:
    await timeProvider.Delay(50ms, ct)

  # NEW — spin-wait until period boundary actually rotates
  for i in 0..99:
    if _GetCacheKey(resource) != previousCacheKey:
      break
    await timeProvider.Delay(1ms, ct)
  else:
    logger.LogThrottlingClockFrozen(resource)         # cap exceeded
```

### `IOutboxPublisher`-optional path

```text
DistributedLockProvider ctor:
  if outboxPublisher is null:
    logger.LogOutboxPublisherAbsent()  # one-shot, source-gen, EventId pegged

ReleaseAsync(resource, lockId, ct):
  removed = await storage.RemoveIfEqualAsync(resource, lockId, ct)
  if removed and outboxPublisher is not null:
    await outboxPublisher.PublishAsync(new DistributedLockReleased(resource, lockId), ct)

Setup.cs (_AddDistributedLockCore):
  hasMessaging = services.Any(d => d.ServiceType == typeof(IOutboxPublisher))
  if hasMessaging:
    services.AddConsumer<LockReleasedConsumer, DistributedLockReleased>("headless.locks.released")
            .Concurrency(1)
  services.TryAddSingleton<IDistributedLockProvider>(sp => new DistributedLockProvider(
    sp.GetRequiredService<TStorage>(),
    sp.GetService<IOutboxPublisher>(),  # nullable — works when messaging absent
    ...))
```

---

## Output Structure

```text
src/
├── Headless.RateLimiting.Abstractions/
│   ├── Headless.RateLimiting.Abstractions.csproj
│   ├── IDistributedRateLimiter.cs
│   ├── IDistributedRateLimiterLease.cs
│   ├── IDistributedRateLimiterStorage.cs
│   └── README.md
├── Headless.RateLimiting.Core/
│   ├── Headless.RateLimiting.Core.csproj
│   ├── SlidingWindowDistributedRateLimiter.cs
│   ├── DistributedRateLimiterLease.cs
│   ├── SlidingWindowRateLimiterOptions.cs
│   ├── LoggerExtensions.cs
│   ├── Setup.cs                                  # AddRateLimitingExtensions
│   └── README.md
├── Headless.RateLimiting.Cache/
│   ├── Headless.RateLimiting.Cache.csproj
│   ├── CacheDistributedRateLimiterStorage.cs
│   └── README.md
└── Headless.RateLimiting.Redis/
    ├── Headless.RateLimiting.Redis.csproj
    ├── RedisDistributedRateLimiterStorage.cs
    ├── Setup.cs                                  # RedisRateLimitingSetup
    └── README.md
tests/
├── Headless.RateLimiting.Tests.Unit/             # incl. FakeTimeProvider period-boundary test
├── Headless.RateLimiting.Tests.Harness/          # DistributedRateLimiterTestsBase
├── Headless.RateLimiting.Cache.Tests.Integration/
└── Headless.RateLimiting.Redis.Tests.Integration/

docs/llms/
└── rate-limiting.md                              # new domain doc
```

Layout is a scope declaration, not a constraint — per-unit `**Files:**` are authoritative. The InMemory throttling test (`tests/Headless.DistributedLocks.InMemory.Tests.Integration/InMemoryResourceThrottlingLockProviderTests.cs`) folds into `Headless.RateLimiting.Tests.Unit` rather than spawning a near-empty separate integration project (it doesn't use Testcontainers).

---

## Implementation Units

### U1. Add `DistributedLockException` base + `LockAcquisitionTimeoutException`

- **Goal:** New exception hierarchy in `Headless.DistributedLocks.Abstractions`. Foundation for U2 and for Phase 2's planned `LockHandleLostException`.
- **Requirements:** R1.2
- **Dependencies:** None
- **Files:**
  - `src/Headless.DistributedLocks.Abstractions/Exceptions/DistributedLockException.cs` (new)
  - `src/Headless.DistributedLocks.Abstractions/Exceptions/LockAcquisitionTimeoutException.cs` (new)
  - `tests/Headless.DistributedLocks.Tests.Unit/Exceptions/LockAcquisitionTimeoutExceptionTests.cs` (new)
- **Approach:**
  - `DistributedLockException : Exception` — abstract, `[PublicAPI]`, standard three ctors (default, message, message + inner).
  - `LockAcquisitionTimeoutException : DistributedLockException` — `[PublicAPI]`, sealed, `Resource` property (`required` `init` or ctor-set), three ctors (resource only with default message, resource + message, resource + message + inner).
  - Namespace `Headless.DistributedLocks` (use `IDE0130` pragma matching existing convention).
  - **Do not** inherit from `TimeoutException` — see Key Technical Decisions.
  - XML docs: describe `LockAcquisitionTimeoutException` as raised when acquisition exceeds the configured timeout, **not** as a stronger-safety variant.
- **Patterns to follow:**
  - `[PublicAPI]` annotation per existing `IDistributedLockProvider` style.
  - `Headless.Checks.Argument.IsNotNullOrWhiteSpace(resource)` at exception constructor boundary.
- **Test scenarios:**
  - `LockAcquisitionTimeoutException` default-message ctor carries `Resource` value verbatim and produces a message that includes the resource name.
  - Message + resource ctor preserves both fields.
  - Inner-exception ctor surfaces the inner exception via `InnerException` and preserves `Resource`.
  - `Argument.IsNotNullOrWhiteSpace` throws on null/empty/whitespace resource (boundary check).
  - `DistributedLockException` is abstract — cannot be instantiated directly; round-trip serialization not required (the framework does not serialize exceptions across processes).
- **Verification:**
  - All planned unit tests pass.
  - `make build` succeeds with no new warnings in `Headless.DistributedLocks.Abstractions`.
  - `[PublicAPI]` applied to both types.

---

### U2. Add `AcquireAsync` overload on `IDistributedLockProvider`

- **Goal:** Throwing-variant of `TryAcquireAsync` on the public interface, implemented in `DistributedLockProvider` as a thin delegating shell.
- **Requirements:** R1.1
- **Dependencies:** U1 (exception type)
- **Files:**
  - `src/Headless.DistributedLocks.Abstractions/RegularLocks/IDistributedLockProvider.cs` (modify — add `AcquireAsync` signature)
  - `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` (modify — implement)
  - `tests/Headless.DistributedLocks.Tests.Unit/RegularLocks/DistributedLockProviderTests.cs` (modify — add `AcquireAsync` cases)
  - `tests/Headless.DistributedLocks.Tests.Harness/DistributedLockProviderTestsBase.cs` (modify — add cross-storage `AcquireAsync` cases)
- **Approach:**
  - Add interface signature exactly as specified in issue R1.1 (`AcquireAsync(resource, timeUntilExpires=null, acquireTimeout=null, releaseOnDispose=true, cancellationToken=default)`). The `releaseOnDispose` parameter is added here but its behavior lands in U3.
  - Implementation delegates to `TryAcquireAsync`; on `null` return, throw `new LockAcquisitionTimeoutException(resource)`.
  - Caller-token cancellation must surface as `OperationCanceledException` (not `LockAcquisitionTimeoutException`) — `TryAcquireAsync` already distinguishes via re-throw at `DistributedLockProvider.cs:194-198`; `AcquireAsync` inherits this behavior because it delegates before catching.
  - Argument validation flows through the delegated `TryAcquireAsync`, which runs `Argument.IsNotNullOrWhiteSpace(resource)` and `Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength, ...)` synchronously **before any await** (current code at `DistributedLockProvider.cs:80-82`). `AcquireAsync` should not duplicate these checks — duplicating only the null check would also skip the length validation. Delegate; the validation already fails fast.
- **Patterns to follow:**
  - Interface signature ordering and parameter defaults match existing `TryAcquireAsync` style.
  - Implementation method placement: directly above `TryAcquireAsync` in `DistributedLockProvider.cs`.
- **Test suite design:** Unit tests in `Headless.DistributedLocks.Tests.Unit` against `FakeDistributedLockStorage` (existing) cover the delegation logic. Cross-storage scenarios (Redis, Cache, InMemory) added to `DistributedLockProviderTestsBase` so they execute against every backend via the existing integration harness.
- **Test scenarios:** *(Covers AE — see acceptance criteria 1 and 2 from #288)*
  - **Happy path:** `AcquireAsync` returns a non-null `IDistributedLock` when the resource is free; lock can be released; `RenewalCount`, `Resource`, `LockId` match expectations.
  - **Timeout path:** `acquireTimeout` of 100ms against a contended resource — `AcquireAsync` throws `LockAcquisitionTimeoutException`; exception's `Resource` equals the requested resource name.
  - **Caller-cancellation path:** Pre-cancelled `CancellationToken` — `AcquireAsync` throws `OperationCanceledException` (not `LockAcquisitionTimeoutException`).
  - **Caller-cancellation mid-wait:** Long `acquireTimeout`, contended resource, caller cancels after 50ms — `AcquireAsync` throws `OperationCanceledException`; `LockAcquisitionTimeoutException` is NOT thrown.
  - **`acquireTimeout: TimeSpan.Zero` path:** Contended resource — `AcquireAsync` throws `LockAcquisitionTimeoutException` immediately (fast-path).
  - **`TryAcquireAsync` parity:** Same scenarios against `TryAcquireAsync` return `null` for timeout, throw `OperationCanceledException` for caller cancellation. Both behaviors covered side-by-side in the harness base.
  - **Resource argument validation:** `null`, empty, whitespace resource — `AcquireAsync` throws `ArgumentException` (via `Argument.*`) before any acquisition logic.
- **Verification:**
  - All planned unit + harness-base tests pass against every storage backend (Fake, InMemory, Cache, Redis).
  - `make build` succeeds with no new warnings.
  - Interface signature on `IDistributedLockProvider` includes `AcquireAsync` with the exact shape from R1.1.

---

### U3. Add `releaseOnDispose: bool` parameter; gate `DisposableDistributedLock.DisposeAsync`

- **Goal:** Optional opt-out of dispose-time release. `releaseOnDispose: false` causes `DisposeAsync` to be a no-op; explicit `ReleaseAsync()` still works; double-release remains idempotent.
- **Requirements:** R1.3
- **Dependencies:** U2 (parameter is added on `AcquireAsync` in U2's interface change; U3 lands the runtime behavior)
- **Files:**
  - `src/Headless.DistributedLocks.Abstractions/RegularLocks/IDistributedLockProvider.cs` (modify — add `releaseOnDispose` to `TryAcquireAsync`)
  - `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` (modify — propagate flag through `_TryAcquireOnceAsync` and `TryAcquireAsync`)
  - `src/Headless.DistributedLocks.Core/RegularLocks/DisposableDistributedLock.cs` (modify — add ctor param + gate `DisposeAsync`)
  - `tests/Headless.DistributedLocks.Tests.Unit/RegularLocks/DisposableDistributedLockTests.cs` (modify)
  - `tests/Headless.DistributedLocks.Tests.Harness/DistributedLockProviderTestsBase.cs` (modify — cross-storage release-on-dispose cases)
- **Approach:**
  - `TryAcquireAsync` and `AcquireAsync` gain `bool releaseOnDispose = true` as the last positional parameter before `cancellationToken`.
  - `DisposableDistributedLock` ctor gains `bool releaseOnDispose` parameter; field stored.
  - `DisposeAsync` (lines 88-113 of `DisposableDistributedLock.cs`): early-return when `!releaseOnDispose`. The trace log at `LogDisposableLockDisposing` still fires; the release call is gated.
  - Explicit `await lock.ReleaseAsync()` continues to call `lockProvider.ReleaseAsync(...)` regardless of `releaseOnDispose`.
  - Double-release idempotency is already provided by the existing `Interlocked`-protected `_released` flag at `DisposableDistributedLock.cs:80-86` — no change there.
- **Patterns to follow:**
  - Logger partial stays at the **bottom** of `DisposableDistributedLock.cs` (per user memory: log partial class placement).
  - `Argument.*` checks unchanged.
- **Test scenarios:** *(Covers AE — see acceptance criterion 3 from #288)*
  - **`releaseOnDispose: true` (default) — dispose releases:** Acquire, dispose, assert `lockProvider.IsLockedAsync` returns `false`.
  - **`releaseOnDispose: false` — dispose is a no-op:** Acquire with flag false, dispose, assert `lockProvider.IsLockedAsync` returns `true` (lock still held until TTL or explicit release).
  - **`releaseOnDispose: false` + explicit `ReleaseAsync()`:** Acquire with flag false, call `ReleaseAsync()` explicitly, dispose, assert lock released exactly once.
  - **Double-release idempotency:** Acquire, call `ReleaseAsync()` twice — second call returns without error and does not call storage twice (NSubstitute call-count assertion against the storage mock).
  - **Cross-storage (harness):** All three above cases execute against InMemory, Cache, and Redis backends.
- **Verification:**
  - All planned unit + harness tests pass.
  - `make build` succeeds with no warnings.
  - Interface signatures on both `TryAcquireAsync` and `AcquireAsync` include `releaseOnDispose: bool = true`.

---

### U4. Make `IOutboxPublisher` nullable; auto-detect messaging in Setup; emit startup warning

- **Goal:** `DistributedLockProvider` can be constructed and used without `Headless.Messaging` registered. Falls back to polling-only wake-up; warning logged once at startup.
- **Requirements:** R1.6
- **Dependencies:** None (independent of U1-U3; can ship in either order)
- **Files:**
  - `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` (modify — ctor signature, ReleaseAsync guard, startup log)
  - `src/Headless.DistributedLocks.Core/Setup.cs` (modify — auto-detect messaging, conditional `AddConsumer`)
  - `src/Headless.DistributedLocks.Core/RegularLocks/LoggerExtensions.cs` (modify — new EventId for `LogOutboxPublisherAbsent`)
  - `tests/Headless.DistributedLocks.Tests.Unit/RegularLocks/DistributedLockProviderTests.cs` (modify — null-publisher cases)
  - `tests/Headless.DistributedLocks.Tests.Unit/SetupTests.cs` (new — DI integration for both modes)
- **Approach:**
  - Ctor parameter `IOutboxPublisher? outboxPublisher` (nullable). Existing call site at `Setup.cs:103` changes from `GetRequiredService<IOutboxPublisher>()` to `GetService<IOutboxPublisher>()`.
  - In constructor, if `outboxPublisher is null`, call new source-gen log `logger.LogOutboxPublisherAbsent()` exactly once (constructor of a DI singleton fires once per process — no additional gating needed).
  - In `ReleaseAsync` (currently `DistributedLockProvider.cs:408-413`), gate the publish call on `outboxPublisher is not null`. The `if (removed)` block becomes `if (removed && outboxPublisher is not null)`.
  - In `Setup.cs._AddDistributedLockCore`, before calling `services.AddConsumer<...>(...)`:
    ```text
    bool hasMessaging = services.Any(d => d.ServiceType == typeof(IOutboxPublisher));
    if (hasMessaging) { services.AddConsumer<LockReleasedConsumer, DistributedLockReleased>(...); }
    ```
  - Keep the existing hard `ProjectReference` to `Headless.Messaging.Core` in `Headless.DistributedLocks.Core.csproj`. Splitting messaging into a separate optional package is deferred (see Deferred section).
  - Log message text: `"No IOutboxPublisher registered; lock-release wake-ups will fall back to polling backoff. Register Headless.Messaging for push-based latency."` (verbatim from issue R1.6).
  - EventId: next available in the regular-locks source-gen partial (current sequence runs 1-15 across two partial classes; pick a non-colliding number, e.g., 16).
- **Patterns to follow:**
  - Source-gen `[LoggerMessage]` partial (e.g., `LogOutboxPublisherAbsent` with `EventId = 16, Level = LogLevel.Warning`).
  - EventId-pegged style mirrors `Headless.Messaging`'s `_WarnIfNoOpProvider` (EventId 77).
- **Test scenarios:** *(Covers AE — see acceptance criterion 4 from #288)*
  - **Construct without `IOutboxPublisher` — works:** Build `DistributedLockProvider` with `outboxPublisher: null`, acquire/release a lock against `FakeDistributedLockStorage`, assert no `NullReferenceException`.
  - **Warning logged exactly once:** Use an `ILogger<DistributedLockProvider>` test sink; construct provider with null publisher; assert the warning is logged exactly once with the expected EventId and message.
  - **No warning when publisher present:** Construct with a real `IOutboxPublisher` mock; assert no warning logged.
  - **`ReleaseAsync` skips publish when null:** Null publisher, acquire + release; assert no publish attempted (null guard prevents the call).
  - **`ReleaseAsync` publishes when present:** Real `IOutboxPublisher` mock, acquire + release; assert `PublishAsync` called once with a `DistributedLockReleased(resource, lockId)` message (existing behavior preserved).
  - **DI Setup — messaging absent:** Use a fresh `ServiceCollection` without messaging; call `services.AddDistributedLock<FakeDistributedLockStorage>(...)`; build provider; assert `IDistributedLockProvider` resolves, no `IOutboxPublisher` registered, no `LockReleasedConsumer` registered.
  - **DI Setup — messaging present:** Same with `Headless.Messaging` test-harness setup added first; assert consumer registration occurs and publisher is non-null on the resolved provider.
- **Verification:**
  - All planned unit tests pass.
  - DI setup tests prove both messaging-present and messaging-absent paths work end-to-end.
  - `make build` succeeds with no warnings.
  - Manual smoke check: a minimal sample app referencing only `Headless.DistributedLocks.Core` (+ a storage backend) without `Headless.Messaging` builds and runs.

---

### U5. Create `Headless.RateLimiting.Abstractions` package

- **Goal:** New package with renamed interfaces. Foundation for U6-U9.
- **Requirements:** R1.4 (interface portion)
- **Dependencies:** None
- **Files (new):**
  - `src/Headless.RateLimiting.Abstractions/Headless.RateLimiting.Abstractions.csproj`
  - `src/Headless.RateLimiting.Abstractions/IDistributedRateLimiter.cs` (was `IThrottlingDistributedLockProvider`)
  - `src/Headless.RateLimiting.Abstractions/IDistributedRateLimiterLease.cs` (was `IDistributedThrottlingLock`)
  - `src/Headless.RateLimiting.Abstractions/IDistributedRateLimiterStorage.cs` (was `IThrottlingDistributedLockStorage`)
  - `src/Headless.RateLimiting.Abstractions/README.md`
- **Files (delete):**
  - `src/Headless.DistributedLocks.Abstractions/ThrottlingLocks/IThrottlingDistributedLockProvider.cs`
  - `src/Headless.DistributedLocks.Abstractions/ThrottlingLocks/IDistributedThrottlingLock.cs`
  - (`IThrottlingDistributedLockStorage` currently lives in `Headless.DistributedLocks.Core/ThrottlingLocks/`, not Abstractions — it deletes in U6 alongside the provider move.)
- **Layer change note:** `IDistributedRateLimiterStorage` in the new `Headless.RateLimiting.Abstractions` is a relocation, not just a rename — the storage interface promotes from `Headless.DistributedLocks.Core` (package-internal seam) to `Headless.RateLimiting.Abstractions` (public contract). Justification: `Headless.RateLimiting.Cache` (U7) and `Headless.RateLimiting.Redis` (U8) must reference `Headless.RateLimiting.Abstractions` only (no transitive Core dep), which requires the storage interface to live in Abstractions. The previous Core-side placement reflects the old throttling layout's tight coupling between Core and the storage backends; the new family treats storage as a public extension seam.
- **Approach:**
  - Project SDK: `Headless.NET.Sdk` (per `global.json` per project CLAUDE.md).
  - Namespace: `Headless.RateLimiting` (with `#pragma warning disable IDE0130` matching existing convention).
  - Public API: `IDistributedRateLimiter` carries the same shape as `IThrottlingDistributedLockProvider` (DefaultAcquireTimeout, TryAcquireAsync, IsLockedAsync) but with renamed return type. Method on `TryAcquireAsync` returns `IDistributedRateLimiterLease?` instead of `IDistributedThrottlingLock?`.
  - `IDistributedRateLimiterLease` carries Resource, DateAcquired, TimeWaitedForLock (no behavior change).
  - `IDistributedRateLimiterStorage`: same shape as existing `IThrottlingDistributedLockStorage` (`GetHitCountsAsync`, `IncrementAsync`, `IAsyncDisposable`) — pure rename.
  - `[PublicAPI]` on all three interfaces.
  - README.md follows `docs/authoring/PACKAGE-README-TEMPLATE.md`.
  - XML docs avoid any "stronger safety" / RedLock-adjacent language.
- **Patterns to follow:**
  - File-scoped namespace (matching existing distributed-locks Abstractions style).
  - `[PublicAPI]` annotation pattern.
  - Source-file copyright header.
- **Test suite design:** This unit is interface-only — no test scenarios needed. Behavior is verified by the moved tests in U9 once Core lands.
- **Test scenarios:** `Test expectation: none — package contains only interfaces; behavior tested via U6/U9.`
- **Verification:**
  - New csproj builds (`make build-project PROJECT=src/Headless.RateLimiting.Abstractions/...`).
  - `[PublicAPI]` applied to all three interfaces.
  - README.md present and conforms to `PACKAGE-README-TEMPLATE.md` invariants.

---

### U6. Create `Headless.RateLimiting.Core`; move provider + apply spin-fix; characterization test

- **Goal:** New Core package containing the moved + renamed provider, options, logger extensions, and Setup class. Period-boundary spin-fix lands in the same change. Characterization test (FakeTimeProvider) drives the fix.
- **Requirements:** R1.4 (Core portion), R1.5
- **Dependencies:** U5
- **Files (new):**
  - `src/Headless.RateLimiting.Core/Headless.RateLimiting.Core.csproj`
  - `src/Headless.RateLimiting.Core/SlidingWindowDistributedRateLimiter.cs` (renamed from `ThrottlingDistributedLockProvider`; spin-fix applied)
  - `src/Headless.RateLimiting.Core/DistributedRateLimiterLease.cs` (renamed from `DistributedThrottlingLock`)
  - `src/Headless.RateLimiting.Core/SlidingWindowRateLimiterOptions.cs` (renamed from `ThrottlingDistributedLockOptions`)
  - `src/Headless.RateLimiting.Core/LoggerExtensions.cs` (source-gen log partials; EventIds reset from 1)
  - `src/Headless.RateLimiting.Core/Setup.cs` (class name `AddRateLimitingExtensions`; mirrors `AddDistributedLockExtensions` shape)
  - `src/Headless.RateLimiting.Core/README.md`
- **Files (delete):**
  - `src/Headless.DistributedLocks.Core/ThrottlingLocks/ThrottlingDistributedLockProvider.cs`
  - `src/Headless.DistributedLocks.Core/ThrottlingLocks/DistributedThrottlingLock.cs`
  - `src/Headless.DistributedLocks.Core/ThrottlingLocks/ThrottlingDistributedLockOptions.cs`
  - `src/Headless.DistributedLocks.Core/ThrottlingLocks/LoggerExtensions.cs`
  - `src/Headless.DistributedLocks.Core/ThrottlingLocks/IThrottlingDistributedLockStorage.cs` (confirmed Core-resident in current code)
- **Files (new test):**
  - `tests/Headless.RateLimiting.Tests.Unit/SlidingWindowPeriodBoundaryTests.cs` (FakeTimeProvider regression for R1.5)
- **Approach:**
  - Move + rename: provider class becomes `SlidingWindowDistributedRateLimiter` implementing `IDistributedRateLimiter`. Lease becomes `DistributedRateLimiterLease`. Options becomes `SlidingWindowRateLimiterOptions` (carries an `internal sealed class SlidingWindowRateLimiterOptionsValidator : AbstractValidator<SlidingWindowRateLimiterOptions>` in the same file, per CLAUDE.md options pattern).
  - Setup class `AddRateLimitingExtensions` lives in `src/Headless.RateLimiting.Core/Setup.cs`. Three overloads (configuration / `Action<TOptions>` / `Action<TOptions, IServiceProvider>`) plus three keyed variants. Shared private helper named `_AddRateLimiterCore` (and `_AddKeyedRateLimiterCore`). Mirrors the existing throttling overloads at `src/Headless.DistributedLocks.Core/Setup.cs:121-249`.
  - **Spin-check fix (R1.5):**
    - Inside the acquire `do-while` (currently `ThrottlingDistributedLockProvider.cs:45-103`), capture `var previousCacheKey = cacheKey;` immediately before the period-boundary sleep block.
    - After `timeProvider.DelayUntilElapsedOrCancel(...)` returns (lines 96 and 101 in current source), insert a spin-wait loop:
      ```text
      for (int spinIteration = 0; spinIteration < 100; spinIteration++)
      {
          if (_GetCacheKey(resource) != previousCacheKey) break;
          await timeProvider.Delay(1.Milliseconds(), cts.Token).ConfigureAwait(false);
      }
      else
      {
          logger.LogRateLimiterClockFrozen(resource);
      }
      ```
      (`for...else` shown for clarity — implement as flag + post-loop check since C# lacks `for/else`.)
    - New source-gen log `LogRateLimiterClockFrozen(resource)` at `LogLevel.Warning` with a stable EventId in the new package's log partial.
  - All XML docs translated to "rate limiter" / "rate-limiting period" wording. Remove "throttling lock" terminology.
  - Use `TimeProvider` for all delays (already the case; preserve it through the rename).
  - `Argument.*` validations preserved.
  - `[PublicAPI]` on `SlidingWindowDistributedRateLimiter` (per current `ThrottlingDistributedLockProvider` annotation) and `AddRateLimitingExtensions`.
  - csproj references: `Headless.RateLimiting.Abstractions` (U5), `Headless.Checks`, `Headless.Hosting` (for FluentValidation wiring on options), `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Diagnostics.Abstractions`. **No** `Headless.Messaging` reference (rate limiter doesn't publish messages).
- **Patterns to follow:**
  - `extension(IServiceCollection services)` C# 14 extension-member pattern for `AddRateLimitingExtensions`.
  - Logger partial at the **bottom** of `LoggerExtensions.cs` (per user memory).
  - Options validator in the same file as options class (per CLAUDE.md).
  - Source-file copyright header.
- **Test suite design:**
  - **`Headless.RateLimiting.Tests.Unit`** (new project, created in this unit since the characterization test needs to live somewhere) — unit-level tests with FakeTimeProvider for the spin-fix; behavior tests using a fake storage (port `FakeThrottlingDistributedLockStorage` → `FakeDistributedRateLimiterStorage`).
  - The full provider behavior test suite (against in-memory + cache + redis storages) ports in U9; this unit only adds the new FakeTimeProvider regression that did not exist before.
- **Test scenarios:** *(Covers AE — see acceptance criterion 6 from #288)*
  - **Period-boundary regression (FakeTimeProvider):** Configure rate limiter with 100ms period and 1 hit/period. Acquire the first lease successfully (consumes the period's hit). Advance `FakeTimeProvider` to `period - 1ms` (simulating early timer wake). Attempt second acquire — expected to wait. Drive `FakeTimeProvider` advance to `period` exactly. Assert acquire completes within **one** period boundary's worth of additional spin (≤100ms total wait beyond the original Delay), proving the spin-wait detects the key rotation and the loop does NOT double-sleep into the next period.
  - **Clock-frozen warning:** Configure rate limiter where `FakeTimeProvider` is deliberately not advanced after the initial Delay (simulating a stuck clock). Acquire attempt — assert the spin cap fires (`LogRateLimiterClockFrozen` emitted once with the resource name).
  - **`Argument.*` boundary:** Null/empty resource → `ArgumentException`.
  - **Happy-path acquire:** First acquire within a fresh period succeeds; lease's `Resource`, `DateAcquired`, `TimeWaitedForLock` are populated.
  - **Cap-exceeded:** `MaxHitsPerPeriod = 2`; three acquires in the same period — first two succeed, third returns `null` after `acquireTimeout`.
  - **Cancellation:** Caller-token cancelled during the acquire wait → returns `null` (matches existing `ThrottlingDistributedLockProvider` behavior at lines 104-106).
  - **Options validation:** `SlidingWindowRateLimiterOptions` with `MaxHitsPerPeriod = 0` or `ThrottlingPeriod = TimeSpan.Zero` → FluentValidation fails at `ValidateOnStart()`.
- **Verification:**
  - All planned unit tests pass — critically, the period-boundary regression test fails against an unfixed copy of the provider and passes against the implemented fix.
  - `make build` succeeds with no new warnings.
  - `Headless.RateLimiting.Core.csproj` has **no** reference to `Headless.Messaging.*`.
  - Setup class follows the three-overload + private `_AddRateLimiterCore` pattern.
- **Execution note:** Write the FakeTimeProvider period-boundary test first. Verify it fails against a snapshot of the original throttling logic ported into the new package without the spin-fix. Then apply the fix and confirm the test passes. The test stays in the new package's test project.

---

### U7. Create `Headless.RateLimiting.Cache` package

- **Goal:** Cache-backed rate-limiter storage. Mirrors `Headless.DistributedLocks.Cache` shape (storage class only, no Setup).
- **Requirements:** R1.4 (Cache portion)
- **Dependencies:** U5
- **Files (new):**
  - `src/Headless.RateLimiting.Cache/Headless.RateLimiting.Cache.csproj`
  - `src/Headless.RateLimiting.Cache/CacheDistributedRateLimiterStorage.cs` (renamed from `CacheThrottlingDistributedLockStorage`)
  - `src/Headless.RateLimiting.Cache/README.md`
- **Files (delete):**
  - `src/Headless.DistributedLocks.Cache/CacheThrottlingDistributedLockStorage.cs`
- **Approach:**
  - Pure rename + namespace shift. Class implements `IDistributedRateLimiterStorage` from `Headless.RateLimiting.Abstractions`.
  - csproj references: `Headless.RateLimiting.Abstractions`, `Headless.Caching.Abstractions`. No Setup class (mirrors current Cache convention).
  - README.md per `PACKAGE-README-TEMPLATE.md`.
- **Patterns to follow:** `public sealed class` with primary-constructor parameter (matches `CacheDistributedLockStorage` shape).
- **Test suite design:** Behavior verified by integration tests ported in U9 (`Headless.RateLimiting.Cache.Tests.Integration`).
- **Test scenarios:** `Test expectation: none — pure rename of existing storage; behavior tests port in U9.`
- **Verification:**
  - csproj builds.
  - README.md present.

---

### U8. Create `Headless.RateLimiting.Redis` package

- **Goal:** Redis-backed rate-limiter storage + Setup class.
- **Requirements:** R1.4 (Redis portion)
- **Dependencies:** U5
- **Files (new):**
  - `src/Headless.RateLimiting.Redis/Headless.RateLimiting.Redis.csproj`
  - `src/Headless.RateLimiting.Redis/RedisDistributedRateLimiterStorage.cs` (renamed from `RedisThrottlingDistributedLockStorage`)
  - `src/Headless.RateLimiting.Redis/Setup.cs` (class name `RedisRateLimitingSetup`; mirrors `RedisDistributedLockSetup` shape)
  - `src/Headless.RateLimiting.Redis/README.md`
- **Files (delete):**
  - `src/Headless.DistributedLocks.Redis/RedisThrottlingDistributedLockStorage.cs`
- **Approach:**
  - Pure rename + namespace shift on the storage class.
  - `RedisRateLimitingSetup` carries `AddRedisRateLimiter` × 3 overloads + `AddKeyedRedisRateLimiter` × 3 overloads, mirroring the existing `AddRedisThrottlingDistributedLock` / `AddKeyedRedisThrottlingDistributedLock` regions at `src/Headless.DistributedLocks.Redis/Setup.cs:64-146`. Private `_CreateRateLimiterStorage` helper.
  - csproj references: `Headless.RateLimiting.Abstractions`, `Headless.RateLimiting.Core` (Setup needs `AddRateLimiter` from Core), `Headless.Redis`.
  - README.md per `PACKAGE-README-TEMPLATE.md`.
- **Patterns to follow:** `[PublicAPI]` on `RedisRateLimitingSetup`; three-overload + private helper extension-member pattern.
- **Test suite design:** Integration tests ported in U9 (`Headless.RateLimiting.Redis.Tests.Integration`).
- **Test scenarios:** `Test expectation: none — pure rename + Setup; behavior tests port in U9.`
- **Verification:**
  - csproj builds.
  - README.md present.
  - `[PublicAPI]` on Setup class.

---

### U9. Move test projects + rename test types; create rate-limiter test harness

- **Goal:** All throttling tests migrate to new test projects under `Headless.RateLimiting.*`, with type renames applied. Test base class moves to a new harness project.
- **Requirements:** R1.4 (test surface)
- **Dependencies:** U5, U6, U7, U8
- **Files (new):**
  - `tests/Headless.RateLimiting.Tests.Unit/Headless.RateLimiting.Tests.Unit.csproj`
  - `tests/Headless.RateLimiting.Tests.Unit/Fakes/FakeDistributedRateLimiterStorage.cs` (renamed from `FakeThrottlingDistributedLockStorage`)
  - `tests/Headless.RateLimiting.Tests.Unit/SlidingWindowRateLimiterCancellationTests.cs` (renamed from `ThrottlingDistributedLockProviderCancellationTests`)
  - `tests/Headless.RateLimiting.Tests.Unit/SlidingWindowRateLimiterTests.cs` (renamed from `ThrottlingResourceLockProviderTests`)
  - `tests/Headless.RateLimiting.Tests.Unit/InMemoryRateLimiterTests.cs` (renamed from `InMemoryResourceThrottlingLockProviderTests`)
  - `tests/Headless.RateLimiting.Tests.Harness/Headless.RateLimiting.Tests.Harness.csproj`
  - `tests/Headless.RateLimiting.Tests.Harness/DistributedRateLimiterTestsBase.cs` (renamed from `DistributedThrottlingLockProviderTestsBase`)
  - `tests/Headless.RateLimiting.Cache.Tests.Integration/Headless.RateLimiting.Cache.Tests.Integration.csproj`
  - `tests/Headless.RateLimiting.Cache.Tests.Integration/MemoryRateLimiterTests.cs` (renamed from `MemoryDistributedThrottlingLockProviderTests`)
  - `tests/Headless.RateLimiting.Cache.Tests.Integration/RedisCacheRateLimiterTests.cs` (renamed from `RedisDistributedThrottlingLockProviderTests`)
  - `tests/Headless.RateLimiting.Cache.Tests.Integration/TestSetup/CacheTestFixture.cs` (copy + adapt the existing fixture)
  - `tests/Headless.RateLimiting.Redis.Tests.Integration/Headless.RateLimiting.Redis.Tests.Integration.csproj`
  - `tests/Headless.RateLimiting.Redis.Tests.Integration/RedisRateLimiterTests.cs` (renamed from `RedisResourceThrottlingLockProviderTests`)
  - `tests/Headless.RateLimiting.Redis.Tests.Integration/RedisRateLimiterStorageTests.cs` (renamed from `RedisThrottlingDistributedLockStorageTests`)
  - `tests/Headless.RateLimiting.Redis.Tests.Integration/RedisTestFixture.cs` (rate-limiter-only fixture; copy + adapt)
- **Files (delete):**
  - `tests/Headless.DistributedLocks.Tests.Unit/Fakes/FakeThrottlingDistributedLockStorage.cs`
  - `tests/Headless.DistributedLocks.Tests.Unit/ThrottlingLocks/ThrottlingDistributedLockProviderCancellationTests.cs`
  - `tests/Headless.DistributedLocks.Tests.Unit/ThrottlingLocks/ThrottlingResourceLockProviderTests.cs`
  - `tests/Headless.DistributedLocks.Tests.Harness/DistributedThrottlingLockProviderTestsBase.cs`
  - `tests/Headless.DistributedLocks.Cache.Tests.Integration/MemoryDistributedThrottlingLockProviderTests.cs`
  - `tests/Headless.DistributedLocks.Cache.Tests.Integration/RedisDistributedThrottlingLockProviderTests.cs`
  - `tests/Headless.DistributedLocks.InMemory.Tests.Integration/InMemoryResourceThrottlingLockProviderTests.cs`
  - `tests/Headless.DistributedLocks.Redis.Tests.Integration/RedisResourceThrottlingLockProviderTests.cs`
  - `tests/Headless.DistributedLocks.Redis.Tests.Integration/RedisThrottlingDistributedLockStorageTests.cs`
- **Files (modify):**
  - `tests/Headless.DistributedLocks.Redis.Tests.Integration/RedisTestFixture.cs` — drop the throttling-storage property (no longer needed after extraction; rate-limiter equivalent ships in the new Redis-integration fixture).
- **Approach:**
  - Project SDKs: `Headless.NET.Sdk.Test` (per `global.json`).
  - All renames performed mechanically: `Throttling*` → `*RateLimiter*` (provider/lease/storage types and class names), `Throttling`-only-word references → `RateLimiter` or `RateLimiting` per context.
  - `DistributedRateLimiterTestsBase` (new harness) is the renamed throttling base; it parameterizes over `IDistributedRateLimiterStorage` for cross-backend execution.
  - The InMemory throttling test (`InMemoryResourceThrottlingLockProviderTests`) folds into `Headless.RateLimiting.Tests.Unit` rather than spawning a separate `Headless.RateLimiting.InMemory.Tests.Integration` project — it doesn't need Testcontainers.
  - `Cache.Tests.Integration` retains both memory-cache and redis-cache variants (mirrors current `Headless.DistributedLocks.Cache.Tests.Integration` layout).
  - `Redis.Tests.Integration` covers both provider and storage tests (mirrors current `Headless.DistributedLocks.Redis.Tests.Integration` layout for the lock side).
  - Existing test fixtures (`CacheTestFixture`, `RedisTestFixture`) copy and adapt — both already inherit `HeadlessRedisFixture` from `Headless.Testing.Testcontainers`, so the Testcontainers wiring is unchanged.
- **Patterns to follow:** xUnit v3 + MTP, AwesomeAssertions, NSubstitute, Bogus per project CLAUDE.md.
- **Test suite design:** All ported tests retain their original assertion shape; only types and namespaces change. No new test scenarios beyond what's already covered today, except the FakeTimeProvider period-boundary regression from U6 which lives in `Headless.RateLimiting.Tests.Unit`.
- **Test scenarios:** Per-file scenarios are inherited from the originals — see the originals for each ported test. New scenarios are owned by U6 (period-boundary regression).
- **Verification:**
  - `make test-project TEST_PROJECT=tests/Headless.RateLimiting.Tests.Unit/...` passes.
  - `make test-project TEST_PROJECT=tests/Headless.RateLimiting.Cache.Tests.Integration/...` passes (Docker required).
  - `make test-project TEST_PROJECT=tests/Headless.RateLimiting.Redis.Tests.Integration/...` passes (Docker required).
  - Pre-extraction throttling test count is preserved (no test loss) — confirmed by listing tests in old and new projects.
  - Existing distributed-locks regular-lock tests still pass against `Headless.DistributedLocks.*` (no regression).

---

### U10. Remove throttling overloads from `Headless.DistributedLocks.Core.Setup`

- **Goal:** Strip the throttling registration surface out of the lock-package Setup classes. Compile-clean break (no shim).
- **Requirements:** R1.4 (cleanup)
- **Dependencies:** U6, U9
- **Files:**
  - `src/Headless.DistributedLocks.Core/Setup.cs` (modify — remove throttling regions)
- **Approach:**
  - Delete public extension methods: `AddThrottlingDistributedLock` × 3, `AddKeyedThrottlingDistributedLock` × 3.
  - Delete private helpers: `_AddThrottlingDistributedLockCore`, `_AddKeyedThrottlingDistributedLockCore`.
  - Remove any `using` for `Headless.DistributedLocks.ThrottlingLocks` (now-deleted namespace).
  - Verify no remaining references to throttling types in this file.
- **Patterns to follow:** Keep regular-lock surface untouched; preserve existing ordering/grouping.
- **Test suite design:** `Headless.DistributedLocks.Tests.Unit/SetupTests.cs` (existing or new from U4) — confirm regular-lock setup still works; no test for absent throttling APIs needed (compile-time deletion).
- **Test scenarios:**
  - **Regular-lock setup unchanged:** `services.AddDistributedLock<TStorage>(...)` continues to register `IDistributedLockProvider` and (when messaging present) the lock-released consumer.
  - `Test expectation: compile-time removal verified by build success and no lingering throttling references in the file.`
- **Verification:**
  - `make build-project PROJECT=src/Headless.DistributedLocks.Core/...` succeeds with no warnings.
  - `grep -r "Throttling" src/Headless.DistributedLocks.Core/` returns zero matches.

---

### U11. Remove throttling overloads from `Headless.DistributedLocks.Redis.Setup`

- **Goal:** Strip Redis-side throttling registration. Compile-clean break.
- **Requirements:** R1.4 (cleanup)
- **Dependencies:** U8, U9
- **Files:**
  - `src/Headless.DistributedLocks.Redis/Setup.cs` (modify — remove throttling regions)
- **Approach:**
  - Delete public extensions: `AddRedisThrottlingDistributedLock` × 3, `AddKeyedRedisThrottlingDistributedLock` × 3.
  - Delete private `_CreateThrottlingStorage`.
  - Remove `using` lines for now-deleted throttling types.
- **Test suite design:** Build-time only.
- **Test scenarios:** `Test expectation: compile-time removal verified by build success.`
- **Verification:**
  - `make build-project PROJECT=src/Headless.DistributedLocks.Redis/...` succeeds.
  - `grep -r "Throttling" src/Headless.DistributedLocks.Redis/` returns zero matches.

---

### U12. Attach new projects to `headless-framework.slnx`

- **Goal:** Solution file knows about all new RateLimiting src + test projects.
- **Requirements:** R1.4 (build infrastructure)
- **Dependencies:** U5, U6, U7, U8, U9
- **Files:**
  - `headless-framework.slnx` (modify)
- **Approach:**
  - Add new `<Folder Name="/RateLimiting/">` block (mirrors the `/DistributedLocks/` folder at lines 90-99). Place alphabetically near other domain folders (likely between `/Quartz/` and `/Redis/` per existing ordering).
  - List all 4 src projects + 4 test projects flatly inside the folder. No additional Solution config needed (no per-project build mappings).
  - Verify no leftover entries for deleted throttling-side projects (there are none — all throttling moves were within existing src/tests directories that retain non-throttling content).
- **Test suite design:** None (build infrastructure).
- **Test scenarios:** `Test expectation: none — solution-file change verified by build.`
- **Verification:**
  - `make restore && make build` succeeds end-to-end.
  - `make list-projects` lists all 8 new RateLimiting projects.
  - `make test-fast` runs all tests across the solution (Docker-gated integration tests skip cleanly when Docker unavailable).

---

### U13. Documentation sync — new domain doc, updated existing doc, READMEs

- **Goal:** Per `CLAUDE.md` doc-sync rule (public API change + new packages = mandatory doc update) and `docs/authoring/AUTHORING.md` template invariants.
- **Requirements:** R1.4 + R1.1 + R1.3 + R1.6 (cross-cutting docs trigger)
- **Dependencies:** U1-U12
- **Files:**
  - `docs/llms/rate-limiting.md` (new — created from `docs/authoring/TEMPLATE.md`)
  - `docs/llms/distributed-locks.md` (modify — update Abstractions section with `AcquireAsync` + `releaseOnDispose` + exception; update Core section for nullable `IOutboxPublisher`; **remove** the throttling subsections; point readers to `rate-limiting.md`)
  - `docs/llms/index.md` (modify — register `rate-limiting.md` in both the Domain documentation list and the Packages catalog)
  - `src/Headless.DistributedLocks.Abstractions/README.md` (modify — `AcquireAsync` + exception + `releaseOnDispose`; drop throttling references)
  - `src/Headless.DistributedLocks.Core/README.md` (modify — nullable `IOutboxPublisher`; drop throttling references)
  - `src/Headless.DistributedLocks.Cache/README.md` (modify — drop throttling storage reference)
  - `src/Headless.DistributedLocks.Redis/README.md` (modify — drop throttling storage / Setup references)
  - READMEs for new rate-limiting packages already land in U5-U8.
- **Approach:**
  - Copy `docs/authoring/TEMPLATE.md` → `docs/llms/rate-limiting.md`. Fill YAML frontmatter (`domain: Rate Limiting`, `packages: RateLimiting.Abstractions, RateLimiting.Core, RateLimiting.Cache, RateLimiting.Redis`).
  - Per-package H2 sections in fixed order: Abstractions → Core → Cache → Redis. Each section's required H3 sub-sections in fixed order per AUTHORING.md: `Problem Solved`, `Key Features`, `Installation`, `Quick Start`, `Configuration`, `Dependencies`, `Side Effects`.
  - `Agent Instructions` bullets: name `IDistributedRateLimiter` as the entry point; ban "distributed lock" terminology for rate limiting; reference the period-boundary spin-check behavior so future agents don't accidentally re-introduce the bug; document the `SlidingWindowRateLimiterOptions` defaults.
  - **Do not** include language suggesting RedLock-style consensus or multi-instance safety. Same rule applies to `AcquireAsync` updates in `distributed-locks.md`.
  - Regenerate the Table of Contents in both `distributed-locks.md` and `rate-limiting.md` after edits, per AUTHORING.md.
  - Verify the AUTHORING.md `Self-check against invariants` checklist before committing.
- **Patterns to follow:** AUTHORING.md is authoritative.
- **Test suite design:** None (docs).
- **Test scenarios:** `Test expectation: none — documentation-only change.`
- **Verification:**
  - `docs/llms/rate-limiting.md` exists and conforms to template invariants (frontmatter, section order, required H3s, no marketing language).
  - `docs/llms/distributed-locks.md` no longer documents throttling APIs.
  - `docs/llms/index.md` references `rate-limiting.md` in both required places.
  - All 8 affected README.md files are coherent with their package contents.
  - Manual scan of XML doc comments on `AcquireAsync`, `LockAcquisitionTimeoutException`, and the moved rate-limiter types — no RedLock-adjacent wording.

---

## Test Suite Design (Cross-Unit)

Three test suites span this work:

1. **`Headless.DistributedLocks.Tests.*`** — existing suites stay; U2/U3/U4 extend them with `AcquireAsync`, `releaseOnDispose`, nullable-`IOutboxPublisher` coverage in both unit and cross-backend harness tests. The harness base is the shared coverage surface across InMemory / Cache / Redis storage.
2. **`Headless.RateLimiting.Tests.*`** — new suite (U6 + U9). Unit tests for the FakeTimeProvider period-boundary regression and InMemory rate-limiter live in `Tests.Unit`. Cross-backend behavior tests live in `Tests.Harness` (renamed base class) and execute via `Cache.Tests.Integration` and `Redis.Tests.Integration` against real Testcontainers Redis.
3. **Build / DI / docs lint** — `make build` (U10, U11, U12), `make restore` (U12), AUTHORING.md self-check (U13).

No new test infrastructure required beyond the new project skeletons — `HeadlessRedisFixture` from `Headless.Testing.Testcontainers` is reused as-is.

---

## Scope Boundaries

### In scope

Everything in Implementation Units U1-U13.

### Deferred to Follow-Up Work

- **`Headless.Api` exception handler → 408 for `LockAcquisitionTimeoutException`.** The cancellation-vs-timeout learning suggests `IExceptionHandler` should map this to HTTP 408. Phase 1 ships the exception; the mapping is a separate change in `Headless.Api`. Track as a follow-up issue.
- **Splitting `Headless.DistributedLocks.Messaging` into its own optional package.** The simpler nullable-`IOutboxPublisher` + auto-detect-in-Setup approach satisfies R1.6 without removing the `Headless.Messaging.Core` `ProjectReference`. A future change could split the consumer + outbox-publish path into a dedicated optional package for cleaner dependencies. Out of scope for Phase 1.
- **Rate-limiter metrics + OpenTelemetry parity.** Current `ThrottlingDistributedLockProvider` has no metrics surface. `SlidingWindowDistributedRateLimiter` ships without metrics for Phase 1. Adding a `RateLimiterMetrics` source-gen counter set (matching `DistributedLockMetrics`) is a follow-up.
- **Migrating tests off `DistributedLockProviderTestsBase` for the new throwing variant.** Phase 1 adds `AcquireAsync` cases to the existing base; a deeper refactor of the harness organization is out of scope.

### Out of scope (Phase 2-4 territory per #287)

- Lease lifecycle changes (`HandleLostToken`, auto-extension) — Phase 2 (#289).
- New lock primitives (RW lock, semaphore, composite) — Phase 3 (#290-#292).
- New storage backends (Postgres, SQL Server, Azure blob) — Phase 4 (#293-#295).
- Multi-instance / RedLock semantics — explicitly rejected (`docs/solutions/tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md`).
- Cross-cutting fencing-token / safety-categories docs — #298.

---

## Risk Analysis & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Period-boundary spin-fix regresses under non-Linux runtimes where timer slop differs | Medium | Medium | `FakeTimeProvider` test (U6) is platform-independent and asserts the spin-wait detects key rotation regardless of how early the underlying `Delay` returns. Integration tests on Linux CI provide real-runtime confirmation. |
| Auto-detect-messaging probe in Setup fails when consumers use unusual DI patterns (e.g., `IOutboxPublisher` registered via factory after `AddDistributedLock` runs) | Low | Medium | Document the probe behavior in the Setup XML doc comment and `distributed-locks.md`. Recommend `AddDistributedLock` is called **after** all messaging registration. If a consumer hits this, the warning log fires at first provider resolution — debuggable. |
| `LockAcquisitionTimeoutException` propagating through API endpoints surfaces as 500 (not 408) because Phase 1 defers the API mapping | Medium | Low | Documented in Deferred Follow-Up. Until the API mapping ships, applications can register a per-app `IExceptionHandler` mapping. |
| Tests ported from `Headless.DistributedLocks.Tests.*` accidentally drop coverage during the rename | Low | High | Pre-extraction baseline: count tests in old throttling test files (use `make list-tests` filtered to throttling). Re-count after U9 — must match (or exceed when U6's period-boundary tests are added). |
| `docs/llms/distributed-locks.md` and `docs/llms/rate-limiting.md` drift after the split | Medium | Medium | U13 self-checks both docs against AUTHORING.md invariants before commit. The wrapper-drift learning (`docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md`) is the standing reminder to land docs in the same PR. |
| Solution-file `<Folder>` entry for `/RateLimiting/` placed in the wrong order, causing slnx merge conflicts later | Low | Low | Place alphabetically per existing convention (verify against `/DistributedLocks/`, `/Quartz/`, `/Redis/` positions). |

---

## Dependencies / Prerequisites

- **None outside this repo.** Phase 1 is the foundation of the distributed-locks roadmap (#287).
- **Internal:** `Headless.Checks`, `Headless.Hosting`, `Headless.Caching.Abstractions`, `Headless.Redis`, `Headless.Testing.Testcontainers`, `Headless.Messaging.Core` (still referenced from `Headless.DistributedLocks.Core` per scope decision) — all already in the solution.
- **Tooling:** Docker required for integration tests (Testcontainers). Standard `make` targets.

---

## Phased Delivery

The 13 units group naturally into three phases that can each ship as a coherent commit cluster:

- **Phase A — Lock ergonomics (U1-U4).** Independent of Phase B. Adds the throwing variant, `releaseOnDispose`, and the optional `IOutboxPublisher`. No package boundary changes.
- **Phase B — RateLimiting package family (U5-U9).** Independent of Phase A. Creates the new packages, moves the provider + storage + tests, fixes the spin-bug during the move (U6 carries the characterization test).
- **Phase C — Cleanup + docs (U10-U13).** Depends on Phase B landing. Removes the now-empty throttling Setup regions, attaches everything to the solution file, and syncs all docs.

Phase A and Phase B can ship in either order or in parallel branches. Phase C must follow Phase B. **Ordering caveat:** Phase A (U4) and Phase C (U10) both modify `src/Headless.DistributedLocks.Core/Setup.cs` — U4 inside `_AddDistributedLockCore`, U10 deleting `_AddThrottlingDistributedLockCore` and the public `AddThrottlingDistributedLock` overloads. They touch different regions, so merge conflict potential is low, but land Phase A before Phase C to avoid file-level churn. Phase B creates new files and does not conflict with A or C on shared source. All three phases ship together as one feature delivery for issue #288, but the phasing exists so reviewers can read U1-U4 independently of U5-U13.

---

## Verification Criteria (Phase-Level)

- All acceptance criteria from issue #288 satisfied:
  - `AcquireAsync` throws `LockAcquisitionTimeoutException` on linked-token cancellation; exception carries `Resource` (covered by U2 tests).
  - `TryAcquireAsync` continues to return `null`; both behaviors covered by integration tests (U2 + harness base).
  - `releaseOnDispose: false` — dispose no-op; explicit `ReleaseAsync` releases; double-release idempotent (U3).
  - Lock provider constructible and usable without `IOutboxPublisher`; warning logged exactly once at startup (U4).
  - Throttling namespace no longer exists in `Headless.DistributedLocks.*`; consumers use `Headless.RateLimiting.*` (U10, U11, grep verification).
  - Period-boundary bug reproducible with `FakeTimeProvider` advancing `period - 1ms` then `period`; fix path completes within one period plus spin cap (U6).
  - Coverage targets met per CLAUDE.md: ≥85% line, ≥80% branch (run `make coverage` after U13).
  - Docs sync: `docs/llms/distributed-locks.md` updated; `docs/llms/rate-limiting.md` created; all affected READMEs updated (U13).
- `make test` green end-to-end (Docker available).
- `make build` zero warnings.
- `grep -r "Throttling\|IThrottling" src/Headless.DistributedLocks.*` returns zero matches outside test-data strings (sanity check).

---

## Documentation Plan

Tracked entirely in U13:

- New: `docs/llms/rate-limiting.md`
- Updated: `docs/llms/distributed-locks.md`, `docs/llms/index.md`
- Updated: 4 existing READMEs (`Abstractions`, `Core`, `Cache`, `Redis` distributed-locks side)
- New: 4 READMEs (`Abstractions`, `Core`, `Cache`, `Redis` rate-limiting side, created in U5-U8)
- Self-check against AUTHORING.md invariants is part of U13 verification.

---

## Operational / Rollout Notes

Greenfield framework — no production rollout. Breaking change is acceptable per `CLAUDE.md`. Downstream consumers must update their using-statements and DI registration calls in the same change that bumps the `Headless.DistributedLocks` and `Headless.RateLimiting` package versions. The follow-up 408 mapping in `Headless.Api` should land before or alongside Phase 2.

---

## Unresolved Questions

None blocking. The decisions taken in Key Technical Decisions cover the choices flagged during research:

1. ✅ Exception hierarchy — `LockAcquisitionTimeoutException : DistributedLockException : Exception` (not `TimeoutException`); API 408 mapping deferred.
2. ✅ Warning log emission style — constructor-time source-gen `[LoggerMessage]`, mirrors messaging EventId 77 pattern.
3. ✅ Messaging dependency posture — keep `ProjectReference`, nullable ctor, auto-detect in Setup. Package split deferred.
4. ✅ Cache rate-limiter Setup class — no Setup (mirrors `Headless.DistributedLocks.Cache`).
5. ✅ Renaming scope — options + storage interface also renamed (`SlidingWindowRateLimiterOptions`, `IDistributedRateLimiterStorage`).
6. ✅ EventId numbering — reset to 1-N in new `Headless.RateLimiting.Core` log partial.
7. ✅ Rate-limiter metrics parity — deferred to follow-up.
8. ✅ Execution posture for the bug fix — characterization-first (U6 execution note).
