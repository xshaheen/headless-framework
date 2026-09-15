---
title: Atomic enqueue idempotency by function and tenant
type: feat
date: 2026-09-15
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: github-issue-313
execution: code
origin: https://github.com/xshaheen/headless-framework/issues/313
base: PR #897 head 3ba5984cf (refactor/commit-coordination-minimal-contract) — issue says "branch from #278 merge"; #278 is long merged into main and this branch carries it, satisfying the intent
---

# Atomic enqueue idempotency by function and tenant

## Goal Capsule

Callers can make one-shot job enqueue/schedule calls idempotent for a caller-supplied window by passing an
`IdempotencyKey` plus a TTL, and concurrent or retried calls with the same key inside the window return the first
job's ID without inserting a second row — on PostgreSQL, SQL Server, and the in-memory provider, under the same
atomicity guarantees as ordinary enqueue.

Means: a Jobs-owned reservation row with a lifecycle independent of job retention; one atomic kernel per provider
modeled on the existing keyed-scheduling kernel; coordination through the rebuilt minimal commit-coordination
contract on this branch.

Issue #313 supplies the locked design and acceptance criteria; the four open forks were settled in this session
(2026-09-15, recorded under Key decisions).

---

## Product Contract

### Summary and problem frame

A permanent unique index on `TimeJobEntity(IdempotencyKey, Function, TenantId)` cannot implement expiring
idempotency: the expired row still owns the unique key, and nullable-tenant uniqueness differs across PostgreSQL
and SQL Server. Idempotency needs a durable reservation whose expiry can be replaced atomically, independently of
job-row retention.

The keyed-scheduling machinery this branch already carries (`JobsKeyLock` advisory locks, `JobsStoreClock`
statement UTC clock, savepoint-wrapped coordinated writes) is the right foundation; the reservation kernel reuses
it rather than inventing per-backend upsert SQL.

### Key decisions

- KD1. **Opt-in surface.** `IdempotencyKey` (string) + `IdempotencyTtl` (TimeSpan) on `JobOptions`, honored by
  `EnqueueAsync`, `ScheduleAsync`, and `ScheduleAfterAsync` (all route through `_ScheduleTimeAsync`); rejected
  before persistence on keyed, recurring, and chain nodes. *(session-settled: user-selected over
  `EnqueueAsync`-only — delayed dedup comes free since the three methods share one insertion path.)*
- KD2. **Reservation identity.** `(ScopeKey, Function, ContractVersion, IdempotencyKey)` where `ScopeKey` is the
  internal non-null canonical `S` / `T:<tenant-id>`, plus nullable `TenantId` stored separately for querying.
  *(session-settled: user-selected — function name + ContractVersion, per the issue's "descriptor canonical
  persisted identity"; a contract bump inside the TTL re-executes once, which is wanted when the payload schema
  changed. Deliberately differs from `JobKeyScope`, which excludes version for keyed scheduling.)*
- KD3. **Dedup-hit contract.** Keep `Task<Guid>`; a hit returns the reservation's stored job ID; hits surface
  through a log line and an instrumentation event. No new result type. *(session-settled: user-selected over a
  rich `JobEnqueueResult`.)*
- KD4. **TTL policy.** Required per call when `IdempotencyKey` is set; 1 second minimum, 30 days maximum;
  validated by the options validator before persistence; no host default. *(session-settled: user-selected.)*
- KD5. **Payload is not identity.** Same key + different payload inside the TTL is a hit returning the first job
  ID; documented footgun. *(issue non-goal: payload hashing / auto-derived keys.)*
- KD6. **Retention independence.** Job retention-deletion never releases an unexpired reservation; a hit may
  return an ID whose row is gone. *(issue-locked.)*
- KD7. **Atomicity.** Reservation mutation and job insert are one transaction. Coordinated writes roll back
  together through the minimal commit-coordination contract (`WriteTimeJobsAsync`-style enlistment +
  `requireSavepoints: true` path). *(issue-locked.)*
- KD8. **Key validation.** `IdempotencyKey` validated with `JobContract.ValidateName` rules (200 UTF-16 code
  units, ordinal, no surrounding whitespace or control characters) — one shared validator with `JobKey`.

