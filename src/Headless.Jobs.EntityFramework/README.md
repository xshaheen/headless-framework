# Headless.Jobs.EntityFramework

Entity Framework Core persistence provider for `Headless.Jobs` — durable, distributed, multi-node job storage with database-clock lease authority.

## Problem Solved

Provides persistence of time jobs and cron occurrences across restarts and across multiple nodes, using EF Core-mapped tables. Integrates with `Headless.Coordination` for distributed node identity (`node@incarnation`), dead-node recovery, and fail-stop on membership loss.

## Key Features

- **Durable storage**: persists `TimeJobEntity`, `CronJobEntity`, and `CronJobOccurrenceEntity` in EF Core-mapped tables (default schema: `jobs`).
- **`UseEntityFramework(ef => …)`**: the EF registration extension on `JobsOptionsBuilder`.
- **`UseJobsDbContext<TDbContext>(dbOptions, schema?)`**: registers a dedicated `JobsDbContext` with configurable schema.
- **`UseApplicationDbContext<TDbContext>(ConfigurationType)`**: shares an existing application `DbContext` instead of a dedicated one.
- **Database-clock lease authority**: lease renewal comparisons use the database server clock (`now()`/`GETUTCDATE()`), not the node's `TimeProvider`. Cross-node clock skew cannot reclaim a healthy renewing job.
- **Atomic chain claims**: a root time-job claim leases its non-timed descendants down to the configured chain depth (`SchedulerOptionsBuilder.MaxChainDepth`, default 10) to the same owner — atomically via a recursive CTE on the native PostgreSQL / SQL Server providers, and via a sequenced frontier walk on the EF CAS fallback where each descendant copies the root's exact lease deadline, a partial claim is pruned to the set actually claimed, and an unexecuted claimed root is recovered by the stalled-lease sweep. Fallback recovery uses the same tree claim and never steals a live queued lease.
- **Portable CAS fallback**: the base package keeps the EF select-and-compare-and-swap claim strategy when no native
  claim provider is installed, ordered by execution time and ID and capped at 100 candidates per recovery sweep.
- **Storage-reduced cron graphs**: the dashboard projection reads distinct UTC date keys, then groups status counts
  inside the selected inclusive range without loading occurrence entities or the `CronJob` navigation.
- **Durable retry state**: root jobs, descendants, and cron occurrences retain their persisted `RetryCount` when projected for execution.
- **Node identity and recovery**: stamps `node@incarnation` as the row owner; dead-node reclaim driven by `NodeLeft` events plus periodic reconcile (`DeadNodeReconcileInterval`).
- **Fail-fast coordination check**: startup throws `InvalidOperationException` when no coordination provider is registered.
- **Cron-expression caching**: reuses the host's `ICache` (optional). No `ICache` → reads from DB, cache invalidation is skipped. Cache failures are fail-open.
- **DbContext pool**: configurable via `SetDbContextPoolSize(n)` (default 1024).
- **Custom schema**: `SetSchema("custom_schema")` or the `schema` parameter on `UseJobsDbContext`.

## Design Notes

Lease acquisition, renewal, and reclaim on the EF path use the **database clock** (`now()` on PostgreSQL, `GETUTCDATE()` on SQL Server), not the node's injected `TimeProvider`. Claims translate `DateTime.UtcNow` inside the existing update statement, so lease comparison and stamping share one authority without a separate scalar clock query. In-memory has no database server and continues to use `TimeProvider`. Do not write EF tests that expect a fake `TimeProvider` to control lease deadlines.

The `JobsDbContext<TTimeJob, TCronJob>` constructor must be `public` for the EF pool to resolve it at startup. Validation fails fast at DI build time.

Install `Headless.Jobs.EntityFramework.PostgreSql` or `Headless.Jobs.EntityFramework.SqlServer` and select it inside the same `UseEntityFramework` builder to replace the CAS pickup path with a provider-native atomic claim-and-return operation. The scheduler and persistence contract remain database-agnostic. Register exactly one native claim provider; selecting both fails during registration.

These packages are EF optimization extensions, not standalone persistence providers. The base package owns the full persistence contract plus provider-neutral mapping definitions and claim-transaction lifecycle primitives; each extension owns provider-specific claim execution, including SQL, parameters, and locking semantics.

Dashboard graph selection intentionally remains history-derived. The EF provider first projects only distinct UTC
occurrence dates to reproduce the existing date-window choice, then issues a second filtered `GROUP BY` query for
date/status counts. This keeps the graph's sparse-date and zero-fill behavior unchanged while making transferred rows
proportional to distinct dates and the selected window rather than lifetime occurrence history.

## Installation

```bash
dotnet add package Headless.Jobs.EntityFramework
```

## Quick Start

```csharp
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;

var conn = builder.Configuration.GetConnectionString("DefaultConnection");

// 1. Register Coordination FIRST (supplies node@incarnation identity + NodeLeft recovery)
builder.Services.AddHeadlessCoordination(c => c.UseSqlServer(conn));

// 2. Register Jobs with the durable operational store
builder
    .Services.AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler => scheduler.SchedulerTimeZone = TimeZoneInfo.Utc);
    })
    .UseEntityFramework(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
        ef.UseSqlServerClaims(); // requires Headless.Jobs.EntityFramework.SqlServer
    });

// Optional: cron-expression caching via ICache
builder.Services.AddHeadlessCaching(setup =>
    setup.UseRedis(o => o.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379"))
);
```

Without a registered coordination provider the durable path throws at startup.

## Configuration

```csharp
builder
    .Services.AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler =>
        {
            // How often the durable path reconciles dead nodes to catch missed NodeLeft signals.
            scheduler.DeadNodeReconcileInterval = TimeSpan.FromMinutes(1); // default: 1 min
        });
    })
    .UseEntityFramework(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
        ef.UseSqlServerClaims();
        ef.SetDbContextPoolSize(512); // default: 1024
        ef.SetSchema("background"); // default: "jobs"
    });
```

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Coordination.Abstractions`
- `Microsoft.EntityFrameworkCore`

## Side Effects

- Replaces the in-memory `IJobPersistenceProvider` with `JobsEFCorePersistenceProvider`.
- Registers `JobsOwnerIdentityAdapter` (overrides the default `DefaultJobsOwnerIdentity`).
- Registers `JobsDeadOwnerReclaimer`, `DeadOwnerRecoveryBridge`, and `JobsCoordinationStartupGate` hosted services.
- Persists job rows in EF Core-mapped tables under the configured schema.
- Uses the portable optimistic-CAS claim path unless one native provider package configures `UsePostgreSqlClaims()` or `UseSqlServerClaims()`.
- Consumes the optional default `ICache` for cron-expression caching.
- Fails fast at startup if no coordination provider is registered.
