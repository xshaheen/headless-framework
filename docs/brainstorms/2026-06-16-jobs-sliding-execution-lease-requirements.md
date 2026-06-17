# Requirements: Sliding execution lease for running jobs (#316)

**Date:** 2026-06-16
**Status:** requirements (ready for `/x-plan`)
**Related:** #316 (worker-side lease renewal, deferred from #268/#315), PR #456 (`OnNodeDeath` input API), #263 (unify Jobs/Messaging substrates)

## Summary

Add a **renewable per-job execution lease** to Headless.Jobs: while a job runs, the owning worker periodically extends its `LockedUntil` ("slides" the lease); a job that stops renewing (crashed or wedged) has its lease lapse and is recovered per its `OnNodeDeath` policy. This closes the one recovery gap the current design has — a job stuck `InProgress` on a still-alive node — and removes the operational burden of sizing a timeout larger than the longest expected job runtime. It is the Hangfire "sliding invisibility timeout" / Temporal "activity heartbeat" pattern, implemented on the existing lease column.

## Problem Frame

Today's recovery has two tiers (both verified in source):

- **Coordination node-death** (`MembershipHeartbeatBackgroundService`, `DeadThreshold` default 30s): declares a whole node dead on missed heartbeats → dead-node sweep reclaims its rows.
- **Fixed lease floor** (`SchedulerOptionsBuilder.LeaseDuration` default 5 min): `WhereCanAcquire` and `QueueTimedOutTimeJobs` re-claim **only `Idle`/`Queued`** rows whose lease lapsed, gated on `OnNodeDeath == Retry`.

**The gap:** a job wedged `InProgress` on a node that keeps heartbeating (infinite loop, hung un-cancellable call) is recovered by *neither* path — both claim predicates require `Idle`/`Queued`, and the sweep needs whole-node death. It is orphaned until the node itself dies or an operator intervenes. The mature long-running-job systems (Hangfire sliding, Temporal heartbeat) close this with a worker-renewed lease; the scheduler-tier systems that lack it (Quartz, Sidekiq, TickerQ) share the same hole.

A secondary cost: because the lease is fixed, operators must size `LeaseDuration`/`DeadThreshold` greater than the longest job runtime or risk a duplicate — a contract that is currently only a startup warning.

## Goals

- Recover a job stuck `InProgress` on a live node, automatically, at per-job granularity.
- Remove the "size the timeout > longest runtime" burden: a healthy long job stays owned indefinitely by renewing.
- Preserve the `OnNodeDeath` safety contract: only `Retry` jobs are ever speculatively reclaimed; `MarkFailed`/`Skip` never run twice.
- Keep it contained in Jobs — no new store or package dependency.

## Non-Goals

