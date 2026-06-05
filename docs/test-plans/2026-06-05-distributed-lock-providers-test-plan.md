---
status: draft
change_ref: provider test plan request
stack: .NET 10 + xUnit v3 + Microsoft Testing Platform + Testcontainers
risk: high
date: 2026-06-05
---

## 1. Context

Distributed lock provider coverage is below the roadmap bar when scoped to `Headless.DistributedLocks*` production assemblies, especially branch coverage in Redis, PostgreSQL, SQL Server, and the database core. This plan strengthens provider tests without changing production behavior: shared conformance should prove the portable contract, while leaf integration tests should cover backend semantics such as Redis Lua atomicity, PostgreSQL advisory locks and LISTEN/NOTIFY, SQL Server `sp_getapplock`, and in-process deterministic storage. The external DistributedLock reference suite suggests additional emphasis on cancellation, argument boundaries, safe/case-sensitive names, high-contention exclusivity, handle-lost behavior, and abandonment/cross-process style scenarios.

## 2. Change Surface

- **Behavior:** Add or refine tests for distributed lock, reader-writer lock, and semaphore providers so each backend proves the same portable contract and its documented backend-specific deviations.
- **Touched:**

  | File / module | Change |
  | ------------- | ------ |
  | `tests/Headless.DistributedLocks.Tests.Harness/DistributedLockProviderTestsBase.cs` | Extend shared lock conformance with missing cancellation, name, waiter-cap, fencing, inspection, and high-contention contract cases. |
  | `tests/Headless.DistributedLocks.Tests.Harness/DistributedReaderWriterLockProviderTestsBase.cs` | Extend shared reader-writer conformance where semantics are portable; keep writer-waiting and TTL cases gated to Redis/InMemory. |
  | `tests/Headless.DistributedLocks.Tests.Harness/DistributedSemaphoreStorageTestsBase.cs` | Strengthen semaphore capacity, expiry, fencing, and concurrency cases shared by storage backends. |
  | `tests/Headless.DistributedLocks.Tests.Unit/` | Add thin deterministic tests for validators, waiter caps, connection monitor cadence, fencing-failure cleanup, and instrumentation seams. |
  | `tests/Headless.DistributedLocks.InMemory.Tests.Integration/` | Prove deterministic fake-clock lock, reader-writer, and semaphore behavior with in-process storage. |
  | `tests/Headless.DistributedLocks.Redis.Tests.Integration/` | Prove Redis Lua/key/TTL/script/load behavior against real Redis. |
  | `tests/Headless.DistributedLocks.Postgres.Tests.Integration/` | Prove PostgreSQL advisory lock, fencing sequence, LISTEN/NOTIFY, transaction, and connection-death behavior against Testcontainers. |
  | `tests/Headless.DistributedLocks.SqlServer.Tests.Integration/` | Prove SQL Server application lock, fencing sequence, transaction, resource-name, and connection-death behavior against Testcontainers. |
  | `src/Headless.DistributedLocks.Core/` | Portable provider lifecycle, instrumentation, lease monitor, waiter guardrail, and inspection behavior under test. |
  | `src/Headless.DistributedLocks.Core.Database/` | Connection-scoped acquire/retry/release, connection monitor, fencing source, and polling/push wake-up behavior under test. |
  | `src/Headless.DistributedLocks.Redis/` | Redis lock, reader-writer, semaphore, script, fencing, and TTL behavior under test. |
  | `src/Headless.DistributedLocks.Postgres/` | PostgreSQL advisory lock, release signal, data source, transaction, fencing, and connection monitor behavior under test. |
  | `src/Headless.DistributedLocks.SqlServer/` | SQL Server application lock, resource encoding, storage initializer, fencing, and connection monitor behavior under test. |

- **Blast radius:** Provider DI setup, docs-promised capability matrix, package README examples, OTel ActivitySource/Meter telemetry, Testcontainers stability, `TimeProvider`-driven timeout behavior, and full coverage generation through `make coverage-json`.

## 3. Test Strategy

Use a balanced test diamond with a heavy integration middle because most risk sits at the provider/backend boundary. Unit tests should stay thin and focus on deterministic branch-heavy logic that is hard to cover through real infrastructure; integration tests should own provider conformance, backend semantics, and Testcontainers-backed failure modes; no E2E tests are planned because this is a server-internal NuGet provider surface with no app journey.

`Diamond: 8 unit / 24 integration / 0 E2E`

## 4. Test Suite

### 4.1 Unit (base - thin)

