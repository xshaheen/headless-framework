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
still stamped `LockedUntil` from the claimant's `TimeProvider`. Pending PR #658 closes that gap across
generic EF and native PostgreSQL and SQL Server claims.
PR #652's native PostgreSQL and SQL Server claim strategies also demonstrated why provider overrides
must preserve correctness invariants, not only the interface and throughput characteristics.

## Guidance

For relational leases shared across nodes, the database is the temporal authority:

1. Evaluate lease expiry using database time.
2. Stamp the new lease deadline using database time. Other audit timestamps need the same authority only
   when they participate in ownership or expiry predicates; Jobs intentionally stamps `UpdatedAt` with
   the claim because it is also an optimistic-concurrency fence.
3. In native SQL, evaluate server time once per claim command and reuse that snapshot for eligibility
   and the deadline stamp.
4. Keep eligibility, ownership transition, deadline stamp, and claim result in one atomic statement.
5. Express the provider clock inside that statement; do not issue a scalar clock query on every polling
   tick.
6. Test generic EF and native provider strategies independently with a deliberately skewed application
   clock.
7. Keep `TimeProvider` for in-memory implementations, where it is the coherent single-process clock and
   the deterministic testing seam.

### Testable without a clock round trip

Separate two time responsibilities instead of forcing one clock abstraction across every provider:

- **Scheduling time** answers when a job should become a candidate. Keep it behind `TimeProvider` so
  unit tests can advance a fake clock deterministically.
- **Ownership time** answers whether a shared lease is valid and what deadline the database should
  persist. Relational providers express this clock directly in the atomic SQL statement.

The relational API should accept a duration, not an application-computed absolute deadline. The SQL
derives the deadline from its own clock while it evaluates eligibility:

```sql
-- One PostgreSQL statement and one round trip
WITH claim_clock AS MATERIALIZED (
    SELECT clock_timestamp() AS now
), candidate AS (
    SELECT id
    FROM jobs, claim_clock
    WHERE locked_until IS NULL OR locked_until <= claim_clock.now
    FOR UPDATE SKIP LOCKED
)
UPDATE jobs AS job
SET owner_id = @owner,
    locked_until = claim_clock.now + (@leaseSeconds * INTERVAL '1 second')
FROM candidate, claim_clock
WHERE job.id = candidate.id
RETURNING job.id, job.locked_until;
```

SQL Server declares a command-local `@claimNow = SYSUTCDATETIME()` and reuses it in the CTE and
`DATEADD` setter before `OUTPUT inserted`. The declaration and update are sent as one command and one
round trip. EF paths put
`DateTime.UtcNow` inside the `ExecuteUpdate` expression tree so the provider translates the comparison
and setter; they must not evaluate it into a local variable first.

Test the split at two levels:

1. Unit-test scheduling and in-memory behavior with `FakeTimeProvider`.
2. Run provider conformance tests against real PostgreSQL and SQL Server with an application
   `TimeProvider` skewed far enough that application-clock and database-clock outcomes disagree.
3. Seed a foreign-owned Retry lease that is expired according to only one clock, then assert the
   relational provider follows the database outcome.
4. Assert the persisted deadline is approximately one lease duration after database time. Use a bounded
   interval around the operation rather than exact timestamp equality.
5. Exercise generic EF and native claim strategies separately; a shared interface does not prove their
   SQL preserves the same clock invariant.

This makes the behavior testable without mocking provider translation and without adding a production
`SELECT now()` solely to expose the database clock to application code.

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
Npgsql translates `DateTime.UtcNow` to `now()`, which is fixed at transaction start. Generic EF claims
execute as individual update commands, while native multi-claim transactions use the explicit
statement-time snapshot above. Provider translation must be proven by integration tests rather than
inferred from LINQ behavior.

Pending PR #658 applies the native rule to direct and fallback time-job claims, existing and newly
inserted cron occurrences, cron fallback claims, descendant stamps, and cron lease refresh. Root claims
return the database-issued clock snapshot alongside their IDs, allowing the existing descendant-stamp
command to reuse the identical value without a clock query or additional round trip.

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

### Messaging applies the same ownership-clock rule

The Messaging PostgreSQL and SQL Server stores apply this rule in their atomic retry pickup paths:

- `src/Headless.Messaging.Storage.PostgreSql/PostgreSqlDataStorage.cs`
- `src/Headless.Messaging.Storage.SqlServer/SqlServerDataStorage.cs`

The implementation:

1. Pass `DispatchTimeout` as a duration rather than binding an absolute `@NewLease`.
2. Snapshot PostgreSQL or SQL Server time once inside the atomic retry-claim command.
3. Use that snapshot for both `LockedUntil <= now` and the new `LockedUntil` value.
4. Return the persisted deadline from `RETURNING`/`OUTPUT` so the in-memory message model matches durable
   state without another read.
5. Uses real-provider tests with a fast application clock; the in-memory provider remains on
   `TimeProvider`.

Dead-owner recovery uses the same provider snapshot when it shortens a live lease to database time.
Otherwise an application-clock fast-forward followed immediately by a database-clock pickup can leave
the row temporarily ineligible or skip the reclaim entirely under host skew.

The same rule now governs fresh dispatch as well as persisted retry pickup. The public `IDataStorage`
lease methods accept a duration rather than an application-computed deadline. On success, storage copies
the persisted `LockedUntil` and `Owner` values back to the supplied message so attempt reservation and
terminal or retry transitions reuse the exact lease identity as their fence. PostgreSQL and SQL Server
take one provider-clock snapshot per atomic lease command; InMemoryStorage uses its injected
`TimeProvider` as the coherent single-process authority.

This removes client-clock skew from relational lease acquisition and expiry decisions. It does not
change the at-least-once boundary: a genuinely expired `DispatchTimeout` makes the row eligible for a
successor, and a process paused beyond that lease can resume already-running transport or user code while
the successor is in flight. Durable fencing can reject the stale process's later state write, but it
cannot cancel work that has already left storage; exactly-once delivery and process-pause fencing are not
promised.

## Examples

Avoid binding both eligibility and the new deadline from the application clock:

```sql
WHERE locked_until IS NULL OR locked_until <= @applicationNow
SET locked_until = @applicationNowPlusLease
```

Prefer one provider-native atomic statement:

```sql
-- PostgreSQL shape
WITH claim_clock AS MATERIALIZED (
    SELECT clock_timestamp() AS now
), candidates AS (
    SELECT id
    FROM jobs, claim_clock
    WHERE locked_until IS NULL OR locked_until <= claim_clock.now
    FOR UPDATE SKIP LOCKED
)
UPDATE jobs AS job
SET owner_id = @owner,
    locked_until = claim_clock.now + (@leaseSeconds * INTERVAL '1 second'),
    updated_at = claim_clock.now
FROM candidates, claim_clock
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
- [PostgreSQL date/time functions](https://www.postgresql.org/docs/current/functions-datetime.html) —
  `CURRENT_TIMESTAMP`/`now()` use transaction-start time; `clock_timestamp()` returns wall-clock time.
- [SQL Server `SYSUTCDATETIME`](https://learn.microsoft.com/sql/t-sql/functions/sysutcdatetime-transact-sql)
  — UTC `datetime2` server time usable inside Transact-SQL expressions.
- [Npgsql EF translations](https://www.npgsql.org/efcore/mapping/translations.html) — confirms
  `DateTime.UtcNow` translates to PostgreSQL `now()`.
- [EF Core SQL Server function mappings](https://learn.microsoft.com/ef/core/providers/sql-server/functions)
  — confirms server translation for `DateTime.UtcNow` and `AddSeconds`.