### Requirements

| ID | Required behavior |
| --- | --- |
| R1 | `JobOptions` MUST gain `IdempotencyKey` and `IdempotencyTtl`; when the key is set, the TTL MUST be supplied
and within [1s, 30d]; validation MUST fail before any persistence effect. |
| R2 | The three ordinary one-shot entry points MUST honor both fields; keyed, recurring, and chain scheduling
MUST reject a set key with a clear validation error before persistence. |
| R3 | Reservation identity MUST be `(ScopeKey, Function, ContractVersion, IdempotencyKey)` with `ScopeKey`
canonical (`S` / `T:<tenant-id>`) and non-null; nullable `TenantId` stored for query only. |
| R4 | Within the TTL: return the stored job ID, insert no job row, emit no second set of enqueue side effects
(dispatch/notify run only for the creator). |
| R5 | After expiry, exactly one contender replaces the reservation (new job ID + new expiry) atomically; losers
observe and return the winner's job ID. |
| R6 | Same key + different descriptor (function or contract version), or different tenant/system scope, MUST NOT
deduplicate. |
| R7 | Tenant resolution MUST complete before the reservation key is formed and MUST NOT change between
reservation and job insertion. |
| R8 | Job cancellation/completion/failure/deletion MUST NOT release an unexpired reservation. |
| R9 | Relational kernels MUST compare expiry against the store's statement UTC clock (`JobsStoreClock`), not an
app clock; the in-memory provider MUST use its injected `TimeProvider` under `_keyedOperations`. |
| R10 | Coordinated enqueue MUST commit or roll back reservation + job together; a crash MUST NOT leave a committed
reservation without its job. |
| R11 | No permanent idempotency unique index on retained `TimeJobEntity` rows; the composite PK on the
reservation table provides uniqueness. |
| R12 | PostgreSQL and SQL Server conformance MUST cover: new, hit, expired race (concurrent post-expiry), scope
isolation, descriptor isolation, coordinated rollback, and process-retry behavior. The in-memory provider MUST
cover new, hit, expired race, scope isolation, descriptor isolation, and side-effect suppression; rollback and
process-retry are relational-only there (the in-memory provider cannot enlist a coordinated write — existing
fail-loud contract in `_RequireCoordinatedWriter` — and its volatile store makes process-retry vacuous). |
| R13 | Docs MUST state idempotency is an enqueue deduplication window, not exactly-once execution. |

### Observable examples

All rows assume valid inputs and satisfied transaction requirements.

| Call | Result | Stored effect |
| --- | --- | --- |
| First enqueue with key `k`, TTL 24h | New job ID | Reservation `(scope, fn, v1, k)` → job A, expires +24h |
| Repeat key `k` within 24h, different payload | Job A's ID (hit, logged) | Nothing inserted |
| Repeat after expiry | One new job B; all concurrent callers get B's ID | Reservation atomically replaced |
| Same key, function `f2` or version `v2` | New job | Separate reservation row |
| Same key, tenant `t1` vs `t2` or system | New jobs | Separate reservation rows |
| Ambient transaction rolls back | Neither visible | Reservation + job both rolled back |
| Reserved job retention-deleted, key unexpired | Job A's stale ID returned (hit) | Reservation unchanged |

### Non-goals

Carried verbatim from the issue: exactly-once execution; handler-side business idempotency; cron
definition/occurrence idempotency; payload hashing or auto-generated keys; key release on job terminal states;
reservation cleanup/compaction (the non-unique `ExpiresAt` index exists so a future sweeper can be added, but
correctness never depends on it).

---

## Planning Contract

### KTD1. Where each piece lives

- `JobOptions` fields: `src/Headless.Jobs.Abstractions/Models/JobOptions.cs` (additive; both `string?` and
  `TimeSpan?`, `init`).
- Shared key/scope validation and canonical `ScopeKey`: extend `JobContract` (or a sibling internal static in
  Jobs.Abstractions) — no new package.
- Reservation entity: `src/Headless.Jobs.Abstractions/Entities/JobIdempotencyReservationEntity.cs`, sealed,
  non-generic, Jobs-owned (like `CronJobOccurrenceEntity`).