| ID | Target | Scenario | Input / setup | Expected |
| -- | ------ | -------- | ------------- | -------- |
| U1 | `DistributedLockOptionsValidator` | Rejects unsafe resource and waiter limits | Options with zero/negative `MaxResourceNameLength`, zero `MaxWaitersPerResource`, zero `MaxConcurrentWaitingResources`, and valid boundary values | Invalid options return FluentValidation errors on the exact property; valid boundary options pass. |
| U2 | `DistributedLockAcquireOptions` validation path in `DistributedLockProvider` and `ConnectionScopedDistributedLockProvider` | Bad acquire timeouts match the public contract inspired by the reference suite | `AcquireTimeout` below zero except `Timeout.InfiniteTimeSpan`, extremely large timeout, and `TimeUntilExpires = Timeout.InfiniteTimeSpan` where lease monitoring is requested | Providers throw `ArgumentOutOfRangeException` or `ArgumentException` before touching storage; accepted infinite values remain documented N/A for connection-scoped locks. |
| U3 | `WaiterCapRegistry` | Enforces per-resource and total waiter caps deterministically | Fill one resource to `MaxWaitersPerResource`, then fill distinct resources to `MaxConcurrentWaitingResources` | Next waiter is rejected with the expected exception; leaving waiters frees capacity without leaking counts. |
| U4 | `ConnectionMonitor` | Keepalive cadence is deterministic and non-flaky | Fake connection, fake command, fake time advanced past the configured cadence | At least one keepalive probe runs; dispose stops further probes; command failures cancel the connection-lost token only for terminal loss. |
| U5 | `ConnectionScopedDistributedLockProvider` | Fencing-token issuance failure releases the native handle | Fake storage returns a handle, fake fencing source throws, release records calls | Acquire rethrows the fencing failure, storage release is called once with `CancellationToken.None`, and failed-acquire metrics are recorded. |
| U6 | `SqlServerResourceName` / `SqlServerIdentifier` | SQL Server resource encoding is stable, length-safe, and case-sensitive | Empty-ish valid string, long string over native `sp_getapplock` limit, two strings differing only by case, special characters | Encoded names fit SQL Server limits; case-different resources do not collide; invalid identifiers are rejected. |
| U7 | `DistributedLocksDiagnostics` / `DistributedLockMetrics` | Success, timeout, cancellation, and fencing-failure paths emit expected telemetry | Fake provider/storage path with subscribed `ActivityListener` and `MeterListener` | Activity has resource/status tags; success increments acquired metric; timeout/failure increments failed metric; wait-time histogram records once. |
| U8 | Provider setup validators | Provider option validators stay colocated and wired through DI | Build services for Redis/Postgres/SQL Server with missing connection settings, too-long prefixes, and valid minimal settings | Invalid setup fails validation with provider-specific property names; valid setup registers lock, reader-writer, and supported semaphore services. |

### 4.2 Integration (middle - fat)

