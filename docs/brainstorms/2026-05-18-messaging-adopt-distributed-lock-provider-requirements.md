# Messaging — Adopt IDistributedLockProvider for coarse-grained retry locks

**Date:** 2026-05-18
**Tracking:** [#266](https://github.com/xshaheen/headless-framework/issues/266) (parent #263)
**Scope:** Standard refactor — feature-tier
**Status:** Requirements settled; ready for `/dev-plan`.

## Problem

`Headless.Messaging.Core` ships its own coarse-grained named-lock primitives on `IDataStorage`
(`AcquireLockAsync` / `ReleaseLockAsync` / `RenewLockAsync`) plus per-provider lock SQL
(~50 LOC each in `SqlServerDataStorage`, `PostgreSqlDataStorage`, `InMemoryDataStorage`) and a
seeded lock table in the storage initializers. The retry processor
(`MessageNeedToRetryProcessor` in `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs`)
is the only consumer.

`Headless.DistributedLocks.Abstractions` already provides a richer handle-based
`IDistributedLockProvider` with TTL, renewal, inspection, metrics, and diagnostics. The
messaging-side duplication adds carrying cost (per-provider SQL, a public DDL table, an
ad-hoc `_instance` identity) for no behavior the framework's lock abstraction can't deliver.

## Goal

Replace the named-lock surface on `IDataStorage` with `IDistributedLockProvider` for the
retry-processor coarse mutex, preserving today's behavior — including the renewal that
keeps the receive-retry lock alive across polling cycles when the consume task spans them.

Per-row leases on `MediumMessage.LockedUntil` (`LeasePublishAsync`, `LeaseReceiveAsync`)
are a different concept and stay on `IDataStorage` unchanged.

## Required behavior

- Coarse-grained mutex: exactly one node runs the publish-retry pickup and one node runs
  the receive-retry pickup per logical resource at any time (only when `UseStorageLock = true`).
- Lock acquire is non-blocking — `acquireTimeout: TimeSpan.Zero`. A non-acquiring node skips
  the cycle (current behavior preserved).
- Lock TTL semantics preserved: `_GetLockTtl()` = `max(currentInterval, baseInterval) + 10s safety margin`.
- Renewal for the receive-retry path: while the previous consume task is still running,
  `ProcessAsync` renews the held lock once per polling tick (see "Decisions" below).
- Release-on-failure via `await using` disposal — no manual try/finally for the lock.
- When `UseStorageLock = false`, every node scans independently. Unchanged.
- DI: `Headless.Messaging.Core` ships an internal no-op `IDistributedLockProvider` registered
  as a `TryAddSingleton` fallback. When `UseStorageLock = true` and only the no-op fallback
  is resolved, the bootstrapper logs a startup warning (not an exception) telling the user
  to register a real provider — see Decisions → "Provider availability (Option C: no-op
  default)".
- Lock resource naming follows `messaging.<operation>-<version>` — specifically
  `messaging.publish-retry-{Version}` and `messaging.receive-retry-{Version}`.

## Decisions

### Renewal path — Option 1: capture handle, parent renews

The receive-retry consume task spans multiple polling cycles when work is heavy. Today,
`ProcessAsync` renews the storage lock every tick while `_failedRetryConsumeTask` is still
running. The refactor preserves this by having the task expose its handle:

- `_ProcessReceivedAsync` writes the acquired `IDistributedLock` into a field
  (`_receivedRetryHandle`) before doing work; clears it in `finally`.
- `ProcessAsync` reads the field and calls `_receivedRetryHandle.RenewAsync(_GetLockTtl(), ct)`
  on each tick where the task is still running.
- Handle field is single-writer (the task) and single-reader (`ProcessAsync`, which is invoked
  sequentially per processor instance). A stale-read at the task-end boundary just means one
  skipped renewal cycle, which the next tick re-renews.
- Renewal goes through `IDistributedLock.RenewAsync` on the handle, not
  `IDistributedLockProvider.RenewAsync(resource, lockId, ...)`.

Rejected alternatives:

