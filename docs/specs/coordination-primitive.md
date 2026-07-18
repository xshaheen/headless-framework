---
title: Headless.Coordination — design spec
status: in-progress (modeling)
issue: 396
date: 2026-06-05
mode: design-spec (no implementation)
---

# Headless.Coordination — Design Spec

> **Status:** Modeling in progress. This document is a faithful snapshot of resolved
> design decisions and their rationale. Unresolved decisions are marked `OPEN`.
> Nothing here is "decided" until it has an explicit **Resolution** with rationale.

## 1. What we are solving

Two problems were originally stacked. They have different urgency and must not be
conflated.

1. **The acute trigger (legacy worker-id collision).** The old numeric ID path
   depended on a small per-instance worker-id space that containers could duplicate
   silently, producing duplicate `long` values in production. **Resolution:
   remove framework-owned numeric ID generation instead of coordinating worker ids.**
   This is a breaking greenfield simplification:
   `IEntity<long>` add-time generation is dropped, Messaging storage ids move to
   `Guid`, and lock lease ids use `IGuidGenerator`.

2. **The structural goal (de-silo coordination).** Membership/liveness logic already
   exists in the repo but is siloed in `Headless.Jobs.Caching.Redis` and Redis-only.
   Run Jobs on Postgres without Redis and dead-node recovery silently vanishes;
   Messaging has no node-level recovery at all. The goal is to extract the genuinely
   shared concern — **liveness + identity** — once, so Jobs, Messaging, dashboards,
   and future recovery features consume it.

**Framing decision (agreed):** we are building the *correct shape* for a maybe-soon
multi-service world, not merely patching the bug. The bug is the forcing function;
the de-silo is the point. Legacy numeric ID generation is treated as a removed
implementation detail, not as a concern Coordination must make safe.

## 1.5 Non-goals & safety ceiling (Decision 7 — RESOLVED)

`Headless.Coordination` is an **application-level coordination substrate**.

**It provides:**
- node identity;
- incarnation-qualified liveness;
- lifecycle events;
- ordered live-node views;
- lease/session integration;
- optional acceleration for feature recovery.

**It is not a consensus system. It does not provide:**
- Raft/Paxos-style agreement;
- split-brain-proof leadership under arbitrary network partitions;
- cross-region linearizability;
- transactional ownership transfer across unrelated stores;
- a generic ownership ledger;
- domain recovery logic.

**The safety ceiling is:**
- **fencing-safe** when consumers validate fencing tokens or incarnation-qualified owners;
- **fail-stop** when lease/session loss is observed;
- **fail-closed** when the authoritative store is unavailable;
- **not consensus-safe** unless backed by a consensus-grade provider.