| ID | Seam / boundary | Scenario | Fixtures / setup | Action | Expected |
| -- | --------------- | -------- | ---------------- | ------ | -------- |
| I1 | Shared lock conformance harness -> all providers | Basic acquire/try-acquire/exclusive behavior remains portable | Expose shared cases through InMemory, Redis, Postgres, and SQL Server provider test classes | Acquire resource, try nested same-resource acquire, acquire different resource, release, reacquire | Same resource is exclusive; different resource is independent; released resource can be reacquired. |
| I2 | Shared lock conformance harness -> all providers | Already-canceled acquire never creates a lock | Hold no lock; pass pre-canceled token to `AcquireAsync` and `TryAcquireAsync` | Call provider methods with `AbortToken` already canceled | Operation is canceled; `IsLockedAsync` remains false; no active lock appears in inspection APIs. |
| I3 | Shared lock conformance harness -> all providers | Acquire timeout contract is consistent under contention | Hold a resource from provider A; acquire same resource from provider B with zero and short timeout | Call `TryAcquireAsync` and `AcquireAsync` | `TryAcquireAsync` returns null on contention; `AcquireAsync` throws `LockAcquisitionTimeoutException` with the resource. |
| I4 | Shared lock conformance harness -> all providers | High-contention exclusivity holds under parallel load | 50-100 parallel acquire tasks against one resource; each increments a guarded counter while holding the lock | Run all tasks through `AcquireAsync` with bounded timeout | Counter never exceeds 1; all successful tasks release; final active lock count is zero. |
| I5 | Shared lock conformance harness -> all providers | Resource names are case-sensitive and prefix-isolated | Two resources differing only by case; two providers with different `KeyPrefix`; two providers with same `KeyPrefix` | Acquire combinations across providers | Case-different resources can be held together; different prefixes do not contend; same prefix contends across service providers. |
| I6 | Shared lock conformance harness -> all providers | Resource length guardrail executes before backend I/O | Configure small `MaxResourceNameLength`; pass longer resource to acquire and inspection methods | Call acquire, `IsLockedAsync`, `GetLockInfoAsync`, and list/count where applicable | Acquire/inspection reject too-long names consistently; no backend lock is created. |
| I7 | Shared lock conformance harness -> fencing-capable providers | Fencing tokens are monotonic and only issued for successful acquires | InMemory, Redis, Postgres, SQL Server with fencing enabled where supported | Acquire resource, attempt failed contended acquire, release, reacquire; also acquire a second resource | Tokens increase per resource only after successful acquire; failed acquire has null token and does not advance the sequence; independent resource starts at its own first token where backend semantics are per-resource. |
| I8 | Shared lock conformance harness -> fencing-capable providers | Stale protected-resource write is rejected by token ordering | Acquire first token, release, acquire second token, then attempt writes to a fenced in-test resource | Write with second token, then write with first token | Second token succeeds; stale first token is rejected and does not overwrite state. |
| I9 | Shared lock conformance harness -> all providers | Release and dispose are idempotent and cannot release another holder | Acquire handle A, release it, acquire handle B, call release/dispose on A again | Inspect lock state and release B | A stale release does not free B; final release frees the resource. |
| I10 | Shared lock conformance harness -> inspection APIs | Inspection APIs report active local locks and backend-specific TTL truth | Hold one finite-lease lock on Redis/InMemory and one connection-scoped lock on Postgres/SQL Server | Call `GetExpirationAsync`, `GetLockInfoAsync`, `ListActiveLocksAsync`, and `GetActiveLocksCountAsync` | Lease providers report TTL and lock ID; DB providers report null TTL; list/count include local active locks; missing resource returns null/zero. |
| I11 | Shared lock conformance harness -> monitoring | HandleLostToken grade matches backend documentation | Redis/InMemory finite TTL without auto-extend; Postgres/SQL Server connection-scoped locks; monitoring disabled path | Let TTL expire or kill backing connection as backend allows | Lease providers cancel token on lost lease; DB providers cancel on connection death; unmonitored handles expose non-cancelable or documented N/A tokens. |
| I12 | Provider -> release signal | Push wake-up beats polling where backend supports push | Redis outbox/release signal and Postgres LISTEN/NOTIFY enabled; a waiter blocked on held resource | Release holder while waiter is pending | Waiter completes promptly after push signal without waiting for the configured polling fallback. |
| I13 | Provider -> release signal | Polling fallback works when push wake-up is disabled | Redis/Postgres configured with push disabled or null outbox | Release holder while waiter is pending | Waiter still acquires within bounded fallback; no push notification is observed in Postgres disabled mode. |
| I14 | Redis storage -> Redis server | Lua scripts remain atomic under contention and `NOSCRIPT` recovery | Real Redis, scripts preloaded, then script cache flushed | Concurrent acquire/remove/replace and next operation after `SCRIPT FLUSH` | Only one acquire succeeds; compare-and-delete cannot delete another lock; script reload path succeeds once and preserves state. |
| I15 | Redis storage -> Redis server | TTL behavior is exact enough and non-shrinking | Locks and semaphores with long TTL, short TTL, null TTL, and shorter extend | Inspect Redis key TTLs and sorted-set scores | Lock key expires when finite; fence key has no TTL; shorter extend/acquire does not shrink holder key TTL; long semaphore TTL does not overflow. |
| I16 | Redis semaphore storage -> Redis server | Expired unpruned semaphore slots do not count | Manually seed holders sorted set with a past score, then call storage count/validate | `GetCountAsync` and `ValidateAsync` | Count returns 0 and validate returns false; test avoids wall-clock race by basing score far in the past. |
| I17 | Redis reader-writer storage -> Redis server | Writer-waiting marker lifecycle is correct | Hold reader, queue writer, cancel or timeout writer, then inspect raw writer key | Call `TryAcquireWriteLockAsync` with cancellation and with timeout | New readers are blocked while marker exists; marker is removed on cancellation/timeout; subsequent readers acquire. |
| I18 | InMemory storage -> fake clock | Deterministic expiry and auto-extend behavior does not depend on wall clock | Fake time provider, finite TTL locks/readers/writers/semaphore slots | Advance fake time and call validate/extend/acquire | Expired entries are pruned; auto-extend renews before expiry; shorter extension never shortens a live lease. |
| I19 | PostgreSQL provider -> advisory locks | Advisory lock keying is stable and collision-safe | Long resources, special characters, case-different resources, same/different prefixes | Acquire locks and inspect `pg_locks` by computed advisory key | Case-different resources do not collide; same prefix contends; different prefix does not contend; keys fit PostgreSQL advisory-lock shape. |
| I20 | PostgreSQL provider -> transaction API | Transaction locks release on commit/rollback and conflict with provider locks | Open two Npgsql connections and transactions; configure same `KeyPrefix` | Acquire transaction-scoped lock, attempt provider and second transaction acquire, commit/rollback | Contenders fail while transaction is active; lock frees after commit/rollback; provider API sees transaction-owned lock. |
| I21 | PostgreSQL provider -> connection monitor | Connection death cancels all affected handles, including multiplexed handles | Acquire one lock and two multiplexed distinct locks; terminate exact backend pid | Observe `HandleLostToken` for each handle | Single handle and every multiplexed handle token cancels within bounded timeout; disposing after loss does not hang. |
| I22 | SQL Server provider -> application locks | `sp_getapplock` modes and transaction APIs match documented behavior | Real SQL Server; session-scoped provider and transaction-scoped API with same prefix | Acquire exclusive/shared locks; attempt conflicting provider and transaction acquires; commit/rollback | Exclusive blocks readers/writers; shared readers coexist; transaction-owned locks conflict and release on commit/rollback. |
| I23 | SQL Server provider -> connection monitor | Killed session cancels handle-lost token and release is robust | Acquire lock with `ReleaseOnDispose = false`; find SPID from `sys.dm_tran_locks`; issue `KILL` | Wait for lost token and dispose handle | Token cancels; disposing/releasing after connection break does not surface an unhandled `SqlException`; next acquire succeeds. |
| I24 | Provider DI setup -> real service provider | Each provider registers only supported primitives and hosted initializers | Build service providers for InMemory, Redis, Postgres, SQL Server with valid options | Resolve `IDistributedLockProvider`, `IDistributedReaderWriterLockProvider`, supported semaphore storage/provider, hosted initializers | Supported services resolve exactly once; unsupported primitives are absent or explicitly N/A; Redis script warmup initializer is provider-owned. |

