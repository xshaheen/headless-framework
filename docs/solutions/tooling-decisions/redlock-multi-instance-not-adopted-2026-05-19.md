---
title: RedLock multi-instance algorithm — explicitly not adopted in Headless.DistributedLocks
date: 2026-05-19
category: tooling-decisions
module: DistributedLocks
problem_type: tooling_decision
component: tooling
severity: medium
applies_when:
  - Choosing or evaluating a distributed lock algorithm for Redis-backed locking
  - A consumer requests "stronger" lock safety guarantees and proposes RedLock
  - Reviewing whether to extend Headless.DistributedLocks.Redis to support multiple databases
tags: [distributed-locks, redlock, redis, consistency, consensus, tooling-decision, safety]
related_components: [Headless.DistributedLocks.Redis, Headless.DistributedLocks.Core]
---

# RedLock multi-instance algorithm — explicitly not adopted in Headless.DistributedLocks

## Context

**RedLock** is a multi-instance distributed locking algorithm proposed by Antirez (Redis creator) at https://redis.io/topics/distlock. The idea: acquire the same lock against **N independent Redis instances** (typically 3 or 5) and consider it held only if a majority succeed. The claim is that this is safer than single-instance Redis locks because a single instance failure or rogue replica promotion cannot cause two holders to believe they own the lock.

When evaluating Headless.DistributedLocks safety guarantees, the question of whether to adopt RedLock — either by extending `Headless.DistributedLocks.Redis` to accept multiple `IDatabase` instances or by shipping a new provider — comes up. This document records the decision **not** to adopt it, the reasoning, and what to recommend to consumers who think they need it.

## Guidance

**Do not implement RedLock in `Headless.DistributedLocks.Redis`.** Stick with single-instance Redis locking. When a consumer requests "stronger" safety:

1. Validate the actual requirement — most "we need stronger locks" requests are solved correctly by **idempotent operations + a single lock**, not by stronger locks.
2. If true cross-failure consensus IS required (rare): point them to a consensus store (ZooKeeper, etcd) or a database-coupled lock (Postgres `pg_advisory_xact_lock` scoped to the same transaction as the data mutation). These give actual safety, not the *appearance* of safety.
3. Document RedLock's limitations so the reflex to reach for it diminishes.

## Why This Matters

### What RedLock claims to give you

The algorithm:

1. Get the current time `t0`.
2. Sequentially try to acquire the lock on N independent Redis instances using `SET key value NX PX ttl`, each with a small per-instance timeout.
3. If the lock is acquired on **a majority** (e.g., 3 of 5) AND total elapsed time is less than the TTL, the lock is held.
4. If not, release on all instances and retry.

The claimed benefit: a single Redis instance failure or rogue replica promotion cannot cause two holders to believe they own the lock.

### Martin Kleppmann's 2016 critique

["How to do distributed locking"](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html) — Kleppmann showed RedLock is unsafe under two realistic conditions:

**1. Clock skew between Redis nodes.** RedLock relies on each Redis instance's local clock to expire the lock at the right time. If clocks drift (which happens — VMs pause, NTP jumps, virtualization), one node may expire the lock early while others still hold it. A second client can acquire the lock on the now-free nodes plus the remaining ones still ticking, achieving a majority *while the original holder also believes it has a majority*. **Two holders, simultaneously, on the same lock.**

**2. GC pauses or process suspensions on the client.** Between the time the algorithm validates "total elapsed < TTL" and the time the protected operation completes, a long GC pause can elapse. The lock expires on Redis. Another client acquires. The original client wakes up and proceeds, believing it still holds the lock. Single-Redis locks have the same problem, but RedLock's multi-instance acquire makes the timing window **harder to reason about, not safer**.

Kleppmann's deeper point: RedLock is in a "timing-based" safety category. It works if and only if clocks are bounded and pauses are bounded. Neither is true in practice. The only safe way to coordinate across failures is to use a **fencing token** — a monotonically increasing number issued with each lock acquisition that the protected resource itself validates and rejects stale tokens. RedLock does not issue fencing tokens.

### Antirez's response

