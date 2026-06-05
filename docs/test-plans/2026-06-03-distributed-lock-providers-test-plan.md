---
status: draft
change_ref: xshaheen/feat-distributed-locks-inmemory-provider (vs main; incl. re-merge of postgres provider + Core.Database multiplexing engine)
stack: .NET 10 — xUnit v3 (MTP), AwesomeAssertions, NSubstitute, Bogus, Testcontainers
risk: high
date: 2026-06-03
updated: 2026-06-04
---

## 1. Context

This branch adds two distributed-lock providers and the shared abstraction they sit on: the in-process **InMemory** provider, the **Postgres** provider (advisory + transaction locks), and **`Headless.DistributedLocks.Core.Database`** — a connection-scoped, no-TTL lock abstraction that any session-bound database backend can implement. The merge of the Postgres branch renamed this package from `Core.Db` to **`Core.Database`** and added an **optimistic connection-multiplexing engine** (`ConnectionMonitor`, `MultiplexedConnectionLockPool`, `OptimisticConnectionMultiplexingDbDistributedLock`, `DedicatedConnectionOrTransactionDbDistributedLock`) so that uncontended locks on distinct keys can share one backend connection while colliding keys fall back to dedicated connections. The branch also adds `WaiterCapRegistry` to Core for per-resource and aggregate waiter DoS caps. The portable contract is exercised by the extracted conformance harness (`Headless.DistributedLocks.Tests.Harness`), and the multiplexing engine arrived with its own fake-driven unit suite (`Headless.DistributedLocks.Core.Database.Tests.Unit`). This plan documents the warranted suite end-to-end and concentrates remaining work on the **gaps** the existing tests do not reach.

## 2. Change Surface

- **Behavior added:**
  - **InMemory provider** — process-local exclusive lock, reader-writer lock, and counting semaphore storages with TTL pruning and monotonic per-resource fencing tokens.
  - **Core.Database abstraction** — `ConnectionScopedDistributedLock` orchestrates non-blocking storage attempts, jittered polling-with-release-signal retry, acquire timeout, waiter caps, and fencing-token stamping over an `IConnectionScopedLockStorage` seam; `ConnectionScopedReadWriteLock` adapts shared/exclusive to read/write; `PollingReleaseSignal` is the default wake-up seam; `IFencingTokenSource` is optional.
  - **Core.Database multiplexing engine** (`Internal/`) — `ConnectionMonitor` keepalive-probes a backend connection and cancels every registered monitoring handle when the connection drops; `MultiplexedConnectionLockPool` shares one connection across distinct lock keys and forces a dedicated connection on key collision; `OptimisticConnectionMultiplexingDbDistributedLock` and `DedicatedConnectionOrTransactionDbDistributedLock` select multiplexed vs dedicated strategy via `IDbSynchronizationStrategy`.
  - **Postgres provider** — `pg_advisory_lock` (session) / `pg_advisory_xact_lock` (transaction) storage, `PostgresAdvisoryLockKey` resource→key derivation, `PostgresFencingTokenSource` (durable sequence), `PostgresReleaseSignal` (LISTEN/NOTIFY), `PostgresDataSourceFactory` pooling, `PostgresDatabaseConnection`/`PostgresAdvisoryLock` driving the multiplexing engine.
  - **WaiterCapRegistry** — bounded waiter accounting; throws when `MaxWaitersPerResource` or `MaxConcurrentWaitingResources` is exceeded.

- **Touched:**

  | File / module | Change |
  | ------------- | ------ |
  | `src/Headless.DistributedLocks.Core.Database/*` | Renamed from `Core.Db`; connection-scoped lock + RW providers, handles, release-signal/fencing/storage seams, polling signal |
  | `src/Headless.DistributedLocks.Core.Database/Internal/*` | New multiplexing engine: connection monitor, multiplexed lock pool, optimistic/dedicated strategies |
  | `src/Headless.DistributedLocks.InMemory/*` | New lock, reader-writer, semaphore storages + DI `Setup` |
  | `src/Headless.DistributedLocks.Postgres/*` | New advisory/transaction storage, advisory-key, fencing source, release signal, data-source factory, `PostgresDatabaseConnection`/`PostgresAdvisoryLock`, DI `Setup` |
  | `src/Headless.DistributedLocks.Core/RegularLocks/WaiterCapRegistry.cs` | New waiter-cap accounting used by regular-lock, semaphore, and connection-scoped providers |
  | `src/Headless.DistributedLocks.Core/Headless.DistributedLocks.Core.csproj` | `InternalsVisibleTo` for new providers + `Core.Database` |

