---
date: 2026-06-15
topic: messaging-dead-owner-recovery-parity
---

# Messaging Dead-Owner Recovery: Dead-Only Reclaim + Shared Coordination Bridge

## Summary

Harden messaging dead-owner recovery so it reclaims only confirmed-`Dead` owners (never `Suspected`), and accelerate recovery with an event-driven + reconcile bridge. Both the bridge and Jobs' existing recovery run on a shared `DeadOwnerRecoveryBridge` extracted into Coordination. Messaging keeps its at-least-once re-dispatch; `LockedUntil` stays the correctness floor.

---

## Problem Frame

PR #421 shipped messaging dead-owner recovery as a lean v1: a reconcile-on-tick pass inside `MessageNeedToRetryProcessor` calls `GetLiveNodesAsync()` and reclaims rows whose `Owner` is not in the live set (`Owner <> ALL(liveOwners)`), pulling `LockedUntil` back to now so the next pickup re-leases. Validating against the Jobs integration (#422, `MembershipRecoveryBridge`) surfaced two gaps.

**Correctness — messaging reclaims on `Suspected`, not just `Dead`.** Liveness has three tiers: `Alive` → `Suspected` → `Dead`. `GetLiveNodesAsync()` returns `Alive` only. Reclaiming the complement of the `Alive` set therefore reclaims rows owned by a `Suspected` node — one that is very likely still alive and mid-dispatch. The reclaim pulls `LockedUntil = now`, the next tick re-dispatches, and the actually-alive suspect also completes its dispatch, producing duplicate delivery on every transient suspect window (GC pause, thread-pool starvation, brief store/network blip), not just on genuine crashes. The `Suspected` tier exists precisely to absorb those transients; messaging acts a full liveness tier too early. Jobs reclaims only `Dead` and `NodeLeft`.

**Latency / parity — reconcile-on-tick only.** Messaging deferred `WatchAsync`; recovery latency is bounded by the retry-poll interval. Jobs already uses the dual-trigger bridge for low-latency recovery with a periodic backstop.

---

## Key Decisions

- **Reclaim the dead set, not the live-set complement.** Read `GetLivenessSnapshotAsync()`, filter `State == Dead`, reclaim `Owner IN (deadOwners)`. This fixes the `Suspected`-reclaim bug by construction — a `Suspected` node is never in the dead set — rather than by documenting a `DeadThreshold >= DispatchTimeout` config invariant.

- **Extract a shared bridge; both domains consume it.** The dual-loop orchestration (watch + reconcile), the in-memory dedup set, the prune-on-reconcile, the `CancellationToken.None` reclaim write, and the remove-on-failure retry are identical between Jobs and Messaging — and they encode subtle correctness. Messaging is the second consumer of this exact pattern, which is the repo's extract-first trigger. A `DeadOwnerRecoveryBridge` in Coordination is parameterized by a reclaim action, a reconcile interval, and a logger category; only those three differ per domain.

- **Keep messaging's at-least-once re-dispatch.** Jobs marks dead-owner in-flight rows `Skipped` ("cannot safely resume") and releases only not-yet-started rows. Messaging intentionally re-dispatches in-flight rows because consumers are idempotent. This domain difference lives entirely inside each domain's reclaim action, so the shared bridge preserves it without special-casing.

- **`LockedUntil` stays the correctness floor.** The bridge only accelerates recovery. An owner that ages out of the `Dead` snapshot before being reclaimed is still recovered by normal pickup once its lease expires — messaging does not depend on the snapshot retention window the way Jobs does.

- **The bridge runs unconditionally, decoupled from `UseStorageLock`.** v1 reclaim ran only under the `UseStorageLock` path (serialized by the held retry lock), so with the default `false` it was effectively off. The standalone bridge registers always — matching Jobs, where the bridge is an always-on `BackgroundService` — making accelerated dead-owner recovery active by default. Cross-node concurrency safety rests on the idempotent conditional reclaim `UPDATE`, not on holding a lock. The `UseStorageLock` flag retains its separate meaning (serializing retry pickups) and its `false` default.

- **Ship as a single change.** Extracting the shared bridge, migrating Jobs onto it, and adding messaging land together, so the shared abstraction is validated by two consumers immediately rather than living briefly as a single-consumer extraction. The `IProcessor.NeedRetry.cs` merge with #296 is coordinated within this change.

---

## Requirements

**Reclaim semantics**

- R1. Messaging reclaim targets the dead set: read `GetLivenessSnapshotAsync()`, filter `State == Dead`, reclaim rows whose `Owner` is in `deadOwners`, replacing the current live-set-complement predicate across InMemory, PostgreSQL, and SQL Server.
- R2. A `Suspected` owner is never reclaimed.
- R3. Reclaim re-dispatches in-flight rows (at-least-once); it does not skip or abandon them.
- R4. `LockedUntil` remains the correctness floor: an owner that ages out of the `Dead` snapshot before reclaim is still recovered by normal lease-expiry pickup.

**Shared recovery bridge**

- R5. A shared `DeadOwnerRecoveryBridge` lives in Coordination, parameterized by a reclaim action, a reconcile interval, and a logger category; Jobs and Messaging both consume it.
- R5a. The bridge is registered unconditionally, independent of `UseStorageLock`, on both consumers.
- R6. The bridge runs dual triggers: a `WatchAsync` loop that reclaims on `NodeLeft` for low latency, and a periodic liveness-snapshot reconcile as the authoritative backstop. A watch-loop failure degrades to the reconcile path, not to no recovery.
- R7. The bridge dedups the event and reconcile paths via an idempotent in-memory reclaimed set, pruned as identities age out of the snapshot so a future same-id incarnation is never suppressed.
- R8. Jobs' existing `MembershipRecoveryBridge` is refactored onto the shared bridge with no behavioral change — its skip-in-flight policy is preserved inside its reclaim action.
- R9. Messaging's reclaim action covers both the published and received tables.

