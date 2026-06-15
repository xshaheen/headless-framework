---
date: 2026-06-15
topic: distributed-locks-safety-deadline-eventid
---

# Distinguish safety-deadline-fired from contended acquire in non-blocking lock logs

## Summary

Make a non-blocking try-once acquire that trips its internal safety deadline
(`_NonBlockingAcquireDeadline = 10s`) distinguishable from routine contention in
both observability layers: a dedicated Warning-level log EventId for the
per-event breadcrumb, and a `reason` dimension (`contended` | `stalled`) on the
existing failure counter for alertable rate detection. Apply across all three
zero-path acquire sites — regular lock, reader/writer lock, and semaphore — with
unit coverage and synced LLM/README docs. The caller-facing return contract is
unchanged (`null` for both cases); surfacing a stall to callers is deferred to a
distinct `AcquireAsync` exception (out of scope here).

## Problem Frame

PR #284 added a zero-path bypass to the non-blocking (`AcquireTimeout =
TimeSpan.Zero`) acquire path. To stop a single storage call from hanging
indefinitely when the caller's token never fires and the lock-store stalls, the
path bounds the attempt with an internal safety deadline. When that deadline
trips, the code catches the resulting `OperationCanceledException`, maps it to a
failed acquire, and emits the same EventId as a normal contended miss.

The result is an operational blind spot: a spike of safety-deadline-fired
entries (a lock-store outage in progress) is categorically indistinguishable
from a spike of contended-acquire entries (workload pressure on one resource).
Time-to-detect for lock-store degradation drops to "an operator notices the
elapsed-time distribution shifted" rather than "an EventId matched an alert
rule." Issue #320 was re-validated against `origin/main` on 2026-06-10 and the
code still carries a `// #320` marker at the regular-lock catch site.

## Key Decisions

- **Cover all three non-blocking acquire sites, not just the regular lock.** The
  identical safety-deadline pattern (`_NonBlockingAcquireDeadline = 10s` →
  `catch (OperationCanceledException)` → mapped to failed) exists in the regular
  lock, the reader/writer lock, and the semaphore. The issue scoped only the
  regular lock; fixing one and leaving two silent reproduces the same blind spot
  on the other two providers at near-zero marginal cost. Closing all three is the
  consistent operational contract.

- **Warning level.** A safety-deadline trip means a storage stall past 10s — an
  actionable health signal, not routine telemetry. Warning matches the existing
  degradation signals in the same logger (`LockStorageRetry`,
  `LockReleaseTimedOut`) and makes alert/threshold rules trivial.