- **Blast radius (regression targets):**
  - `Headless.DistributedLocks.Core` regular-lock / semaphore / reader-writer providers — `WaiterCapRegistry` is now shared by all three; a cap bug there breaks every provider.
  - `Headless.DistributedLocks.Redis` — shares the same conformance contract; the harness extraction must keep Redis green (no scenario regressions).
  - `Headless.Redis` shared CAS Lua scripts — referenced by Redis lock + cache; not changed here but adjacent to the fencing semantics under test.
  - **Multiplexing correctness** — a key-collision-detection bug in `MultiplexedConnectionLockPool` would let two logically-distinct holders share a backend that one can unlock, breaking mutual exclusion. Highest-severity regression surface on the branch.
  - DI composition (`Setup` classes) — three overloads each (`IConfiguration` / `Action<TOptions>` / `Action<TOptions,IServiceProvider>`) must all resolve a working provider.

## 3. Test Strategy

The center of gravity is **integration**: a distributed lock's real failure mode is contention and backend semantics across connections, which only surfaces against a real (or real-in-process) storage seam. The conformance harness supplies that band for the portable contract — every provider runs the same `should_*` scenarios against its backend. The **unit** base is larger than usual for a locking library because much of `Core.Database` is *orchestration logic* best proven in isolation with fakes (`FakeDbConnection`, `FakeSynchronizationStrategy`, `FakeTimeProvider`): the multiplexing pool's key-collision and connection-sharing decisions, the connection monitor's keepalive/cancellation behavior, `PostgresAdvisoryLockKey` derivation, and `WaiterCapRegistry` accounting. These are deterministic, fast, and would be expensive and flaky to prove against a real backend. **E2E is intentionally empty** — this is a library with no in-repo application journey; the "full system" for a lock is the multi-connection integration test, which lives in the integration band.

`Diamond: 37 unit · ~34 integration (grouped conformance rows expand to many scenarios) · 0 E2E`. The unit band grew from the original plan because the merged multiplexing engine is fake-driven orchestration; the integration band remains the largest by scenario count once the grouped conformance suites are expanded.

## 4. Test Suite

Legend: **[exists]** already implemented on this branch · **[gap]** warranted but missing · **[expand]** exists but under-covered.

### 4.1 Unit (base: pure logic + fake-driven orchestration)

#### Advisory key, waiter caps, provider orchestration

