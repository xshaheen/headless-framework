---
title: Jobs Typed-Chain Residual Findings - Remediation Plan
type: fix
date: 2026-07-24
topic: jobs-chain-residual-findings
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: docs/residual-review-findings/xshaheen-jobs-typed-chain.md
execution: code
---

# Jobs Typed-Chain Residual Findings - Remediation Plan

## Goal Capsule

- **Objective:** Close every open item in `docs/residual-review-findings/xshaheen-jobs-typed-chain.md` on branch `xshaheen/jobs-typed-chain` before PR #766 merges — the poll-path perf finding (#765, both halves) and all seven recorded testing gaps.
- **Product authority:** The residual record above plus issue #765, corrected by the severity re-assessment in KTD1 and by two design defects this plan's own review pass found in its first draft (KTD3, KTD4).
- **Stop conditions:** Surface as a blocker (do not improvise) if (a) U4's fence test exposes a real split-ownership defect rather than confirming the fence — that is a design conversation, not a test fix; or (b) the new index measurably regresses claim/enqueue throughput in the PostgreSql conformance suite.
- **Execution profile:** Single branch (`xshaheen/jobs-typed-chain`, PR #766) carrying the perf fix, the schema change, and all coverage. Integration suites run locally with Docker; CI gates unit tests only.
- **Tail ownership:** The invoking session owns verification and the PR-body/issue updates (U9).

---

## Product Contract

### Summary

The typed-chain PR ships a poll-time correctness backstop that runs an unbounded relational scan on a 1ms-paced loop, and seven behaviors whose correctness rests on inspection alone. This slice makes the backstop cheap and provably progressing, indexes the query it depends on, and converts each recorded coverage gap into an executing test — with the two highest-value gaps (descendant-lease fence, gate-predicate parity) designed so they actually reach the code they claim to cover.

### Problem Frame

`SkipStrandedTimedChildrenAsync` exists so a missed terminalization path degrades to eventual consistency instead of permanently stranding a timed chain descendant. It is invoked at the top of every `GetNextJobs` with no interval gate, and the scheduler loop sleeps 1ms whenever work is due — so the backstop's candidate scan, which no index serves, runs at up to ~1kHz per node in every deployment, including ones that never enqueue a chain.

Separately, PR #766 replaced the planned single-transaction tree claim with fenced autocommit statements (the KTD2 deviation). That trade is well-reasoned, but it moved correctness onto a fence (`EXISTS(root still owned by me, lease unexpired)`) and onto `PruneToClaimedSet` — neither of which any test exercises. The timed-descendant gate is likewise implemented three times by hand, in three languages, with a comment asserting they must stay in lockstep and nothing enforcing it.

### Key Decisions

- **Portable composite index over provider-filtered partial indexes.** One `HasIndex("Status", "ParentId")` in the shared `TimeJobConfigurations`. (default-taken: put to the user, no response in window — chosen over per-provider `HasFilter` SQL: this feature already maintains three hand-mirrored predicates; a fourth triplicated config path buys disk savings at the cost of the exact drift hazard U5 exists to close.)
- **Findings and coverage only.** The three analyzer suggestions and the pre-existing SqlServer cron flake stay out. (default-taken: put to the user, no response in window — chosen over folding them in: both sit outside the chain feature and dilute the diff.)
- **All work on PR #766.** (default-taken: put to the user, no response in window — chosen over a stacked PR: the perf regression was introduced by this PR and should not reach `main` unaccompanied; no human reviews are pending to disturb.)
- **The skip-only sweep is bounded on the rows it mutates, not on all terminal candidates.** (review-corrected — see KTD3.)
- **The fence test drives lease loss directly; a two-owner root race does not reach the fence.** (review-corrected — see KTD4.)

### Requirements

- **R1** — The relational safety net runs at most once per `SchedulerOptionsBuilder.FallbackIntervalChecker`, and still runs on the first poll after startup. Skip-only semantics are unchanged.
- **R2** — The skip-only sweep's **selection** is bounded per invocation and **guarantees forward progress**: every bounded page consists solely of rows the sweep mutates, so a large stranded set drains monotonically across sweeps. The subsequent subtree cascade is deliberately **not** capped — see KTD6 for why capping it would strand rows, and what is bounded instead.
- **R3** — The candidate probe is served by an index on both PostgreSql and SqlServer (seek, not relation scan), verified from an actual query plan **on each provider**.
- **R4** — The descendant-lease fence is exercised under **deterministic** lease loss, in both variants: root lease lapsed, and root stolen by another owner mid-walk. Neither leases descendants; the hydrated tree is pruned to the claimed prefix; no split ownership is observable.
- **R5** — Every implementation of the parent-terminal gate predicate — LINQ, native SQL, in-memory, and the new mismatch predicate from R2 — agrees on one shared case matrix.
- **R6** — `JobsExecutionContext.CacheFunctionReferences` attaches delegates across a deep branching tree.
- **R7** — The durable-cancellation and `TerminateExecutionException` catch blocks' fenced writes are covered.
- **R8** — The SqlServer `MAXRECURSION` boundary is locked in against an over-depth persisted chain.
- **R9** — A timed descendant of a skipped non-timed sibling reaches `Skipped` end-to-end via the safety net.
- **R10** — Lowering `MaxChainDepth` below a persisted chain's depth truncates identically across in-memory, generic-EF, PostgreSql, and SqlServer.
- **R11** — The residual record, PR #766 body, and issue #765 reflect the true final state.

### Key Flows

1. **Bounded sweep (R2).** Scheduler tick → cadence gate (R1) → if due, one query selecting *only* mismatched terminal children, ordered and capped → skip those rows + cascade their subtrees to completion → rows leave the candidate set → next sweep sees a strictly smaller set. The capped part is the recurring scan; the cascade is one-time work proportional to a subtree actually being retired.
2. **Fence under lease loss (R4).** Owner claims root → test seam fires between the root write and the first frontier lease and invalidates the root lease (expire it, or reassign `OwnerId` to a second owner) → descendant UPDATE's `EXISTS(root owned by me AND LockedUntil > now)` fails → `leased == 0` → walk breaks → `PruneToClaimedSet` yields root-only → nothing below the root executes.
3. **Gate parity (R5).** One case set declared once → replayed against four implementations → any drift fails a test rather than a production claim.

### Acceptance Examples

- **A1 (R1).** Given a due backlog and a fake `TimeProvider`, when `GetNextJobs` is polled twice in immediate succession, then the safety net is invoked once; when the clock advances past `FallbackIntervalChecker` and it is polled again, then it is invoked a second time.
- **A2 (R2).** Given `2 × cap` stranded mismatched children plus a full page's worth of *matching* terminal children, when the sweep runs repeatedly, then every mismatched child reaches `Skipped` — the matching children never occupy the page and never starve the sweep.
- **A2b (R2/KTD6).** Given one mismatched child rooting a wide subtree, when a single sweep runs, then the **entire** subtree reaches `Skipped` in that sweep — no Idle descendant is left behind for a later sweep that would never re-select it.
- **A3 (R4).** Given a 3-node non-timed chain and a seam that invalidates the root lease after the root write, when the tree claim runs, then only the root is owned, no descendant carries an owner, and the yielded tree contains the root alone. Two cases, both state-driven and independent of statement latency: (i) seam expires the root lease; (ii) seam reassigns the root to a second owner.
- **A4 (R5).** Given the shared matrix of (7 `RunCondition` values incl. `null`/`InProgress` × 6 parent statuses incl. a non-terminal control), when replayed against all four implementations, then every implementation returns the same claimable/mismatched verdict for every case.
- **A5 (R3).** Given a seeded backlog, when the probe query's actual plan is captured on **PostgreSql** (`EXPLAIN (ANALYZE)`) and on **SqlServer** (actual execution plan / `SET STATISTICS XML`), then each shows an index seek/scan on `IX_TimeJob_Status_ParentId` rather than a sequential scan or clustered-index scan of `TimeJobs`.

### Scope Boundaries

**In:** `SkipStrandedTimedChildrenAsync` cadence + bound, the mismatch predicate, `IX_TimeJob_Status_ParentId` + demo migrations, an `InternalsVisibleTo` grant for the EF harness, the seven recorded coverage gaps, the paper trail.

**Out:** `MA0045`/`RCS1239`/`MA0003` analyzer suggestions (deliberate, info-level); the pre-existing SqlServer cron-graph flake (untouched path); the KTD2 claim-shape deviation itself (accepted as designed); everything PR #766 defers by design (fan-out/fan-in, DAG joins, compensation, whole-chain cancellation, per-node independent pickup, Dashboard visualization).

### Dependencies / Assumptions

- Docker available locally for both integration suites; CI will not run them.
- `SchedulerOptionsBuilder` is DI-registered and reachable from `InternalJobsManager`'s constructor (it is already injected into `EfCoreCasJobsClaimStrategy`).
- Consumers own their own EF migrations; the index is a consumer-visible deployment change and must be documented as such.

---

## Planning Contract

### Key Technical Decisions

- **KTD1 — #765 is under-prioritized; raise to `priority:p2`.** Verified: `InternalJobsManager.cs:39` invokes the sweep unconditionally per `GetNextJobs`, and `JobsSchedulerBackgroundService.cs:177` sets `sleepDuration` to 1ms whenever `timeRemaining <= 0`. The pre-transaction probe already applied on-branch removed the transaction, not the scan. Correctness is unaffected (skip-only can never make a child eligible early), so this is throughput — but a hot-path regression shipped by this PR is not "optional perf".
- **KTD2 — Cadence gate via an explicit constructor dependency.** `InternalJobsManager` gains a `SchedulerOptionsBuilder` parameter rather than resolving it from the already-injected `IServiceProvider`; service location would hide the dependency. The class is `internal sealed`, so there is no public API impact. Cost: 9 mechanical construction-site updates in `Tests.Unit/Managers/`.
- **KTD3 — Bound the sweep on the mismatched set, not on all terminal candidates (review correction).** The first draft proposed `OrderBy` + `Take` over `terminalChildIds`. That starves: in `skipOnly` mode the method returns at `BasePersistenceProvider.cs:656` **before** the release branch, so matching terminal children are never mutated, remain candidates forever, and would permanently occupy a deterministic first page — mismatched rows below it would never drain. The bound must therefore be applied to a query that selects *only* rows the sweep mutates (parent terminal **and** run-condition mismatched). Every page then makes monotone progress because skipping removes the row from the candidate set.
- **KTD4 — Drive lease loss through an explicit state seam; a two-owner root race cannot reach the fence (review correction).** In `_ClaimTimeJobTreeAsync` a losing claimant gets `rootAffected <= 0` and returns at line 595 — before the descendant loop. So two owners racing one root exercises the root CAS only. Reaching `EXISTS(root owned by me, lease unexpired)` requires the *winner* to lose its lease between the root write and the descendant write. A short `LeaseDuration` was considered and **rejected**: it races statement latency against a wall-clock deadline, so on a fast or loaded runner the walk can land on either side of it — and with no CI signal on these suites, that nondeterminism would surface as an unexplained flake or, worse, a silently vacuous pass. Instead add an internal test-only seam (`internal Func<Task>? OnFrontierBeforeLease` on `EfCoreCasJobsClaimStrategy`, `null` in production) invoked between the root write and the first frontier lease; the test callback expires or reassigns the root row. This is state-driven, deterministic, and — unlike the timing approach — also makes the *stolen-root* interleaving testable, which was otherwise going to be recorded as knowingly untested. `internal`-as-test-seam is the codebase's existing idiom (see the comment in `JobsInitializationHostedService.cs`). The two-owner race stays as a separate test, scoped honestly to what it covers (root CAS + `PruneToClaimedSet`).
- **KTD6 — Cap the selection, not the cascade (review correction).** `Take` on the mismatch set bounds the direct children, not the total write volume: `_CascadeSkipSubtreeAsync` then walks each selected child's whole subtree, and chains branch — only depth is capped, breadth is not. Capping the cascade was considered and **rejected as unsafe**: the cascade skips *any* Idle descendant (`BasePersistenceProvider.cs:467-469`, no `ExecutionTime` filter) while the sweep's candidate predicate requires `ExecutionTime != null` (`:578`), so a half-finished cascade would leave non-timed descendants Idle under a Skipped ancestor with **no path that ever re-selects them** — converting a bounded-work concern into the exact stranding this backstop exists to prevent. What is bounded is therefore the recurring **scan**, which is what #765 actually identified; the cascade is one-time, self-terminating (each row leaves `Idle`), proportional to a subtree genuinely being retired, and now gated behind the R1 cadence so it cannot recur per-tick. R2 is worded to claim exactly this and no more.
- **KTD5 — Grant the EF harness `InternalsVisibleTo` rather than duplicating the test per leaf.** `EfCoreCasJobsClaimStrategy` and `IJobsOwnerIdentity` are internal; `Headless.Jobs.EntityFramework` grants `Composition.Tests.Unit`, `PostgreSql.Tests.Integration`, and `SqlServer.Tests.Integration`, but **not** `Headless.Jobs.EntityFramework.Tests.Harness` — so direct construction in the harness will not compile today. `Headless.Jobs.Abstractions` already grants the harness; adding the matching line to `Headless.Jobs.EntityFramework.csproj` keeps the conformance test single-sourced.

### High-Level Technical Design

**Cadence gate (U1).** A `long` tick stamp on `InternalJobsManager`, read/written via `Volatile`/`Interlocked`, initialized so the first poll runs. The existing non-fatal try/catch and `OperationCanceledException` rethrow are preserved verbatim.

**Mismatch predicate + bound (U2).** A new `WhereParentTerminalRunConditionMismatched` in `HeadlessJobsQueryExtensions`, composed as "parent reached a terminal state" **and not** "parent satisfies this child's condition" — the negation of the existing gate's `EXISTS` arm, valid to reduce this way because `_TimedChildReconcileCandidates` has already excluded the ungated escape arms (`ParentId == null`, `ExecutionTime == null`, non-gated `RunCondition`). `SkipStrandedTimedChildrenAsync` selects through it with a deterministic `OrderBy` + `Take(JobsClaimStrategyDefaults.MaxClaimBatchSize)`. The `parentId` (per-parent) path stays **unbounded** — it must reconcile a terminalizing parent's children completely, or a parent's subtree is left half-reconciled. This is a fourth mirror of the gate semantics and is therefore folded into U5's matrix by construction.

**Index (U3).** `HasIndex("Status", "ParentId")` in the shared configuration, commented in the style of the existing sweep/reclaim indexes. Fixtures use `EnsureCreated` and pick it up automatically; the two demo projects need generated migrations.

**Fence test (U4).** Constructed against `EfCoreCasJobsClaimStrategy` directly in the harness (enabled by KTD5), driven by the KTD4 seam. No wall-clock dependency.

### Assumptions

- `Take` + `OrderBy` on the mismatch query translates on both providers without a correlated-subquery rewrite that defeats the index. Verified by the U3 plan check.
- The seam's callback can mutate the root row through a separate `DbContext` while the claim is mid-walk — true, because the walk is autocommit (the KTD2 deviation), so there is no open transaction to block on.

### Risks

- **Index write cost.** A fifth index on `TimeJobs` is maintained on every insert/update of a hot table. Mitigation: the U3 throughput check on the PostgreSql conformance suite; stop condition (b) if it regresses.
- **A production-code seam for a test.** `OnFrontierBeforeLease` is dead weight in shipped code. Accepted: it is `internal`, null-checked once per frontier iteration, matches an existing codebase idiom, and buys deterministic coverage of the single riskiest invariant this PR introduces. It must carry a comment saying so, or a later reader will delete it.
- **9 constructor sites.** Mechanical, but a missed site fails the build, not a test — run the unit suite immediately after U1.

---

## Implementation Units

### U1. Cadence-gate the safety net
- **Files:** `src/Headless.Jobs.Core/Managers/InternalJobsManager.cs`; 9 sites in `tests/Headless.Jobs.Composition.Tests.Unit/Managers/` (`InternalJobsManagerTests.cs` ×6, `TimedDescendantReconcileManagerTests.cs` ×2, `InternalJobsManagerContextDepthTests.cs` ×1).
- **Delivers:** R1. **Test:** A1 in `InternalJobsManagerTests`.
- **Note:** no existing test asserts per-tick invocation (verified), so the gate is behaviour-additive.

### U2. Mismatch predicate and bounded sweep
- **Files:** `src/Headless.Jobs.EntityFramework/Infrastructure/HeadlessJobsQueryExtensions.cs`, `.../BasePersistenceProvider.cs` (`SkipStrandedTimedChildrenAsync`, `_ReconcileParentTerminalTimedChildrenAsync`), XML doc on `IJobPersistenceProvider.SkipStrandedTimedChildrenAsync`.
- **Delivers:** R2. **Test:** A2 and A2b, in an **EF** fixture — `TimedDescendantGatingProviderTests` constructs only `JobsInMemoryPersistenceProvider` and cannot verify relational ordering or the cap.
- **Doc:** state that the backstop is now eventually consistent across ticks.

### U3. Covering index and migrations
- **Files:** `src/Headless.Jobs.EntityFramework/Configurations/TimeJobConfigurations.cs`; new migrations in `demo/Headless.Jobs.Api.Demo/Migrations/` and `demo/Headless.Jobs.Console.Demo/Migrations/`; `docs/llms/jobs.md`; `src/Headless.Jobs.EntityFramework/README.md`.
- **Delivers:** R3. **Test:** A5 plan check on **both** PostgreSql and SqlServer + the throughput comparison from risk 1. R3 is not complete on a PostgreSql plan alone.
- **Also:** tick PR #766's configuration/deployment compatibility box (currently unchecked) — consumers must generate a migration.

### U4. Descendant-lease fence under lease loss
- **Files:** `src/Headless.Jobs.EntityFramework/Infrastructure/JobsClaimStrategy.cs` (KTD4 seam, `internal`, commented as a deliberate test seam); `src/Headless.Jobs.EntityFramework/Headless.Jobs.EntityFramework.csproj` (KTD5 grant); `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsChainConformanceTests.cs`.
- **Delivers:** R4. Two distinct coverage sets — the recorded gap names *both* "native CTE + CAS frontier", and the seam reaches only the latter:
  - **U4a — CAS frontier fence (generic EF).** Directly constructed `EfCoreCasJobsClaimStrategy`, driven by the KTD4 seam: A3 cases (i) and (ii), plus a separately-named two-owner root race scoped to root CAS + `PruneToClaimedSet`. This is the only strategy that has a frontier fence; the seam is meaningless elsewhere.
  - **U4b — Native-CTE contention (PostgreSql, SqlServer).** Through the fixtures' normal strategy selection, so `PostgreSqlJobsClaimStrategy` / `SqlServerJobsClaimStrategy` actually run: two owners race the same ≥2-node chain root; assert exactly one owner wins root **and** descendants, the loser claims nothing, no node is split across owners, and every claimed descendant carries the root's exact persisted `LockedUntil`. No seam — the native claim is a single recursive-CTE statement, so contention is the whole test.
- **Runs on:** U4a — generic EF CAS strategy only. U4b — both native strategies, one per conformance class.

### U5. Four-way gate parity matrix
- **Files:** shared case set in `tests/Headless.Jobs.EntityFramework.Tests.Harness/`; replays in `Headless.Jobs.Composition.Tests.Unit` (in-memory + LINQ) and both conformance classes (native SQL).
- **Delivers:** R5. **Test:** A4. Must include U2's new mismatch predicate as the fourth implementation.

### U6. Remaining recorded gaps
- **R6** `CacheFunctionReferences` deep branching tree → `Tests.Unit`.
- **R7** fenced writes in the durable-cancellation and `TerminateExecutionException` catch blocks → `JobExecutionTaskHandlerTests`.
- **R8** SqlServer `MAXRECURSION` boundary → SqlServer conformance. *Verified safe by inspection*: the CTE self-limits (`descendants.depth < @maxDepth`; anchor gated `@maxDepth >= 2`), so at most `maxDepth - 1` recursion levels run against `OPTION (MAXRECURSION maxChainDepth)`; error 530 is unreachable, and `MaxChainDepth >= 1` is enforced at setup so `MAXRECURSION 0` (unbounded) cannot be emitted. The test locks the boundary in.
- **R9** timed descendant of a skipped sibling → harness conformance. *Verified correct by inspection*: `WhereParentIsTerminal` includes `JobStatus.Skipped` and `ParentTerminalMatches` returns false for it, so the subtree is skipped, not stranded.
- **R10** lowering `MaxChainDepth` below persisted depth → harness conformance + in-memory unit.

### U7. Paper trail
- **Files:** `docs/residual-review-findings/xshaheen-jobs-typed-chain.md`, PR #766 body, issue #765.
- **Delivers:** R11. Move closed items out; keep the KTD2-deviation section; annotate the analyzer/flake sections as deliberately out of scope; record the KTD4 seam as a deliberate production-code test affordance so a later reader does not delete it. Raise #765 to `priority:p2` with the KTD1 evidence, then close it.

**Sequencing:** U1 → U2 → U3 (src first; run the unit suite after U1's 9 sites) → U4 (highest defect-discovery odds; honour stop condition (a)) → U5 → U6 (parallelizable) → U7 last, once the true final state is known. One commit per unit, scoped to touched paths, Conventional Commits.

---

## Verification Contract

- Per unit: `make build-project PROJECT=…` at `-c Release` on every changed src project. MTP can compile what `dotnet build` rejects (repo learning, AsyncFixer04) — a green test run is not a clean build.
- `make test-project TEST_PROJECT=tests/Headless.Jobs.Composition.Tests.Unit`.
- **Both** integration suites locally with Docker: `Headless.Jobs.EntityFramework.PostgreSql.Tests.Integration` and `…SqlServer.Tests.Integration`. CI runs unit tests only, so U4/U5/U6's provider coverage has **no** CI signal — local runs are the only evidence.
- Actual query plans confirming A5 on **both** providers — `EXPLAIN (ANALYZE)` on the PostgreSql fixture and the actual execution plan on the SqlServer fixture. Capture both in the PR body.
- `make format-check` and `make quality-analyzers-project` on Core + EntityFramework before review.

## Definition of Done

1. R1-R11 each have a passing test or a recorded plan/throughput artifact.
2. Both integration suites green locally on PostgreSql and SqlServer; unit suite green.
3. Changed src projects build clean at `-c Release` with warnings-as-errors.
4. #765 raised to p2, then closed; residual doc reflects the true final state including anything knowingly left untested.
5. PR #766 body updated: validation block, configuration/deployment compatibility box ticked for the index.

## Sources / Research

- `docs/residual-review-findings/xshaheen-jobs-typed-chain.md` — the finding set this plan closes.
- Issue #765; PR #766.
- `docs/solutions/design-patterns/atomic-database-clock-relational-lease-claims.md` — the invariant behind the KTD2 deviation.
- Verified in source: `InternalJobsManager.cs:39`, `JobsSchedulerBackgroundService.cs:177`, `BasePersistenceProvider.cs:532-690`, `JobsClaimStrategy.cs:554-692`, `SqlServerJobsClaimStrategy.cs:744-780`, `HeadlessJobsQueryExtensions.cs:143-209`, `TimeJobConfigurations.cs:29-47`.
- Review pass: `codex-companion review` on this plan's first draft — sourced KTD3, KTD4, KTD5 and the U2 test relocation.