Consumers that require consensus-grade behavior must use a consensus-backed provider
such as Kubernetes/etcd/Consul/ZooKeeper, or an external platform primitive. This
ceiling must be explicit in the API surface and docs (so the primitive is never cited
as RedLock-equivalent — consistent with the repo's standing RedLock rejection).

## 2. Prior art studied (source-read, not paraphrased)

We read the concrete contracts of the three proven designs. They draw the **same
boundary**:

| | Stored / provider contract (dumb) | Where the algorithm lives |
|---|---|---|
| **Orleans `IMembershipTable`** | 8 methods: read/insert/update rows + CAS table-version + 1 CAS-free dirty-write (`UpdateIAmAlive`). Stores votes + a `SiloStatus` enum but doesn't know what they *mean* | `MembershipTableManager` (runtime): probing, suspicion, voting, declare-dead — all as ordinary CAS writes |
| **Akka `Lease`** | 5 members: `Acquire×2`, `Release`, `CheckLease`, `Settings`. No membership, no quorum, no leadership. **No fencing token (its gap).** | The consumer (SBR / Singleton / Sharding) holds quorum, retry, what-to-do-on-loss |
| **K8s `Lease`** | 7 dumb fields. No server-side TTL/expiry/callbacks. API server enforces only `resourceVersion` CAS | The `leaderelection` client holds the entire expiry/takeover/fail-stop algorithm |

**Universal lesson:** keep the stored contract dumb; put intelligence in a
consumer-side service. This maps onto what we already have — `IDistributedLease`
(Acquire/Release/Renew/**FencingToken**/LostToken) is Akka's `Lease` *with the
fencing token Akka lacks*, and `IDistributedLock` / `IDistributedReadWriteLock` are
the acquisition primitives over dumb backend storage.

**Shape fit:** we are a **K8s-shaped** problem, not an Orleans-shaped one. Orleans
does P2P probing + quorum voting because it distrusts the store's availability for
the liveness *decision*. Our consumers already depend on a shared DB/Redis, so we
trust the store for ordering — K8s' "dumb store + client-side algorithm + don't
trust remote wall clocks" template fits and **deletes the voting/suspicion oracle**.

## 3. Decision surface

Load-bearing (resolve first): 1–3. Downstream (gated): 4–8.

| # | Decision | Status |
|---|----------|--------|
| 0 | DistributedLocks-first primitive shape (lock vs lease naming, owner token, fencing) | **RESOLVED** (§3a) |
| 1 | Membership/liveness model (substrate shape, identity, clocks) | **RESOLVED** (§4) |
| 1b | Substrate read contract (liveness-tracker vs ownership-ledger) | **RESOLVED** (§4b) |
| 2 | Numeric ID boundary — remove instead of coordinate | **RESOLVED** (§5) |
| 3 | Mutual exclusion + package decomposition | **RESOLVED** (§6) |
| 4 | Correctness invariants & where enforced (fail-stop, graceful release, GUID uniqueness) | **RESOLVED** (§7) |
| 5 | Consumer integration contracts (ID cleanup / Jobs / Messaging) | **RESOLVED** (§9, verified vs code) |
| 6 | Provider model (backends, capability tiers, conformance harness) | **RESOLVED** (see `docs/plans/2026-06-06-001-feat-coordination-membership-substrate-plan.md`) |
| 7 | Failure & partition semantics (fail-stop vs fail-closed; "fencing-safe, not consensus-safe") | **RESOLVED** (§1.5) |
| 8 | Scope line for v1 (leadership election? HRW rebalance? Messaging hardening?) | **RESOLVED** (see `docs/plans/2026-06-06-001-feat-coordination-membership-substrate-plan.md`) |

## 3a. Decision 0 — Enhance DistributedLocks first — RESOLVED

`Headless.DistributedLocks` is the low-level lease/fencing primitive. Coordination
must build on it rather than re-inventing lock ownership, renewals, loss signals, or
fencing.

### Public vocabulary

**Resolution:**
- `IDistributedLock` is the regular mutual-exclusion acquisition primitive.
- `IDistributedReadWriteLock` is the read/write acquisition primitive.
- `IDistributedSemaphoreProvider` remains provider-shaped because
  `IDistributedSemaphore` already names the created bounded-N semaphore object.
- `IDistributedLease` is the held acquisition handle returned by locks, read/write
  locks, and semaphore slots.
- `DistributedLockInfo` is the inspection DTO for active regular locks.

**Why:** "Provider" was noise for the lock/RW-lock API. A caller asks for a lock and
acquires a lease. This makes the model read like:

```csharp
await using IDistributedLease lease = await distributedLock.AcquireAsync(resource);
lease.ThrowIfLost();
```

### Lease handle contract

`IDistributedLease` is the first-class holder-side lease handle:

```csharp
public interface IDistributedLease : IAsyncDisposable
{
    string LeaseId { get; }
    string Resource { get; }
    long? FencingToken { get; }
    CancellationToken LostToken { get; }
    bool IsLost => LostToken.IsCancellationRequested;
    void ThrowIfLost() { ... } // default implementation
    Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default);
}
```

The implementation may expose extra operational fields such as acquisition time,
renewal count, wait time, and whether loss can be observed. The load-bearing members
are `LeaseId`, `Resource`, `FencingToken`, `LostToken`, `IsLost`, `ThrowIfLost()`,
`RenewAsync(...)`, and async disposal/release.

### OwnerId vs LeaseId

**Resolution:** do **not** add `OwnerId` to `IDistributedLease` v1.

There are two different "owners" and mixing them is dangerous:

- `LeaseId` is the opaque acquisition token stored by the lock backend. It answers:
  "is this exact holder still the one allowed to renew/release?"
- `node@incarnation` is the Coordination ownership stamp stored by consumers. It
  answers: "which process incarnation should recover this job/message row if it dies?"

Those are not the same identity. A lock lease can be held by code that has no
Coordination node. A Coordination node may stamp many domain rows without a
corresponding per-row lock lease. Therefore the spec uses:

- `LeaseId` for lock-store ownership and release/renew equality;
- `FencingToken` for protected-resource stale-write rejection;
- `node@incarnation` for Jobs/Messaging/consumer-owned recovery predicates.

If a future backend needs human-readable attribution, add a non-correctness
`OwnerHint` / metadata field to acquire options, not a correctness-bearing
`OwnerId` on the lease handle.

### Correctness role

DistributedLocks provides:
- mutual exclusion;
- lease renewal/release;
- holder-side loss observation (`LostToken`);
- holder-side self-fencing (`ThrowIfLost()`);
- stale-holder rejection by downstream resources (`FencingToken`);
- bounded-N concurrency via semaphores;
- read/write concurrency where the backend supports it.

It does **not** provide:
- cluster membership;
- node incarnation identity;
- `NodeLeft` events;
- domain ownership ledgers;
- Jobs or Messaging recovery rules.

This keeps the stack layered:

```
DistributedLocks: lock/lease/fence
Coordination: node identity/liveness/events
Consumers: ownership rows + recovery predicates
```

## 4. Decision 1 — Membership & liveness model — RESOLVED

### Foundations (agreed)
- **K8s-style** lease/membership semantics over **framework-owned SQL/Redis providers**.
- **Orleans-style incarnation identity** — a node identity carries a monotonic
  incarnation/generation so a restarted node is distinct from its dead predecessor
  (Orleans `SiloAddress = endpoint + Generation`; restart ⇒ new larger generation ⇒
  predecessor's `Dead` row and successor's `Active` row never collide).
- Our **fencing-aware `IDistributedLease`** sits underneath, acquired via
  `IDistributedLock` / `IDistributedReadWriteLock` (we keep the fencing token Akka
  deliberately omits — consistent with the repo's standing rejection of RedLock).
- **Coordination reports liveness + identity. It does not own domain recovery.**
  The substrate answers "who is alive / who joined / who left / what is this node's
  incarnation-qualified identity." The **reaction and ownership queries stay in each
  feature** — reclaiming jobs, reclaiming in-flight messages — each carrying its own
  terminal-state-aware logic. (Coordination never answers "who owns what"; see §4b.)

### Fork 1 — single-row vs split descriptor/liveness → **SPLIT**
- **Resolution:** two rows per node — a **cold descriptor** (write-once on join:
  identity, incarnation, host/ports, role/metadata) and a **hot liveness row**
  (heartbeat timestamp, status).
- **Rationale:** heartbeat is hot data; the descriptor is cold. Mixing them forces
  frequent writes to a row that also carries metadata. Split is cleaner for SQL,
  Redis, dashboards, and future ownership queries. (Contrast Orleans, which folds
  both into one CAS row + a CAS-free dirty-write fast path; the split achieves the
  same hot/cold separation structurally instead of via a relaxed write path.)

### Fork 2 — observation-of-change vs store-server-clock TTL → **HYBRID, single-authority rule**
- **Resolution:** the provider **may** use its own authoritative server clock for
  expiration where supported (PostgreSQL `now()` / transaction timestamp; Redis
  `TIME`). Liveness is **exposed to consumers as observed state**, never as "trust
  remote clocks."
- **Correctness invariant (spec rule):**
  > Expiry and takeover are decided by exactly one clock — the store's. A node's
  > heartbeat is written with the store's server time; dead-node detection is a
  > store-evaluated predicate (`last_beat < now() - ttl`). **No application node ever
  > compares another node's wall clock to its own.** The store is the sole temporal
  > authority; app clocks are never cross-compared. Takeover depends on observed
  > lease-renewal semantics, not on arbitrary remote app clocks.
- **Why hybrid (not pure K8s observation):** K8s uses local observation-of-change
  because etcd exposes no server-side "now" for this. Our SQL/Redis providers *do*
  expose an authoritative server clock, which is a stronger single-authority basis
  than per-client observation. We take it for provider-side cleanup/expiry while
  keeping the consumer-facing guarantee identical to K8s' intent (no cross-node
  wall-clock comparison).

### Authoritative-store rule for liveness decisions

Store-clock-based liveness is valid only on the authoritative write path.

Heartbeat writes, lease acquisition, lease renewal, lease expiry, slot takeover, and
`NodeLeft` decisions must be evaluated against the same authoritative consistency
domain.

For PostgreSQL, this means the primary/write connection. For Redis, this means the
primary executing the Lua script / transaction. For quorum-backed stores, this means
a linearizable operation.

Async replicas, read replicas, cache replicas, and eventually consistent reads may be
used for dashboards or approximate read-only views, but they must not drive failover
decisions, `NodeLeft` events, slot takeover, or recovery triggers.

If a provider cannot offer an authoritative clock plus strongly ordered read/write
semantics for the liveness row, it is not eligible for correctness-sensitive
coordination decisions and must be marked as degraded or unsupported for failover.

**Invariant:** no failover decision may be made from stale reads.

### Removed residual: worker-id reuse
Fork 2 makes **liveness/takeover** clock-safe. It deliberately does **not** solve
worker-id reuse, because the framework no longer coordinates numeric ID workers.
Framework-owned numeric ID generation is removed from the default architecture
instead. This deletes the need for slot leases, durable timestamp reservations,
custom time-source replacement, and mint-site gates.

## 4b. Decision 1b — Substrate read contract — RESOLVED (option C)

**Question:** Where does "what did the dead node own?" knowledge live, given that
Coordination must not own domain recovery?

**Resolution — C: liveness-tracker + incarnation-qualified identity as the stamp;
ownership lives in consumer tables.**

- Coordination exposes: register / heartbeat / leave, `IsAlive(node)`, the **ordered
  live-node set** (`GetLiveNodes()`), the **incarnation-qualified identity**
  (`nodeId@incarnation`) to stamp, and **events** `NodeJoined` / `NodeLeft` /
  `NodeSuspected`.
- Consumers **stamp the identity onto their own rows** (`jobs.owner = nodeId@inc`,
  `outbox.owner = nodeId@inc`) and, on `NodeLeft(node, inc)`, query *their own* tables
  (`WHERE owner = node@inc AND <not terminal>`) to reclaim — each keeping its own
  terminal-state-aware logic.
- Partition / shard ownership is **computed** from the live-node set (HRW /
  rendezvous hashing), not stored — every node derives the same owner deterministically.

**Rationale:** Orleans' grain directory (who owns which activation) is deliberately
**not** in `IMembershipTable` — membership provides identity + liveness; ownership is
a separate structure built on top, per-consumer. Ownership data is domain-shaped (a
job-claim and a message-claim have different terminal states and reclaim rules), so a
generic ownership store is either lossy or leaks domain logic into the substrate.
Rejected **B** (ownership-ledger) for re-introducing recovery into the substrate;
rejected **A** (pure liveness-tracker) for under-specifying the shared stamp every
consumer needs.

**Invariant:** the `NodeLeft` event payload carries the **incarnation** (`node@inc`),
not just the node id. Keying recovery on node-id alone would let a fast restart
(new incarnation, same node id) have its legitimately-owned rows reclaimed by a
survivor reacting to the *previous* incarnation's death. Recovery predicates must
match `owner = node@inc` exactly.

## 5. Decision 2 — Numeric ID boundary — RESOLVED (remove, do not coordinate)

Numeric ID worker ids are **not** part of the Coordination membership primitive, and
the framework does not introduce `IWorkerIdProvider`.

**Resolution:**
- Remove framework-owned numeric ID generation.
- Remove framework add-time generation for `IEntity<long>` rather than preserving
  a compatibility layer.
- Use `IGuidGenerator` as the framework-owned ID source.
- Use `Guid` for Messaging storage ids.
- Use GUID-derived opaque strings for DistributedLocks lease ids where the storage
  contract already expects `string`.

**Rationale:** coordinating worker ids is accidental complexity caused by preserving
a 64-bit numeric ID contract. The project is greenfield, so the cleaner architecture
is to break the `long` contract and standardize on the existing sequential GUID
abstraction. Coordination should solve liveness and incarnation identity, not make a
numeric generator safe across restarts and containers.

**Consequence:** there is no worker-id bridge package, no lease-backed worker-id
provider, no durable per-worker-id timestamp bound, no custom time-source replacement,
and no degraded bounded-skew production mode. Consumers that still want numeric
database-generated IDs must own that provider-specific choice outside the framework
default.

## 6. Decision 3 — Mutual exclusion + package decomposition — RESOLVED

The issue's "Coordination vs Cluster vs fold into DistributedLocks" question
**dissolves** by separating the two remaining shapes; both placements are true at
once.

| Shape | Home | Why |
|---|---|---|
| **Mutual exclusion** — bounded-slot lease, leadership (N=1) | **`Headless.DistributedLocks.*`** (existing) | The Redis ZSET semaphore (bounded-N lease) already lives here; leadership = N=1 lock; keeps all mutual-exclusion in one place with the fencing token |
| **Membership / liveness / identity / events** | **new `Headless.Coordination.{Abstractions,EntityFramework,PostgreSql,Redis}`** | Genuinely new shared concern (the K8s-style substrate); *consumes* DistributedLocks, never reinvents it |

**Dependency rules (explicit, to avoid accidental cycles):**
- `Headless.Extensions.Abstractions` defines `IGuidGenerator`.
- `Headless.Extensions` provides sequential GUID implementations (depends only on
  its own Abstractions and core helpers).
- `Headless.DistributedLocks` depends on `IGuidGenerator` only to mint opaque lease
  ids; it does not depend on Coordination.
- `Headless.Coordination` depends on `Headless.DistributedLocks` **only if** it needs
  lease helpers; it does **not** depend on Jobs, Messaging, or ORM.
- Jobs / Messaging / ORM consume only the small abstractions they need.

No arrow points back: no feature is a dependency of the primitives. This keeps the
design acyclic.

## 7. Decision 4 — Correctness invariants — RESOLVED

Coordination and GUID-backed consumers must enforce three invariants.

### 4.1 Fail-stop on lease loss
Any holder performing lease-protected work must stop using the protected resource
once its lease-loss token fires. Consumers self-fence at the work site by calling
`lease.ThrowIfLost()` or observing `LostToken`; infrastructure exposes the signal,
but the consumer owns the reaction.

### 4.2 Graceful release on shutdown
Lease handles are `IAsyncDisposable`. During host shutdown the holder releases its
lease so other nodes can acquire the resource without waiting for TTL expiry.
Graceful release speeds handover; correctness still depends on lease expiry,
fencing tokens, and incarnation-qualified recovery predicates.

### 4.3 GUID uniqueness instead of clock-safe worker-id reuse
Framework-owned ID generation uses `IGuidGenerator` and does not require a shared
worker-id namespace. Sequential GUID implementations keep database inserts
index-friendly while avoiding the legacy worker-id collision class.

There is no app-clock monotonicity proof to maintain across restarts: the GUID
generator is responsible for uniqueness, and Coordination is responsible only for
liveness/identity. Consumers that need monotonic fencing still use
`IDistributedLease.FencingToken`; they do not infer ordering from IDs.

## 8. Provider model and v1 scope -- RESOLVED

Decisions 5 (§9), 6, 7 (§1.5), and 8 are now **resolved**.

- **6. Provider model** — v1 ships `Headless.Coordination.{Abstractions,Core,Core.Database,PostgreSql,SqlServer,Redis}` plus `Headless.Coordination.Tests.Harness`. PostgreSQL and SQL Server are native ADO providers over the shared `Core.Database` substrate. Redis is a Lua-backed authoritative provider. There is no generic EntityFramework provider and no shipped InMemory provider in v1. Provider eligibility remains a correctness gate: failover-driving reads require an authoritative store clock plus linearizable liveness read/write.
- **8. Scope line for v1** — v1 is the membership/liveness substrate only: `node@incarnation` identity, register/heartbeat/leave, ordered live/snapshot reads, lifecycle events, fail-stop self-loss, and provider conformance. Jobs and Messaging consumer wiring, leadership election, HRW rebalance, dashboards, and consensus semantics are deferred to follow-up plans.

## 9. Decision 5 — Consumer integration contracts — RESOLVED (verified against code)

All consumer impacts were verified against actual source (file:line below).
**Verdict: the GUID pivot simplifies the ID path, and the Coordination abstraction
still fits Jobs and Messaging recovery.** Effort ordering becomes ID cleanup < Jobs <
Messaging recovery, matching the v1 lean.

### Why consumers need Coordination instead of only DistributedLocks

DistributedLocks answers "may I exclusively do this small critical section right
now?" Coordination answers "which process incarnations are alive, and which dead
incarnation should consumers recover from?"

Jobs and Messaging need the second question because they have durable work rows. A
lock can prevent two processors from claiming the same row at the same instant, but
it cannot tell a surviving node which rows were last owned by a crashed process
unless those rows carry a node-incarnation stamp and the system emits a reliable
`NodeLeft(node@inc)` signal.

The ownership rule is therefore:

```
acquire/claim path: stamp owner = current node@incarnation in the consumer table
death path: on NodeLeft(node@inc), consumer reclaims WHERE owner = node@inc AND not terminal
```

Coordination intentionally does not know what "not terminal" means. Jobs and
Messaging each keep their own terminal-state predicate.

### 5.1 ID generation — breaking GUID pivot
- **Resolution:** remove the framework-owned `long` generator path instead of making
  it production-safe. `IEntity<long>` no longer receives framework add-time key
  generation. Messaging `StorageId` becomes `Guid`. DistributedLocks lease ids are
  generated from `IGuidGenerator` and formatted as opaque strings.
- **Why `Guid`, not `string`, for Messaging:** `StorageId` is a framework-owned row
  identifier, not a provider-opaque external id. A `Guid` keeps type safety in public
  APIs, maps natively in SQL providers, and uses the existing sequential
  `IGuidGenerator` implementations. `string` remains appropriate for lock `LeaseId`
  because lease ids are already opaque storage tokens and some lock backends store
  them as strings.
- **Risks:** public API and schema-breaking change across Messaging monitoring,
  dashboard routes, storage providers, and tests. This is acceptable because the
  project is greenfield and no migration/deprecation path is required.

### 5.2 Jobs — fits-with-friction (schema-free; behavioral risk)
- **Current:** node identity = `SchedulerOptionsBuilder.NodeIdentifier` (string =
  `Environment.MachineName`, **stable across restarts, no incarnation** —
  `JobsOptionsBuilder.cs:182`). Ownership is **explicit and already present**:
  `TimeJobEntity.LockHolder`/`LockedAt` + `CronJobOccurrenceEntity.LockHolder`/`LockedAt`
  (`TimeJobEntity.cs:12,15`; `CronJobOccurrenceEntity.cs:10,13`) — real nullable DB
  columns. `InternalJobsManager.ReleaseDeadNodeResources(owner)` (`InternalJobsManager.cs:616`)
  already reclaims via `WHERE LockHolder = owner` (EF `BasePersistenceProvider.cs:250-287,520-557`).
  Membership is **Redis-only** (confirmed): without Redis, `IJobsRedisContext` →
  `NoOpJobsRedisContext` (returns no dead nodes), heartbeat service unregistered.
- **Migration:** remap `NodeIdentifier` → `node@incarnation` (single set-site); stamp
  `LockHolder` with it (no schema change — column exists); delete `JobsRedisContext`
  membership + `NodeHeartBeatBackgroundService` + the startup self-reclaim hook; trigger
  `ReleaseDeadNodeResources(node@inc)` from `NodeLeft`. Tighten the reclaim predicate
  `WhereCanAcquire` (`HeadlessJobsQueryExtensions.cs:12-28`) to strict `LockHolder == owner`
  (drop the `LockedAt == null` arm — incarnation makes the loose match wrong).
- **Biggest risk (Finding B, behavioral not schema):** the stable incarnation-free
  `NodeIdentifier` is load-bearing for the **no-Redis startup self-reclaim**
  (`EF/ServiceExtension.cs:61`). Adding an incarnation breaks name-match, so the
  no-Redis path's recovery becomes entirely dependent on `IMembership.NodeLeft`. ⇒
  **the EF/Postgres Coordination provider (DB-heartbeat liveness) must ship in the same
  v1 as the Jobs migration**, or pure-EF/Postgres Jobs loses recovery it had. (Feeds §8.)

**Why Jobs need Coordination:** Jobs already have ownership columns and recovery
logic, but liveness is Redis-only and identity is incarnation-free. Coordination
makes dead-node recovery backend-neutral and restart-safe: the row owner becomes
`node@inc`, and `NodeLeft(node@inc)` becomes the trigger. Without Coordination, the
Postgres/EF-only path either has no dead-node recovery or must keep duplicating
membership logic inside Jobs.

### 5.3 Messaging — fits-with-friction (net-new; 3-store schema migration)
- **Current:** CAP-derived outbox/inbox (`published`/`received`, `MediumMessage` row).
  Storage IDs now use provider-native GUID columns, and there is **no owner/node
  column anywhere**. Orphan recovery today = the per-row `LockedUntil`
  visibility-lease + `NextRetryAt`:
  a dead node's row is re-pickable when its lease expires via atomic
  `UPDATE … SET LockedUntil … FOR UPDATE SKIP LOCKED` (`PostgreSqlDataStorage.cs:796-810`).
  **No sweeper, no node concept** — genuinely net-new. Terminal-state scar predicate:
  `NOT (StatusName IN ('Succeeded','Failed') AND NextRetryAt IS NULL)`
  (`PostgreSqlDataStorage.cs:43-52`) — a `Failed` row with non-null `NextRetryAt` is
  retry-pending and must stay mutable.
- **ID migration:** change `StorageId` to `Guid` across Core, Dashboard,
  InMemory/PostgreSQL/SqlServer storage providers, monitoring APIs, route constraints,
  and tests. SQL providers store the id in native UUID/`uniqueidentifier` columns.
- **Recovery migration:** add `owner` (`owner_node` + `owner_incarnation`) to
  `published` + `received` across Postgres/SqlServer/InMemory; **stamp
  `owner = node@inc` at the same atomic claim** that sets `LockedUntil` (not separately
  — else re-introduce the SELECT-then-write double-dispatch race); add a `NodeLeft`
  reclaim UPDATE that ANDs `owner = node@inc` onto the **exact** terminal-guard
  predicate; keep `LockedUntil` as the safety floor (reclaim must not bypass it —
  guards against membership false-positive double-processing).
- **Biggest risk:** hand-written **positional SQL** column lists across 3 providers ×
  many statements (`PostgreSqlDataStorage.cs:809,837-848`) — a missed ordinal silently
  corrupts reads; and the terminal predicate must be replicated byte-for-byte in a 4th
  place. **Mitigation:** drive `owner`-stamp + `NodeLeft`-reclaim conformance through
  `Headless.Messaging.Core.Tests.Harness` (per repo harness rule) so all 3 stores test
  against one contract. (Team already anticipates a `LeaseMonitor`+autoExtend evolution
  here — #289/#296/#300.)

**What Messaging does today:** Messaging currently relies on per-row visibility
leases (`LockedUntil`) and retry scheduling (`NextRetryAt`). When a process dies, a
row becomes eligible again only after `LockedUntil` expires. The optional
`UseStorageLock` path uses `IDistributedLock` as coarse-grained mutual exclusion for
retry-pickup ticks; it does **not** identify the owning node, stamp rows, or recover
work on `NodeLeft`.

**Why Messaging needs Coordination:** Messaging can keep `LockedUntil` as the safety
floor, but node-incarnation ownership lets recovery become explicit and observable:
when `NodeLeft(node@inc)` fires, Messaging can accelerate retry visibility for rows
owned by that exact dead incarnation while preserving the terminal-state guard and
the visibility lease floor. This is not required for correctness today because
`LockedUntil` eventually recovers work, but it improves recovery latency,
operability, and parity with Jobs once the three storage schemas carry owner stamps.

**Spec constraint:** `IDistributedLock` remains useful for coarse retry-pickup
coordination, but it is not a replacement for Messaging ownership stamps. The two
layers compose:

```
IDistributedLock: one retry pickup tick at a time
Coordination owner stamp: which dead node's claimed rows may be recovered
LockedUntil: minimum safety floor against false-positive liveness decisions
```

## Appendix — key source references
- Orleans: `IMembershipTable`, `MembershipEntry`, `SiloAddress`/`Generation`,
  `SiloStatus`, `IGatewayListProvider` (read-only projection); liveness decisions in
  `MembershipTableManager` (dotnet/orleans).
- Akka: `Akka.Coordination.Lease` (+ `LeaseSettings`/`TimeoutSettings`/`LeaseProvider`);
  consumers SBR / Singleton / Sharding (akkadotnet/akka.net).
- K8s: `coordination.k8s.io/v1` `LeaseSpec`; `client-go` `leaderelection`
  (`observedTime`, the LeaseDuration > RenewDeadline > RetryPeriod triangle,
  `leaseTransitions`, `OnStoppedLeading`).
- Repo prior art: `Headless.Jobs.Caching.Redis` membership; `IDistributedLease`
  (FencingToken/LostToken); Redis ZSET semaphore (server-clock timestamps).