### 4.3 E2E (top - thin)

No E2E tests are planned. The provider surface is a library-internal/backend integration concern; full app journeys would duplicate integration coverage without adding meaningful signal.

## 5. Coverage Map

| Changed behavior | Covered by | Notes |
| ---------------- | ---------- | ----- |
| Portable lock acquire, contention, timeout, release, and high-contention exclusivity | I1, I2, I3, I4, I9 | Inspired by the reference suite's basic, timeout, cancellation, and parallelism cases. |
| Resource name safety, case sensitivity, prefix isolation, and length guardrails | U1, U6, I5, I6, I19 | SQL Server resource encoding gets unit coverage; provider-visible semantics get integration coverage. |
| Fencing-token correctness and stale-token consumer behavior | U5, I7, I8 | Covers provider acquire path, storage failure cleanup, and protected-resource stale rejection. |
| Inspection APIs for locks | I10 | Confirms TTL/null-TTL distinction instead of forcing impossible DB lease semantics. |
| HandleLostToken documented grade | I11, I21, I23 | Lease providers cover TTL loss; DB providers cover connection death; unsupported paths stay explicit N/A. |
| LeaseMonitor and ConnectionMonitor lifecycle reuse | U4, I11, I21, I23 | Focuses on active probe behavior and token fan-out, including the recently flaky cadence path. |
| DoS guardrails | U1, U3, I6 | Covers validator and runtime waiter/resource-name enforcement. |
| Push wake-up and polling fallback | I12, I13, I17 | Redis/Postgres prove push/fallback; SQL Server remains N/A because the engine blocks server-side. |
| Redis backend Lua, TTL, script, and semaphore semantics | I14, I15, I16, I17 | Targets Redis branch coverage and the failed expired-slot coverage run. |
| PostgreSQL backend advisory locks, transaction locks, and LISTEN/NOTIFY | I12, I13, I19, I20, I21 | Keeps transaction/advisory behavior leaf-specific. |
| SQL Server backend application locks, transaction locks, and connection death | U6, I22, I23 | Includes app-lock mode behavior and release robustness after killed sessions. |
| Provider setup and options validation | U8, I24 | Confirms validators and supported primitive registrations for each provider. |
| OTel ActivitySource + Meter instrumentation | U7 | Listener-based unit coverage is cheaper and less flaky than asserting telemetry in each Testcontainer backend. |

**Gaps:** Cross-process executable tests like the reference suite's lock-taker process are deferred; Headless can get equivalent signal through same-container multi-provider and killed-connection tests without adding a separate helper process. Load/performance testing beyond high-contention correctness is also deferred. Coverage threshold verification is not a test case; it remains a validation step through `make coverage-json` after implementation and Testcontainers stability fixes.

## 6. Out of Scope / Deferred

- Changing provider public APIs or backend semantics; this plan only describes tests.
- Implementing new distributed-lock providers.
- Testing third-party database/Redis internals beyond the observable provider contract.
- Full cross-process helper executable parity with the reference suite unless same-process multi-provider tests miss a concrete failure mode.
- Performance/load benchmarking beyond bounded high-contention correctness tests.