- Provider contract: one new member on `IJobPersistenceProvider<TTimeJob,TCronJob>` — see KTD3.
- Coordinated seam: one new member on the internal `ICoordinatedJobWriter` — see KTD3.
- EF kernel: `src/Headless.Jobs.EntityFramework/Infrastructure/JobsEFCorePersistenceProvider.Idempotency.cs`
  (new partial), modeled on `.Keyed.cs`; model configuration beside `JobsKeyedModelConfiguration`.
- In-memory kernel: `src/Headless.Jobs.Core/Provider/JobsInMemoryPersistenceProvider.Idempotency.cs` (new
  partial).
- Manager integration: `src/Headless.Jobs.Core/Managers/JobsManager.cs` `_AddTimeJobAsync` — see KTD4.

### KTD2. Reservation kernel shape (relational)

Acquire `JobsKeyLock` on the reservation identity (digest of `jobs:idem:{ScopeKey}:{Function}:{ContractVersion}:{key}`,
reusing the existing length-delimited format), then within the transaction:

1. Read the reservation by composite PK.
2. `now = JobsStoreClock.GetStatementUtcNowAsync` — expiry compares against the store clock (ownership time),
   never an app-computed absolute deadline.
3. None, or `ExpiresAt <= now`: insert-or-replace the reservation with the **entity's pre-stamped Id** as the
   reserved job ID (the manager stamps `entity.Id` before any provider call — `JobsManager._StampTimeJobTree`
   with `assignIds: true`; the kernel mirrors `Keyed.cs`'s `job.Id == Guid.Empty` guard rather than minting a
   fresh Guid), set `ExpiresAt = now + ttl` — computed **in the statement** where possible, else from the
   store-clock read — insert the job row, `SaveChangesAsync`, disposition `Created`.
4. `ExpiresAt > now`: return the stored job ID, disposition `Existing`, insert nothing.

Uniqueness comes from the composite PK; the advisory lock serializes contenders so the read-modify-write is race
free; a contender blocked on the lock sees the winner only after commit (the lock is transaction-owned). No
per-backend upsert SQL is needed — identical semantics for PostgreSQL and SQL Server through the same EF kernel,
mirroring `JobsEFCorePersistenceProvider.Keyed.cs:60`.

Model configuration: composite PK `(ScopeKey, Function, ContractVersion, IdempotencyKey)` — all ordinal-collated
columns validated the way `JobsKeyedModelConfiguration.ValidateOrdinalScope` validates keyed columns; non-unique
`ExpiresAt` index for a future sweeper; `TenantId` nullable, no unique index involving it. Mapped by
`JobsModelCustomizer` automatically and exposed for `IgnoreModelCustomizer` consumers through the existing
`FinalizeJobsModel` extension (add the reservation configuration there). Breaking model change — accepted for
greenfield (documented in the PR description; consumers on `IgnoreModelCustomizer` must add the configuration and
migrate).

**Upgrade artifact for existing databases.** The harness gets the table through model-first
`RelationalDatabaseCreator.CreateTablesAsync` (fresh container per fixture), but real consumers upgrade existing
databases, and the issue's delivery section demands the migration artifact explicitly. The PR ships the additive
DDL — `CREATE TABLE` for the reservation table plus its `ExpiresAt` index, additive-only, no backfill, no drops —
as a reviewed artifact covering **both** customizer postures: default-customizer consumers get the mapping
automatically via `JobsModelCustomizer` but must still apply the DDL (their first idempotent enqueue would
otherwise fail at runtime with "table does not exist"); `IgnoreModelCustomizer` consumers add the configuration
via `FinalizeJobsModel` and apply the same DDL. Named in the PR description and in the EF package README's
upgrade note. U4's conformance fixtures additionally assert the table exists after `CreateJobsSchemaAsync` so
the model mapping and the documented DDL cannot drift.

### KTD3. Provider and coordinated-write contracts

One new member on `IJobPersistenceProvider<TTimeJob,TCronJob>`:

```csharp
/// <summary>Atomically reserves an idempotency key and inserts the job, or observes the live reservation's job ID.</summary>
Task<Guid> AddIdempotentTimeJobAsync(
    TTimeJob job,
    string idempotencyKey,
    TimeSpan idempotencyTtl,
    CancellationToken cancellationToken = default
);
```

Abstract would be a documented breaking change for third-party implementers; the interface's stated policy on
this branch already treats that as acceptable for greenfield, but a default implementation delegating to
`AddTimeJobsAsync` + a note is wrong (non-atomic), so: **abstract member, documented breaking change** — same
choice keyed scheduling made.

The internal `ICoordinatedJobWriter` gains the twin with the `IRelationalCommitContext` parameter:
`WriteIdempotentTimeJobAsync(job, key, ttl, relationalContext, ct)` — enlists through the caller's transaction,
`requireSavepoints: true` validation, returns `(Guid JobId, bool Created)` semantics via the same result shape
the keyed path uses (the manager needs `Created` to know whether to arm side effects).

### KTD4. Manager integration (`_AddTimeJobAsync`)

The scheduler's `_ScheduleTimeAsync` already resolves policies/descriptor before calling
`_timeJobManager.AddAsync(entity)`. The manager path branches once, early — after `_RunSchedulePipelineAsync`
and tenant capture, before the direct/coordinated write split:

- Entity carries the idempotency fields (internal setters on `TimeJobEntity`: `IdempotencyKey`,
  `IdempotencyTtl`; not part of the persisted row's unique anything — they are transport, stripped before
  persistence… **correction**: persist them as ordinary nullable columns so coordinated writes can re-read the
  intent — decision: store `IdempotencyKey` and `IdempotencyExpiresAt` as nullable columns on the reservation
  only; the entity fields stay transport-only and are nulled before insert).

Simplify: the entity does **not** carry new persisted columns. The manager calls
`AddIdempotentTimeJobAsync(entity, key, ttl)` (direct) or the coordinated writer twin, passing key/TTL as
arguments. Tenant capture runs first (it already does — it happens in `_SnapshotTreeTenants`/pipeline order);
scope is formed from the resolved tenant. **ID flow rule:** the kernel uses the entity's pre-stamped Id as the
reserved job ID and returns `(Guid JobId, bool Created)`; on a hit the manager overwrites `entity.Id` with the
returned `JobId` before returning so `JobScheduler._ScheduleTimeAsync`'s `persisted.Id` surfaces the first
job's ID — and treats the hit as persisted (no tenant-restore in the `finally`) since the returned entity is a
dedup observation, not a failed write. On `Created`: continue with existing side-effect flow (immediate
dispatch signal / notify). On hit: skip dispatch/notify, log + instrument, return the existing ID. Validation
(R1/R2) runs in `JobOptions` validation / `JobSchedulingPolicies.Resolve` territory — before the entity is
built — and the rejection of keyed/recurring/chain combinations happens at the same site (those callers simply
never populate the fields; `ScheduleKeyedAsync` etc. throw if `options.IdempotencyKey` is set).

### KTD5. Scheduler surface

No new public overloads. `_ScheduleTimeAsync` passes `options?.IdempotencyKey` / `options?.IdempotencyTtl`
through to the manager. `JobSchedulerExtensions` needs no change. XML docs on `JobOptions` fields and
`IJobScheduler` enqueue/schedule methods state the dedup-window semantics and the payload footgun (KD5).

### KTD6. In-memory kernel

`AddIdempotentTimeJobAsync` on the in-memory provider: `lock (_keyedOperations)`, `now =
timeProvider.GetUtcNow()`, same branch logic, reservation stored in a private dictionary keyed by the composite
identity. Disposes nothing extra; honors `cancellationToken` at entry.

### KTD7. Alternatives considered