Antirez disagreed: ["Is Redlock safe?"](http://antirez.com/news/101). His argument is that clock skew assumptions in real-world deployments are weaker than Kleppmann modeled. The debate is unresolved and worth knowing exists. Skim both posts — neither party convinces the other.

### Operational reality

Even setting safety aside, RedLock has operational costs:

- You run and monitor N independent Redis instances, not 1. Each has its own failure modes, replication topology (if any), and operational toil.
- Acquire requires N round-trips. Latency floor rises.
- Quorum loss (e.g., 3 of 5 instances unreachable) means **no lock can be acquired** until restored. Availability worse than single-instance.
- StackExchange.Redis must be configured with separate `ConnectionMultiplexer` instances per Redis. More moving parts.

For the safety RedLock *claims*, use a consensus store. For the latency/availability single-instance Redis already gives you, RedLock adds cost without proportional gain.

### What "stronger safety" usually means in practice

When a consumer says "I need stronger locks," 9 times out of 10 the real requirement is one of:

- **Idempotency** — duplicate execution must be safe, not impossible. Add an idempotency key + dedupe table to the protected operation.
- **Exactly-once side effects** — typically solved by outbox patterns, not locks.
- **Atomicity with data mutation** — use a database-coupled lock (Postgres advisory locks in the same transaction, or row-level locks). Headless does not currently ship this, but it's the right primitive when this requirement appears.
- **Fencing against zombie holders** — emit a monotonically increasing token from the lock acquisition and have the protected resource reject stale tokens. This requires resource cooperation, not a stronger lock algorithm.

A single-Redis lock with TTL + auto-extension (shipped in `Headless.DistributedLocks` — mutex, reader-writer lock, and N-holder semaphore each carry the lease lifecycle plus monotonic fencing tokens) + careful idempotency handles the vast majority of real distributed-coordination needs.

## When to Apply

- A consumer asks to multi-database their Redis locks → point them here.
- A consumer cites RedLock as motivation for adding a feature → validate the actual requirement first; the requirement is almost never "RedLock specifically."
- A team member proposes "let's just add RedLock to be safe" → this doc is the rebuttal.
- A real consensus requirement appears (financial settlement, regulated exactly-once delivery) → recommend ZooKeeper, etcd, or Postgres-coupled advisory locks. Do not respond by adding RedLock.

## Examples

### Example 1 — request that does NOT need RedLock

> "We're running across 3 regions and our lock service has occasional Redis failovers. Two workers sometimes process the same job."

Real fix: workers verify the job hasn't already been processed (idempotency check on a database row) before doing work. Lock prevents the common case; idempotency check prevents the rare race during failover. RedLock would not help here because the race window is "client A's GC pause" not "Redis node disagreement."

### Example 2 — request that does NOT need RedLock

> "We need exactly-once for outbound emails."

Real fix: outbox pattern with idempotency on the email side (deduplication key in the email service or SMTP idempotency header). Locks alone do not give exactly-once; they give at-most-once *if* the protected code never partially fails. The right pattern is at-least-once delivery + downstream dedup.

### Example 3 — request that genuinely needs consensus (rare)

> "We need to elect a single leader across N replicas, where 'two leaders' is a regulatory violation."

Real fix: ZooKeeper, etcd, or Consul leader election. These have purpose-built consensus protocols (Zab, Raft) with mathematical safety proofs. RedLock does not.

### Example 4 — request that needs atomicity with data

> "We need to make sure no one else mutates row X while we're computing the new value."

Real fix: Postgres advisory transaction lock (`pg_advisory_xact_lock`) acquired in the same transaction as the row update. The lock and the data mutation succeed or fail atomically — true safety, no timing assumptions. This belongs in a future `Headless.DistributedLocks.Postgres` provider when the demand arises.

## Related

- GitHub tracking issue [#287](https://github.com/xshaheen/headless-framework/issues/287) — DistributedLocks 4-phase enhancement, where RedLock is listed under "Out of scope" with this rationale.
- Kleppmann, "How to do distributed locking" — https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html
- Antirez, "Is Redlock safe?" — http://antirez.com/news/101
- Original RedLock spec — https://redis.io/topics/distlock
