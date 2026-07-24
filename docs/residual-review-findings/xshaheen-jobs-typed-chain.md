# Residual Review Findings — xshaheen/jobs-typed-chain

Source: x-code-review run `20260722-010940-f5a63808` (typed job chains, issue #311), reviewed at `4f4061a72` plus the simplification pass; fixes applied through `45f61d70a`.

## Filed

- P1 (filed at p3) — `src/Headless.Jobs.EntityFramework/Infrastructure/BasePersistenceProvider.cs:532` — Bound the timed-descendant safety-net scan on the relational poll path (cadence/bound/filtered index; the no-candidates-no-transaction half was applied on this branch) — https://github.com/xshaheen/headless-framework/issues/765

## Settled-decision deviation (proceeded and flagged)

- Plan KTD2 specified the generic-EF frontier claim run "inside one transaction". Integration conformance proved that conflicts with the repo's DB-clock lease invariant (PostgreSQL `now()` freezes at transaction open, shortening leases; SqlServer per-statement clocks diverge descendant leases). Replaced with fenced autocommit statements: descendants copy the root's persisted lease deadline via a DB-evaluated subquery, every frontier UPDATE re-asserts eligibility and root ownership, and crash-mid-claim recovery is owned by `PruneToClaimedSet` plus the stalled-lease sweep. KTD2 was a planning default, not a `session-settled:` label; the deviation is documented in code comments citing `docs/solutions/design-patterns/atomic-database-clock-relational-lease-claims.md`.

## Review-recorded testing gaps (not blocking; candidates for follow-up)

- Direct unit test for `JobsExecutionContext.CacheFunctionReferences` on a deep branching tree (validated as coverage gap, not defect).
- Fenced-write tests for the durable-cancellation and `TerminateExecutionException` catch blocks.
- Concurrent two-node chain-claim contention conformance (native CTE + CAS frontier).
- Differential three-gate matrix (RunCondition x parent status x null condition) across in-memory/LINQ/native SQL.
- SqlServer `MAXRECURSION` boundary with an over-depth persisted chain.
- Timed descendant of a skipped non-timed sibling reaching Skipped end-to-end via the safety net.
- Lowering `MaxChainDepth` below a persisted chain's depth (documented truncation) across providers.

## Analyzer suggestions intentionally skipped (info-level, out of scope)

- `MA0045` `JobsHelper.cs` sync compression method (pre-existing sync design; async ripple).
- `RCS1239` while->for on the two frontier loops (deliberate commented loop shape).
- `MA0003` on pre-existing lines (`BasePersistenceProvider.cs:331`, `TimeJobConfigurations.cs:21`).

## Known flake (pre-existing)

- SqlServer `cron_graph_projection_uses_distinct_dates_and_storage_side_status_aggregation` can flake under full-suite parallel load (SQL-capture window interference from the new background poll; passes in isolation; cron path unchanged by this branch).