- Reusing `Headless.DistributedLocks` `IDistributedLease`/`AutoExtend` (would add a dependency + a second lease store + a second liveness system — see Key Decisions).
- Temporal-style checkpoint/resume (event-sourced progress so a reclaimed job resumes mid-work). Durable-execution territory; out of scope.
- Any change to `Headless.Messaging` (it already has `LockedUntil` leases; convergence tracked by #263).
- Raising `DeadThreshold` 30s → 5 min as a blanket change (superseded by the node-death/lease interaction in R4).

## Requirements

- **R1 — Renew the running job's lease.** While a worker executes a job, it periodically extends that row's `LockedUntil` to `now + LeaseDuration`. Renewal cadence is a fraction of the TTL (≈ ⅓, the Hangfire/DistributedLocks convention) so a single missed renewal does not lapse the lease.
- **R2 — Reclaim lapsed-lease running jobs.** The reclaim path gains an arm matching `InProgress` rows whose `LockedUntil <= now`, gated on `OnNodeDeath == Retry` (mirrors the existing `Idle`/`Queued` lease-expiry arm). `MarkFailed`/`Skip` lapsed-lease rows are transitioned terminal, never re-claimed — identical to the dead-node sweep's per-policy behavior.
- **R3 — Cancel on lease loss.** When a worker can no longer renew (it observes its own lease taken or lapsed), it cancels the running job via its `CancellationToken`, so a reclaimed job is not also still executing on the original worker. (Best-effort: a truly hung job may ignore cancellation; correctness for non-idempotent work still rests on `OnNodeDeath`.)
- **R4 — Node-death sweep defers `InProgress` to the lease.** When Coordination declares a node dead, the sweep reclaims that node's `Idle`/`Queued` rows immediately (they never started), but for `InProgress` rows it acts only when the per-job lease has *also* lapsed. A busy node whose running jobs are still renewing their leases does not lose them to a transient membership-store blip. (`Idle`/`Queued`/terminal behavior unchanged.)
- **R5 — Defaults unchanged, contract relaxed.** `LeaseDuration` stays 5 min; `DeadThreshold` stays responsive (not raised). With R1–R2 in place, the "timeout must exceed longest runtime" contract no longer holds for *running* jobs (they renew) — it remains only for the `Idle`→start gap. Update the startup warning / docs accordingly.

## Key Technical Decisions

- **KTD1 — Renew our own column, not `IDistributedLease`.** Jobs already references `Coordination` but **not** `DistributedLocks` (verified in `src/Headless.Jobs.Core/Headless.Jobs.Core.csproj`). Reusing `IDistributedLease`+`AutoExtend` would add that dependency plus a second lease store alongside the Jobs row, and create two liveness systems whose state can diverge. Renewing `LockedUntil` ourselves is self-contained and reuses the existing predicate/sweep shape. We borrow DistributedLocks' proven *parameters* (⅓-TTL cadence, cancel-on-loss via `LostToken`-style signal) without its engine.
- **KTD2 — Coordination stays node-level (answers "does Coordination support activity heartbeat?").** `INodeMembership` exposes only node-level `HeartbeatAsync()` — there is no per-resource/per-job liveness primitive (verified in `src/Headless.Coordination.Abstractions/INodeMembership.cs`). The per-job heartbeat is therefore implemented as lease renewal on the Jobs row, not via Coordination. Coordination remains the coarse node-death backstop.
- **KTD3 — `OnNodeDeath` is still the correctness authority.** The renewed lease changes *liveness detection* (how fast a stalled job is noticed), not *delivery semantics* (what happens to it). `Retry` = at-least-once (may be reclaimed on lapse), `MarkFailed`/`Skip` = at-most-once (terminal, never reclaimed). Unchanged.

## High-Level shape (directional — not implementation spec)

```text
worker picks job → InProgress, LockedUntil = now + 5m
   ├─ every ~90s while running: LockedUntil = now + 5m   (R1 renew / slide)
   ├─ finishes      → terminal status (completion fence from PR #456 applies)
   ├─ crashes/wedges→ renewals stop → LockedUntil <= now
   │                    └─ reclaim arm: InProgress && lapsed && OnNodeDeath==Retry → released  (R2)
   │                                    MarkFailed/Skip → terminal                              (R2)
   └─ node declared dead by Coordination:
        Idle/Queued → reclaimed now;  InProgress → only if lease also lapsed                    (R4)
```

## Scope Boundaries

### In scope
R1–R5: lease renewal while running, lapsed-lease `InProgress` reclaim arm, cancel-on-loss, node-death/lease interaction, defaults + contract/doc update.

### Deferred to follow-up
- **Checkpoint/resume** (carry progress so a reclaimed job resumes mid-work, Temporal heartbeat-details style). Natural next layer on R1 if demand appears; not now.
- **Configurable renewal cadence / monitoring mode** (None/Monitor/AutoExtend, à la DistributedLocks). Start with a single sensible cadence; expose knobs only if needed.

### Outside this product's identity
- Durable-execution engine (event-sourced workflow replay, deterministic workflow code). That is a different product (Temporal/Durable-Task), not a scheduler enhancement.
- Per-message at-most-once in Messaging (#263 territory; at-least-once is correct there).

## Open Questions / Assumptions (for `/x-plan`)

- **OQ1 — Renewal driver.** Who issues the renewal: the execution task handler wrapping each running job, or a periodic background sweep that extends all locally-owned `InProgress` rows in one UPDATE? (Batch is cheaper on the DB; per-job is more precise. Plan decides.)
- **OQ2 — Claim→start ownership recheck.** Verify (Jobs code, not yet read) whether a worker rechecks ownership at `Queued → InProgress`; under worker-pool saturation a `Queued` lease can lapse and be re-claimed in the gap. R2's work is the natural place to close this if open.
- **OQ3 — Cancel-on-loss detection.** How a worker learns its own lease lapsed/was taken (renewal UPDATE affected 0 rows is the obvious signal) and wires that to the job's `CancellationToken`.
- **Assumption — thread-pool starvation is out of band.** A fully CPU-pegged node delays both its node-heartbeat and its lease renewal (both async-on-thread-pool), so the lease cannot save a starved node from itself. That case stays operational (don't starve the pool) + `OnNodeDeath`. The renewed lease protects against membership-store blips and ordinary stalls, not total starvation.

## Success Criteria

- A job that stops making progress on a live node is reclaimed within ≈ one lease TTL (default 5 min) — today it never is.
- A legitimately long job (e.g. 15 min, idempotent) runs to completion under default settings with no duplicate and no timeout tuning.
- A `MarkFailed`/`Skip` job is never executed twice under any lease-lapse or node-death scenario.
- Cross-provider conformance (Postgres + SqlServer + in-memory) covers: renewal extends the lease; a lapsed-lease `InProgress` `Retry` row is reclaimed; `MarkFailed`/`Skip` lapsed rows go terminal; node-death defers `InProgress` to the lease.

## Sources & Research

- In-repo (verified this session): `WhereCanAcquire`/`QueueTimedOutTimeJobs` gate on `Idle`/`Queued` (`src/Headless.Jobs.EntityFramework/Infrastructure/JobsQueryExtensions.cs`, `BasePersistenceProvider.cs`); no lease renewal exists; `MembershipHeartbeatBackgroundService` is node-level async loop; `CoordinationOptions` defaults (Heartbeat 5s / Suspicion 15s / Dead 30s); `LockMonitoringMode.AutoExtend` exists in DistributedLocks but Jobs has no dependency on it.
- External (verified via web): Hangfire `SlidingInvisibilityTimeout` (5 min, worker-renewed, now default); Temporal activity heartbeat + heartbeat timeout (fast failure detection independent of Start-To-Close); Quartz `RequestsRecovery` (binary, no per-job heartbeat); Sidekiq `super_fetch` (process-heartbeat orphan recovery, 5min–3hr, no per-job renewal); TickerQ (EF optimistic-concurrency claim + timeout-policy recovery, no per-job renewal). Headless.Jobs is the only one of these with a 3-way per-job death policy; this enhancement adds the per-job liveness signal the engine-tier systems have.