- **Emit on the safety-deadline branch of the existing OCE catch.** In each
  site, the `catch (OperationCanceledException)` block already distinguishes
  caller cancellation (rethrows) from the safety deadline (falls through when the
  caller's token is *not* cancelled). That fall-through branch *is* the
  safety-deadline signal — emitting the new EventId there is cleaner than
  threading a flag into the shared post-attempt fall-through block, where the
  failed result no longer carries its cause.

- **Two value statements, one change.** Only the regular lock currently logs
  `LogFailedToAcquireLockAfter` on the zero-path, so for it the new EventId
  *disambiguates an existing log*. The reader/writer and semaphore zero-paths log
  nothing on failure today, so for them the EventId is *net-new stall
  visibility*. The behavior contract (new EventId on deadline trip) is identical;
  only the framing differs per provider.

- **Distinguish in observability, not in the return contract.** The contended
  vs stalled distinction lives in logs and metrics — never in the
  `TryAcquireAsync` return value. `TryAcquireAsync` keeps its binary contract
  (`null` = not acquired for any reason, always safe to not enter the section);
  collapsing both cases there never violates the lock safety invariant. Adding a
  third return outcome would force every consumer to branch on storage health to
  stay correct — high carrying cost for a need no in-repo consumer has, and a
  regression of the framework's existing Try (null on any miss) / `AcquireAsync`
  (throws) split.

- **Metrics carry the distinction too, not just logs.** The issue deferred the
  metrics tier; this brings it in because the goal — fast time-to-detect for
  lock-store degradation — is served by an alertable counter dimension, not by
  log scraping. The existing failure counters are source-generated and
  tag-capable, so the distinction is one attribute dimension plus a tagged
  `Add` call. Log gives the per-event breadcrumb; metric gives the rate an alert
  fires on.

- **Caller-facing distinction belongs in `AcquireAsync`, deferred.** If a
  consumer ever needs to *react* to storage-down (fail-closed leader election, a
  circuit breaker), the consistent home is the throwing wrapper — a distinct
  `LockStoreUnavailableException` separate from the existing
  `LockAcquisitionTimeoutException` — not a third value on the `Try` return. No
  in-repo consumer needs this today; filed as follow-up.

## Requirements

**Logging signal**

- R1. A new `LoggerMessage` EventId in `RegularLockLoggerExtensions`
  (`src/Headless.DistributedLocks.Core/RegularLocks/LoggerExtensions.cs`) fires
  at `Warning` level when a non-blocking try-once acquire trips its safety
  deadline. Suggested signature: `LogTryOnceSafetyDeadlineFired(string resource,
  string leaseId, TimeSpan elapsed)` (align the param name to the file's existing
  `leaseId` convention rather than the issue's `lockId`).

- R2. The EventId is wired into all three non-blocking acquire sites:
  `DistributedLock.cs`, `DistributedReadWriteLock.cs`, and
  `DistributedSemaphoreProvider.cs` (`RegularLocks/` and `ReaderWriterLocks/`).
  All three share the single `RegularLockLoggerExtensions` class.

- R3. Routine contended acquires (the storage call returns "not acquired"
  without the deadline tripping) continue to log via `LogFailedToAcquireLockAfter`
  on the regular lock and are unchanged on the reader/writer and semaphore paths.

- R4. Caller cancellation (the caller's own token fires) does not emit the new
  EventId — it continues to rethrow `OperationCanceledException` as today. The
  EventId fires only on the safety-deadline branch where the caller's token is
  not cancelled.

- R5. The EventId number is the next clean append in the
  `RegularLockLoggerExtensions` block. The block currently ends at 23; 24–29 and
  18 are free. Append at **24** — avoid reusing the retired 18 slot (operator
  dashboards may still key on its prior meaning). Final number is confirmed at
  planning time against the live file.

**Metrics**

- R6. The existing failure counters gain a `reason` dimension with values
  `contended` and `stalled`. The safety-deadline branch increments with
  `reason=stalled`; every other failure path (contention, swallowed transient
  errors) increments with `reason=contended`. The total count is unchanged — the
  tag splits the existing counter, it does not add a new instrument or
  double-count.

- R7. The tag covers all three providers without a new instrument: the regular
  lock and the reader/writer lock both increment `headless.lock.failed`, and the
  semaphore increments `headless.semaphore.failed`. Tagging the existing `Add`
  call sites on both counters is sufficient.

**Tests**

- R8. A unit test asserts the new EventId fires when the safety deadline trips,
  using the existing `_HangForever`-style storage substitute (the hanging-insert
  pattern in the `DistributedLocks.Tests.Unit` suite) plus a capturing logger
  provider, driven by the test `TimeProvider` so the 10s deadline is
  deterministic without real waiting.

- R9. The same (or a sibling) unit test asserts the new EventId does NOT fire for
  a contended-only acquire (storage returns "not acquired" promptly), and that
  the regular lock still logs `LogFailedToAcquireLockAfter` in that case.

- R10. A test asserts the metric `reason` dimension: the failure counter records
  `reason=stalled` on the deadline-trip scenario and `reason=contended` on the
  contended scenario.

- R11. Coverage exists for each provider that received the wiring (regular,
  reader/writer, semaphore) — the assertions that the deadline-trip EventId fires
  and the metric is tagged `stalled` hold for all three, not just the regular
  lock.

**Documentation**

- R12. The new EventId and the metric `reason` dimension are documented in
  `docs/llms/distributed-locks.md` and, if it carries a distributed-lock EventId
  table, `docs/llms/messaging.md`.

- R13. The package README (`src/Headless.DistributedLocks.Core/README.md`) is
  kept in lockstep with the LLM doc per the repo's two-surface authoring rule —
  both the README and `docs/llms/distributed-locks.md` reflect the new log and
  metric signals.

## Acceptance Examples

- AE1. Safety deadline trips (regular lock).
  - **Given:** a non-blocking acquire (`AcquireTimeout = TimeSpan.Zero`) whose
    storage `InsertAsync` hangs and a caller token that never cancels.
  - **When:** the test `TimeProvider` advances past `_NonBlockingAcquireDeadline`
    (10s) and the safety CTS fires.
  - **Then:** the new safety-deadline EventId is logged at Warning; the failure
    counter records `reason=stalled`; the acquire returns null.

- AE2. Routine contention (regular lock).
  - **Given:** a non-blocking acquire whose storage `InsertAsync` returns "not
    acquired" promptly (lock held by another holder).
  - **When:** the acquire completes without the deadline tripping.
  - **Then:** the new EventId is NOT logged; `LogFailedToAcquireLockAfter` is
    logged as today; the failure counter records `reason=contended`; the acquire
    returns null.

- AE3. Caller cancellation (any provider).
  - **Given:** a non-blocking acquire whose caller token is cancelled while the
    storage call is in flight.
  - **When:** the resulting `OperationCanceledException` is caught.
  - **Then:** the new EventId is NOT logged; the exception rethrows (existing
    behavior) after best-effort orphan cleanup.

## Scope Boundaries

In scope (expands the issue, which deferred metrics):

- Log EventId distinction across all three providers.
- Metric `reason` dimension (`contended` | `stalled`) on the existing failure
  counters.

Deferred / out of scope:

- Caller-facing distinction — `TryAcquireAsync` keeps returning `null` for both
  cases. Surfacing a stall to callers (a distinct `LockStoreUnavailableException`
  on `AcquireAsync`, separate from `LockAcquisitionTimeoutException`) is a
  follow-up; no in-repo consumer needs it today.
- Promoting the safety deadline to `DistributedLockOptions` and tuning its 10s
  value — deferred per the PR #284 plan; tracked alongside `_LockSafetyMargin`
  (#300). Note: the log/metric is retrospective and does not reduce the latency
  hit a tripped deadline already incurred; tightening the deadline is the lever
  that makes "non-blocking" honor its name.
- Changing the contended-miss log shape — purely additive signal, no existing
  behavior changes.

## Sources / Research

- Issue #320 — this requirement; re-validated against `origin/main` 2026-06-10.
- PR #284 — introduced the zero-path bypass and the safety deadline.
- Issue #297 — original perf scope, closed by #284.
- Issue #300 — deferred `_LockSafetyMargin` tuning (adjacent).
- Code: `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLock.cs`
  (`_TryAcquireOnceAsync`, OCE catch carries the `// #320` marker);
  `src/Headless.DistributedLocks.Core/ReaderWriterLocks/DistributedReadWriteLock.cs`
  (`_TryAcquireOnceAsync`);
  `src/Headless.DistributedLocks.Core/RegularLocks/DistributedSemaphoreProvider.cs`
  (inline zero-path);
  `src/Headless.DistributedLocks.Core/RegularLocks/LoggerExtensions.cs`
  (`RegularLockLoggerExtensions`, EventIds 1–23).
- EventId map (verified): used 1–17, 19–23, 30–32; free 18, 24–29.
