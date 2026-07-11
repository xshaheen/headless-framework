# Headless.Jobs.EntityFramework.PostgreSql

## Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with PostgreSQL-native atomic claim-and-return operations under scheduler contention.

This is an optimization extension for `Headless.Jobs.EntityFramework`, not an independent Jobs persistence provider. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns PostgreSQL-specific claim execution, including SQL, parameters, and locking behavior.

## Key Features

- Claims existing time jobs and cron occurrences with `UPDATE ... RETURNING` over a `FOR UPDATE SKIP LOCKED` candidate query.
- Creates cron occurrences with `INSERT ... ON CONFLICT DO NOTHING ... RETURNING` to deduplicate each execution-time and cron-job pair.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.

## Design Notes

`SKIP LOCKED` lets concurrent workers move past candidates locked by another claim transaction. The update, descendant stamping, and returned winners share one explicit transaction, so a rolled-back claim exposes no executable work. PostgreSQL 14 or later is the supported baseline; the underlying primitive exists on older releases, but they are outside this package's tested support target.

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
