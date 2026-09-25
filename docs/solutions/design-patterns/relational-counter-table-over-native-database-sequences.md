---
title: "Tenant-scoped counters use a relational table row, not native database SEQUENCE objects"
date: 2026-09-25
category: design-patterns
module: Headless.Sequences
problem_type: design_pattern
component: database
severity: high
applies_when:
  - "Considering native SEQUENCE objects (nextval()/NEXT VALUE FOR) for Headless.Sequences counters"
  - "A hot fast-mode counter needs higher throughput than the current row-lock upsert provides"
  - "Adding a new Sequences provider or extending ReserveAsync's consecutive-range contract"
  - "Comparing Sequences' storage design against Headless.DistributedLocks fencing tokens, which do use native sequences"
related_components:
  - "Headless.Sequences.Core"
  - "Headless.Sequences.PostgreSql"
  - "Headless.Sequences.SqlServer"
  - "Headless.DistributedLocks.PostgreSql"
  - "Headless.DistributedLocks.SqlServer"
tags:
  - sequences
  - native-sequence
  - gap-free
  - tenant-scoped
  - counters
  - postgresql
  - sql-server
  - unit-of-work
---
# Tenant-scoped counters are table rows, not native SEQUENCE objects

## Context

`Headless.Sequences` hands out numbers per `(tenant_id, name, partition)` key in two modes. Fast mode (`ISequenceGenerator`) commits each increment on its own connection. Gap-free mode (`unit.Sequences`) writes the increment into the caller's unit-of-work transaction. The obvious alternative is one native `SEQUENCE` object per key, which is what the fencing-token sources in `Headless.DistributedLocks.*` already use. This document records why the counters use one table and one upsert instead, and when to reconsider.

## Guidance

**Decision:** each counter is one row in one table with primary key `(tenant_id, name, partition)`. A single statement both creates the row and advances it, and returns the new value. Do not replace the table with per-key native sequences.

- **Table DDL.** The PostgreSQL table uses `COLLATE "C"` key columns with `PRIMARY KEY (tenant_id, name, partition)` (`src/Headless.Sequences.PostgreSql/PostgreSqlSequencesStorageInitializer.cs:71-83`). The SQL Server table uses a `PRIMARY KEY CLUSTERED` over the same three columns (`src/Headless.Sequences.SqlServer/SqlServerSequencesStorageInitializer.cs:70-82`). The clustered key is load-bearing because the increment's `HOLDLOCK` range lock sits on it (`:48-50`).
- **PostgreSQL increment.** One `INSERT … ON CONFLICT (tenant_id, name, partition) DO UPDATE SET value = t.value + @Delta … RETURNING value` (`src/Headless.Sequences.PostgreSql/PostgreSqlSequenceStore.cs:176-191`). When two first calls on a new key race, the loser waits on the winner's unique-index entry and then takes the update branch, so no retry is needed (`:18-20`).
- **SQL Server increment.** One batch: `UPDATE … WITH (UPDLOCK, HOLDLOCK) … OUTPUT … INTO @allocated`, then `IF NOT EXISTS … INSERT`, then one trailing `SELECT` (`src/Headless.Sequences.SqlServer/SqlServerSequenceStore.cs:203-227`). The batch has no `TRY/CATCH`, because a caught duplicate-key error dooms an `XACT_ABORT ON` caller transaction (`:25-26`).
- **Fast path.** `IncrementAsync` opens its own `READ COMMITTED` transaction, commits it, and retries only on a deadlock, up to 3 attempts (PG `:31-68`, SQL Server `:38-73`).
- **Gap-free path.** `UnitOfWorkSequencesFeature.NextAsync` refuses fast-mode names, requires a live `DbTransaction`, validates that the unit runs on the counters' database, and then calls `IncrementEnlistedAsync` on the unit's own connection and transaction (`src/Headless.Sequences.Core/UnitOfWorkSequencesFeature.cs:29-57`). The enlisted call never retries, because a deadlock has already rolled back the caller's transaction (PG `PostgreSqlSequenceStore.cs:104-107`).
- **Range reserve.** `ReserveAsync(count)` is one increment with `delta = count * step`. On a new row it inserts `start + (count - 1) * step`, and it returns `SequenceRange(last - span, count, step)` (`src/Headless.Sequences.Core/SequenceGenerator.cs:36-42`). The whole block comes from one atomic statement, which is what backs the "Atomically takes `count` consecutive values" contract (`src/Headless.Sequences.Abstractions/ISequenceGenerator.cs:32`).
- **Start and step are policy, not schema.** `SequencePolicy.Start` and `Step` travel as statement parameters (`UnitOfWorkSequencesFeature.cs:56`, `SequenceGenerator.cs:19`), so changing a registration needs no DDL.

**Contrast: where native sequences are right.** Both fencing-token sources use one global native sequence. PostgreSQL uses `SELECT nextval(...)` with a lazy `CREATE SEQUENCE IF NOT EXISTS` behind an advisory lock (`src/Headless.DistributedLocks.PostgreSql/PostgresFencingTokenSource.cs:93`, `:120-143`). SQL Server uses `SELECT NEXT VALUE FOR …` (`src/Headless.DistributedLocks.SqlServer/SqlServerFencingTokenSource.cs:87`), and its storage initializer runs `CREATE SEQUENCE … AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE` (`src/Headless.DistributedLocks.SqlServer/SqlServerDistributedLocksStorageInitializer.cs:110`). That use has one fixed key, no transactional requirement, and gaps are harmless, because a fencing token only has to increase strictly.

