---
title: "feat: DistributedLocks Phase 3a — Redis-backed reader-writer lock"
type: feat
status: completed
created: 2026-05-24
depth: Standard
origin_issue: https://github.com/xshaheen/headless-framework/issues/290
related_issues:
  - https://github.com/xshaheen/headless-framework/issues/287
  - https://github.com/xshaheen/headless-framework/issues/289
reference_implementation: madelson/DistributedLock (RedisDistributedReaderWriterLock)
---

# feat: DistributedLocks Phase 3a — Redis-backed reader-writer lock

---

## Summary

Phase 3a of the DistributedLocks enhancement adds a distributed **reader-writer lock**: multiple readers may hold a resource concurrently while writers acquire exclusively. Provider methods live alongside the existing mutex provider, and the returned handle reuses the Phase 2 `IDistributedLock` shape so `HandleLostToken` and the auto-extension flow apply uniformly to read and write handles.

Storage uses two Redis keys per resource — a writer flag (string holding the writer's lock id, or a writer-waiting marker) and a readers set — coordinated by Lua scripts for atomicity. Writer-preference is enforced: once a writer queues, new reader acquires are blocked until the writer either acquires or releases its waiting marker.

Non-upgradeable. No multi-instance RedLock variant. Cache backend is out of scope after OQ4 analysis — the generic `ICache` contract cannot atomically coordinate across two keys.

---

## Problem Frame

A regular distributed mutex over-serializes read-heavy workloads where concurrent readers are safe and a writer is the only operation requiring exclusion (cache rebuild, config hot-reload, admin write vs. fan-out read). Consumers need a primitive that mirrors `ReaderWriterLockSlim`'s semantics across processes, while composing with the existing lease-monitor + auto-extend flow shipped in Phase 2.

The Redis backend is well understood (madelson's `RedisDistributedReaderWriterLock` is the reference for the Lua-script algorithm). Headless's contribution is the integration: provider/storage abstraction that fits the existing package layout, lease-monitor integration so RW handles surface `HandleLostToken` identically to mutex handles, and the same options/setup ergonomics.

---

## Key Technical Decisions

### D1. Handle type — reuse `IDistributedLock`; no new handle interface.

`IDistributedLock` already carries everything a read or write handle needs (`LockId`, `Resource`, `HandleLostToken`, `IsMonitored`, `RenewAsync`, `ReleaseAsync`, `DateAcquired`, `TimeWaitedForLock`, `RenewalCount`). Introducing a marker `IDistributedReaderWriterLock : IDistributedLock` adds API surface without behavioral value — consumers do not need to discriminate read vs. write from the handle (they already know which method they called). If a future need arises (e.g., callers receiving handles from a registry), it can be added non-breakingly.

The issue's phrase "`IDistributedReaderWriterLock` (handle interface)" is treated as loose phrasing — the rest of the issue ("returned handle is the same `IDistributedLock` shape") settles it.

### D2. Fairness — writer-preference via writer-waiting flag.

Matches madelson. Without writer-preference, a steady stream of read acquires can starve writers indefinitely. The mechanism: when a writer call cannot acquire (readers present), it claims the writer key with its own lock id + a `:_WRITERWAITING` suffix; the read-acquire script checks for the writer key and refuses if present (whether the value is a real writer or a waiting marker). The waiting writer then re-runs the acquire loop on the standard retry cadence until readers drain.

Documented in the `IDistributedReaderWriterLockProvider` XML doc and in `docs/llms/distributed-locks.md`.

### D3. Storage shape — two Redis keys per resource (writer string + readers set).

Algorithm follows madelson:

- `{resource}:writer` — writer key (string): empty | `<writerLockId>` (held) | `<writerLockId>:_WRITERWAITING` (queued).
- `{resource}:readers` — readers key (set): each reader's lock id is a set member; expiry is the max active reader's TTL.

Single-hash alternative was considered (one Redis key with `HSET writer ... readers ...`) but rejected: the readers set + cardinality check is cleaner with `SADD` / `SCARD` / `SREM` than emulating set ops inside a hash. Redis cluster slot-hashing co-locates both keys via the `{resource}` brace-delimited hash tag, so the storage layer must build keys as `"{" + resource + "}:writer"` and `"{" + resource + "}:readers"` (see Hash-tag enforcement under Risks).

### D8. Writer-waiting cleanup reuses `ReleaseWriteAsync`.

When a write acquire fails (timeout or cancellation) after planting the waiting marker, the provider invokes `ReleaseWriteAsync(resource, waitingId, ct)` — the same script that clears a fully-acquired writer. The script's "delete if value equals expected" semantic naturally handles both cases without a dedicated cleanup method. Keeps the storage interface lean (8 members; no extra `ClearWriteWaitingAsync`).

### D4. OQ4 spike result — Redis-only, defer cache backend.

`ICache` exposes mutex-shaped primitives (`TryInsertAsync`, `TryReplaceIfEqualAsync`, `RemoveIfEqualAsync`) that operate on a single key. The RW lock's read-acquire requires *check writer flag absent AND mutate readers set* atomically across two keys — outside the contract. Adding new `ICache` primitives (e.g., `AddToSetIfOtherKeyAbsentAsync`) to support this would leak Redis semantics into the generic cache abstraction. Defer until a concrete consumer asks; revisit by introducing a backend-specific RW lock storage rather than expanding `ICache`.

Documented in `docs/llms/distributed-locks.md` and the `Headless.DistributedLocks.Cache` README ("Cache provider supports mutex only; RW lock is Redis-only").

### D5. Storage interface lives in Core, not Abstractions.

Consistent with `IDistributedLockStorage`, which lives in `Headless.DistributedLocks.Core`. The interface is implementation-facing; only the provider/handle/options live in Abstractions.

### D6. Reuse `DistributedLockAcquireOptions` for both modes.

Read and write acquires share semantics for `TimeUntilExpires`, `AcquireTimeout`, `ReleaseOnDispose`, and `Monitoring`. A separate `DistributedReaderWriterLockAcquireOptions` would duplicate the type for no behavioral delta. If mode-specific options appear later (e.g., max reader count cap), introduce a derived/specialized type then.

### D7. Read renewal extends TTL on the readers set only; write renewal extends the writer key.

The lease monitor's `RenewOrValidateLeaseAsync` dispatches based on which mode the handle was acquired in — backed by distinct Lua scripts (`TryExtendRead` and `TryExtendWrite`). Each mode owns its own key; readers do not interfere with each other's TTL extensions because the set TTL is `max(lifetime)` of any participating reader.

---

## High-Level Technical Design

```
Provider (IDistributedReaderWriterLockProvider)
  AcquireReadLockAsync / TryAcquireReadLockAsync
  AcquireWriteLockAsync / TryAcquireWriteLockAsync
      |
      v
DistributedReaderWriterLockProvider (Core)
  - retry loop (driven by DistributedLockOptions.AcquireRetryDelay; identical cadence to mutex)
  - on each iteration: invokes storage TryAcquireRead / TryAcquireWrite
  - on success: builds DisposableReaderWriterLock handle, attaches LeaseMonitor (when Monitoring != None)
  - publishes "released" wake-up via outbox (when configured) on release — same channel as mutex
      |
      v
IDistributedReaderWriterLockStorage (Core)
  TryAcquireReadAsync(resource, lockId, ttl, ct)        -> bool
  TryExtendReadAsync(resource, lockId, ttl, ct)         -> bool
  ReleaseReadAsync(resource, lockId, ct)                -> ValueTask
  TryAcquireWriteAsync(resource, lockId, ttl, ct)       -> bool
  TryExtendWriteAsync(resource, lockId, ttl, ct)        -> bool
  ReleaseWriteAsync(resource, lockId, ct)               -> ValueTask
  ValidateReadAsync(resource, lockId, ct)               -> bool   // polling-mode liveness check
  ValidateWriteAsync(resource, lockId, ct)              -> bool   // polling-mode liveness check
      |
      v
RedisReaderWriterLockStorage (Redis)
  - Lua scripts loaded via HeadlessRedisScriptsLoader
  - keys: {resource}, {resource}:readers (Redis cluster hash-tag {resource} co-locates them)

LUA SCRIPTS (directional sketch — illustrates intent, not the final source):

  -- TRY ACQUIRE READ (KEYS: writerKey, readerKey; ARGV: lockId, expiryMs)
  if redis.call('EXISTS', writerKey) == 1 then return 0 end
  redis.call('SADD', readerKey, lockId)
  if redis.call('PTTL', readerKey) < expiryMs then
      redis.call('PEXPIRE', readerKey, expiryMs)
  end
  return 1

  -- TRY ACQUIRE WRITE (KEYS: writerKey, readerKey; ARGV: lockId, waitingId, expiryMs)
  local writerValue = redis.call('GET', writerKey)
  if writerValue == false or writerValue == waitingId then
      if redis.call('SCARD', readerKey) == 0 then
          redis.call('SET', writerKey, lockId, 'PX', expiryMs)
          return 1
      end
      if writerValue ~= false then
          redis.call('PEXPIRE', writerKey, expiryMs)
      else
          -- claim writer-waiting marker so new readers are blocked
          redis.call('SET', writerKey, waitingId, 'PX', expiryMs)
      end
  end
  return 0
```

*This illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce. Final scripts must include argument-name verification, `:_WRITERWAITING` suffix handling, and the matching extend/release scripts.*

---

## Scope Boundaries

In scope:

- `IDistributedReaderWriterLockProvider` in `Headless.DistributedLocks.Abstractions`
- `IDistributedReaderWriterLockStorage` in `Headless.DistributedLocks.Core`
- `DistributedReaderWriterLockProvider` (concrete) in `Headless.DistributedLocks.Core`
- `RedisDistributedReaderWriterLockStorage` in `Headless.DistributedLocks.Redis` with Lua scripts (acquire/extend/release × read/write + validate)
- DI Setup: `RedisDistributedReaderWriterLockSetup` (three overloads — `IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`) and a core `AddDistributedReaderWriterLock<TStorage>` registration triad
- Lease-monitor integration so `HandleLostToken` and `Monitoring = AutoExtend` work for both read and write handles
- Integration tests against Testcontainers Redis covering acceptance criteria from the issue
- Documentation sync: `docs/llms/distributed-locks.md`, `src/Headless.DistributedLocks.Abstractions/README.md`, `src/Headless.DistributedLocks.Core/README.md`, `src/Headless.DistributedLocks.Redis/README.md`

### Deferred to Follow-Up Work

- Upgradeable read locks (`IDistributedUpgradeableReaderWriterLock`) — revisit on consumer demand
- Multi-instance / RedLock variant of RW lock
- `Headless.DistributedLocks.Cache` RW backend — requires `ICache` contract additions; defer
- `Headless.DistributedLocks.EntityFramework` RW backend (if/when an EF storage lands) — out of scope until the mutex EF backend exists
- Phase 3b (semaphore) and Phase 3c (composite) — separate issues (#290 follow-up tracking #287)
- `IsWriterWaitingAsync(resource)` observability method — useful for debugging writer starvation but not required by any AC; add when a consumer or oncall scenario surfaces the need

---

## Dependencies

- **#289 (Phase 2 — lease monitor)** has landed on this branch (`a0071734f`). New RW handles must implement `LeaseMonitor.ILeaseHandle` to plug into the existing monitor + `LeaseMonitorRegistry`.
- `HeadlessRedisScriptsLoader` (in `Headless.Redis`) hosts script preloading; add the 7 new RW scripts (acquire-read, extend-read, release-read, acquire-write, extend-write, release-write, validate-read, validate-write — but validate-read and -write can be `Exists`-based, so likely 6 Lua scripts) to its load list.
- No new package dependencies.

---

## Requirements

| Origin | Requirement | Covered by |
|---|---|---|
| R3.1 | Provider exposes 4 acquire methods returning `IDistributedLock` | U1, U4 |
| R3.1 | Returned handle exposes `HandleLostToken` (Phase 2) | U4, U6 |
| R3.1 | Auto-extension flows through both modes | U4, U6 |
| R3.2 | Lua-based atomic state transitions for read/write acquire/release/extend | U3 |
| R3.2 | New `IDistributedReaderWriterLockStorage` in Core | U2 |
| R3.2 | Redis implementation in `Headless.DistributedLocks.Redis` | U3, U5 |
| OQ4 | Cache backend spike conclusion | D4, U8 (docs) |
| AC | Multiple concurrent readers | U7 |
| AC | Write lock blocks until readers release | U7 |
| AC | Writer-preference: queued writer blocks new readers | D2, U3, U7 |
| AC | `HandleLostToken` fires on lease loss for both modes | U6, U7 |
| AC | Auto-extension works on both modes | U6, U7 |
| AC | Coverage targets per CLAUDE.md (≥85% line / ≥80% branch) | U7 |
| AC | Docs sync | U8 |
| Setup | `Setup{Provider}RedisDistributedReaderWriterLock` with three overloads | U5 |

---

## Implementation Units

### U1. Define `IDistributedReaderWriterLockProvider` interface

**Goal:** Public API surface for acquiring read/write handles, mirroring `IDistributedLockProvider`'s ergonomics.

**Requirements:** R3.1.

**Dependencies:** none (interface only).

**Files:**

- `src/Headless.DistributedLocks.Abstractions/ReaderWriterLocks/IDistributedReaderWriterLockProvider.cs` (new)
- `src/Headless.DistributedLocks.Abstractions/ReaderWriterLocks/NullDistributedReaderWriterLockProvider.cs` (new — mirror of `NullDistributedLockProvider`)
- `src/Headless.DistributedLocks.Abstractions/ReaderWriterLocks/DistributedReaderWriterLockProviderExtensions.cs` (new — string-only acquire overloads if needed)

**Approach:**

- Mirror `IDistributedLockProvider` shape: 4 acquire methods (`AcquireReadLockAsync`, `TryAcquireReadLockAsync`, `AcquireWriteLockAsync`, `TryAcquireWriteLockAsync`) each taking `(string resource, DistributedLockAcquireOptions?, CancellationToken)`.
- Return type is `IDistributedLock` (D1) — `Try*` returns `IDistributedLock?`.
- Expose `DefaultTimeUntilExpires` and `DefaultAcquireTimeout` properties (parity with mutex provider).
- Inspection methods (`IsReadLockedAsync(resource)`, `IsWriteLockedAsync(resource)`, `GetReaderCountAsync(resource)`) — minimal observability surface, callers should not rely on these for correctness. Document this in XML docs.
- `[PublicAPI]` on the interface and the null impl.
- XML docs reference D2 (writer-preference) and Phase 2's lease-monitor behavior.
- `NullDistributedReaderWriterLockProvider`: every acquire succeeds immediately and returns a no-op handle (matching the null mutex provider's "tests-only / no contention" behavior).

**Patterns to follow:**

- `src/Headless.DistributedLocks.Abstractions/RegularLocks/IDistributedLockProvider.cs` (signature shape, XML doc style, `[PublicAPI]` placement).
- `src/Headless.DistributedLocks.Abstractions/RegularLocks/NullDistributedLockProvider.cs` (no-op impl).
- Namespace convention: `Headless.DistributedLocks` (single namespace via `#pragma warning disable IDE0130`).

**Test suite design:** No tests for this unit alone — interface-only, contract verified through U4 and U7.

**Test scenarios:** Test expectation: none — interface and null provider are scaffolding; behavior is verified through the concrete provider in U4 and the integration tests in U7.

**Verification:**

- Solution builds with `make build`.
- `make format-check` passes.
- No `Headless.DistributedLocks.Abstractions` API analyzer warnings introduced.

---

### U2. Define `IDistributedReaderWriterLockStorage` interface

**Goal:** Provider-agnostic storage contract for RW lock atomic ops.

**Requirements:** R3.2, D5.

**Dependencies:** U1 (so the interface can reference `IDistributedLock`-shape contract in XML docs).

**Files:**

- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/IDistributedReaderWriterLockStorage.cs` (new)

**Approach:**

- 8 members:
  - `TryAcquireReadAsync(string resource, string lockId, TimeSpan? ttl, CancellationToken)` → `ValueTask<bool>`
  - `TryExtendReadAsync(string resource, string lockId, TimeSpan? ttl, CancellationToken)` → `ValueTask<bool>`
  - `ReleaseReadAsync(string resource, string lockId, CancellationToken)` → `ValueTask` (idempotent — no error if reader is not in set)
  - `TryAcquireWriteAsync(string resource, string lockId, string waitingId, TimeSpan? ttl, CancellationToken)` → `ValueTask<bool>` (waitingId is the writer-waiting marker, e.g., `lockId:_WRITERWAITING`)
  - `TryExtendWriteAsync(string resource, string lockId, TimeSpan? ttl, CancellationToken)` → `ValueTask<bool>`
  - `ReleaseWriteAsync(string resource, string lockId, CancellationToken)` → `ValueTask` (no-op if writer key holds a different value — defensive against stale releases after lease loss)
  - `IsReadLockedAsync(string resource, CancellationToken)` → `ValueTask<bool>` (any reader present)
  - `IsWriteLockedAsync(string resource, CancellationToken)` → `ValueTask<bool>` (writer key holds a non-waiting value)
  - `GetReaderCountAsync(string resource, CancellationToken)` → `ValueTask<long>`
- XML docs note: implementations must guarantee atomicity of the acquire/extend/release primitives within their backend (Redis = single Lua script per op).
- Internal-facing interface; mark `internal` if Headless's convention allows (but `IDistributedLockStorage` is public — match that for consistency, since custom storage factories accept it; D5 keeps it in Core).

**Patterns to follow:**

- `src/Headless.DistributedLocks.Core/RegularLocks/IDistributedLockStorage.cs` for signature shape and `ValueTask` discipline.
- Naming: read/write methods name their mode in the verb (`TryAcquireReadAsync`, not `TryAcquireAsync` with a mode enum) — keeps Lua-script dispatch obvious.

**Test suite design:** No tests for this unit alone — verified via U3's concrete impl + U7's integration tests.

**Test scenarios:** Test expectation: none — pure contract.

**Verification:**

- Solution builds.
- The interface is referenced (build-time) by U3 and U4.

---

### U3. Implement `RedisDistributedReaderWriterLockStorage` with Lua scripts

**Goal:** Atomic Redis-backed implementation of `IDistributedReaderWriterLockStorage`. Owns the algorithmic correctness of RW lock semantics.

**Requirements:** R3.2, D2, D3, D7.

**Dependencies:** U2.

**Files:**

- `src/Headless.DistributedLocks.Redis/RedisDistributedReaderWriterLockStorage.cs` (new)
- `src/Headless.Redis/HeadlessRedisScriptsLoader.cs` (modify — register the new RW Lua scripts and expose typed methods following the existing `ReplaceIfEqualAsync` / `RemoveIfEqualAsync` pattern)
- `tests/Headless.DistributedLocks.Redis.Tests.Integration/RedisReaderWriterLockStorageTests.cs` (new — storage-level Lua-script behavior, before the provider integration)

**Approach:**

- Two keys per resource, both wrapped in the `{resource}` Redis cluster hash-tag for slot co-location:
  - `"{" + resource + "}:writer"` — writer key (string).
  - `"{" + resource + "}:readers"` — readers key (set).
  - Storage builds these key names internally; consumers always pass plain resource names. Reject (or sanitise — settle in implementation) resource names containing `{` or `}` to keep the hash-tag deterministic.
- Lua scripts (6 total, registered via `HeadlessRedisScriptsLoader`):
  1. `TryAcquireRead` (KEYS: writer, reader; ARGV: lockId, expiryMs) — see sketch above.
  2. `TryExtendRead` (KEYS: reader; ARGV: lockId, expiryMs) — verify lockId is in set, extend TTL.
  3. `ReleaseRead` (KEYS: reader; ARGV: lockId) — `SREM` (idempotent).
  4. `TryAcquireWrite` (KEYS: writer, reader; ARGV: lockId, waitingId, expiryMs) — see sketch above; writer-preference via waiting-marker.
  5. `TryExtendWrite` (KEYS: writer; ARGV: lockId, expiryMs) — verify writer key equals lockId, extend TTL.
  6. `ReleaseWrite` (KEYS: writer; ARGV: lockId) — clear only if writer key equals lockId.
- `IsReadLockedAsync` → `EXISTS readerKey` (or `SCARD > 0`).
- `IsWriteLockedAsync` → `GET writerKey`, return non-null AND not ending with `:_WRITERWAITING`.
- `GetReaderCountAsync` → `SCARD readerKey`.
- Constructor: `(IConnectionMultiplexer multiplexer, HeadlessRedisScriptsLoader scriptsLoader)` — mirrors `RedisDistributedLockStorage`.
- Argument validation via `Headless.Checks.Argument.IsNotNullOrEmpty(...)`.

**Technical design (directional, not specification):**

The release-write script must be defensive: if the writer key value is the `:_WRITERWAITING` marker (writer never acquired but is dropping out), release should clear it iff the marker's prefix matches the caller's lockId. If the value is a fully-acquired writer's lockId, clear iff it matches. If it does not match, no-op (safe against stale releases after lease loss + reacquisition by another writer).

For the `TryAcquireWrite` script, the writer-waiting claim must use `SET writer waitingId PX expiryMs` (not `NX`) — because the previous `writerValue == false` branch already confirmed there's no real writer. Avoid stomping over another writer's waiting marker by including the `writerValue == waitingId` re-up branch in the script (already in the sketch).

**Patterns to follow:**

- `src/Headless.DistributedLocks.Redis/RedisDistributedLockStorage.cs` for constructor shape, `IDatabase Db => multiplexer.GetDatabase()`, `ConfigureAwait(false)`, `Argument.IsNotNullOrEmpty`.
- `/Users/xshaheen/Dev/oss/DistributedLock/src/DistributedLock.Redis/Primitives/RedisReaderWriterLockPrimitives.cs` (reference — algorithmic source).
- Script loading: study existing `HeadlessRedisScriptsLoader` script registration to mirror the typed accessor method pattern (`scriptsLoader.TryAcquireReadAsync(Db, writerKey, readerKey, lockId, expiryMs, ct)`).
- Naming: `Headless.DistributedLocks.Redis.RedisDistributedReaderWriterLockStorage` (sealed class, no interfaces other than `IDistributedReaderWriterLockStorage`).

**Test suite design:**

- New integration test project: **none new** — extend `Headless.DistributedLocks.Redis.Tests.Integration` (confirm it exists; if not, create it mirroring the regular-lock integration test project).
- Fixture: existing Testcontainers Redis fixture in the Redis integration tests. Tests must be independent — each test generates a unique resource name (Bogus or `Guid`-suffixed) to avoid cross-test interference.
- Storage-level tests use `RedisDistributedReaderWriterLockStorage` directly (not through the provider) — keeps Lua-script behavior covered even if provider-level retries mask bugs.

**Test scenarios:**

- `TryAcquireRead` returns true when writer key absent → reader added to set, set TTL ≥ requested expiry.
- `TryAcquireRead` returns false when writer key holds a real writer's lockId.
- `TryAcquireRead` returns false when writer key holds a `:_WRITERWAITING` marker (writer-preference).
- `TryAcquireRead` is concurrent-safe: 10 parallel calls all succeed; `SCARD readerKey` equals 10.
- `TryExtendRead` returns false when lockId is not a set member.
- `TryExtendRead` returns true when lockId is in set; extends `PTTL` only if current TTL < new expiry (does not shorten).
- `ReleaseRead` removes lockId from set; idempotent (second release returns no-op).
- `TryAcquireWrite` returns true when writer absent AND `SCARD readerKey == 0`; writer key now holds the lockId with the requested TTL.
- `TryAcquireWrite` returns false when readers present; writer key now holds the waiting marker (writer-preference effective on next reader attempt).
- `TryAcquireWrite` returns true when writer key holds caller's own waiting marker AND readers drained.
- `TryAcquireWrite` returns false when another writer's waiting marker is present (two writers do not collide).
- `TryExtendWrite` returns false when writer key holds a different lockId or is empty.
- `TryExtendWrite` returns true when writer key matches lockId; extends TTL.
- `ReleaseWrite` clears writer key only if value matches lockId.
- `ReleaseWrite` is a no-op if writer key holds a different value (stale-release defense).
- `IsReadLockedAsync` true when readers ≥ 1, false otherwise.
- `IsWriteLockedAsync` true only when writer key holds a real (non-`:_WRITERWAITING`) value.
- `GetReaderCountAsync` returns set cardinality.
- Cluster hash-tag check (optional, environment-permitting): writer key and readers key resolve to the same Redis slot.

**Verification:**

- `make test-project TEST_PROJECT=tests/Headless.DistributedLocks.Redis.Tests.Integration/...csproj` passes.
- Coverage on `RedisDistributedReaderWriterLockStorage.cs` ≥85% line, ≥80% branch.
- No new build warnings.

---

### U4. Implement `DistributedReaderWriterLockProvider` (Core)

**Goal:** Concrete provider that wraps the storage, runs the acquire-retry loop, generates lock ids, integrates with `LeaseMonitor`/`LeaseMonitorRegistry`, and constructs `DisposableReaderWriterLock` handles.

**Requirements:** R3.1, D1, D6, D7.

**Dependencies:** U1, U2.

**Files:**

- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/DistributedReaderWriterLockProvider.cs` (new)
- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/DisposableReaderWriterLock.cs` (new — handle implementing `IDistributedLock` + `LeaseMonitor.ILeaseHandle`)
- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/ReaderWriterLockMode.cs` (new — internal enum: `Read` / `Write`)
- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/LoggerExtensions.cs` (new — `[LoggerMessage]` partial class for RW-specific log events; place at bottom of file or in its own file per the codebase pattern)
- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/DistributedReaderWriterLockMetrics.cs` (new — metrics for read/write acquire counts, wait time, contention)
- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/ScopedDistributedReaderWriterLockStorage.cs` (new — mirrors `ScopedDistributedLockStorage` for prefix scoping if the regular lock has it; if scoping does not apply, drop this file)

**Approach:**

- `DistributedReaderWriterLockProvider` constructor: `(IDistributedReaderWriterLockStorage storage, IOutboxPublisher? publisher, DistributedLockOptions options, ILongIdGenerator idGenerator, TimeProvider timeProvider, ILogger<DistributedReaderWriterLockProvider> logger)`.
- Shared acquire flow:
  1. Generate lock id (`idGenerator.Create()` returning a stable string; reuse the regular provider's id generator).
  2. Compute effective TTL and acquire deadline from options + defaults.
  3. Loop: call storage `TryAcquireRead`/`TryAcquireWrite` with the requested TTL.
     - Read: pass `(resource, lockId, ttl)`.
     - Write: pass `(resource, lockId, $"{lockId}:_WRITERWAITING", ttl)`. (Waiting marker derivation lives in the provider.)
  4. On success: stop timer, build `DisposableReaderWriterLock`, attach `LeaseMonitor` if `Monitoring != None`, return.
  5. On failure: await retry delay (using `TimeProvider.Delay`, **never** `Task.Delay`) or short-circuit if `AcquireTimeout == TimeSpan.Zero` (single-shot, matching the mutex provider's existing semantic — see `IDistributedLockProvider.TryAcquireAsync` XML doc).
  6. On deadline: `AcquireAsync` throws `LockAcquisitionTimeoutException`; `TryAcquireAsync` returns null.
- On writer-preference cleanup (D8): when a write acquire fails permanently (deadline hit) or the caller's `CancellationToken` fires AND the provider planted the waiting marker, the provider calls `storage.ReleaseWriteAsync(resource, waitingId)` to clear it. This reuses the same Lua script that clears a fully-acquired writer; the script's "delete if value equals expected" semantic handles both cases. No new storage method.
- `DisposableReaderWriterLock`:
  - Implements `IDistributedLock` + `LeaseMonitor.ILeaseHandle`.
  - Holds the mode (`ReaderWriterLockMode`), the storage reference, the lock id, the resource, the TTL, the acquired-at timestamp, the wait time.
  - `RenewOrValidateLeaseAsync(ct)`: dispatch by mode — read mode calls `TryExtendReadAsync` / read-validate; write mode calls `TryExtendWriteAsync` / write-validate. Map result to `LeaseState.Renewed` / `LeaseState.Lost` / `LeaseState.Held` / `LeaseState.Unknown` consistent with the regular handle's mapping.
  - `RenewAsync(timeUntilExpires, ct)`: dispatch by mode to the matching extend script; return `bool`.
  - `ReleaseAsync()`: dispatch by mode; idempotent; emit `DistributedLockReleased` via outbox publisher when configured (same channel as mutex, so waiters using the existing wake-up infra hear both lock types).
- Use **source-generated logging** (`[LoggerMessage]` partial class). Place the partial class at the **bottom** of each file (matches the `LeaseMonitorLog` pattern in `LeaseMonitor.cs`).
- All time reads via injected `TimeProvider`; never `DateTime.UtcNow`, never `Task.Delay` without `TimeProvider`.

**Execution note:** Implement the provider after the storage tests in U3 are green — the retry/timeout/lease-monitor wiring is easier to reason about when the underlying script behavior is locked in.

**Patterns to follow:**

- `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` — overall provider shape, options resolution, retry loop, outbox integration, `LeaseMonitorRegistry` use.
- `src/Headless.DistributedLocks.Core/RegularLocks/DisposableDistributedLock.cs` — handle shape, `LeaseMonitor.ILeaseHandle` impl.
- `src/Headless.DistributedLocks.Core/RegularLocks/LoggerExtensions.cs` — log message style (`[LoggerMessage]` with stable `EventId`s; pick IDs that do not collide with the regular lock's range).
- `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockMetrics.cs` — metric naming convention. Reuse the existing mutex metric names (acquire-count, wait-time, contention) with a first-class `mode` tag (`read` | `write`) rather than introducing `rw.`-prefixed parallel metrics — keeps dashboards consistent.

**Test suite design:**

- Unit tests in `tests/Headless.DistributedLocks.Tests.Unit/ReaderWriterLocks/` — non-trivial logic only: option validation, deadline math, mode dispatch in the handle. Storage is faked with `NSubstitute`.
- Heavy integration coverage moves to U7 (Testcontainers Redis).

**Test scenarios:**

- Read acquire succeeds → returned handle has `LockId`, `Resource`, `RenewalCount=0`, `DateAcquired ≈ now`, `IsMonitored=false` when monitoring disabled.
- Write acquire succeeds → same handle shape.
- Read `RenewAsync` dispatches to `TryExtendReadAsync` (verify via `NSubstitute.Received()`).
- Write `RenewAsync` dispatches to `TryExtendWriteAsync`.
- Read `ReleaseAsync` dispatches to `ReleaseReadAsync` and increments the released-counter metric.
- Write `ReleaseAsync` dispatches to `ReleaseWriteAsync`.
- `AcquireAsync` with `AcquireTimeout = TimeSpan.Zero` runs exactly one storage attempt (no retry).
- `AcquireAsync` deadline exceeded throws `LockAcquisitionTimeoutException`.
- `TryAcquireAsync` deadline exceeded returns null.
- Monitoring=Monitor with `TimeUntilExpires=InfiniteTimeSpan` throws `ArgumentException` (parity with mutex).
- Cancellation token: cancelling during retry-wait surfaces `OperationCanceledException`.
- Write-failure cleanup: when a write acquire times out after planting a waiting marker, the marker is cleared (verify storage cleanup call).
- Lease loss: `LeaseMonitor` faulting cancels `HandleLostToken` on the RW handle (parity with mutex behavior — verify via `LeaseMonitor` integration test).
- `RenewalCount` increments on each successful `RenewAsync` for both modes.
- Disposal with `ReleaseOnDispose=true` releases; with `false` does not.

**Verification:**

- `make test-project TEST_PROJECT=tests/Headless.DistributedLocks.Tests.Unit/...csproj` passes.
- No new analyzer warnings.

---

### U5. DI Setup — `RedisDistributedReaderWriterLockSetup` + core `AddDistributedReaderWriterLock<TStorage>`

**Goal:** Idiomatic DI registration matching the three-overload pattern used elsewhere.

**Requirements:** Issue "Setup" line.

**Dependencies:** U3, U4.

**Files:**

- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/AddDistributedReaderWriterLockExtensions.cs` (new — core triad of overloads)
- `src/Headless.DistributedLocks.Redis/RedisDistributedReaderWriterLockSetup.cs` (new — Redis-specific triad delegating to core)

**Approach:**

- Core `AddDistributedReaderWriterLock<TStorage>` — three overloads: `(Action<DistributedLockOptions, IServiceProvider>)`, `(Action<DistributedLockOptions>)`, `(IConfiguration)`.
  - Use `Configure<DistributedLockOptions, DistributedLockOptionsValidator>(...)` for options + validation.
  - Core helper `_AddDistributedReaderWriterLockCore<TStorage>` performs shared wiring:
    - `services.TryAddSingleton<TStorage>()`.
    - `services.TryAddSingleton<DistributedReaderWriterLockProvider>(...)` with explicit factory (mirrors the mutex provider's factory shape).
    - `services.TryAddSingleton<IDistributedReaderWriterLockProvider>(sp => sp.GetRequiredService<DistributedReaderWriterLockProvider>())`.
    - Reuse `DistributedLockOptions` (D6) — no new options type.
    - Reuse `TimeProvider`, `ILongIdGenerator` (already registered by the mutex `AddDistributedLock`, idempotent via `TryAddSingleton`).
    - Do **not** re-register the `LockReleasedConsumer` here — the mutex provider already owns that registration when messaging is wired; the RW provider participates in the same release channel.
- Redis `RedisDistributedReaderWriterLockSetup`:
  - Three overloads `AddRedisDistributedReaderWriterLock(...)` mirroring `AddRedisDistributedLock`.
  - Each calls `services.TryAddSingleton<HeadlessRedisScriptsLoader>()` then delegates to the core `AddDistributedReaderWriterLock<RedisDistributedReaderWriterLockStorage>`.
- Use C# 14 extension members (matching existing setup classes).

**Patterns to follow:**

- `src/Headless.DistributedLocks.Core/Setup.cs` — three-overload + private core helper pattern.
- `src/Headless.DistributedLocks.Redis/Setup.cs` — Redis-side wiring.

**Test suite design:**

- DI registration tests in `tests/Headless.DistributedLocks.Tests.Unit/ReaderWriterLocks/SetupTests.cs`. Build a `ServiceCollection`, register, resolve `IDistributedReaderWriterLockProvider`, assert concrete type and singleton lifetime.

**Test scenarios:**

- All three core overloads register `IDistributedReaderWriterLockProvider` as singleton.
- Idempotent: calling `AddDistributedReaderWriterLock<...>` twice yields one `DistributedReaderWriterLockProvider` registration.
- Calling `AddRedisDistributedReaderWriterLock` registers `HeadlessRedisScriptsLoader` exactly once even if called alongside `AddRedisDistributedLock`.
- Resolving `IDistributedReaderWriterLockProvider` after only `AddRedisDistributedLock` (no RW setup) returns the null impl OR is unresolvable — pick one and document. **Decision:** unresolvable (consumers must opt in explicitly; the null provider is for testing only and not auto-registered).
- Options validation: `DistributedLockOptions` with monitoring on + infinite TTL surfaces a validator error at startup.

**Verification:**

- DI tests pass.
- `make build` clean.

---

### U6. Lease-monitor integration

**Goal:** Read and write handles surface `HandleLostToken` and respect `Monitoring = AutoExtend` identically to mutex handles.

**Requirements:** R3.1 (HandleLostToken flow), AC ("HandleLostToken fires on lease loss for both", "Auto-extension works on both"), D7.

**Dependencies:** U3, U4.

**Files:**

- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/DisposableReaderWriterLock.cs` (modify — finalize `LeaseMonitor.ILeaseHandle` impl)
- `src/Headless.DistributedLocks.Core/ReaderWriterLocks/DistributedReaderWriterLockProvider.cs` (modify — attach `LeaseMonitor` via `LeaseMonitorRegistry` when options enable monitoring)

**Approach:**

- `DisposableReaderWriterLock.RenewOrValidateLeaseAsync(ct)`:
  - If `Monitoring == AutoExtend`: call `TryExtend{Mode}Async`; return `Renewed` on true, `Lost` on false.
  - If `Monitoring == Monitor` (polling): for read mode, call `ValidateReadAsync` — true iff `SISMEMBER readerKey lockId`. For write mode, call `ValidateWriteAsync` — true iff `GET writerKey == lockId`. Return `Held` on true, `Lost` on false.
  - On transient exception (network blip): return `Unknown` (the lease monitor's safety net handles repeated `Unknown`s).
- Provider attaches the monitor exactly as the mutex provider does: build `LeaseMonitor(handle, timeProvider, logger)`, register with `LeaseMonitorRegistry` for unified shutdown.
- `MonitoringCadence` and `LeaseDuration` properties on the handle come from `DistributedLockOptions.MonitoringCadence` and the effective TTL — mirror the mutex.
- Storage interface additions for validation (`ValidateReadAsync`, `ValidateWriteAsync`): finalize in U2/U3 — if Phase 2's mutex provider uses `Get` + compare instead of a dedicated validate, follow that pattern to minimize new surface.

**Patterns to follow:**

- `src/Headless.DistributedLocks.Core/RegularLocks/DisposableDistributedLock.cs` — how the mutex handle implements `ILeaseHandle` and dispatches renew/validate.
- `src/Headless.DistributedLocks.Core/RegularLocks/LeaseMonitor.cs` — the contract (already exists, no changes needed here).
- `src/Headless.DistributedLocks.Core/RegularLocks/LeaseMonitorRegistry.cs` — registration shape.

**Test suite design:**

- Integration tests in U7 cover the end-to-end lease-loss + auto-extension scenarios against real Redis.
- Unit tests in `tests/Headless.DistributedLocks.Tests.Unit/ReaderWriterLocks/LeaseMonitorIntegrationTests.cs` cover the handle's `RenewOrValidateLeaseAsync` dispatch using a faked storage and `FakeTimeProvider`.

**Test scenarios:**

- Read handle with `Monitoring=AutoExtend`: storage returns true from `TryExtendReadAsync` → monitor state `Renewed`; storage returns false → state `Lost` → `HandleLostToken` fires.
- Write handle with `Monitoring=AutoExtend`: storage returns true from `TryExtendWriteAsync` → `Renewed`; false → `Lost`.
- Read handle with `Monitoring=Monitor`: storage's validate returns true → `Held`; false → `Lost`.
- Write handle with `Monitoring=Monitor`: same dispatch on write-validate.
- Transient storage exception → `Unknown`; persistent transient exceptions over `LeaseDuration` window → safety net flips to `Lost`.
- `HandleLostToken` is `CancellationToken.None` when `Monitoring=None` (parity with mutex).
- Disposal of monitored handle cancels the monitor cleanly without firing `HandleLostToken`.

**Verification:**

- Lease-monitor unit tests for both modes pass.
- Integration tests in U7 covering lease-loss work green.

---

### U7. Integration tests — Testcontainers Redis acceptance scenarios

**Goal:** End-to-end coverage of every issue acceptance criterion against real Redis.

**Requirements:** All issue ACs.

**Dependencies:** U3, U4, U5, U6.

**Files:**

- `tests/Headless.DistributedLocks.Redis.Tests.Integration/ReaderWriterLocks/RedisReaderWriterLockProviderTests.cs` (new — covers AC scenarios; storage-level Lua tests already added in U3)
- `tests/Headless.DistributedLocks.Redis.Tests.Integration/ReaderWriterLocks/RedisReaderWriterLockLeaseMonitorTests.cs` (new — lease-loss + auto-extend scenarios)

**Approach:**

- Use the existing Testcontainers Redis fixture from `Headless.DistributedLocks.Redis.Tests.Integration`.
- Each test uses a unique resource name (Bogus / `Guid`) to avoid interference. Generate "valid but unremarkable" payloads via Bogus per the testing guidance.
- Pass `TestContext.Current.CancellationToken` to every async call (or `TestBase.AbortToken` if the project has a `TestBase`).
- Concurrency tests: use `Task.WhenAll` with `N` parallel readers; assert via the storage's reader-count or set-cardinality, plus visible side effects on a shared in-memory counter to prove concurrent execution.
- Use real `TimeProvider.System` for cadence-sensitive tests; for tests that simulate lease loss, induce loss by force-deleting the Redis keys (back-door) and waiting for monitor cadence to detect it.

**Test scenarios:**

- *Covers AC:* Multiple readers concurrent — 10 parallel `AcquireReadLockAsync` calls all succeed; release order does not matter; `IsReadLockedAsync` true throughout; `GetReaderCountAsync == 10` mid-execution.
- *Covers AC:* Write blocks until readers release — `AcquireReadLockAsync` × 3; `AcquireWriteLockAsync` task does not complete; release readers one by one; write completes only after last reader releases.
- *Covers AC:* Reader-blocking-on-queued-writer (writer preference) — `AcquireReadLockAsync`; start a write acquire (queues with waiting marker); subsequent `TryAcquireReadLockAsync` with zero timeout returns null; release reader; writer completes; release writer; new read succeeds.
- *Covers AC:* `HandleLostToken` fires on lease loss — read handle with `Monitoring=Monitor`; force-delete the readers key; within (≤2 × monitoring cadence), `HandleLostToken` becomes cancelled.
- *Covers AC:* `HandleLostToken` fires on lease loss for write — symmetric: write handle with `Monitoring=Monitor`; force-delete writer key; token cancels.
- *Covers AC:* Auto-extension works on read — read handle with `Monitoring=AutoExtend`, TTL=2 s; wait 5 s; reader still in set; `IsReadLockedAsync` true.
- *Covers AC:* Auto-extension works on write — symmetric.
- Acquire timeout — write acquire with readers held and `AcquireTimeout=1 s` throws `LockAcquisitionTimeoutException`; waiting marker cleared after timeout (verify via direct `GET writerKey`).
- Try-acquire timeout — same scenario with `TryAcquireWriteLockAsync` returns null.
- Release-on-dispose — `await using` block disposes handle and releases lock; `IsWriteLockedAsync` false after block exits.
- ReleaseOnDispose=false — dispose does NOT release; explicit `ReleaseAsync` required.
- Cancellation — cancel `CancellationToken` while a write acquire is queued; surfaces `OperationCanceledException` AND clears the waiting marker.
- Two writers contending — only one acquires; the other waits or fails per its timeout; no deadlock.
- Reader after writer release — write acquire/release; subsequent read acquire succeeds.
- Stale read release — release a reader's lock id that has expired/been evicted; idempotent, no error.
- Stale write release — release with a lockId that no longer matches the writer key; no-op (defense covered in U3 too).
- Lease loss reacquisition — after `HandleLostToken` fires for a writer, another writer can acquire the same resource.
- Cluster hash-tag co-location (skip in single-node Redis; gate via `[Trait("Mode", "Cluster")]` if cluster fixture exists).

**Verification:**

- All integration tests pass under `make test-integration`.
- Combined Phase 3a coverage hits the targets: ≥85% line / ≥80% branch on `Headless.DistributedLocks.Core/ReaderWriterLocks/*` and `Headless.DistributedLocks.Redis/RedisDistributedReaderWriterLockStorage.cs`.
- `make coverage-json` reports no regression on existing mutex coverage.

---

### U8. Documentation sync

**Goal:** Keep agent-facing docs aligned with the new public surface and the OQ4 conclusion.

**Requirements:** AC ("Docs sync"), CLAUDE.md "Documentation" section.

**Dependencies:** U1, U5 (public surface settled).

**Files:**

- `docs/llms/distributed-locks.md` (modify — add RW lock section: concept, when to use, writer-preference, cache-backend caveat per D4, lease-monitor parity)
- `src/Headless.DistributedLocks.Abstractions/README.md` (modify — add `IDistributedReaderWriterLockProvider` to the public surface section)
- `src/Headless.DistributedLocks.Core/README.md` (modify — add provider concrete + `IDistributedReaderWriterLockStorage`)
- `src/Headless.DistributedLocks.Redis/README.md` (modify — add `AddRedisDistributedReaderWriterLock` registration example and key-shape note)
- `src/Headless.DistributedLocks.Cache/README.md` (modify — explicit "RW lock is Redis-only; cache backend does not implement RW per D4")

**Approach:**

- Read `docs/authoring/AUTHORING.md` first (per CLAUDE.md) and follow the templates for both surfaces (`docs/llms/` and per-package `README.md`).
- `docs/llms/distributed-locks.md`: add a `## Reader-Writer Lock (Redis)` section with: concept, registration example, read/write usage example, writer-preference behavior, lease-monitor parity, OQ4 conclusion.
- Per-package `README.md`: short additive sections, links back to the llms doc.
- Verify all paths and code refs in the docs after U7 lands (file paths and class names finalized at that point).

**Test suite design:** Documentation — no test scenarios. Verification is human review + doc-build check (if one exists in CI).

**Test scenarios:** Test expectation: none — documentation-only changes.

**Verification:**

- `make build` clean (in case docs are included in any project's content items).
- Manual scan: every new public type/method has a doc entry; the OQ4 conclusion is stated; the writer-preference choice is documented.
- All code refs in the new doc sections use repo-relative paths and resolve.

---

## System-Wide Impact

- **`Headless.DistributedLocks.Abstractions`** — new public surface (`IDistributedReaderWriterLockProvider`, null impl, extensions). Additive; no existing API changes.
- **`Headless.DistributedLocks.Core`** — new internal types under `ReaderWriterLocks/`; new public storage interface; new public DI extensions. Existing mutex code under `RegularLocks/` untouched.
- **`Headless.DistributedLocks.Redis`** — new storage class; new setup overloads; `HeadlessRedisScriptsLoader` gains 6 new typed script accessors.
- **`Headless.Redis`** — `HeadlessRedisScriptsLoader` modified (additive; existing scripts unchanged).
- **`Headless.DistributedLocks.Cache`** — no code change; README annotated with the Redis-only caveat.
- **Consumers** — Phase 3a is purely additive. Existing mutex callers see no API change. Consumers wanting RW must call `AddRedisDistributedReaderWriterLock(...)` in addition to (or instead of) `AddRedisDistributedLock(...)`.
- **Lease-monitor channel** — both mutex and RW providers participate in the same `DistributedLockReleased` outbox channel when messaging is configured. No new channel.
- **Greenfield framework** — no migration risk; consumers will adopt as they need RW semantics.

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Lua script bug deadlocks readers/writers under load | Integration tests cover concurrent reader fan-out, writer queueing, two-writer contention, stale-release idempotency. Algorithm tracks madelson (battle-tested). |
| Writer-waiting marker leaks (writer cancelled mid-wait) | Explicit cleanup in `DistributedReaderWriterLockProvider` on acquire-failure and cancellation paths. Integration test "Cancellation clears the waiting marker" enforces this. |
| Redis cluster slot fragmentation between writer/readers keys | Storage layer builds keys as `"{" + resource + "}:writer"` and `"{" + resource + "}:readers"` so the `{resource}` brace-delimited hash tag co-locates both keys on the same Redis slot. Consumers pass plain resource names; the storage owns the wrapping. Verified by an integration test (gated on cluster mode availability). |
| Lock-id collision with `:_WRITERWAITING` suffix | `ILongIdGenerator` (snowflake-based) emits numeric ids that cannot contain `:` — collision is impossible by construction. Add an assertion in `DistributedReaderWriterLockProvider` constructor that the generated id contains no `:` to fail loud if the id generator is ever swapped for one that breaks this invariant. |
| `HeadlessRedisScriptsLoader` script-version churn from adding 6 scripts | Scripts loader pre-loads on first use; integration tests exercise every script at least once. |
| Auto-extend race: extend script fires while another caller is releasing | Extend scripts check membership (read) or identity (write) before extending — a release-then-extend race naturally yields `Lost` on the next extend, which the lease monitor handles. |
| Lease loss on a writer leaves a stale writer key for full TTL | Default TTL is finite (20 min per `DistributedLockOptions` default); consumers needing faster recovery configure shorter TTL + monitoring. Documented in the new RW section of `docs/llms/distributed-locks.md`. |

---

## Deferred Implementation-Time Unknowns

These are non-blocking; resolve in `dev-code`:

- Exact `[LoggerMessage] EventId` range for RW provider logs (must not collide with existing mutex log IDs in `LoggerExtensions.cs`).
- Whether `ScopedDistributedReaderWriterLockStorage.cs` is needed — depends on whether the regular provider's scoping has any RW analogue. If not, drop the file.
- Final shape of `HeadlessRedisScriptsLoader` accessor methods (parameter order, return type) — settle to match the existing `ReplaceIfEqualAsync` / `RemoveIfEqualAsync` shape.
- Whether `Validate{Read,Write}Async` storage methods are needed or whether `GetReaderCountAsync` + `IsWriteLockedAsync` suffice for polling-mode validation.
- Whether the writer-waiting marker is best derived in the provider (`$"{lockId}:_WRITERWAITING"`) or returned from the storage layer — coordinate when implementing U3 and U4.
- Whether resource-name sanitisation should reject `{`/`}` outright with `ArgumentException` or strip them — settle when wiring U3's validation.

---

## Verification Checklist

- [ ] All implementation units have passing tests per their `Verification` section.
- [ ] `make build` clean (no new warnings).
- [ ] `make format-check` passes.
- [ ] `make test` green (unit + integration where Docker is available).
- [ ] Coverage targets met on the new packages (≥85% line, ≥80% branch).
- [ ] All acceptance criteria in the origin issue have at least one covering integration test.
- [ ] OQ4 conclusion is explicit in `docs/llms/distributed-locks.md` and `Headless.DistributedLocks.Cache/README.md`.
- [ ] Writer-preference choice is documented in `IDistributedReaderWriterLockProvider` XML doc and `docs/llms/distributed-locks.md`.
- [ ] No `Task.Delay` without `TimeProvider`; no `DateTime.UtcNow` — all time access via injected `TimeProvider`.
- [ ] No use of `ArgumentNullException.ThrowIfNull` — all validation via `Headless.Checks`.
- [ ] No `Dto` suffix on new types.
- [ ] `[LoggerMessage]` partial classes placed at the bottom of their hosting file.
- [ ] All file paths in the plan resolved to actual files at implementation time.