**Safety**

- R10. Reclaim writes use `CancellationToken.None` so a reclaim racing host shutdown is not torn mid-write.
- R11. A failed reclaim is logged and removed from the dedup set so the next reconcile tick retries it.
- R12. A restarted node (new incarnation) is never reclaimed by stale dead-owner state belonging to its prior incarnation.

**Tests and docs**

- R13. Cross-provider conformance runs through `Headless.Messaging.Core.Tests.Harness` (InMemory / PostgreSQL / SQL Server): `Suspected`-not-reclaimed, `Dead`-reclaimed, event+reconcile dedup, fast-restart incarnation fencing, and aged-out-recovered-via-floor.
- R14. `docs/llms/messaging.md` and `src/Headless.Messaging.Core/README.md` are synced with the dead-only semantics and the bridge.

---

## Acceptance Examples

- AE1. Suspected is not reclaimed.
  - **Covers R1, R2.**
  - **Given** an owner whose liveness is `Suspected` with in-flight rows it owns.
  - **When** a reconcile and a watch cycle both run.
  - **Then** no row owned by that owner has its `LockedUntil` pulled back; the owner completes its own dispatch with no duplicate.

- AE2. Dead is reclaimed.
  - **Covers R1, R3.**
  - **Given** an owner whose liveness is `Dead` with in-flight rows it owns.
  - **When** a reconcile runs.
  - **Then** those rows are re-dispatched (not skipped), recovered at-least-once.

- AE3. Event and reconcile do not double-reclaim.
  - **Covers R7.**
  - **Given** a `Dead` owner surfaced by both a `NodeLeft` event and the next reconcile.
  - **When** both paths fire.
  - **Then** the owner is reclaimed once; the second path is a no-op.

- AE4. Aged-out owner still recovers via the floor.
  - **Covers R4.**
  - **Given** a `Dead` owner that ages out of the snapshot before any reclaim runs.
  - **When** its row lease (`LockedUntil`) expires.
  - **Then** normal pickup re-leases and dispatches the row.

- AE5. Fast-restart incarnation fencing.
  - **Covers R12.**
  - **Given** a node that died at incarnation N and restarted as incarnation N+1.
  - **When** dead-owner state for `node@N` is reconciled.
  - **Then** rows owned by `node@N+1` are untouched.

---

## Scope Boundaries

**Deferred for later**
- `UseStorageLock` default stays `false`.
- Leadership election and a messaging dashboard.

**Outside this issue's axis**
- `IDistributedLock` lease-lifecycle cleanup (#296) — a different primitive (self lock-loss via `HandleLostToken`), blocked on #289. This work is the `INodeMembership` axis and does not depend on #289. The two co-edit `IProcessor.NeedRetry.cs`, so the merge must be coordinated, but the scope is disjoint.

---

## Dependencies / Assumptions

- **Concurrent per-node reclaim is safe.** Because the bridge runs unconditionally on every node, the per-node dedup set does not coordinate across nodes, and correctness rests on the conditional reclaim `UPDATE` (`... WHERE Owner IN (deadOwners) AND LockedUntil > now AND <terminal-guard>`) being idempotent and safe under concurrent application — which it is for the current per-provider queries. This is the load-bearing assumption of the unconditional-bridge decision; the harness concurrency test (R13) must exercise two nodes reclaiming the same `Dead` owner.
- Membership events are best-effort acceleration; the periodic reconcile is the authoritative backstop. A Coordination outage degrades recovery to the `LockedUntil` floor, not to a stalled retry tick.

---

## Outstanding Questions

**Deferred to planning**
- Shape of the reclaim-action contract (a delegate vs. a small interface), its naming, and where each domain registers the bridge.
- The messaging reconcile-interval option (name, default, and whether it reuses an existing messaging interval).

---

## Sources / Research

- `src/Headless.Jobs.Core/Coordination/MembershipRecoveryBridge.cs` — the validated reference bridge (dual loop, `_reclaimed` dedup + prune, `CancellationToken.None` reclaim, remove-on-failure retry).
- `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs` — messaging v1 reclaim (`_GetLiveOwnersForReclaimAsync` via `GetLiveNodesAsync`, `_TryReclaimDeadOwnersAsync`, `LiveOwnersForReclaimCache`), gated on `UseStorageLock`.
- `src/Headless.Coordination.Abstractions/INodeMembership.cs` — `GetLiveNodesAsync` (`Alive`-only), `GetLivenessSnapshotAsync`, `WatchAsync`.
- `src/Headless.Coordination.Abstractions/NodeLivenessState.cs` — `Alive = 0`, `Suspected = 1`, `Dead = 2`.
- `src/Headless.Coordination.Core/MembershipService.cs:90` — `GetLiveNodesAsync` = `Alive`-only, the root of the correctness gap.
- Per-provider reclaim queries (current complement form, to flip to dead-set membership): PostgreSQL `Owner <> ALL(@LiveOwners)`, SQL Server `Owner NOT IN (...)`, InMemory `Owner in liveOwnerSet` skip.
- Issues: #427 (this), #421 (messaging v1), #422 (Jobs reference), #396 (coordination epic), #296 / #289 (adjacent distributed-lock lifecycle).
