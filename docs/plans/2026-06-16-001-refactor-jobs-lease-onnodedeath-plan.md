---
title: "refactor(jobs): lease/status vocabulary alignment + OnNodeDeath per-job policy (#268 + #315)"
type: refactor
date: 2026-06-16
origin:
  - "GitHub #268 — refactor(jobs): align lease convention and JobStatus vocabulary with messaging"
  - "GitHub #315 — feat(jobs): OnNodeDeath per-job policy with claim-predicate filter for sweeper coordination"
  - "GitHub #263 — tracker: unify Messaging and Jobs substrates"
depth: deep
---

# refactor(jobs): lease/status vocabulary alignment + OnNodeDeath per-job policy (#268 + #315)

## Summary

Combine two Jobs roadmap issues into one schema wave so both column changes ship under a single EF migration:

- **#268** — align the Jobs per-row lease vocabulary with Messaging: rename `LockHolder → OwnerId` and `LockedAt`(start timestamp) → `LockedUntil`(UTC deadline); introduce a minimal `LeaseDuration` option so `LockedUntil` holds a real deadline; add a lease-expiry self-heal arm to the claim predicate; rename `JobStatus.Done → Succeeded` and `SchedulerOptionsBuilder.NodeIdentifier → NodeId` (new unique-by-construction default).
- **#315** — add a `NodeDeathPolicy` enum + `OnNodeDeath` column (default `Retry`); gate the lease-expiry reclaim arm so only `OnNodeDeath = Retry` rows are speculatively re-claimable; make the dead-node sweep apply per-policy terminal transitions (`MarkFailed → Failed`, `Skip → Skipped`).

The two issues are one mechanism viewed from two angles: `LeaseDuration` makes lease expiry possible; `OnNodeDeath` makes it *safe* by excluding idempotency-critical jobs from speculative re-claim.