- **Self-renewing consumer task** — cleaner encapsulation but adds a background renewal loop
  per task plus `TimeProvider` plumbing (the class doesn't inject one today). Wider blast
  radius for a minimum-delta refactor.
- **Drop renewal; size TTL to fit** — simplest, but silently changes failure semantics: a
  consume pass that exceeds TTL would let another replica double-pick.
- **Drop renewal AND in-progress guard** — even larger semantic change to the receive path.

### `_instance` field

The `_instance` identifier (hostname + worker-id) is used only inside the three lock calls.
The new handle owns its own `LockId`; `_instance` becomes dead. Delete the field entirely
along with its `SnowflakeIdLongIdGenerator` construction in the constructor.

### Lock table DDL and `IStorageInitializer.GetLockTableName()`

- Delete the lock table `CREATE TABLE` / `INSERT` blocks from `PostgreSqlStorageInitializer`
  and `SqlServerStorageInitializer`.
- Delete `IStorageInitializer.GetLockTableName()` and its three implementations. Greenfield
  framework — no need for a no-op compatibility shim. The method is dead once the lock SQL
  is gone.
- `InMemoryStorageInitializer.GetLockTableName()` also goes.

### Published-retry path

Stays single-acquire, no renewal — matches today's behavior on that path. Only the
receive-retry path needs renewal.

### Provider availability (Option C: no-op default)

Discussed and rejected in favor of a hybrid:

- **Option A (hard DI dep when `UseStorageLock = true`)** — fail-fast at startup if no
  `IDistributedLockProvider` is registered. Rejected: forces every messaging consumer to
  take a DI dependency they may not need; harshens the "should we have the coarse lock
  at all?" decision into a runtime exception.
- **Option B (drop coarse lock for messaging entirely)** — modern .NET messaging libraries
  (MassTransit, NServiceBus) skip coarse locking; per-row `LockedUntil` lease alone is
  sufficient for correctness. Rejected: loses operational observability ("which node holds
  the retry lease?"), loses the framework-canonical primitive that `Headless.Jobs` is
  about to depend on (issue references P4), and forecloses on non-`SKIP LOCKED` storage
  providers.
- **Option C (adopted): no-op fallback, real provider opt-in.**
  - `Headless.Messaging.Core` ships an internal `NoOpDistributedLockProvider` whose
    `TryAcquireAsync` always succeeds, returning a no-op `IDistributedLock` handle whose
    `RenewAsync`/`ReleaseAsync`/`DisposeAsync` are no-ops.
  - Registered in `SetupMessaging` via
    `TryAddSingleton<IDistributedLockProvider, NoOpDistributedLockProvider>()` — the
    `TryAdd` ensures any earlier consumer registration (e.g.,
    `services.AddDistributedLocks()` from `Headless.DistributedLocks.Core` plus a Redis
    storage backing) wins.
  - `MessageNeedToRetryProcessor` always takes `IDistributedLockProvider` as a constructor
    dependency. Resolution always succeeds — either the user-registered provider or the
    no-op fallback.
  - When `UseStorageLock = false`, the processor skips `TryAcquireAsync` entirely (saves
    a method call per polling tick). Today's behavior preserved.
  - When `UseStorageLock = true` and the resolved provider is the no-op fallback, the
    bootstrapper logs a `Warning`-level message at startup telling the user to register
    a real provider or set `UseStorageLock = false` to silence the warning. No exception.

**Default `UseStorageLock` stays `false`** — matches the brainstorm's "do not change the
default" non-goal and aligns with the modern norm of "row-level lease handles correctness;
opt into coarse lock for observability / extreme replica counts".

The no-op handle requires no field plumbing for the receive-retry renewal path —
`handle.RenewAsync` becomes a no-op call when no real provider is registered. Option 1
(capture handle + parent renews) still applies uniformly.

### Lock store and data store may be different backends

After this refactor, nothing requires the `IDistributedLockProvider` and the `IDataStorage`
to live in the same backend. A consumer can run Redis-backed locks with SQL Server messaging
data. This is safe because:

- **The coarse lock is an efficiency primitive, not a correctness primitive.** It reduces
  redundant pickup queries when multiple replicas tick simultaneously; it does not protect
  any transactional boundary in the data store.
- **Correctness comes from `MediumMessage.LockedUntil` (per-row lease).**
  `GetPublishedMessagesOfNeedRetryAsync` and `GetReceivedMessagesOfNeedRetryAsync` are
  atomic claim-and-return — the same SQL that selects rows advances `LockedUntil`. Two
  replicas cannot pick up the same row regardless of what the coarse lock did.
- Today's design already isn't transactional across lock + data — `AcquireLockAsync` and
  the pickup query are separate round-trips against the same `IDataStorage`. The refactor
  loosens "same backend" to "any backend" without losing a guarantee that ever existed.

**Operational guidance** (to be reflected in `Headless.Messaging.Core/README.md` and
`docs/llms/messaging.md` / `docs/llms/distributed-locks.md`):

- Recommend a fast lock provider when `UseStorageLock = true` — typically
  `Headless.DistributedLocks.Redis` or `Headless.DistributedLocks.Cache`. A SQL-backed
  lock provider (none ships today) would re-introduce the per-provider duplication we're
  removing.
- Lock store and data store have independent failure modes. Lock store down (real
  provider) means the no-op fallback kicks in if the user registered both, but more
  realistically a Redis outage just means every replica's `TryAcquireAsync` fails or
  hangs based on the provider's error semantics — falling back to no-op is a user
  decision, not framework-default. Data store down fails the work itself — same as today.
- Clock skew between the two stores doesn't matter — lock TTL is a relative timeout, not
  a wall-clock boundary against the data store.

## Scope boundaries

**In scope:**

- Delete `AcquireLockAsync` / `ReleaseLockAsync` / `RenewLockAsync` from `IDataStorage`.
- Delete the corresponding implementations and seed DDL from the three providers.
- Delete `IStorageInitializer.GetLockTableName()` and implementations.
- Rewrite `_ProcessPublishedAsync` and `_ProcessReceivedAsync` against `IDistributedLockProvider`.
- Preserve receive-retry renewal via the handle (Option 1).
- Inject `IDistributedLockProvider` into `MessageNeedToRetryProcessor`.
- Add `Headless.DistributedLocks.Abstractions` as a project reference of `Headless.Messaging.Core`.
- Ship an internal `NoOpDistributedLockProvider` in `Headless.Messaging.Core` registered
  as a `TryAddSingleton` fallback.
- Bootstrapper startup warning when `UseStorageLock = true` and the resolved
  `IDistributedLockProvider` is the no-op fallback.
- Update `src/Headless.Messaging.Core/README.md` to document the dependency, the no-op
  fallback, and how to opt into a real provider.
- Update `docs/llms/messaging.md` with a "Coarse-grained retry lock — enable/disable
  trade-offs" subsection.
- Update `docs/llms/distributed-locks.md` cross-referencing messaging's adoption and the
  shape of the no-op fallback pattern.

**Out of scope (deferred or "different concept"):**

- Per-row lease semantics (`MediumMessage.LockedUntil`, `LeasePublishAsync`,
  `LeaseReceiveAsync`) — concept B in the issue's per-row-lease-vs-coarse-mutex distinction.
  Stays on `IDataStorage` unchanged.
- Changing the `UseStorageLock` default.
- Replacing `_instance` usage elsewhere — already verified there is no elsewhere.
- Adopting the same pattern in `Headless.Jobs` — pattern reference for a later issue.
- Migration tooling / DROP TABLE scripts for the lock table on existing deployments —
  framework is greenfield with no deployed consumers.

## Acceptance criteria

Inherited from the issue, with two added items surfaced during brainstorm:

- [ ] `IDataStorage.AcquireLockAsync`, `ReleaseLockAsync`, `RenewLockAsync` removed.
- [ ] Lock SQL deleted from `SqlServerDataStorage`, `PostgreSqlDataStorage`, `InMemoryDataStorage`.
- [ ] Lock table `CREATE` + seed `INSERT` removed from `SqlServerStorageInitializer` and
      `PostgreSqlStorageInitializer`.
- [ ] `IStorageInitializer.GetLockTableName()` and all three implementations removed.
      **(added)**
- [ ] `Headless.Messaging.Core` references `Headless.DistributedLocks.Abstractions`.
- [ ] `Headless.Messaging.Core` ships an internal `NoOpDistributedLockProvider` registered
      via `TryAddSingleton<IDistributedLockProvider, NoOpDistributedLockProvider>()` so a
      user-registered real provider always wins. **(added)**
- [ ] `MessageNeedToRetryProcessor` rewritten against `IDistributedLockProvider`:
  - [ ] Published-retry: single `TryAcquireAsync` + `await using` disposal, no renewal.
  - [ ] Received-retry: same acquire pattern; handle captured in `_receivedRetryHandle`;
        `ProcessAsync` renews via the handle while the consume task is still running.
        **(added)**
  - [ ] When `UseStorageLock = false`, processor skips `TryAcquireAsync` entirely (every
        node scans independently). **(added)**
- [ ] `_instance` field and its `SnowflakeIdLongIdGenerator` construction removed.
- [ ] Resource names are `messaging.publish-retry-{Version}` and `messaging.receive-retry-{Version}`.
- [ ] When `UseStorageLock = true` and the resolved `IDistributedLockProvider` is the no-op
      fallback, `Bootstrapper` logs a `Warning`-level message at startup explaining the
      situation and the two recovery paths. **No exception is thrown.** **(revised)**
- [ ] Integration test: two simultaneous `_ProcessPublishedAsync` invocations across two
      processor instances result in exactly one running its body; the other returns early.
      Same for `_ProcessReceivedAsync`. (Requires a real `IDistributedLockProvider` —
      typically `Headless.DistributedLocks.Cache` or `.Redis` via Testcontainers.)
- [ ] Integration test: receive-retry lock survives across at least 2 polling ticks under
      `UseStorageLock = true` — i.e., renewal works end-to-end against a real provider.
- [ ] Integration test: with `UseStorageLock = true` and only the no-op fallback registered,
      both replicas run the retry processor body (no coordination), and the startup warning
      fires. **(added)**
- [ ] All existing messaging tests pass.
- [ ] `src/Headless.Messaging.Core/README.md` documents the dependency, the no-op fallback,
      and how to opt into a real provider.
- [ ] `docs/llms/messaging.md` includes a "Coarse-grained retry lock — enable/disable
      trade-offs" subsection covering both states. **(added)**
- [ ] `docs/llms/distributed-locks.md` cross-references messaging's adoption and the no-op
      fallback pattern. **(added)**

## Files affected

- `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`
- `src/Headless.Messaging.Core/Persistence/IStorageInitializer.cs`
- `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs`
- `src/Headless.Messaging.Core/Internal/NoOpDistributedLockProvider.cs` (new)
- `src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs` (startup warning)
- `src/Headless.Messaging.Core/Setup.cs` (TryAdd no-op fallback)
- `src/Headless.Messaging.Core/Headless.Messaging.Core.csproj` (add reference)
- `src/Headless.Messaging.Core/README.md`
- `src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`
- `src/Headless.Messaging.InMemoryStorage/InMemoryStorageInitializer.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs`
- `docs/llms/messaging.md` (enable/disable trade-offs subsection)
- `docs/llms/distributed-locks.md` (cross-reference messaging adoption)
- Messaging integration test suite (two-node mutual-exclusion + cross-tick renewal +
  no-op-fallback warning tests)

## Assumptions and open notes

- `IDistributedLock.RenewAsync(TimeSpan?, CancellationToken)` is the renewal mechanism;
  no need to plumb `(resource, lockId)` separately. Verified against
  `src/Headless.DistributedLocks.Abstractions/RegularLocks/IDistributedLock.cs`.
- Acquire semantics use `acquireTimeout: TimeSpan.Zero` (non-blocking). Confirm
  `IDistributedLockProvider` honors `Zero` as "try once, no wait" rather than "default" —
  the interface accepts `TimeSpan?` where `null` means default. **Open for `/dev-plan` to
  verify in implementations** (`Headless.DistributedLocks.Cache`, `.Redis`, `.Core`).
- The retry-processor renewal cadence stays tied to the polling tick (today's behavior).
  Renewal frequency = once per `ProcessAsync` call. TTL = `_GetLockTtl()` already adds a
  10s safety margin over the current polling interval, so a single missed renewal does not
  immediately expire the lock.
- Bootstrapper startup-warning detection: `Bootstrapper` already takes `IServiceProvider`
  (`src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs:15`). It can resolve
  `IDistributedLockProvider` at `StartAsync` time and compare against
  `typeof(NoOpDistributedLockProvider)` to detect the fallback state.

## Handoff

Ready for `/dev-plan` to produce a step-by-step implementation plan covering: handle-field
threading, DI fail-fast wiring, integration-test fixtures for mutual exclusion + renewal,
and verifying `acquireTimeout: TimeSpan.Zero` behavior across all `IDistributedLockProvider`
implementations.
