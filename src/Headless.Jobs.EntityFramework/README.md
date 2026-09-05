# Headless.Jobs.EntityFramework

Entity Framework Core persistence provider for `Headless.Jobs` — durable, distributed, multi-node job storage with database-clock lease authority.

## Problem Solved

Provides persistence of time jobs and cron occurrences across restarts and across multiple nodes, using EF Core-mapped tables. Integrates with `Headless.Coordination` for distributed node identity (`node@incarnation`), dead-node recovery, and fail-stop on membership loss.

## Key Features

- **Durable contract tuples**: time jobs and cron definitions map required bounded `Function`/`ContractVersion` columns; occurrences additionally persist their own function, version, request bytes, correlation, causation, and nullable tenant. Newly materialized occurrences copy the current definition tuple while holding its write lock; retries and restart reads use the occurrence row. Runtime write converters reject invalid identities.
- **Consumer-owned contract migration**: apply the versioned-contract schema before starting upgraded workers or definition writers. Backfill legacy versions to `"1"` and occurrence tuples from available parent rows while both are quiesced; previously overwritten historical payloads cannot be recovered. Abort oversized/invalid legacy values instead of truncating, remove temporary defaults, disallow mixed old/new binaries, and reject downgrade after incompatible versions are written. Library mappings never mutate the schema automatically.
- **Durable storage**: persists `TimeJobEntity`, `CronJobEntity`, and `CronJobOccurrenceEntity` in EF Core-mapped tables (default schema: `jobs`).
- **`UseEntityFramework(ef => …)`**: the EF registration extension on `JobsOptionsBuilder`.
- **`UseJobsDbContext<TDbContext>(dbOptions, schema?)`**: registers a dedicated `JobsDbContext` with configurable schema.
- **`UseApplicationDbContext<TDbContext>(ConfigurationType)`**: shares an existing application `DbContext` instead of a dedicated one.
- **Database-clock lease authority**: lease renewal comparisons use the database server clock (`now()`/`GETUTCDATE()`), not the node's `TimeProvider`. Cross-node clock skew cannot reclaim a healthy renewing job.
- **Atomic cron materialization**: one transaction locks the expected schedule position, recognizes or inserts the exact unclaimed `Idle` occurrence, and advances the watermark only with that durable outcome. Claiming and database-clock lease stamping happen afterward.
- **Atomic chain claims**: a root time-job claim leases its non-timed descendants down to the configured chain depth (`SchedulerOptionsBuilder.MaxChainDepth`, default 10) to the same owner — atomically via a recursive CTE on the native PostgreSQL / SQL Server providers, and via a sequenced frontier walk on the EF CAS fallback where each descendant copies the root's exact lease deadline, a partial claim is pruned to the set actually claimed, and an unexecuted claimed root is recovered by the stalled-lease sweep. Fallback recovery uses the same tree claim and never steals a live queued lease.
- **Portable CAS fallback**: the base package keeps the EF select-and-compare-and-swap claim strategy when no native
  claim provider is installed, ordered by execution time and ID and capped at 100 candidates per recovery sweep.
- **Storage-reduced cron graphs**: the dashboard projection reads distinct UTC date keys, then groups status counts
  inside the selected inclusive range without loading occurrence entities or the `CronJob` navigation.
- **Backend-keyed row identity**: the installed native claim package declares its backend's GUID ordering once, and every EF write path resolves that keyed `IGuidGenerator` — the native strategy, the CAS half of the compatible pair, and the shared occurrence-materialization path alike. Generic EF (no backend package) registers no key and keeps the unkeyed Version 7 default.
- **Store-clock schedule seeding**: creating a cron definition at runtime positions it in the same transaction, anchored on the store's current-statement clock. Registered for PostgreSQL and SQL Server; other EF backends throw `NotSupportedException` on that path.
- **Durable retry state**: root jobs, descendants, and cron occurrences retain their persisted `RetryCount` when projected for execution.
- **Node identity and recovery**: stamps `node@incarnation` as the row owner; dead-node reclaim driven by `NodeLeft` events plus periodic reconcile (`DeadNodeReconcileInterval`).
- **Fail-fast coordination check**: startup throws `InvalidOperationException` when no coordination provider is registered.
- **Cron-expression caching**: reuses the host's `ICache` (optional). No `ICache` → reads from DB, cache invalidation is skipped. Cache failures are fail-open.
- **DbContext pool**: configurable via `SetDbContextPoolSize(n)` (default 1024).
- **Custom schema**: `SetSchema("custom_schema")` or the `schema` parameter on `UseJobsDbContext`.

