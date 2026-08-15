---
title: "Close the Jobs review backlog inside PR #822 - Plan"
type: feat
date: 2026-08-14
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: x-review-followup
execution: code
origin:
  - https://github.com/xshaheen/headless-framework/pull/822
  - https://github.com/xshaheen/headless-framework/issues/816
  - https://github.com/xshaheen/headless-framework/issues/817
  - https://github.com/xshaheen/headless-framework/issues/818
  - https://github.com/xshaheen/headless-framework/issues/819
  - https://github.com/xshaheen/headless-framework/issues/830
  - https://github.com/xshaheen/headless-framework/issues/834
  - https://github.com/xshaheen/headless-framework/issues/836
---

# Close the Jobs review backlog inside PR #822 - Plan

## Goal Capsule

Land the seven still-open Jobs issues that orbit PR #822 inside that same PR, so the cron-recovery
delivery ships with its own review backlog drained rather than trailing it. Three of them (#816,
#817, #819) are inherited from the PR #785 review that #822 supersedes; three (#830, #834, #836)
came out of the #822 review follow-up; one (#818) is already half-fixed by #822 and needs its
remaining half.

The branch already carries three verified fix commits from that follow-up (`778195128`,
`139a84ded`, `40df0648c`, findings #1-#4 and #8-#14). This plan adds to that, it does not redo it.

## Live Baseline

| Ref | SHA | Relationship |
|---|---|---|
| `origin/main` | `0f7bf25d6` | Carries the SSH.NET 2026.0.0 fix (PR #837) that unblocks every integration suite. Not yet merged into the branch. |
| `xshaheen/jobs-cron-recovery-consolidation` | `40df0648c` | PR #822 head, three follow-up fix commits applied, still on SSH.NET 2025.1.0. |

Current verification reach on the branch: Jobs unit suite 878/878 green; PostgreSQL and SQL Server
integration suites **unrunnable** until `origin/main` is merged.

## Product Contract

### Problem Frame

Seven issues remain open against the subsystem this PR rewrites. Left open, they fragment the work:
#816 defines a cross-provider semantic that #834's extraction must encode, #817 is a p1 skipped-tick
window in the exact path #822 hardens, and #819 degrades the clustered-key locality of the rows #822
made the provider responsible for creating. Deferring them means a second pass over the same files
with the same conformance suites.

The constraint that shapes everything below: **cross-provider behaviour cannot be verified until
`origin/main` is merged**, because the SSH.NET advisory blocks restore for every Testcontainers-
referencing test project. That makes the merge unit U1, not an afterthought.

### Requirements

- **R1 - Merge before change.** Merge current `origin/main` into the branch first and re-establish
  the full green baseline (unit + both relational integration suites) before any new edit. No unit
  below may be claimed verified on unit tests alone.
- **R2 - Establish behaviour before changing it.** The conformance test
  `queueing_an_instant_with_a_terminal_occurrence_materializes_nothing` currently PASSES on both
  native providers even though static reading says the native insert should re-materialize. Resolve
  which assumption is wrong before implementing R3. If the test passes for a reason unrelated to
  occupancy it is a silent-pass test and must be fixed as part of R3.
- **R3 - One occupied-instant rule (#816).** All four providers (in-memory, generic EF, native
  PostgreSQL, native SQL Server) resolve `QueueCronJobOccurrencesAsync` with no occurrence identity
  identically against a pre-existing terminal row. The chosen rule is stated in KTD1. Both provider
  READMEs and the SQL comments must match the implemented rule. A conformance scenario drives the
  `NextCronOccurrence is null` path against a pre-existing terminal row on every provider.
- **R3a - Ordering follows the rule.** `MaterializeCronScheduleOccurrenceAsync` orders candidate
  occurrences live-first before `CreatedAt`/`Id`, so a terminal row cannot mask a coexisting live
  one. Applies to the EF and in-memory providers. (Folded from closed #831.)
- **R3b - Guard cost follows the rule.** If the rule retains a per-candidate occupancy probe, it is
  batched into one query per claim wave rather than one round trip per candidate. If the rule
  removes the probe, this requirement is satisfied by its removal. (Folded from closed #832.)
- **R4 - Seed the position at creation (#817).** A cron definition created at runtime through
  `ICronJobManager` persists `ReconciledThroughUtc` and `NextDueUtc` in the same insert, for both
  the single and bulk paths, so no tick between creation and first scheduler encounter is silently
  skipped. The anchor is **store time read inside the inserting transaction**, not the node clock.
  `JobsManager` holds only a node `TimeProvider` and hands prebuilt entities to persistence, so this
  requirement is owned by the **persistence boundary** - both the direct insert path and the
  coordinated write path - which obtains the store anchor transactionally and computes the initial
  projection from it. A node-clock seed does not satisfy R4: under clock skew it reintroduces the
  same skipped-tick window the requirement exists to close.
- **R4a - Statement clock, not transaction-start clock.** The anchor must be the provider's *current
  statement* time - PostgreSQL `clock_timestamp()`, SQL Server `SYSUTCDATETIME()` - not
  `DateTime.UtcNow` translated by EF. This repository already documents that PostgreSQL translates
  that to `now()`, which is frozen at transaction start. The coordinated write path attaches to an
  already-open caller transaction, so a long-running ambient transaction would seed a definition
  with an anchor from before it existed and manufacture immediate false backlog for skip/coalesce to
  process. Coverage must include a coordinated PostgreSQL insert whose ambient transaction began
  well before the insert, asserting the seed is anchored at insertion time.
- **R4b - The seed result is returned, not recomputed.** Today `JobsManager` computes
  `nextOccurrence` from the node clock before persistence (`JobsManager.cs:259-313`) and then uses
  that value to arm the restart (`:319-323`), while the direct and coordinated write paths only
  accept entities (`JobsEFCorePersistenceProvider.cs:84-93, 712-720`) and the coordinated side-effect
  closure captures the pre-persistence value. Moving the anchor into persistence without changing
  that leaves the node-computed projection alive as a second source of truth, so the row and the
  restart disagree under skew. The write must therefore **return the seed result** - store anchor plus
  persisted `NextDueUtc` - and every restart side effect, direct and coordinated, must arm from the
  returned value. No caller may retain a locally computed projection.
- **R5 - Backend-keyed occurrence ids (#819).** `MaterializeCronScheduleOccurrenceAsync` creates
  occurrence rows with the backend-appropriate keyed sequential GUID generator, matching what the
  native claim strategies already use, so SQL Server `uniqueidentifier` clustered-key locality is
  preserved.
- **R6 - Node-local failures stay node-local AND stay contained (#830).** An `ArgumentException`
  from timezone resolution is treated as a per-host condition: logged and skipped, never written as
  durable fleet-visible defer state. Deterministic definition errors (undefined policy, non-positive
  grace, unparseable expression) keep deferring.
  **Containment is part of this requirement, not a follow-up.** Removing the durable defer means the
  dispatch query keeps selecting that definition on the affected host, where timezone resolution
  fails again with no per-candidate guard - so a single unresolvable definition would fail the whole
  poll and stall that node's scheduling of unrelated cron and time jobs every cycle. The fix must
  therefore also suppress the affected definition on the affected host only, keyed by definition id
  plus schedule revision or fingerprint, so the unhealthy node keeps scheduling everything else and
  a healthy peer still dispatches the excluded definition. Node-local suppression is in-memory and
  must not be persisted.
- **R6a - Suppression applies before the candidate cap.** The cron candidate read is bounded
  (`.Take(...)` on the dispatch projection; `JobsClaimStrategyDefaults.MaxClaimBatchSize` = 100 on
  the claim path). Filtering a fully-read page inside the manager therefore trades a stalled node for
  a starved definition: a page whose entries are all suppressed empties on every poll, and a healthy
  later definition never enters the window. Push the exclusion into the candidate query, or
  keyset-page / over-fetch until a non-suppressed group is found. Coverage must place more suppressed
  definitions than one page holds ahead of a healthy due definition and assert the healthy one still
  dispatches.
  **This requires an SPI change, not just a manager change.** The candidate contract accepts only
  `limit` (`IJobPersistenceProvider.cs:372-400`) and the provider applies `Take(limit)` before the
  manager ever sees a row (`BasePersistenceProvider.cs:1807-1814`; consumed at
  `InternalJobsManager.cs:301-307`), so a manager-owned suppression set cannot satisfy R6a. Extend the
  candidate contract with an explicit exclusion input keyed by definition id plus revision or
  fingerprint, applied inside **both** provider queries before `Take`, or define a keyset/over-fetch
  API with the same starvation guarantee. This widens the already-breaking provider SPI - fold it into
  the same break rather than shipping a second one.
- **R7 - Store-clock wake sleeps (#818 remainder).** Time-job wake sleeps derive their remaining
  duration from store time, as cron wake sleeps already do on this branch. The cron path carries
  `StoreUtcNow` on its projection at no extra round trip, so the time-job projection is expected to
  do the same. If implementation finds it genuinely needs an additional round trip, that is an
  explicit re-scoping decision for the maintainer - accept the cost, or drop R7, keep #818 open, and
  remove it from this plan's closure claim. It may **not** be resolved by documenting the gap while
  still counting #818 as closed.
- **R7a - One wake/restart clock domain.** Fixing only the remaining-duration calculation is not
  sufficient and can make things worse. `JobsSchedulerBackgroundService` records the planned wake as
  *node time plus that duration*, while `RestartIfNeeded` compares incoming absolute due times
  against that node-domain timestamp. Mixing a store-derived duration into a node-domain deadline
  means a skewed node mis-arbitrates restarts: with store time 12:00 and node time 11:00, a 12:30
  wake is recorded as 11:30, so a newly enqueued 12:05 job looks later and does not interrupt the
  sleep - running late or falling into misfire recovery. R7 and R4 must therefore settle on ONE
  domain for wake deadlines and restart comparison: either compare store-relative deadlines
  end-to-end, or use a monotonic local deadline derived once from the store anchor.
  **"Restart unconditionally after enqueue" is not a sufficient alternative** - `RestartIfNeeded`
  also runs after single and batch time-job updates and after committed timed-child release, so an
  enqueue-only restart still leaves a skewed node sleeping past a job whose due time was brought
  forward by an update or a release. Whichever domain is chosen must hold for **every**
  `RestartIfNeeded` caller; if the unconditional-restart route is taken, it must fire after every
  committed schedule mutation, not just enqueue.
  **Name the representation explicitly** - one clock domain, or a relative-duration/deadline type -
  and thread it through `GetNextJobs`, the execution context, `RestartIfNeeded`, and every caller,
  rather than leaving each call site to convert. The scheduler currently records its planned wake in
  node time (`JobsSchedulerBackgroundService.cs:300-302`) and compares restart requests against node
  time (`:330-355`), while cron durations already derive from `StoreUtcNow` and time-job durations do
  not (`InternalJobsManager.cs:112-138`) - that mixture is the defect. The full caller set is larger
  than enqueue: single and batch time-job updates and released children (`JobsManager.cs:354-365`,
  `:904-906`, `:1241-1254`; `InternalJobsManager.cs:1551-1561`) and coordinated cron writes
  (`JobsManager.CommitCoordination.cs:189-220`). Coverage must, with a deliberately skewed scheduler
  already sleeping, assert the sleep is interrupted by every one of those paths - direct enqueue,
  cron insert, single update, batch update, coordinated write, and released timed child.
- **R8 - Provider-agnostic recovery planner (#834).** The coalesce recovery decision - which instant
  to materialize, repurpose, or step past, and where the resolution window ends under saturation -
  lives in one storage-agnostic unit in `Headless.Jobs.Core` that both providers call. Providers
  retain only fenced read/write mechanics. The triplicated store-anchored occurrence-factory lambda
  is folded into the same extraction.
- **R9 - Honest concurrency conformance (#836).** The claim conformance test gives each worker its
  own candidate snapshot so it exercises real cross-node contention, and the entity-mutation side
  effect of `ClaimTimeJobsAsync` is documented on the SPI.
- **R10 - Cross-provider evidence.** Every unit that changes provider behaviour is verified by the
  Jobs unit suite AND both relational integration suites at the final head. Unit-only verification
  is not sufficient evidence for R3, R4, R5, R7, or R8.
- **R11 - Docs stay in lockstep.** Public API, behaviour, or configuration changes update the
  matching `docs/llms/jobs.md` and package `README.md` per `docs/authoring/AUTHORING.md`.
- **R12 - No regression of landed work.** The three follow-up fix commits already on the branch keep
  their behaviour and their tests.

### Acceptance Examples

- **AE1 (R2/R3).** With a terminal occurrence seeded at instant T and `NextCronOccurrence = null`,
  every provider produces the same durable outcome at T, and the conformance test fails if any
  provider is changed to disagree. Run once per terminal shape: a `Succeeded` row suppresses the
  fire on all four providers; a `DueDone`, `Failed`, or `Cancelled` row likewise suppresses; a
  migration-disposition `Skipped` row allows the replacement fire on all four; a recovery-`Skipped`
  row suppresses on all four.
- **AE2 (R3a).** A terminal and a live occurrence coexist at one `(CronJobId, ExecutionTime)`.
  Materialization reports the live row, not the older terminal one.
- **AE3 (R4).** Create a definition whose next tick falls before the scheduler's next poll, then
  crash the process before that poll. On restart the tick is recovered rather than skipped.
- **AE4 (R5).** Occurrence ids created through the EF materialization path under `UseSqlServerClaims`
  are sequential in SQL Server ordering, matching ids created by the native strategy.
- **AE5 (R6).** One node cannot resolve a timezone that its peers can. That node skips the
  definition and logs; the definition remains dispatchable on every healthy node and acquires no
  durable defer state.
- **AE6 (R7).** A node whose clock lags the store does not oversleep past a due time job.
- **AE7 (R8).** A behaviour change made in the planner takes effect for the in-memory and both
  relational providers with no per-provider edit, and one shared scenario set proves it.
- **AE8 (R9).** The claim conformance test passes repeatedly under full-suite concurrency, and still
  fails if the claim CAS is weakened.
- **AE9 (R10).** The final head has a recorded green run of the Jobs unit suite plus both relational
  integration suites.

### Scope Boundaries

In scope: the seven issues named above plus the two folded into #816 (ordering, probe cost).

Out of scope and staying open: #784 (overlap policy), #793 (whole-tree deletion), #313 (atomic
enqueue idempotency), #317 (dashboard authorization) - all four explicitly excluded by the PR #822
body and unchanged by this plan.

## Planning Contract

### Key Technical Decisions

- **KTD1 - Occupied-instant rule: an ACCOUNTING MATRIX, not a blanket any-row rule.** A blanket
  "any terminal row accounts for the instant" was the initial lean and is **wrong**: it would regress
  an existing conformance contract. `JobsClaimConformanceTests.cs:755-756` states that the terminal
  row "a cron-expression migration marks `Skipped` without creating a replacement - must not
  suppress the fire", and the migration path writes exactly that row with `SkippedReason = "Cron
  definition updated"` in all four providers. Under a blanket rule the replacement fire would be
  permanently dropped. The rule is therefore terminal-status-aware:

  The rule must be **total over every `JobStatus`** and fail closed on anything unrecognized:

  | Row at the instant | Accounts for it? | Rationale |
  |---|---|---|
  | `Idle` / `Queued` / `InProgress` | Yes - suppress | Live row already owns the instant |
  | `Succeeded` (3) | Yes - suppress | The instant executed |
  | `DueDone` (4) | Yes - suppress | Terminal success disposition |
  | `Failed` (5) | Yes - suppress | The instant ran; retry is the retry path's job, not re-materialization |
  | `Cancelled` (6) | Yes - suppress | Terminal and deliberate |
  | `Skipped` (7), migration-replacement disposition | **No - allow replacement** | Migration retired the row *without* creating a replacement; the replacement fire is owed |
  | `Skipped` (7), any other disposition | Yes - suppress | Recovery and ordinary skips retire the instant as accounted-for |
  | any unrecognized terminal value | Yes - suppress (**fail closed**) | A new status must not silently become a re-fire |

  **The migration exception must be carried by a typed persisted disposition, not by matching the
  free-form `SkippedReason` string.** `SkippedReason` is human-facing text (today literally "Cron
  definition updated"); keying cross-provider correctness on string equality is brittle and cannot
  be enforced by the compiler or the schema. Introduce an explicit disposition/enum column, or an
  equivalent typed marker, set by the migration path and read by the guard.

  This reconciles the two halves that currently disagree: the natives' live-only SQL satisfies the
  migration contract but wrongly allows a re-fire after a `Succeeded` row; the EF/in-memory
  unfiltered guard blocks the `Succeeded` re-fire but would wrongly block the migration replacement.
  Neither existing rule is correct on its own.

  **MEASURED 2026-08-15 (U7 phase 1) - description confirmed, mechanism corrected.** With the
  vacuous test repaired (it now starts the host, so the call reaches the occupancy branch; proven by
  mutation - the old test passes with the guard deleted, the repaired one fails), the observed grid
  is: **no provider discriminates by terminal status at all.** Both natives MATERIALIZE on all six
  statuses; EF and in-memory SUPPRESS on all six. The divergence is total, not partial.
  `ApplyCronRecoveryAsync` sides with EF/in-memory on all six, so today one row is suppressed via
  recovery and re-materialized via the native claim path. Three mechanism corrections follow, all
  decided on the lead's judgment while the maintainer was unavailable and flagged for review:

  - **KTD1a - the migration exception needs TWO disposition values, not one.**
    `"Cron definition updated"` is written by two producers whose correct answers are opposite: the
    startup seeding migration retires a row *without* a replacement (a re-fire is owed), while the
    runtime edit path (`JobsEFCorePersistenceProvider.cs:649`, mirrored in-memory at
    `JobsInMemoryPersistenceProvider.cs:2246`) writes the same string and then CREATES its own
    replacement at `:656-664` (suppressing is correct; allowing a re-fire would double-run every
    expression edit). One value mapped from that string encodes the wrong answer for one producer.
  - **KTD1b - `"Node is not alive!"` suppresses.** It is one of six Skipped producers, not the two
    KTD1 assumed, and it is the only ambiguous one: that occurrence never executed, its owner died.
    It still suppresses at materialization, because getting it re-run is the dead-owner reclaim and
    recovery path's job; re-materializing at claim time would race that path and risk a duplicate.
    Called out explicitly rather than left to the catch-all, since it is the same class of judgment
    as the migration case.
  - **KTD1c - the rule binds recovery, not just materialization.** One shared accounting predicate is
    called by both `MaterializeCronScheduleOccurrenceAsync` and `ApplyCronRecoveryAsync`, so the two
    paths cannot disagree about the same row. This is what makes U8's extraction load-bearing rather
    than cosmetic.
- **KTD2 - Planner returns a plan, providers apply it.** The extraction produces a pure decision
  value (which occurrence id to create or repurpose, which to retire, resulting watermark and
  projection), not a callback into storage. Mirrors the split already used for cron-expression
  evaluation and the resume/edit occurrence-factory callback, and keeps every fenced write inside
  the provider's own transaction or critical section.
- **KTD3 - All seven land in PR #822.** *(session-settled: user-directed - chosen over a follow-up
  PR because the issues touch the same files and the same conformance suites, and the maintainer
  prefers one consolidated change over incremental passes.)* Accepted cost: PR #822 grows well past
  its current 110 files, and reviewability depends on the per-unit commit boundaries below.
- **KTD4 - Merge first, planner last.** U1 merges main because nothing is verifiable without it; U8
  extracts the planner last because KTD1's rule determines what the planner must encode. The small
  independent fixes sit between so they land verified early rather than queueing behind the refactor.

## Implementation Units

### U1 - Merge main, re-baseline, and establish native occupied-instant truth
**STATUS: DONE (2026-08-15).** `origin/main` merged (`dca94fe82`), SSH.NET now 2026.0.0, and the full
baseline is green: Jobs unit 878/878, PostgreSQL integration 131/131, SQL Server integration 136/136.

**R2 finding - the guard test is vacuous.**
`queueing_an_instant_with_a_terminal_occurrence_materializes_nothing` builds a host but never calls
`StartAsync` (`JobsSchedulePositionConformanceTests.cs:665`), so no node membership exists.
`QueueCronJobOccurrencesAsync` delegates to `ClaimCronJobOccurrencesAsync`, which bails at
`PostgreSqlJobsClaimStrategy.cs:245` (`!ownerIdentity.TryGetStampOwner(...)` -> `yield break`) because
`TryGetStampOwner` requires `membership.Identity` (`JobsOwnerIdentityAdapter.cs:23-35`). The call
returns empty without reaching the occupancy branch, so `.Should().BeEmpty()` passes trivially and
would still pass with the guard deleted. The class rationale at `:31` ("the advance ... takes no
ownership and stamps no lease") is correct for the advance and materialize tests but does not hold
for the claim path, which does take ownership.

**Consequences, which change U7:**
1. The occupied-instant contract has **no working coverage on any provider**. It is unverified, not
   verified-and-passing, and an earlier report that the native divergence "does not reproduce" was
   drawn from this vacuous test and is withdrawn.
2. Fixing the test is a **prerequisite** to choosing the rule, not a deliverable of it: start the
   host (or otherwise establish a stamp owner), then observe each provider's real behaviour on a
   terminal-occupied instant before KTD1 is settled.
3. KTD1 therefore remains open on evidence. Do not implement U7 until the repaired test reports what
   each of the four providers actually does today.

Remaining U1 work: repair the test, run it against all four providers, and record the observed
behaviour matrix.

### U1 (original scope, for reference)
Merge `origin/main` (`0f7bf25d6`) into the branch, regenerate any lock-file drift per the repo's
CI-shaped lock rule, and record a green baseline: Jobs unit suite plus both relational integration
suites. Then resolve R2 - instrument or step through
`queueing_an_instant_with_a_terminal_occurrence_materializes_nothing` on native PostgreSQL to
determine why it passes. Report which of the four assumptions is false (native path not reached,
definition lock failing, seeded status not what it appears, or an unread guard). Output feeds KTD1.
Cites: R1, R2, R12.

### U2 - Backend-keyed occurrence ids (#819)
Resolve the keyed sequential GUID generator per backend in `CustomizerServiceDescriptor` so
`MaterializeCronScheduleOccurrenceAsync` matches the native strategies' generator choice. Verify id
ordering on SQL Server. Cites: R5, R10, AE4.

### U3 - Node-local timezone failures (#830)
Reclassify `ArgumentException` from `CronTimeZoneResolver.Resolve` in `RebaseStaleFingerprintsAsync`
as node-local, matching `_CurrentFingerprintsAsync`. **Then close the containment hole this opens**
(R6): with no durable defer, the dispatch path re-selects the same definition every poll and fails
the whole cycle on the affected host. Add in-memory per-host suppression keyed by definition id plus
schedule revision or fingerprint, so only the affected definition is skipped. Per R6a the exclusion
must be applied **before** the bounded candidate read, not to an already-read page, or a page full of
suppressed definitions starves healthy later ones. Test that the unhealthy node keeps scheduling
unrelated cron and time jobs, that a healthy peer still dispatches the excluded definition, and that
a healthy definition ordered behind more than one page of suppressed ones still dispatches.
Cites: R6, R6a, AE5.

### U4 - Honest claim conformance (#836)
Give each worker its own candidate snapshot in `JobsClaimConformanceTests`, and document on the SPI
that `ClaimTimeJobsAsync` mutates caller-owned entities so a candidate collection must not be shared
or reused. Run the suite repeatedly to confirm the flake is gone. Cites: R9, R11, AE8.

### U5 - Seed the schedule position at creation (#817)
Persist `ReconciledThroughUtc` and `NextDueUtc` as part of definition insertion, anchored on store
time read inside the inserting transaction. Per R4 the change lands at the **persistence boundary**,
not in `JobsManager`: extend both the direct insert path and the coordinated write path to obtain
the store anchor transactionally and derive the initial projection from it, then have
`JobsManager._AddCronJobAsync` and its bulk sibling stop shipping an unseeded entity. Per R4a the
anchor is the provider's current *statement* clock (`clock_timestamp()` / `SYSUTCDATETIME()`), never
EF-translated `DateTime.UtcNow`, which PostgreSQL freezes at transaction start. Cover the single,
bulk, and coordinated paths; assert the seeded anchor comes from the store under a deliberately
skewed node clock; and add a coordinated PostgreSQL case whose ambient transaction opened well
before the insert, asserting the seed is anchored at insertion rather than transaction start.
Per R4b the write must **return** the seed result and every restart side effect must arm from it, so
`JobsManager` stops carrying its node-computed `nextOccurrence` as a parallel source of truth.
Settle the wake/restart clock domain jointly with U6 per R7a rather than independently - U5 and U6
must land one representation between them, not two. Cites: R4, R4a, R4b, R7a, R10, AE3.

### U6 - Store-clock time-job wake sleeps (#818 remainder)
Carry `StoreUtcNow` through the time-job projection - as `GetEarliestCronJobGroupAsync` already does
for cron - and use it for the wake-sleep computation at `InternalJobsManager.cs:128` and `:138`.
Verify with a skewed-clock test that a lagging node does not oversleep a due time job. **Then settle
the domain mismatch R7a names**, jointly with U5: the planned-wake timestamp and `RestartIfNeeded`'s
comparison must live in one clock domain, or a store-derived duration folded into a node-domain
deadline will mis-arbitrate restarts on a skewed node. Test that inserting an earlier time job and
an earlier cron definition - including through the coordinated write path - interrupts a sleeping
skewed scheduler. If carrying store time genuinely needs an extra round trip, stop and raise the
re-scoping decision named in R7 rather than documenting around it. Cites: R7, R7a, AE6.

### U7 - Unify the occupied-instant rule (#816, folding #831 and #832)
Implement KTD1's confirmed accounting matrix across all four providers. **The typed disposition is
the sole accounting input; `SkippedReason` is display-only text and must never be read to decide
accounting** (`CronJobOccurrenceEntity.cs:56-60`, and the migration writer at
`BasePersistenceProvider.cs:1251-1267` currently sets only the free-form string). Every migration
writer in all four providers must set the typed migration-replacement value.

Express the rule as **one shared predicate** and call it from both the materialization read
(`BasePersistenceProvider.cs:2058-2090`, which today collapses every non-live status into "terminal")
and the recovery read (`ApplyCronRecoveryAsync`, `BasePersistenceProvider.cs:1616-1647`, which today
reads bare `Status` and treats any non-live row as occupying the missed instant). Without this, a
migration-replacement row correctly allows a re-fire during ordinary materialization while still
suppressing an owed *recovery* occurrence - the two paths would disagree about the same row. The
recovery read model must therefore carry the disposition, not just the status.

Unknown persisted status values must **fail closed as accounted-for**. Status is materialized as a
string-backed enum (`CronJobOccurrenceConfigurations.cs:30-35`), so a value written by a newer
binary must not throw or silently re-fire; handle it before enum conversion or with an equivalent
guard. Apply live-first ordering in materialization (R3a).
Batch or remove the occupancy probe per the resulting rule (R3b). Correct both native READMEs and
the SQL comments to describe the matrix. Conformance must drive the `NextCronOccurrence is null`
path against **each** terminal shape on every provider: `Succeeded` (suppress), migration-`Skipped`
(allow the replacement fire - this is the existing contract at
`JobsClaimConformanceTests.cs:755-756` and must not regress), and recovery-`Skipped` (suppress).
Coverage must be **table-driven over every known `JobStatus`** - each live status, `Succeeded`,
`DueDone`, `Failed`, `Cancelled`, both `Skipped` dispositions - plus a live/terminal coexistence case
and a raw unknown status value written directly to the database. Run the complete table against
**both** the materialization and recovery paths, and re-run it after U8's extraction so the planner
cannot reintroduce a divergence.

**This unit now carries a schema change.** KTD1 requires a typed persisted disposition to mark the
migration-replacement case, so U7 adds a column to `CronJobOccurrences` plus migrations for both
demo contexts and both relational test contexts, following the same Up/Down and state-loss-guard
shape as `AddCronScheduleWatermark`. Existing rows default to the ordinary disposition, which
preserves today's behaviour for every non-migration skip. Sequence the migration ahead of the guard
change so the column exists before anything reads it.
The `Down` guard must be **disposition-aware**: the existing watermark guard
(`20260803224049_AddCronScheduleWatermark.cs:111-132`) predicates only on watermark and recovery
fields, so reusing its shape verbatim would silently drop migration-replacement provenance on
downgrade and turn owed replacement fires into permanently suppressed ones. Refuse the downgrade when
any non-ordinary disposition exists, in all four migration contexts, with tests proving both the
refusal and a clean downgrade when every value is ordinary. Cites: R3, R3a, R3b, R11, R12, AE1, AE2.

### U8 - Extract the recovery planner (#834, folding review finding #17)
Extract the coalesce decision into a storage-agnostic planner per KTD2; both providers call it and
retain only fenced mechanics. Fold the triplicated store-anchored occurrence-factory lambda into one
builder. Drive both providers from one shared scenario set. Cites: R8, R10, AE7.

### U9 - Final evidence, docs, and issue closure
Run the full gate at the final head: unit suite, both relational integration suites, `make
format-check`, `make quality-analyzers`, Release build. Sync `docs/llms/jobs.md` and the affected
package READMEs. Update the PR #822 body to list the newly closed issues, and update #818 to record
that its cron half landed in #822. Cites: R10, R11, R12, AE9.

## Risks

- **PR size.** This roughly doubles an already large PR. Mitigation: one commit per unit, each
  independently verified, so the diff can be reviewed unit by unit rather than as one blob.
- **KTD1 unconfirmed.** U7 and U8 both encode the rule. If it flips after U8, rework is real.
  Mitigation: U1 establishes the fact and KTD1 is confirmed before U7 starts.
- **U8 is the largest single change** and touches the most correctness-sensitive code in the
  subsystem. Mitigation: it runs last, on a branch whose integration suites are green and whose
  smaller fixes are already landed and verified.
- **U7 grew a second migration.** KTD1's typed disposition means PR #822 now ships two schema
  changes rather than one. That compounds the rollout story the PR body already documents, and the
  new column needs its own state-loss downgrade guard. Consider whether the disposition can instead
  be derived from data already persisted before committing to the column.
- **Scope has grown well past "seven small fixes."** Four rounds of review turned this into: a
  breaking provider-SPI widening (R6a exclusion input), a second schema migration with its own
  disposition-aware downgrade guard (U7), a new seed-result contract on both write paths (R4b), and a
  single clock-domain representation threaded through every restart caller (R7a). Each is justified
  by a grounded defect, but together they roughly double the plan's original weight and land on top
  of an already-110-file PR. **Re-confirm KTD3 before starting U5** - splitting U5-U8 into a second
  PR is now a materially more defensible option than it was when KTD3 was chosen.
- **Review budget is spent, and the last round's fixes are unreviewed.** Four rounds ran (one review,
  three adversarial; the fourth was an explicit user override of the three-round cap). They found 13
  P1 defects in total, including one - a blanket any-row rule that would have regressed the
  migration-replacement contract - that would have caused silent data loss. The final round's fixes
  (disposition as sole accounting input, shared predicate across materialization and recovery,
  fail-closed unknown status, SPI exclusion input, seed-result contract, disposition-aware downgrade
  guard) have **not** been reviewed. Treat U1 as where they get scrutinised, and expect KTD1 to move
  again once U1 reports what each provider actually does today.