- **Unique index + upsert on the job table** — rejected: cannot expire (issue's core problem).
- **App-clock expiry comparison** — rejected: ownership time belongs to the store clock (time-guide rule).
- **Rich `JobEnqueueResult`** — rejected this round (KD3): splits the enqueue contract; telemetry covers
  observability.
- **Default interface implementation** — rejected: cannot be atomic; silently wrong for third parties.
- **Cleanup sweeper now** — deferred (issue non-goal); index only.

---

## Implementation Units

### U1. Contract + entity + EF model

**Covers R1–R3, R11.** Add `JobOptions` fields + validator rules; `JobIdempotencyReservationEntity`;
EF model configuration (composite PK, `ExpiresAt` index, ordinal collation validation, `FinalizeJobsModel`
exposure); `IJobPersistenceProvider.AddIdempotentTimeJobAsync` (abstract); `ICoordinatedJobWriter.WriteIdempotentTimeJobAsync`.

Tests: `tests/Headless.Jobs.Composition.Tests.Unit` — validator bounds (below 1s, above 30d, missing TTL,
whitespace/control/oversized key), scope canonicalization, keyed/recurring/chain rejection.

### U2. EF kernel + coordinated path

**Covers R4–R10. Depends on U1.** `JobsEFCorePersistenceProvider.Idempotency.cs`: kernel per KTD2, direct
transaction via `_ExecuteKeyedTransactionAsync` shape, coordinated via the new writer member using
`_WithKeyedSavepointAsync`/`requireSavepoints: true`.

Tests: unit kernel tests where feasible; integration primarily in the harness (U4).

### U3. In-memory kernel + manager/scheduler integration

**Covers R1, R2, R4–R9. Depends on U1.** KTD4 manager branch, KTD5 scheduler pass-through, KTD6 in-memory
kernel, instrumentation event (`IJobsInstrumentation` gains `LogIdempotentEnqueueHit`-style member) + log line.

Tests: composition unit tests for hit/new/expiry/scope/descriptor isolation on the in-memory store;
**terminal-state independence (R8)** — cancel/complete/fail/retention-delete the reserved job and assert the
key still dedups; side-effect suppression on hit (no dispatch/notify assertions via fakes); **ID-flow** — a
hit returns the first job's ID through the full `IJobScheduler` path, not the second caller's minted ID.

### U4. Harness conformance + docs

**Covers R12, R13. Depends on U2, U3.** Add `JobsIdempotentEnqueueConformanceTests` to
`tests/Headless.Jobs.EntityFramework.Tests.Harness` (PostgreSQL + SQL Server leaves inherit): new, hit,
expired-race (concurrent post-expiry — exactly one new job, all callers get its ID), scope isolation, descriptor
isolation, **terminal-state independence (R8)** — cancel/complete/fail the reserved job and, separately,
retention-delete it, then re-enqueue the same key inside the TTL and assert a hit returning the original ID —
coordinated rollback (reservation + job invisible after rollback), process-retry shape (re-run after
simulated crash mid-transaction leaves no orphan reservation). Fixtures extend
`JobsCoordinationFixtureBase`/`IJobsCoordinationFixture` — the harness owns schema creation via
`CreateJobsSchemaAsync`, so the new reservation table needs no migration (model-first `EnsureCreated` path);
update `ResetSql` to drop/truncate reservations between tests.

Docs per the sync trigger: `docs/llms/jobs.md`, `src/Headless.Jobs.Abstractions/README.md`,
`src/Headless.Jobs.Core/README.md`, `src/Headless.Jobs.EntityFramework/README.md` — dedup-window semantics,
payload footgun, TTL bounds, scope identity, "not exactly-once" statement (R13).

---

## Verification Contract

- `make build-project PROJECT=src/Headless.Jobs.EntityFramework/Headless.Jobs.EntityFramework.csproj` and the
  Core/Abstractions counterparts.
- `make test-project-fast TEST_PROJECT=tests/Headless.Jobs.Composition.Tests.Unit` (or the repo's unit-test
  target for these projects).
- Docker: Jobs EF PostgreSQL + SQL Server integration projects (harness conformance) — CI runs unit only, so
  these run locally per the repo learning (2026-07-21).
- `make format-check`, `make quality-analyzers-project` for touched projects before PR.
- Full `make test-unit` before opening the PR.

## Definition of Done

All R1–R13 pass at their named boundaries; docs updated per the sync trigger; PR opened against
`refactor/explicit-transaction-guarantees` (PR #897's base) or `main` post-#897-merge — target chosen when the
PR is opened, matching where #897 lands.