## Design Notes

Lease acquisition, renewal, and reclaim on the EF path use the **database clock** (`now()` on PostgreSQL, `GETUTCDATE()` on SQL Server), not the node's injected `TimeProvider`. Claims translate `DateTime.UtcNow` inside the existing update statement, so lease comparison and stamping share one authority without a separate scalar clock query. In-memory has no database server and continues to use `TimeProvider`. Do not write EF tests that expect a fake `TimeProvider` to control lease deadlines.

Seeding a cron definition's schedule position is the one write that cannot use that translated clock. It runs inside a transaction — the caller's own on the coordinated path — and PostgreSQL resolves the translated `DateTime.UtcNow` to `now()`, which is frozen at transaction start, so an ambient transaction opened minutes earlier would position a definition before it existed. The seed reads the **current statement** clock (`clock_timestamp()` / `SYSUTCDATETIME()`) on the inserting connection instead. Backend detection is by EF provider name rather than by which Headless backend package is installed, because generic EF (CAS claiming, no backend package) runs against those same two databases and needs the same anchor. A backend with no known statement-clock function throws `NotSupportedException` rather than seeding from a transaction-start clock — deliberately loud, because a false anchor manufactures an immediate backlog for that definition's missed-run policy to resolve, and there is no portable substitute. `ICronJobManager.AddAsync` / `AddBatchAsync` is the affected path; the unseeded `InsertCronJobsAsync(jobs, ct)` overload still works there for callers that position their own rows, and attribute-seeded definitions are anchored by the activation gate instead.

The scheduler's due-work peek (`GetEarliestTimeJobsAsync`) runs both of its reads through the context's execution strategy, so a SQL Server deadlock victim (1205) on the candidate read is retried when the application configured `EnableRetryOnFailure`. This deliberately honors whatever strategy the consumer configured instead of adding an always-on retry: it is a pass-through under EF's default non-retrying strategy, which is the right trade for a pure read whose failure costs one delayed poll. The claim path keeps its own deadlock pipeline, because a deadlock there is correctness-relevant rather than a missed poll.

The occurrence table carries the persisted `Disposition` column that `CronOccurrenceAccounting` reads as the sole input to the occupied-instant rule. Its migration backfills existing rows to `Accounted`, and its `Down` refuses while any non-`Accounted` value exists — dropping the column would collapse an owed replacement fire into a permanently suppressed one.

Cron materialization uses a read-committed transaction whose first statement is the fenced definition update. That write lock is the per-definition mutex held through occurrence-key arbitration and commit, so concurrent nodes converge on one occurrence without serializable-transaction aborts. Quiesce old scheduler binaries before migration because only providers implementing the new SPI participate in this mutex.

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

### Consumer-managed Jobs models

`UseApplicationDbContext<TContext>(ConfigurationType.IgnoreModelCustomizer)` preserves the application's model ownership. Keyed operations require explicit ordinal collations on the time-job `Function`, `TenantId`, and `BusinessKey` columns: PostgreSQL `C` or SQL Server `Latin1_General_100_BIN2`. Pass that value as `contractCollation` to `TimeJobConfigurations<TTimeJob>` in `OnModelCreating`, or configure the matching model-default collation. Missing or different configuration rejects keyed scheduling and cancellation; ordinary unkeyed operations remain available. The provider never changes the consumer schema. See the [keyed scheduling migration guide](../../docs/migrations/jobs-keyed-scheduling.md) for the complete configuration example and rollout requirements.

Ordinary adds and updates also reject a child whose persisted parent reference targets any retained keyed generation, including inputs materialized or rebound through consumer EF APIs and coordinated writes. The row and parent checks share transaction-owned run locks with keyed insertion and replacement.

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
