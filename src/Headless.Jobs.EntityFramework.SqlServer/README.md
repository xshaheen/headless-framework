# Headless.Jobs.EntityFramework.SqlServer

## Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with SQL Server-native atomic claim-and-output operations under scheduler contention.

This is an optimization extension for `Headless.Jobs.EntityFramework`, not an independent Jobs persistence provider. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns SQL Server-specific claim execution, including SQL, parameters, and locking behavior.

## Key Features

- Selects claim candidates with `UPDLOCK`, `READPAST`, and `ROWLOCK`, then returns winners from the same update through `OUTPUT inserted...`.
- Adds `READCOMMITTEDLOCK` when `READ_COMMITTED_SNAPSHOT` is enabled, as required for `READPAST` under read-committed snapshot isolation.
- Creates cron occurrences atomically against the unique execution-time and cron-job key.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.

## Design Notes

`READPAST` skips row locks, not page locks. Page locking or lock escalation can therefore block competing claimers even with `ROWLOCK`, which is a preference rather than a guarantee. The package does not change `LOCK_ESCALATION`; operators should measure contention, lock memory, and workload behavior before applying database-level changes. SQL Server 2019 or later and Azure SQL are the supported targets.

## Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.SqlServer
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
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(connectionString));
        ef.UseSqlServerClaims();
    });
```

## Configuration

`UseSqlServerClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback. The strategy detects `READ_COMMITTED_SNAPSHOT` and adjusts its locking hints.

## Dependencies

- `Headless.Jobs.EntityFramework`
- `Microsoft.EntityFrameworkCore.SqlServer`

## Side Effects

- Replaces the default Jobs EF claim strategy with the SQL Server atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change lock-escalation settings, scheduler cadence, leases, retry policy, or the public persistence contract.