**Out of scope (deferred siblings):** SKIP-LOCKED provider packages `Headless.Jobs.PostgreSql`/`Headless.Jobs.SqlServer` (#308/#309), the `SupportsSkipLocked` capability flag and CAS-fallback contract (#310), and worker-side lease renewal (#316 — only the duration knob is pulled forward, not renewal). This plan touches only the optimistic-CAS claim path that exists today.

---

## Problem Frame

`Headless.Jobs` and `Headless.Messaging` express the same per-row pickup-lease concept with divergent vocabulary. Messaging settled on `OwnerId` + `LockedUntil`(deadline) with a uniform `LockedUntil <= now` reclaim; Jobs still uses `LockHolder` + `LockedAt`(start) and has **no lease-deadline comparison at all** — the only recovery path for an in-flight row is the Coordination dead-node sweep (`WhereOwnedBy`). A node that stalls without being declared dead by Coordination (GC pause, transient membership loss while the process lives) leaves its rows stuck until the sweep fires.

Separately, when a node *does* die, every reclaimed in-flight row is uniformly re-executed. For jobs with weak idempotency guarantees this is incorrect — they need a per-job policy that says "on node death, fail" or "skip" rather than "retry".

This plan closes both gaps together because they share the same entities, the same claim predicate, the same dead-node sweep methods, and (per #268's own acceptance criteria) the same EF migration.

---

## Key Technical Decisions

### KTD1 — Lease uses the injected `TimeProvider` (client clock), not the DB server clock

`LockedUntil` is written as `TimeProvider.GetUtcNow() + LeaseDuration` and compared against a bound `@now` parameter — **never** against `now()`/`GETUTCDATE()`. This mirrors Messaging's explicit decision (`src/Headless.Messaging.Storage.SqlServer/SqlServerDataStorage.cs` comment: *"Use the injected TimeProvider rather than the DB server clock … so InMemory and SQL providers share identical pickup semantics — keeps tests with a fake clock honest and avoids subtle drift"*). Jobs already routes all time through `TimeProvider` for the same EF↔InMemory parity reason.

**Trusted-clock posture (answers the open design question):** clock skew is *tolerated, not eliminated*. `LockedUntil` is a per-row **duplicate-suppression / self-heal floor**, not the liveness authority. The authority on "is a node dead" remains the Coordination **incarnation + heartbeat** layer (store-clock liveness in the membership store), already wired into Jobs via `JobsOwnerIdentityAdapter` + `JobsDeadOwnerReclaimer` + `DeadOwnerRecoveryBridge`. A still-alive node is never reclaimed mid-flight because Coordination has not declared it dead; only a stalled `OnNodeDeath = Retry` row is speculatively re-claimed once its lease expires.

### KTD2 — Introduce a minimal `LeaseDuration` option (duration only, not renewal from #316)

`SchedulerOptionsBuilder.LeaseDuration` (default `TimeSpan.FromMinutes(5)`) makes the `LockedAt → LockedUntil` rename honest. Pulling only the *duration* knob forward from #316 keeps #268 coherent; worker-side renewal stays in #316. Add a startup validation/warning analogous to Messaging's `DeadThreshold >= DispatchTimeout` guard: warn when `LeaseDuration` is shorter than the dead-node reconcile/suspicion window such that a still-alive node could be speculatively re-claimed before Coordination would notice — directional, the exact relationship is resolved in U4.

### KTD3 — Drop #268's NodeId startup collision-warning; change the default only

Messaging has **no** startup heartbeat-collision warning — incarnation-guarded membership (`node@incarnation`, `MembershipService.AllocateIncarnationAsync`) structurally makes a duplicate/restarted NodeId a *distinct* owner. Jobs uses the same `INodeMembership`. The warning #268 proposed is redundant with incarnation, so this plan ships only the default change `Environment.MachineName → {MachineName}:{ProcessId}:{Guid8}` (fixes K8s pod-collision) and routes the warning to follow-up. See Scope Boundaries.

### KTD4 — One EF migration for all four column changes; greenfield, no backfill

`LockHolder → OwnerId` (rename), `LockedAt` drop + `LockedUntil` add (semantic change start→deadline means a true rename would carry wrong values, so drop+add), `OnNodeDeath` add (default `Retry`). Per #268 this is greenfield — no historical backfill. Migration artifacts in this repo live in the **demo apps** (`demo/Headless.Jobs.Api.Demo/Migrations/`, `demo/Headless.Jobs.Console.Demo/Migrations/`) plus the model snapshot; the integration tests create schema via `RelationalDatabaseCreator.CreateTablesAsync` and need no migration.

### KTD5 — `OnNodeDeath` gates the lease-expiry arm specifically, not the whole predicate

The composed claim eligibility becomes: a non-started row is claimable if **(unowned) OR (already mine) OR (lease expired AND `OnNodeDeath = Retry`)**. The `OnNodeDeath` filter narrows only the new lease-expiry disjunct — it must not affect the unowned or self-owned arms. This composition is the crux of U7 and is unit-tested directly.

### KTD6 — Per-policy sweep stays inside the two provider `ReleaseDeadNode*Resources` methods

The dead-node sweep already lives in `BasePersistenceProvider.ReleaseDeadNodeTimeJobResources` / `ReleaseDeadNodeOccurrenceResources` (+ in-memory mirror), reached via `JobsDeadOwnerReclaimer` → `IInternalJobManager.ReleaseDeadNodeResources`. #315 replaces the two hardcoded phases (Idle/Queued→released, InProgress→Skipped) with policy-grouped transitions. **No change** to the bridge, reclaimer, manager fan-out, or DI wiring — they pass the dead owner string straight through. Single transaction, `CancellationToken.None`, idempotency (second pass affects zero rows) preserved.

---

## High-Level Technical Design

### Composed claim eligibility (after U4 + U7)

```text
claimable(row, me, now) :=
      row.Status in {Idle, Queued}
  AND (
          row.OwnerId IS NULL                              -- never leased
       OR row.OwnerId == me                                -- re-pickup my own (crash recovery)
       OR (row.LockedUntil <= now                          -- lease expired (self-heal floor)
           AND row.OnNodeDeath == Retry)                   -- ...only for idempotent jobs   [#315 gate]
      )
```
`now` is a bound parameter from `TimeProvider`, not a DB clock (KTD1). Directional — exact predicate shape resolved against `JobsQueryExtensions.WhereCanAcquire` in U4/U7.

### Two recovery paths, two authorities

```text
          stall (alive, lease lapsed)            crash (Coordination: NodeLeft / Dead)
                    │                                          │
                    ▼                                          ▼
        WhereCanAcquire lease-expiry arm            DeadOwnerRecoveryBridge → JobsDeadOwnerReclaimer
        (only OnNodeDeath=Retry rows)               → ReleaseDeadNode*Resources (per-policy sweep)
                    │                                          │
                    ▼                                          ▼
        speculative re-claim (idempotent only)      Retry→released · MarkFailed→Failed · Skip→Skipped
```

### Dead-node sweep per-policy transition (replaces the two fixed phases, U8)

```text
rows WHERE WhereOwnedBy(deadOwner):                       -- single transaction, CancellationToken.None
  Status in {Idle, Queued}                  → release (OwnerId=null, LockedUntil=null, Status=Idle)
  Status == InProgress AND OnNodeDeath==Retry → release (re-claimable by the lease-expiry arm)
  Status == InProgress AND OnNodeDeath==MarkFailed → Status=Failed,  ExecutedAt=now
  Status == InProgress AND OnNodeDeath==Skip       → Status=Skipped, SkippedReason="Node is not alive!", ExecutedAt=now
```
Note the behavior change for `Retry`: today every in-flight row goes to `Skipped`; under #315, `Retry` rows are *released* for re-claim instead. The conformance test `reclaim_touches_only_the_dead_incarnations_non_terminal_rows` must be rewritten to assert the new per-policy outcomes.

---

## Implementation Units

> Units share heavily-overlapping files (`BasePersistenceProvider`, `JobsInMemoryPersistenceProvider`, `JobsQueryExtensions`, the two entities). Execute **sequentially, one editor per file** — do not parallelize across agents. The EF migration (U5) lands last, after all column-shaping units (U1, U4, U6).

### U1. Rename `LockHolder → OwnerId`

**Goal:** Pure rename of the owner-identity lease column across entities, predicates, providers, mapping, harness, and tests. No behavior change.
**Requirements:** #268 (entity + predicate + mapping rename).
**Dependencies:** none.
**Files:**
- `src/Headless.Jobs.Abstractions/Entities/TimeJobEntity.cs` (`LockHolder` → `OwnerId`, keep `internal set`)
- `src/Headless.Jobs.Abstractions/Entities/CronJobOccurrenceEntity.cs` (`LockHolder` → `OwnerId`, plain `set`)
- `src/Headless.Jobs.EntityFramework/Infrastructure/JobsQueryExtensions.cs` (`WhereCanAcquire`, `WhereOwnedBy` — both overloads)
- `src/Headless.Jobs.EntityFramework/Infrastructure/BasePersistenceProvider.cs`, `JobsEFCorePersistenceProvider.cs`, `MappingExtensions.cs`
- `src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs` (incl. `_CloneTicker`/`_CloneCronOccurrence`, `_CanAcquire*`, `ReleaseDeadNode*`)
- `src/Headless.Jobs.EntityFramework/Configurations/TimeJobConfigurations.cs`, `CronJobOccurrenceConfigurations.cs` (`IsRequired(false)` on renamed property)
- `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsCoordinationFixtureBase.cs` (`_InsertColumns`, `SeedTimeJobAsync` SQL/params, `ReadTimeJobAsync` SELECT)
- `tests/Headless.Jobs.Tests.Unit/Infrastructure/JobsQueryPredicateTests.cs` (named args)
**Approach:** Mechanical find/replace within Jobs; verify no stray `LockHolder` remains via solution-wide search. `OwnerId` is the coordination owner string (`node@incarnation` on the durable path).
**Patterns to follow:** existing `WhereOwnedBy`/`WhereCanAcquire` shape; harness raw-SQL seeding pattern (raw because of `internal set`).
**Test suite design:** existing unit + harness coverage; no new tests — this unit is a rename, behavior is pinned by U4/U7/U9.
**Test scenarios:** Test expectation: none — pure rename; existing `JobsQueryPredicateTests` + conformance suite must stay green after the rename.
**Verification:** solution builds; `JobsQueryPredicateTests` and the Postgres/SqlServer conformance suites pass unchanged (modulo identifier renames).

### U2. Rename `JobStatus.Done → Succeeded`

**Goal:** Align the terminal-success status name with Messaging's `Succeeded`. Pure rename; `DueDone` retained (retry-success), `Failed`/`Skipped`/`Cancelled` unchanged.
**Requirements:** #268 (status vocabulary alignment).
**Dependencies:** none.
**Files:**
- `src/Headless.Jobs.Abstractions/Enums/JobStatus.cs` (`Done = 3` → `Succeeded = 3`; preserve integer value — harness/tests cast `(int)`)
- `src/Headless.Jobs.Core/Src/JobsExecutionTaskHandler.cs` (3 callsites)
- `src/Headless.Jobs.Dashboard/Infrastructure/Dashboard/JobsDashboardRepository.cs` (1)
- `tests/Headless.Jobs.Tests.Unit/InternalFunctionContextTests.cs` (3), `Infrastructure/JobsQueryPredicateTests.cs` (2)
- `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsCoordinationConformanceTests.cs` (2)
**Approach:** Keep the ordinal `3` so persisted rows and raw-SQL seeds are unaffected (enum persisted by value). Front-end mirrors (`wwwroot/src/views/TimeJob.vue`, compiled `dist`) are out of C# scope; flag the dashboard status-label mapping as a doc/UI follow-up if it maps by name.
**Patterns to follow:** CONCEPTS.md "one canonical name, explicit aliases" discipline.
**Test suite design:** existing unit/harness coverage.
**Test scenarios:** Test expectation: none — rename; existing tests asserting `Succeeded`/`DueDone` terminal behavior must pass.
**Verification:** build green; no `JobStatus.Done` references remain in C#; conformance "terminal row untouched" case passes.

### U3. Rename `NodeIdentifier → NodeId` + unique-by-construction default

**Goal:** Rename the option and change its default to `{MachineName}:{ProcessId}:{Guid8}`.
**Requirements:** #268 (options vocabulary + NodeId default).
**Dependencies:** none.
**Files:**
- `src/Headless.Jobs.Abstractions/JobsOptionsBuilder.cs` (`NodeIdentifier` → `NodeId`; default helper producing `{MachineName}:{ProcessId}:{Guid8}`)
- `src/Headless.Jobs.Abstractions/Coordination/DefaultJobsOwnerIdentity.cs`, `src/Headless.Jobs.Core/Coordination/JobsOwnerIdentityAdapter.cs` (fallback display owner)
- `src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs` (ctor `_lockHolder` source)
- `src/Headless.Jobs.Dashboard/Endpoints/DashboardEndpoints.cs`
- `tests/Headless.Jobs.Tests.Unit/JobsOptionsBuilderTests.cs`, `Coordination/JobsOwnerIdentityAdapterTests.cs`
**Approach:** Compute the default once (static helper); `Guid8` = first 8 chars of `Guid.NewGuid().ToString("N")`. **Do not** implement the heartbeat-collision startup warning (KTD3) — incarnation already disambiguates.
**Patterns to follow:** existing option-property style in `JobsOptionsBuilder`.
**Test suite design:** unit tests in `JobsOptionsBuilderTests`.
**Test scenarios:**
- Happy path: default `NodeId` matches `{MachineName}:{ProcessId}:{8 hex}` shape (regex assert); two builder instances in the same process produce distinct defaults (Guid8 differs).
- Edge: explicitly-set `NodeId` is preserved verbatim (no default override).
- Edge: in-memory provider owner reflects the configured `NodeId`.
**Verification:** unit tests pass; dashboard "machine jobs" grouping regression-checked against the new per-process-unique default.

### U4. `LockedAt → LockedUntil` deadline semantics + `LeaseDuration` option

**Goal:** Make the lease a real deadline. Add `LeaseDuration`; write `LockedUntil = now + LeaseDuration` at every stamp site; add the `LockedUntil <= now` self-heal arm to the claim predicate. **Riskiest unit.**
**Requirements:** #268 (lease deadline + uniform `LockedUntil < now` reclaim), KTD1, KTD2.
**Dependencies:** U1 (shares predicate/provider files).
**Files:**
- `src/Headless.Jobs.Abstractions/Entities/TimeJobEntity.cs`, `CronJobOccurrenceEntity.cs` (`LockedAt` → `LockedUntil`)
- `src/Headless.Jobs.Abstractions/JobsOptionsBuilder.cs` (+ `LeaseDuration`, default 5 min; + validator/startup warning per KTD2)
- `src/Headless.Jobs.EntityFramework/Infrastructure/JobsQueryExtensions.cs` (`WhereCanAcquire` gains the `LockedUntil <= now` arm — accept `now` param)
- `src/Headless.Jobs.EntityFramework/Infrastructure/BasePersistenceProvider.cs`, `JobsEFCorePersistenceProvider.cs` (every `LockedAt = now` stamp → `LockedUntil = now + LeaseDuration`; `QueueTimedOut*` reclaim respects the lease)
- `src/Headless.Jobs.EntityFramework/Infrastructure/MappingExtensions.cs` (release setters null `LockedUntil`)
- `src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs` (mirror: `_CanAcquire*`, stamp sites, clone)
**Approach:** Source `LeaseDuration` into the providers (it lives on the options builder already injected). The claim predicate currently has no time comparison; `WhereCanAcquire` must take a `now` argument (bound parameter) so EF translates `LockedUntil <= @now`. Document the client-clock decision inline citing the Messaging precedent (KTD1). Decide and document the `LeaseDuration` vs reconcile-window ordering guard (KTD2).
**Execution note:** Start with a failing unit test for the new lease-expiry arm in `JobsQueryPredicateTests` before touching the providers — the predicate composition is the subtle part.
**Technical design:** see HLD "Composed claim eligibility" (the U4 arms; U7 adds the `OnNodeDeath` gate). Directional.
**Patterns to follow:** Messaging `LockedUntil <= @Now` pickup UPDATE (`SqlServerDataStorage`/`PostgreSqlDataStorage`); existing `TimeProvider.GetUtcNow().UtcDateTime` usage.
**Test suite design:** unit-level predicate tests in `JobsQueryPredicateTests` (in-memory `IQueryable`, no Docker — fastest loop); cross-provider behavior covered by U9 conformance additions.
**Test scenarios:**
- Happy path: a row with `LockedUntil` in the past is claimable; a row with `LockedUntil` in the future is **not** claimable by a different owner.
- Happy path: claiming stamps `LockedUntil == now + LeaseDuration` (assert against fake `TimeProvider`).
- Edge: `LockedUntil IS NULL` (never leased) remains claimable (unowned arm intact).
- Edge: a future-leased row is still re-claimable by its **own** owner (crash-recovery arm intact).
- Edge: lease comparison uses the injected clock — advancing the fake `TimeProvider` past `LockedUntil` flips claimability (proves no DB-clock dependency).
- Error/config: `LeaseDuration <= TimeSpan.Zero` rejected by the validator; misordered `LeaseDuration` vs reconcile window emits the startup warning.
**Verification:** new predicate unit tests pass; conformance suites (U9) green; build green with no `LockedAt` references.

### U5. Single EF migration + entity config + demo snapshots

**Goal:** Land all schema changes (U1, U4, U6 columns) in one migration per consumer.
**Requirements:** #268 + #315 (EF migration), KTD4.
**Dependencies:** U1, U4, U6 (all column-shaping units complete).
**Files:**
- `src/Headless.Jobs.EntityFramework/Configurations/TimeJobConfigurations.cs`, `CronJobOccurrenceConfigurations.cs` (`OwnerId` required(false); `LockedUntil` nullable; `OnNodeDeath` default `Retry`; index support for the new claim arm if warranted — wrap any new index in the repo's idempotent-DDL envelope convention)
- `demo/Headless.Jobs.Api.Demo/Migrations/*` (+ snapshot), `demo/Headless.Jobs.Console.Demo/Migrations/*` (+ snapshot)
**Approach:** Use the `x-dotnet-ef-migration` skill. Migration ops: `RenameColumn LockHolder→OwnerId`; `DropColumn LockedAt`; `AddColumn LockedUntil` (nullable `timestamptz`/`datetime2`); `AddColumn OnNodeDeath` (int, default `Retry`). Greenfield — no data transform. Regenerate the model snapshot. Tests need no migration (`CreateTablesAsync`).
**Patterns to follow:** existing `*_InitialJobsOperationalStore` demo migrations; `storage-initializer-lifecycle-correctness` idempotent-DDL guidance for any added index.
**Test suite design:** migration apply verified via demo app; conformance suites already create schema from the model.
**Test scenarios:** Test expectation: none — migration is generated DDL; correctness proven by U9 conformance suites running against a freshly-created schema that includes the new columns.
**Verification:** `dotnet ef database update` on a demo app applies cleanly; snapshot matches model (no pending-changes warning); both DB conformance suites pass against the new schema.

### U6. `NodeDeathPolicy` enum + `OnNodeDeath` column

**Goal:** Add the per-job policy enum and column (default `Retry`) on both lease-bearing entities.
**Requirements:** #315 (enum + column).
**Dependencies:** U1, U4 (shares entity files).
**Files:**
- `src/Headless.Jobs.Abstractions/Enums/NodeDeathPolicy.cs` (new: `Retry`, `MarkFailed`, `Skip`)
- `src/Headless.Jobs.Abstractions/Entities/TimeJobEntity.cs`, `CronJobOccurrenceEntity.cs` (`OnNodeDeath` property, default `Retry`)
- `src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs` (`_CloneTicker`/`_CloneCronOccurrence` copy `OnNodeDeath` — **or state silently drops**)
- `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsCoordinationFixtureBase.cs` (`_InsertColumns`, `SeedTimeJobAsync` gains `onNodeDeath` param + column, `ReadTimeJobAsync` selects it)
**Approach:** Enum persisted by ordinal (consistent with `JobStatus`); `Retry = 0` so the default and `default(enum)` agree. The clone-method update is load-bearing for in-memory parity. No change to `InternalFunctionContext`/`InternalManagerContext` — the policy is read directly off entity rows in the sweep/predicate.
**Patterns to follow:** existing enum file shape; harness seed-column pattern.
**Test suite design:** column round-trip covered by U9; clone-parity implicitly covered by in-memory conformance.
**Test scenarios:**
- Happy path: a seeded row defaults to `OnNodeDeath = Retry` when unspecified.
- Edge: in-memory clone preserves a non-default `OnNodeDeath` across an update cycle (guards the clone-drop bug).
**Verification:** build green; harness can seed all three policies; in-memory round-trip preserves the value.

### U7. Fold the `OnNodeDeath = Retry` gate into the claim predicate

**Goal:** Narrow the U4 lease-expiry arm so only `OnNodeDeath = Retry` rows are speculatively re-claimable.
**Requirements:** #315 (claim-predicate filter), KTD5.
**Dependencies:** U4 (lease-expiry arm), U6 (column).
**Files:**
- `src/Headless.Jobs.EntityFramework/Infrastructure/JobsQueryExtensions.cs` (`WhereCanAcquire` — add `AND OnNodeDeath == Retry` to the lease-expiry disjunct only)
- `src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs` (`_CanAcquire*` mirror)
**Approach:** The gate attaches to the lease-expiry arm exclusively (HLD "Composed claim eligibility"). The unowned and self-owned arms are untouched — a row you already own is always re-pickable regardless of policy. Verify EF query translation of the composed boolean.
**Execution note:** Test-first — extend the U4 predicate tests with policy permutations before editing the predicate.
**Patterns to follow:** existing `WhereCanAcquire` disjunction structure.
**Test suite design:** `JobsQueryPredicateTests` (in-memory `IQueryable`); cross-provider in U9.
**Test scenarios:**
- Covers #315 claim filter. Happy path: lease-expired `OnNodeDeath=Retry` row → claimable.
- Happy path: lease-expired `OnNodeDeath=MarkFailed` row → **not** claimable (excluded from speculative re-claim).
- Happy path: lease-expired `OnNodeDeath=Skip` row → not claimable.
- Edge: `OnNodeDeath=Skip` row that is **unowned** (never leased) → still claimable (gate doesn't touch the unowned arm).
- Edge: `OnNodeDeath=MarkFailed` row owned by **me** → still re-claimable (self-owned arm intact).
**Verification:** predicate unit tests pass for all policy×lease-state permutations; EF translation succeeds (no client-eval warning).

### U8. Per-policy dead-node sweep

**Goal:** Replace the two fixed sweep phases with policy-grouped transitions in both providers.
**Requirements:** #315 (sweep per-policy transitions), KTD6.
**Dependencies:** U6 (column).
**Files:**
- `src/Headless.Jobs.EntityFramework/Infrastructure/BasePersistenceProvider.cs` (`ReleaseDeadNodeTimeJobResources`, `ReleaseDeadNodeOccurrenceResources`)
- `src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs` (mirrored sweep methods)
**Approach:** Within the existing single transaction (`CancellationToken.None`, `WhereOwnedBy(dead)`): Idle/Queued → released; InProgress split by `OnNodeDeath` — `Retry` → released (re-claimable via U7 arm), `MarkFailed` → `Failed`, `Skip` → `Skipped` (reason "Node is not alive!", `ExecutedAt = now`). Keep idempotency: a second pass matches zero rows because released rows lose the dead owner and terminal rows fail `WhereOwnedBy`'s non-terminal guard.
**Technical design:** HLD "Dead-node sweep per-policy transition". Directional.
**Patterns to follow:** the current two-phase `ExecuteUpdateAsync` structure; `terminal-state-overwrite-on-redelivery` (respect affected-rows; never overwrite terminal state).
**Test suite design:** cross-provider conformance in U9 (this is integration-shaped — exercises real transactional UPDATE grouping).
**Test scenarios:** (enumerated in U9 — the sweep is only observable end-to-end).
**Verification:** U9 conformance scenarios pass on both DB providers and in-memory; idempotency scenario (second pass = 0 rows) passes.

### U9. Conformance + unit tests for OnNodeDeath behavior

**Goal:** Pin the new sweep + claim semantics across all three providers.
**Requirements:** #315 acceptance criteria (kill-a-node-mid-execution per policy), KTD5/KTD6.
**Dependencies:** U7, U8.
**Files:**
- `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsCoordinationConformanceTests.cs` (new abstract `virtual` scenarios; rewrite `reclaim_touches_only_the_dead_incarnations_non_terminal_rows` for per-policy outcomes)
- `tests/Headless.Jobs.EntityFramework.PostgreSql.Tests.Integration/PostgreSqlConformanceTests.cs`, `tests/Headless.Jobs.EntityFramework.SqlServer.Tests.Integration/SqlServerConformanceTests.cs` (override-with-`[Fact]`)
- `tests/Headless.Jobs.Tests.Unit/Infrastructure/JobsQueryPredicateTests.cs` (any remaining predicate permutations not in U4/U7)
**Approach:** Add three sweep scenarios mirroring the issue's acceptance criteria; seed per-policy rows via the U6-extended `SeedTimeJobAsync`. Reuse the existing `IJobsCoordinationFixture` + `ControlledNodeMembership`-style dead-node trigger; do not add a new fixture (CLAUDE.md harness rule). Follow `tests/Headless.Messaging.Core.Tests.Harness/DeadOwnerReclaimConformanceTests.cs` as the parity template.
**Test suite design:** conformance (cross-provider, Docker) owns the sweep + claim integration; unit (`JobsQueryPredicateTests`) owns predicate composition. No new test infrastructure — extend the existing harness.
**Test scenarios:**
- Covers #315. Dead node with `OnNodeDeath=Retry` in-flight row → released and subsequently re-claimable by a survivor.
- Covers #315. Dead node with `OnNodeDeath=MarkFailed` → row transitions to `Failed`, not re-executed.
- Covers #315. Dead node with `OnNodeDeath=Skip` → row transitions to `Skipped` with reason.
- Idempotency: second sweep pass over the same dead owner affects zero rows.
- Isolation: sweep touches only the dead incarnation's non-terminal rows; a live incarnation's freshly-stamped row and a terminal `Succeeded` row are untouched.
- Cross-arm: a still-leased (`LockedUntil` future) `OnNodeDeath=Skip` row owned by a *live* node is not claimable by a survivor (KTD1 floor + KTD5 gate together).
**Verification:** all new conformance scenarios pass on Postgres + SqlServer + in-memory; full Jobs unit + harness suites green.

### U10. Docs sync

**Goal:** Update agent-facing + package docs for the public-API changes (doc-sync trigger: option rename, new option, new enum, status rename).
**Requirements:** CLAUDE.md doc-sync contract.
**Dependencies:** U3, U4, U6.
**Files:**
- `src/Headless.Jobs.Core/README.md` (NodeId rename, `LeaseDuration`, `OnNodeDeath`/`NodeDeathPolicy`, `Succeeded`)
- `docs/llms/jobs.md` (keep in lockstep per `docs/authoring/AUTHORING.md`)
**Approach:** Follow `docs/authoring/AUTHORING.md` drift checks; explain the lease-as-floor vs Coordination-as-authority model (KTD1) and the OnNodeDeath safety interaction, not just the API surface.
**Test suite design:** n/a (docs).
**Test scenarios:** Test expectation: none — documentation.
**Verification:** both doc surfaces updated and consistent; no stale `NodeIdentifier`/`LockHolder`/`LockedAt`/`Done` references.

---

## Scope Boundaries

### In scope
All of #268 (lease + status + NodeId vocabulary, minimal `LeaseDuration`, lease-expiry self-heal) and #315 (`NodeDeathPolicy`, `OnNodeDeath` column, claim-predicate gate, per-policy sweep), under one EF migration.

### Deferred to Follow-Up Work
- **NodeId heartbeat-collision startup warning** (#268 sub-item) — redundant with incarnation-guarded membership (KTD3); ship the default change only.
- **Dashboard status-label mapping** for `Done → Succeeded` if the Vue/`dist` front-end maps by name — UI follow-up, outside the C# contract.

### Outside this plan (separate issues)
- SKIP-LOCKED provider packages #308 (`Headless.Jobs.PostgreSql`) / #309 (`Headless.Jobs.SqlServer`).
- `SupportsSkipLocked` capability flag + CAS-fallback contract #310.
- Worker-side lease renewal #316 (only the `LeaseDuration` knob is pulled forward here, not renewal).

---

## Risks & Dependencies

- **In-memory ↔ EF parity drift (high).** Every column add/rename and the sweep rewrite must land in `JobsInMemoryPersistenceProvider` clone + predicate + sweep mirrors in lockstep, or in-memory silently drops state (`_CloneTicker`/`_CloneCronOccurrence`) or diverges on policy. Mitigation: U6/U7/U8 each name the mirror file; conformance suite runs in-memory too.
- **Lease-expiry false re-claim (high).** If `LeaseDuration` is shorter than a job's real execution time, a *still-running* `OnNodeDeath=Retry` job is speculatively re-claimed (duplicate execution). Mitigation: the `OnNodeDeath` gate (idempotency-critical jobs opt out), the startup ordering warning (KTD2), and a sane 5-min default. Document the LeaseDuration-must-exceed-runtime contract.
- **Predicate translation (medium).** The composed `WhereCanAcquire` boolean must translate to SQL (no client-eval). Mitigation: U4/U7 are test-first against `IQueryable`; conformance proves real SQL.
- **Migration drift (medium).** Demo snapshots must match the model; the new claim-arm index (if added) must use the idempotent-DDL envelope. Mitigation: U5 verifies no pending-changes; `storage-initializer-lifecycle-correctness` guidance.
- **Coordination coupling (low).** The sweep relies on the existing `JobsDeadOwnerReclaimer` → `ReleaseDeadNodeResources` path being unchanged (KTD6). No DI/bridge edits — keeps blast radius inside the providers.

---

## Sources & Research

- **#268 / #315 / #263** GitHub issues (scope, acceptance criteria, dependency graph).
- **Messaging clock decision** — `src/Headless.Messaging.Storage.SqlServer/SqlServerDataStorage.cs` (`LockedUntil <= @Now` with injected `TimeProvider`, explicit "rather than DB server clock" comment); `src/Headless.Messaging.Storage.PostgreSql/PostgreSqlDataStorage.cs` mirror. Basis for KTD1.
- **Coordination identity model** — `src/Headless.Coordination.Core/MembershipService.cs` (`node@incarnation`), `IMembershipStore.cs` (incarnation-guarded registration), `DeadOwnerRecoveryBridge.cs`, `README.md`. Basis for KTD1/KTD3.
- **Current Jobs claim/sweep map** — `JobsQueryExtensions.WhereCanAcquire`/`WhereOwnedBy`, `BasePersistenceProvider.ReleaseDeadNode*Resources`, `JobsInMemoryPersistenceProvider`, `JobsDeadOwnerReclaimer`, `IInternalJobManager.ReleaseDeadNodeResources` (repo research).
- **Learnings** — `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery.md` (predicate/CAS gating, respect affected-rows), `docs/solutions/architecture-patterns/coordination-register-establishes-durable-liveness.md` (store-as-temporal-authority, incarnation), `docs/solutions/best-practices/storage-initializer-lifecycle-correctness.md` (idempotent multi-provider DDL), `docs/solutions/architecture-patterns/unified-provider-setup-builder-pattern.md` (conformance-harness shape).
- **Note:** the #427/#449/#422/#396 dead-owner recovery-bridge parity work is not yet distilled in `docs/solutions/` — primary sources are the source files above plus `docs/brainstorms/2026-06-15-messaging-dead-owner-recovery-parity-requirements.md`. Capture via `/x-compound` after this lands.