| ID | Target | Scenario | Input / setup | Expected |
| -- | ------ | -------- | ------------- | -------- |
| U1 | `PostgresAdvisoryLockKey.FromString` | round-trip short ASCII name to single bigint key | name within ASCII-encodable length | `HasSingleKey == true`; `Key` decodes back; deterministic across calls **[exists]** |
| U2 | `PostgresAdvisoryLockKey.FromString` | round-trip explicit `int,int` pair format | hash-string `"xxxxxxxx,xxxxxxxx"` | `HasSingleKey == false`; `Keys` equals input pair **[exists]** |
| U3 | `PostgresAdvisoryLockKey.FromString` | hash long name when hashing allowed | name longer than ASCII budget, `allowHashing=true` | returns a single bigint key; same name → same key **[exists]** |
| U4 | `PostgresAdvisoryLockKey.FromString` | reject un-hashable name when hashing disallowed | long name, `allowHashing=false` | throws `FormatException` — no silent truncation **[exists]** |
| U5 | `PostgresAdvisoryLockKey.FromString` | ASCII length boundary | name at (9 chars) / one past (10) the ASCII budget | at-budget stays a readable ASCII single-key; one-past throws under `allowHashing=false` **[exists]** |
| U6 | `PostgresAdvisoryLockKey.FromString` | unicode / non-ASCII name | multibyte name | hashes deterministically; equal names equal, distinct names differ **[exists]** |
| U7 | `PostgresAdvisoryLockKey` | `Key` accessor on a pair-keyed value throws | construct int-pair key, read `.Key` | throws `InvalidOperationException` (guards single-key misuse) **[exists]** |
| U8 | `WaiterCapRegistry` | `Enter`/`Exit` frees the slot | enter then exit one resource; re-enter | re-enter succeeds, then cap rejects the next — proves the slot was freed **[exists]** |
| U9 | `WaiterCapRegistry` | per-resource cap throws | `MaxWaitersPerResource=2`, 3rd `Enter` on one resource | 3rd throws `InvalidOperationException` naming the cap **[exists, direct]** |
| U10 | `WaiterCapRegistry` | aggregate cap throws (+ extra waiter on existing resource doesn't count) | `MaxConcurrentWaitingResources=2`, 3rd distinct resource | 3rd distinct-resource `Enter` throws; a second `Enter` on an existing resource does not **[exists, direct]** |
| U11 | `WaiterCapRegistry` | null caps = uncapped | both caps null, 50×50 enters | never throws **[exists, direct]** |
| U12 | `WaiterCapRegistry` | unmatched `Exit` is a no-op | `Exit` a resource never entered | no throw; no underflow — subsequent `Enter`/cap behave normally **[exists, direct]** |
| U13 | `DistributedLock` | acquire throws when per-resource waiters exceeded | fake storage holds resource; spawn > `MaxWaitersPerResource` waiters | excess waiter throws cap error; cap released in `finally` **[exists]** |
| U14 | `DistributedLock` | acquire throws when distinct-resource cap exceeded | fake storage holds many resources; exceed `MaxConcurrentWaitingResources` | excess resource throws cap error **[exists]** |
| U15 | `ConnectionScopedDistributedLock` | fencing-token-source failure rolls back the storage handle | fake storage acquires; fake `IFencingTokenSource.NextAsync` throws | storage handle released; exception propagates; no leaked lock **[exists]** |
| U16 | `ConnectionScopedDistributedLock` | jittered polling fallback varies across waits | fake storage stays contended; `FakeTimeProvider`; observe successive `WaitAsync` fallbacks | fallbacks land in 0.8–1.2× band, not identical **[exists]** |
| U17 | `ConnectionScopedDistributedLock` | `TryAcquire` returns null at acquire-timeout; `Acquire` throws | fake storage perpetually contended; advance `FakeTimeProvider` past timeout | `TryAcquireAsync` → null; `AcquireAsync` → `LockAcquisitionTimeoutException`; storage attempted ≥1× **[gap]** |
| U18 | `ConnectionScopedDistributedLock` | caller cancellation surfaces and releases waiter slot | contended storage; cancel caller token mid-wait | `OperationCanceledException`; `WaiterCapRegistry` count returns to zero (no leak) **[gap]** |
| U19 | `ConnectionScopedReadWriteLock` | read maps to shared, write to exclusive; read carries no fencing token | fake storage records `isShared` per call | read → `isShared=true`, `FencingToken==null`; write → `isShared=false`, token issued **[gap]** |
| U20 | `PollingReleaseSignal` | `WaitAsync` returns at fallback when no signal | `FakeTimeProvider`; no `PublishAsync`; advance by fallback | not completed before advance; completes after fallback interval (correctness floor holds) **[exists]** |
| U21 | `PollingReleaseSignal` | `PublishAsync` wakes a pending `WaitAsync` before fallback | start wait with 10-min fallback; publish same resource | wait completes without advancing the fake clock **[exists]** |
| U22 | `PollingReleaseSignal` | signal-before-wait does not deadlock | publish (absorbed), then wait | wait not completed by the earlier publish; still returns by fallback **[exists]** |
| U23 | `PollingReleaseSignal` | `WaitAsync` honors cancellation | cancel linked token during wait | throws `OperationCanceledException` **[exists]** |

#### Core.Database multiplexing engine (merged from Postgres branch — fake-driven)

| ID | Target | Scenario | Input / setup | Expected |
| -- | ------ | -------- | ------------- | -------- |
| U24 | `ConnectionMonitor` | start monitoring and probe on handle registration | fake connection; register monitoring handle | monitor starts; probes the connection **[exists]** |
| U25 | `ConnectionMonitor` | already-closed connection yields an already-cancelled handle | fake connection in closed state | returned handle's token is already cancelled **[exists]** |
| U26 | `ConnectionMonitor` | cancel every registered handle on open→closed transition | several handles registered; flip connection state | all handles' tokens cancel (fan-out) **[exists]** |
| U27 | `ConnectionMonitor` | keepalive probe runs at configured cadence | `FakeTimeProvider`; advance by cadence | probe fires once per interval **[exists]** |
| U28 | `ConnectionMonitor` | bounded command timeout applied to the probe | fake command records timeout | probe uses the configured bounded timeout **[exists]** |
| U29 | `ConnectionMonitor` | dispose unsubscribes and stops worker cleanly | dispose mid-monitor | no throw; worker stops; no further probes **[exists]** |
| U30 | `MultiplexedConnectionLockPool` | two distinct keys acquired uncontended share one connection | fake connections; acquire keys A, B | both served by one shared connection **[exists]** |
| U31 | `MultiplexedConnectionLockPool` | same key acquired twice forces a dedicated connection | acquire key A twice | second acquire gets its own connection **[exists]** |
| U32 | `MultiplexedConnectionLockPool` | two resource strings colliding to one key force dedicated connections | distinct strings, same derived key | collision detected → dedicated connections **[exists]** |
| U33 | `MultiplexedConnectionLockPool` | releasing one of several locks keeps the connection pooled | acquire several on one conn; release one | connection stays open for the remaining holders **[exists]** |
| U34 | `MultiplexedConnectionLockPool` | a release that throws while other locks are held does not close the connection | force release to throw; others held | connection remains open; no fan-out close **[exists]** |
| U35 | `OptimisticConnectionMultiplexingDbDistributedLock` | non-upgradeable strategy multiplexes onto one connection | fake non-upgradeable `IDbSynchronizationStrategy` | acquires multiplex onto a single connection **[exists]** |
| U36 | `OptimisticConnectionMultiplexingDbDistributedLock` | upgradeable strategy uses a dedicated connection per acquire | fake upgradeable strategy | each acquire gets a dedicated connection **[exists]** |
| U37 | `OptimisticConnectionMultiplexingDbDistributedLock` | nested acquire reuses the context handle's connection | acquire within an existing handle context | nested acquire reuses the same connection **[exists]** |

### 4.2 Integration (middle — fat: conformance × providers + backend-specific)

Portable conformance scenarios are defined once in the harness and run per provider. Grouped suite rows (each = the full base contract for that provider) plus the non-portable backend cases that have no sibling.

| ID | Seam / boundary | Scenario | Fixtures / setup | Action | Expected |
| -- | --------------- | -------- | ---------------- | ------ | -------- |
| I1 | InMemory storage → regular-lock contract | full `DistributedLockTestsBase` suite | in-process storage, `FakeTimeProvider` | run all `should_*` lock scenarios | all pass; fencing monotonic; TTL/observability/lease-monitor honored **[exists]** |
| I2 | InMemory storage → reader-writer contract | full `DistributedReadWriteLockTestsBase` suite | in-process RW storage, `FakeTimeProvider` | run all `should_*` RW scenarios incl. writer-waiting-marker + TTL expiry | all pass **[exists]** |
| I3 | InMemory storage → semaphore contract | full `DistributedSemaphoreStorageTestsBase` suite | in-process semaphore storage | run all `should_*` semaphore scenarios | max-count enforced; fencing monotonic; expiry pruned **[exists]** |
| I4 | InMemory storage (deterministic) | prune/expiry/marker/extend under fake clock | `FakeTimeProvider` | advance time across lease boundaries | expired entries pruned; fencing stays monotonic; read-extend refused while writer waiting **[exists]** |
| I5 | Postgres advisory storage → regular-lock contract | conformance subset (session-scoped) | Testcontainers Postgres | run portable lock scenarios | 17/20 pass; TTL/auto-extend/expiration scenarios correctly skipped (no lease) **[exists]** |
| I6 | Postgres advisory storage → reader-writer contract | conformance subset | Testcontainers Postgres | run portable RW scenarios | 8/13 pass; queue-marker + TTL scenarios skipped (no queue representation) **[exists]** |
| I7 | Postgres → advisory key correctness | acquire uses derived key consistently across connections | Testcontainers Postgres | two connections lock same resource name | second blocks/fails; same name → same advisory key **[exists, via I5]** |
| I8 | Postgres transaction lock | `pg_advisory_xact_lock` releases on commit | Testcontainers; explicit txn | commit holding txn | lock released post-commit; re-acquirable **[exists]** |
| I9 | Postgres transaction lock | releases on rollback | Testcontainers; explicit txn | rollback holding txn | lock released post-rollback **[exists]** |
| I10 | Postgres transaction lock | cross-transaction contention | Testcontainers; two txns | txn B tries resource held by txn A | B's `TryAcquire` returns null/false **[exists]** |
| I11 | Postgres transaction lock | guard when no ambient connection/txn | Testcontainers | request xact lock without a connection | throws clear error **[exists]** |
| I12 | Postgres connection death (single) | handle-lost token fires on backend connection drop | Testcontainers; kill backend conn | terminate the holding connection | `HandleLostToken` cancels; consumer observes loss **[exists]** |
| I13 | Postgres contention wake | LISTEN/NOTIFY wakes a waiter faster than polling fallback | Testcontainers; two connections | holder releases and NOTIFYs | waiter wakes before the polling fallback elapses **[exists]** |
| I14 | Postgres fencing source | monotonic tokens across acquires & connections | Testcontainers | acquire/release/re-acquire exclusive repeatedly | tokens strictly increase per resource **[expand — confirm cross-connection]** |
| I15 | Postgres fencing source | concurrent first-callers race the `CREATE SEQUENCE IF NOT EXISTS` gate | Testcontainers; cold source; N parallel `NextAsync` | many threads call before sequence ensured | sequence created once; all callers get distinct increasing tokens; no DDL error **[gap]** |
| I16 | Postgres data-source factory | external vs owned `NpgsqlDataSource` lifetime | Testcontainers | configure with external data source, then dispose provider | external source NOT disposed; owned source IS disposed **[gap]** |
| I17 | Postgres DI `Setup` | all three overloads resolve a working provider | host builder | register via `IConfiguration`, `Action<TOptions>`, `Action<TOptions,IServiceProvider>` | each resolves and acquires a real lock **[exists — setup tests]** |
| I18 | InMemory DI `Setup` | registration resolves working providers | host builder | `AddInMemory…` overloads | lock/RW/semaphore providers resolve and operate **[exists — setup tests]** |
| I19 | Redis regression (blast radius) | conformance still green after harness extraction | Testcontainers Redis | run RW + semaphore conformance + Redis-specific guard | no scenario regressions vs pre-extraction **[exists]** |
| I20 | Postgres release on dead connection | `ReleaseAsync` is idempotent and skips NOTIFY when connection not Open | Testcontainers; drop conn then release | release a handle whose connection died | no throw; no NOTIFY attempted; second release also no-ops **[gap]** |
| I21 | Postgres multiplexing | two distinct resources share one backend connection | Testcontainers; lock A and B | acquire two distinct keys uncontended | both served by a single shared backend connection **[exists]** |
| I22 | Postgres multiplexing | colliding key dedicates a separate backend | Testcontainers; two keys that collide | acquire colliding keys | a separate backend connection is dedicated **[exists]** |
| I23 | Postgres connection-death fan-out | all multiplexed handles cancel when their shared backend dies | Testcontainers; several locks on one backend; kill it | terminate the shared backend connection | every multiplexed handle's lost-token cancels **[exists]** |

> Grouped suite rows (I1–I3, I5–I6, I19) each stand for many concrete `should_*` cases enumerated by the harness. Net ≈ 34 integration behaviors after per-scenario expansion.

### 4.3 E2E (top — thin)

None. This is a library; there is no in-repo application journey to traverse. The multi-connection/real-backend behavior that would be "end-to-end" for a lock is captured in the integration band (I5–I16, I19–I23). State this rather than inventing an E2E shell.

## 5. Coverage Map

| Changed behavior | Covered by | Notes |
| ---------------- | ---------- | ----- |
| InMemory lock / RW / semaphore round-trip + TTL + fencing | I1–I4, U-anchors | Fully covered (incl. deterministic fake-clock suite) |
| Connection-scoped acquire retry / jitter / release-signal | U16, U20–U23, I5, I13 | Fully covered — `PollingReleaseSignal` unit contract (U20–U23) added |
| Connection-scoped timeout & cancellation & cap release | U17, U18, I5 | **Gap:** U17/U18 not yet present at unit band (only Postgres-integration adjacent) |
| Fencing-token stamping + rollback on source failure | U15, I14 | Rollback covered; cross-connection monotonicity to confirm (I14) |
| Reader/write → shared/exclusive mapping | U19, I2, I6 | **Gap:** U19 direct unit mapping missing |
| Connection multiplexing — share vs dedicate on key collision | U30–U37, I21, I22 | Fully covered at both fake-unit and Postgres-integration bands |
| Connection monitor — keepalive + handle fan-out cancellation | U24–U29, I23 | Fully covered; integration confirms multi-handle fan-out on real backend |
| `WaiterCapRegistry` accounting (per-resource + aggregate + null + unmatched) | U8–U14 | Fully covered — direct registry units U8–U12 added; provider-level U13/U14 retained |
| `PostgresAdvisoryLockKey` derivation (ASCII / pair / hash / disallow / boundary / unicode) | U1–U7 | Fully covered — U4–U7 (disallow-throw, boundary, unicode, pair-key misuse) added |
| Postgres advisory + transaction lock semantics | I5–I11 | Fully covered |
| Postgres connection-death (single + multiplexed fan-out) | I12, I23 | Covered at both granularities |
| Postgres contention-wake | I13 | Covered |
| Postgres fencing sequence DDL race | I15 | **Gap** |
| Postgres data-source ownership/lifetime | I16 | **Gap** |
| Postgres release on dead connection idempotence | I20 | **Gap** (release path now runs through the multiplexing pool — verify pool-level idempotence) |
| DI registration (Postgres + InMemory, three overloads) | I17, I18 | Covered |
| Redis conformance unaffected by harness extraction | I19 | Regression guard |

**Implemented in this pass (was gap → now `[exists]`):**
- **Unit (14 tests):** U4–U7 advisory-key edges (`PostgresAdvisoryLockKeyTests`), U8–U12 direct `WaiterCapRegistry` contract (`WaiterCapRegistryTests`), U20–U23 `PollingReleaseSignal` contract (`PollingReleaseSignalTests`). All fast, fake-driven, no container — the architecture-independent subset untouched by the multiplexing merge. Tests.Unit 198 → 208; advisory-key class 3 → 7.

**Remaining gaps (warranted, not yet covered):**
- **Unit:** U17, U18, U19 — connection-scoped timeout, cancellation+cap-release, and read→shared/write→exclusive mapping. Deferred because `ConnectionScopedDistributedLock` now routes through the multiplexing engine; confirm the fake-storage seam still drives the timeout/cancellation paths directly before adding these (avoid asserting against a layer the merge reshaped).
- **Integration:** I15 (fencing DDL race), I16 (data-source ownership), I20 (release on dead connection — now via the multiplexing pool). Postgres-only concurrency/lifetime edges; need Docker.
- **Accepted-as-untested:** none silently — see Out of Scope.

## 6. Out of Scope / Deferred

- **Postgres semaphore** — not implemented on this branch; no provider, so no tests. Not a gap until the provider exists.
- **Redis regular-lock conformance wrapper** — pre-existing absence (Redis lock is covered by storage-level unit tests, not the `DistributedLockTestsBase` wrapper). Redis is not part of this change; tracked separately, not blocked by this plan.
- **Cross-process / multi-host Postgres contention** — true multi-process advisory-lock behavior is asserted within a single test host using multiple connections (I7, I10, I21–I23); spinning up separate OS processes is deferred as disproportionate to the risk delta over multi-connection coverage.
- **Load / soak / throughput** — waiter-cap *correctness* is covered (U13/U14); cap behavior under sustained high contention (latency, fairness at scale) is a separate performance effort.
- **`Npgsql` / Postgres engine internals** — advisory-lock and sequence atomicity are trusted as engine guarantees; tests assert our usage, not the engine.
