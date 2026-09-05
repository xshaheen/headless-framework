# Headless.Jobs.EntityFramework.PostgreSql

## Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with PostgreSQL-native atomic claim-and-return operations under scheduler contention.

This is an optimization extension for `Headless.Jobs.EntityFramework`, not an independent Jobs persistence provider. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns PostgreSQL-specific claim execution, including SQL, parameters, and locking behavior.

## Key Features

- **Ordinal contract storage**: Jobs function/version columns use PostgreSQL `C` collation. Physical `varchar(200)`/`varchar(100)` limits count Unicode code points; the shared runtime contract counts UTF-16 units, so supplementary characters require the stricter migration preflight. Native creation snapshots name, version, and request together under the definition lock; existing claims hydrate the stored occurrence tuple.
- Claims existing time jobs and cron occurrences with `UPDATE ... RETURNING` over a `FOR UPDATE SKIP LOCKED` candidate query.
- Bounds set-based root and fallback-occurrence selection to 100 winners per transaction; skipped or excess work remains eligible for the next scheduler pass.
- Creates cron occurrences with `INSERT ... WHERE NOT EXISTS ... ON CONFLICT DO NOTHING ... RETURNING` to deduplicate each execution-time and cron-job pair. The `NOT EXISTS` guard is the shared occupied-instant rule: any row that **accounts for** the instant — live, terminal, or a status this binary does not recognize — suppresses the insert, and the only row that does not account is one a startup seeding migration retired without a replacement (`CronOccurrenceDisposition.ReplacementOwed`). `ON CONFLICT` remains, arbitrating the concurrent-live race the unlocked read cannot see. The predicate and its literals are derived from `CronOccurrenceAccounting`, so this SQL cannot drift from the SQL Server sibling or the portable EF path.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.
- Declares UUIDv7 as the GUID ordering for every PostgreSQL-backed Jobs row, so `UsePostgreSqlClaims()` fixes row-id ordering for the whole EF store rather than for the claim strategy alone.

## Design Notes

`SKIP LOCKED` lets concurrent workers move past candidates locked by another claim transaction. The update, descendant stamping, and returned winners share one explicit transaction, so a rolled-back claim exposes no executable work. PostgreSQL 14 or later is the supported baseline; the underlying primitive exists on older releases, but they are outside this package's tested support target.

PostgreSQL compares `uuid` in plain byte order, so UUIDv7's leading timestamp keeps index inserts at the right edge — the same ordering as the framework-wide unkeyed default, which is why generic EF on PostgreSQL loses nothing by not installing this package. The value is declared once here and consumed both by the claim strategy (keyed injection) and by the shared occurrence-materialization path (through the option builder), so no EF write path can drift onto a different generator.

## Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.PostgreSql
```

## Quick Start

```csharp
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;

builder
    .Services.AddHeadlessJobs()
    .UseEntityFramework(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseNpgsql(connectionString));
        ef.UsePostgreSqlClaims();
    });
```

## Configuration

`UsePostgreSqlClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback.

## Dependencies

- `Headless.Jobs.EntityFramework`
- `Npgsql.EntityFrameworkCore.PostgreSQL`

## Side Effects

- Replaces the default Jobs EF claim strategy with the PostgreSQL atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change scheduler cadence, leases, retry policy, or the public persistence contract.
