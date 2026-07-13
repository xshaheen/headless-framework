---
title: Atomic database-clock relational lease claims
date: 2026-07-13
category: design-patterns
module: Distributed persistence
problem_type: design_pattern
component: database
severity: high
applies_when:
  - A relational lease is written and evaluated by multiple application nodes
  - Application and database clocks may differ
  - Claim polling is too frequent for a separate database-clock query per tick
  - The same abstraction also has an in-memory implementation
related_components:
  - background_job
  - service_class
tags:
  - distributed-leases
  - database-clock
  - clock-skew
  - atomic-claim
  - postgresql
  - sql-server
---

# Atomic database-clock relational lease claims

## Context

A durable lease is shared correctness state: one node writes its deadline and another node may decide
that the deadline elapsed. If the write and comparison use different application clocks, skew changes
the effective lease. A fast observer may reclaim live work early; a slow observer may delay recovery.

Jobs exposed this split after relational renewal and reclaim moved to database time while initial claims
still stamped `LockedUntil` from the claimant's `TimeProvider`. PR #658 is the pending implementation for
that gap; its generic EF path follows this pattern, while its native paths still require the audit listed
below.
PR #652's native PostgreSQL and SQL Server claim strategies also demonstrated why provider overrides
must preserve correctness invariants, not only the interface and throughput characteristics.

## Guidance

For relational leases shared across nodes, the database is the temporal authority:

1. Evaluate lease expiry using database time.
2. Stamp the new lease deadline using database time. Other audit timestamps need the same authority only
   when they participate in ownership or expiry predicates; Jobs intentionally stamps `UpdatedAt` with
   the claim because it is also an optimistic-concurrency fence.
3. Keep eligibility, ownership transition, deadline stamp, and claim result in one atomic statement.
4. Express the provider clock inside that statement; do not issue a scalar clock query on every polling
   tick.
5. Test generic EF and native provider strategies independently with a deliberately skewed application
   clock.
6. Keep `TimeProvider` for in-memory implementations, where it is the coherent single-process clock and
   the deterministic testing seam.

An EF-translated claim can place `DateTime.UtcNow` inside the expression tree so the relational provider
translates it into its server-time expression:

```csharp
await jobs
    .Where(x => x.LockedUntil == null || x.LockedUntil <= DateTime.UtcNow)
    .ExecuteUpdateAsync(
        setters => setters
            .SetProperty(x => x.OwnerId, owner)
            .SetProperty(
                x => x.LockedUntil,
                _ => DateTime.UtcNow.AddSeconds(leaseDuration.TotalSeconds))
            .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
        cancellationToken)
    .ConfigureAwait(false);
```

The Jobs implementation names its database-clock predicates explicitly in
`src/Headless.Jobs.EntityFramework/Infrastructure/JobsQueryExtensions.cs`. Native root and descendant
claim updates use PostgreSQL `CURRENT_TIMESTAMP` and SQL Server `SYSUTCDATETIME()`. PostgreSQL
`CURRENT_TIMESTAMP` is fixed at transaction start, which is suitable only because Jobs claim
transactions are intentionally short; use `clock_timestamp()` when statement-time rather than
transaction-time semantics are required. Provider translation must be proven by integration tests
rather than inferred from LINQ behavior.

Pending PR #658 database-stamps native root and descendant claim deadlines, but its raw PostgreSQL and
SQL Server fallback and existing-occurrence eligibility clauses still contain application-supplied
`@now` comparisons. Some cron insertion, fallback stamp, and lease-refresh paths also retain
application-clock deadlines. Those paths must be converted before the native strategies satisfy this
pattern completely.

## Why This Matters

A timestamp is meaningful only relative to the clock that evaluates it. Using the database clock on
both sides of `LockedUntil <= now` makes lease duration independent of host skew. Keeping the clock
expression inside the atomic update also avoids an additional scalar clock-query round trip and avoids
sampling time outside the statement that consumes it.

Provider parity is behavioral, not an obligation to use the same clock source. Relational providers
need a shared durable authority; in-memory providers need one injected deterministic authority.

## When to Apply

- A durable row contains a lease or visibility deadline whose expiry changes ownership.
- Different processes can write and evaluate the deadline.
- Expiry can cause duplicate execution, loss, terminalization, or delayed recovery.
- A generic implementation has provider-native overrides that can drift from its invariants.

Do not apply this mechanically to ordinary cache freshness timestamps or connection-scoped database
locks. First establish that the timestamp participates in cross-node ownership correctness.

## Examples

Avoid binding both eligibility and the new deadline from the application clock:

```sql
WHERE locked_until IS NULL OR locked_until <= @applicationNow
SET locked_until = @applicationNowPlusLease
```

Prefer one provider-native atomic statement:

```sql
-- PostgreSQL shape
WITH candidates AS (
    SELECT id
    FROM jobs
    WHERE locked_until IS NULL OR locked_until <= CURRENT_TIMESTAMP
    FOR UPDATE SKIP LOCKED
)
UPDATE jobs AS job
SET owner_id = @owner,
    locked_until = CURRENT_TIMESTAMP + (@leaseSeconds * INTERVAL '1 second'),
    updated_at = CURRENT_TIMESTAMP
FROM candidates
WHERE job.id = candidates.id
RETURNING job.id;
```

The SQL Server equivalent uses `SYSUTCDATETIME()` and `DATEADD` in the same `UPDATE ... OUTPUT`
statement.

## Related

- PR #658 — pending Jobs implementation and skewed-clock conformance coverage.
- PR #652 — merged native atomic PostgreSQL and SQL Server Jobs claim strategies.
- PR #456 — merged database-clock renewal and reclaim work that exposed the residual acquisition gap.
- `docs/solutions/architecture-patterns/coordination-register-establishes-durable-liveness.md` — the same
  store-time authority principle for Coordination liveness.
- `docs/solutions/design-patterns/redis-zset-semaphore-prune-count-separation.md` — Redis server time
  inside atomic semaphore acquisition.
- `docs/solutions/tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md` — related clock and
  ownership trade-offs.
