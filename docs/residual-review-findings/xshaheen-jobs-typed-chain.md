# Residual Review Findings — xshaheen/jobs-typed-chain

Source: x-code-review run `20260722-010940-f5a63808` (typed job chains, issue #311), reviewed at `4f4061a72` plus the simplification pass; fixes applied through `45f61d70a`. Remediation pass (2026-07-24/25) closed the filed finding and every recorded testing gap, and surfaced two defects the original review missed — see **Remediation**.

## Remediation (2026-07-24/25)

Plan: `docs/plans/2026-07-24-001-fix-jobs-chain-residual-findings-plan.md`. All work landed on this branch. Verification: unit 564/564; PostgreSql integration full suite green; SqlServer chain conformance 18/18. Every new test was proven red→green (fence disabled, bound removed, or fix reverted) before being kept.

### Blocker found and fixed (not in the original review)

- **Immediate-dispatch path leased only the chain root, stranding every descendant.** `IJobScheduler.EnqueueAsync(JobChain)` with no execution time — the documented default — dispatches through `AcquireImmediateTimeJobsAsync`, which leased the root but only *hydrated* its non-timed descendants. The executor runs a claimed chain by in-process recursion and fences every node on lease renewal, so each hydrated-but-unleased child renewed 0 rows, was marked `LeaseLost`, and stayed `Idle` forever: typed chains ran their root and nothing else. Every existing chain conformance scenario used a *timed* root (the scheduled tree-claim path), so the immediate path had zero chain coverage and the defect was invisible. Fixed by extracting the frontier lease-walk into a shared `JobsSubtreeLeaseWalk` used by both relational claim paths; the in-memory provider reuses its `_ClaimIdleDescendants`. Coverage added in-memory and across both relational providers. Proven red on PostgreSql (`OwnerId` null) and in-memory.
- **Coverage-priority lesson:** the original review ranked concurrent claim contention as the top gap; the real defect was an *entire entry path* (immediate dispatch) with no chain coverage. Weight "is every entry path exercised?" above "is this path exercised concurrently?".

### Filed finding — CLOSED

- **#765 (poll-path safety-net scan)** — closed by two changes, not the originally-proposed index:
  - **Cadence gate (U1):** `SkipStrandedTimedChildrenAsync` ran at the top of every `GetNextJobs`, and the scheduler loop sleeps 1ms whenever work is due, so its unbounded candidate scan executed at up to ~1kHz per node in every deployment. Now gated to `FallbackIntervalChecker` (first poll after startup still runs it). This raised the real severity above the filed p3.
  - **Bounded sweep (U2):** the skip-only sweep now selects only the rows it mutates (a new `WhereParentTerminalRunConditionMismatched` predicate) with a deterministic `OrderBy` + `Take`, so a large stranded backlog drains monotonically and a page of matching children can never starve it. The subtree cascade stays uncapped by design (capping it would strand non-timed descendants — see the code comment / KTD6).
  - **Index — deliberately NOT shipped (U3).** The plan's portable `(Status, ParentId)` index was reverted after `EXPLAIN (ANALYZE)` proved the planner never selects it for the sweep: it seeks `Status = Idle` via an existing `IX_TimeJob_Status_*` index, and `ParentId IS NOT NULL` is not a seekable equality nor the sort key. With U1+U2 the sweep runs ~once per 30s and is a non-issue for realistic backlogs, so a portable index would be pure write-amplification. **Escalation if ever needed:** a provider-specific *partial* index (e.g. PostgreSQL `... (ExecutionTime) WHERE Status = 'Idle' AND ParentId IS NOT NULL AND RunCondition IN (...)`) would serve the ordered bounded select directly; it was declined here only because it reopens the per-provider-config path the portability decision avoided. No schema change, no demo migrations.

### Review-recorded testing gaps — CLOSED

- Direct unit test for `JobsExecutionContext.CacheFunctionReferences` on a deep branching tree (R6).
- Fenced-write tests for the durable-cancellation and `TerminateExecutionException` catch blocks (R7, both branches).
- Concurrent two-node chain-claim contention conformance — native CTE (U4b) and CAS frontier (U4a), the CAS fence driven by a deterministic `internal` test seam under real lease loss (both root-expired and root-stolen).
- Differential four-gate matrix (RunCondition × parent status × non-gated control) across in-memory logic / LINQ / native SQL, anchored on `ChainRunConditionRules` (U5).
- SqlServer `MAXRECURSION` boundary with an over-depth persisted chain (R8) — proven safe (the CTE self-limits at `MaxChainDepth`) and locked in.
- Timed descendant of a skipped parent reaching Skipped end-to-end via the safety net (R9).
- Lowering `MaxChainDepth` below a persisted chain's depth (documented truncation) across providers (R10).

## Settled-decision deviation (proceeded and flagged) — unchanged

- Plan KTD2 specified the generic-EF frontier claim run "inside one transaction". Integration conformance proved that conflicts with the repo's DB-clock lease invariant (PostgreSQL `now()` freezes at transaction open, shortening leases; SqlServer per-statement clocks diverge descendant leases). Replaced with fenced autocommit statements: descendants copy the root's persisted lease deadline via a DB-evaluated subquery, every frontier UPDATE re-asserts eligibility and root ownership, and crash-mid-claim recovery is owned by `PruneToClaimedSet` plus the stalled-lease sweep. The remediation added the U4a fence coverage this shape rests on.

## Deliberate production-code test affordance

- `EfCoreCasJobsClaimStrategy.OnFrontierBeforeLease` (and the `onBeforeFirstLease` parameter on `JobsSubtreeLeaseWalk`) is an `internal`, null-in-production seam fired once between the root claim and the first descendant lease. It exists solely so the U4a fence tests can invalidate root ownership as a deterministic state change rather than racing statement latency against a wall-clock lease deadline. Do not delete it as dead code — it is the only deterministic way to reach the descendant fence.

## Analyzer suggestions intentionally skipped (info-level, out of scope)

- `MA0045` `JobsHelper.cs` sync compression method (pre-existing sync design; async ripple).
- `RCS1239` while->for on the two frontier loops (deliberate commented loop shape).
- `MA0003` on pre-existing lines (`BasePersistenceProvider.cs:331`, `TimeJobConfigurations.cs:21`).

## Known flake (pre-existing) — out of scope

- SqlServer `cron_graph_projection_uses_distinct_dates_and_storage_side_status_aggregation` can flake under full-suite parallel load (SQL-capture window interference from the new background poll; passes in isolation; cron path unchanged by this branch). Not addressed here — it predates this branch and lies on an untouched path.