## Why This Matters

1. **Native sequences are non-transactional, so they cannot back gap-free mode.** The PostgreSQL docs say: "the value obtained by `nextval` is not reclaimed for re-use if the calling transaction later aborts … PostgreSQL sequence objects cannot be used to obtain 'gapless' sequences" ([Sequence Manipulation Functions](https://www.postgresql.org/docs/current/functions-sequence.html)). The SQL Server docs say: "Sequence numbers are generated outside the scope of the current transaction. They're consumed whether the transaction using the sequence number is committed or rolled back" ([Sequence Numbers](https://learn.microsoft.com/en-us/sql/relational-databases/sequence-numbers/sequence-numbers)). Gap-free mode depends on a rollback undoing the increment, and that holds only when the increment is an ordinary row write inside the caller's transaction (`UnitOfWorkSequencesFeature.cs:9-10`).
2. **Keys are dynamic.** Every new tenant, name, or partition would need a runtime `CREATE SEQUENCE`. The costs:
   - In gap-free mode that DDL would run inside the caller's transaction.
   - The schema would grow one object per key.
   - Racing first creations would need a lock-and-absorb dance like the one the fencing source uses for its single object (`PostgresFencingTokenSource.cs:103-164`).
   - Every start or step policy change would need an `ALTER SEQUENCE`.

   With a table, a new key is an insert, and policy is a parameter.
3. **PostgreSQL has no atomic consecutive-range reserve.** The only functions are `nextval`, `setval`, `currval`, and `lastval`. Calling `nextval` N times interleaves with concurrent callers, so the block is not consecutive. SQL Server has `sp_sequence_get_range` ("can retrieve several numbers in the sequence at once", same Microsoft page). Native sequences would therefore make `ReserveAsync`'s consecutive-range contract behave differently per provider. The row upsert gives both providers the same semantics in one statement.
4. **One storage model serves both modes and both providers.** Fast and gap-free differ only in whose transaction runs the same `_ExecuteAsync` (PG `PostgreSqlSequenceStore.cs:52` vs `:106`), so there is one table, one DDL, and one conformance surface.

**Trade-off accepted.** A row serializes the writers of one counter. In fast mode the lock is held only for the call's own short transaction. In gap-free mode it is held until the unit commits (PG `PostgreSqlSequenceStore.cs:19-20`). On SQL Server, the range lock on a new key's gap also briefly blocks first use of neighbouring new keys (`SqlServerSequenceStore.cs:19-22`). Native sequences avoid this lock and add `CACHE`, but they give up points 1-3.

## When to Apply

- Adding a provider (for example MySQL or SQLite): implement `ISequenceStore` as a keyed row plus one atomic upsert-and-return, not a sequence object.
- Someone proposes native sequences "for performance". Before they do, use the escalation below.
- **Escalation for a hot fast-mode counter:** add hi/lo block caching in Core rather than switching to native sequences. Reserve a block with `ReserveAsync(blockSize)` and hand the values out in memory:
  - About `blockSize` times fewer round trips.
  - No DDL.
  - Values arrive out of order across processes.
  - A crash leaves the unused part of the block as a gap.

  That is acceptable only in fast mode. Never cache gap-free numbers.
- Revisit native sequences only if one fast counter needs thousands of values per second across many processes and hi/lo is proven insufficient.
- Native sequences are the right tool when there is one global counter, gaps do not matter, and no transaction has to own the increment. The fencing tokens are the example.

## Examples

Rejected per-key native design (PostgreSQL):

```sql
-- On first use of each (tenant, name, partition): DDL at runtime, inside the caller's tx in gap-free mode
CREATE SEQUENCE IF NOT EXISTS seq_t42_invoice_2026;
SELECT nextval('seq_t42_invoice_2026');   -- survives ROLLBACK -> gap
-- Reserve 10: ten nextval calls, interleaved with other callers -> not consecutive
```

Chosen design, one statement for both modes (`PostgreSqlSequenceStore.cs:176-191`):

```sql
-- Simplified: the real statement schema-qualifies the table and quotes the columns.
INSERT INTO sequences AS t (tenant_id, name, "partition", value, created_at, updated_at)
VALUES (@TenantId, @Name, @Partition, @InsertValue, clock_timestamp(), clock_timestamp())
ON CONFLICT (tenant_id, name, "partition")
DO UPDATE SET value = t.value + @Delta, updated_at = clock_timestamp()
RETURNING value;
-- NextAsync:    InsertValue = start,                    Delta = step
-- ReserveAsync: InsertValue = start + (count-1)*step,   Delta = count*step  -> one consecutive block
```

## Related

- `docs/llms/sequences.md`, the consumer contract for fast vs gap-free modes
- `docs/llms/unit-of-work.md`, which covers how the unit's transaction and `PreventRetry` apply to enlisted writes (`UnitOfWorkSequencesFeature.cs:45-53`)
- PostgreSQL: [Sequence Manipulation Functions](https://www.postgresql.org/docs/current/functions-sequence.html)
- SQL Server: [Sequence Numbers](https://learn.microsoft.com/en-us/sql/relational-databases/sequence-numbers/sequence-numbers), [sp_sequence_get_range](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-sequence-get-range-transact-sql)
- `docs/solutions/database-issues/sqlserver-trailing-space-padding-collapses-distinct-keys.md` explains why key parts that start or end with whitespace are refused, since SQL Server would otherwise merge them into one counter row
