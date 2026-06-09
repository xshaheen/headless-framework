# Headless.Jobs.EntityFramework

Entity Framework Core integration for Jobs, a high-performance background job scheduler for .NET.

This package enables persistence of time-based and cron-based jobs using EF Core, allowing for robust tracking, retry logic, and job state management. This is the durable operational-store path; it requires a `Headless.Coordination` provider for node identity and dead-node recovery.

---

## 📦 Installation

```bash
dotnet add package Headless.Jobs.EntityFramework
```

## Quick Start

The durable operational store requires a coordination provider. Register `AddHeadlessCoordination(...)` BEFORE `AddHeadlessJobs(...)`:

```csharp
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;

var conn = builder.Configuration.GetConnectionString("DefaultConnection");

// 1. Coordination provider FIRST -- supplies node@incarnation identity + NodeLeft recovery.
builder.Services.AddHeadlessCoordination(c => c.UseSqlServer(conn));

// 2. Jobs durable operational store.
builder.Services
    .AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler => scheduler.SchedulerTimeZone = TimeZoneInfo.Utc);
    })
    .AddOperationalStore(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
    });
```

Without a registered coordination provider, the durable path throws `InvalidOperationException` naming `AddHeadlessCoordination`.

### Node Identity and Recovery Model

- **Owner identity is `node@incarnation`** (store-allocated incarnation), not the machine name. Each durable job row is stamped with the current node's `node@incarnation` owner.
- **Recovery is event-driven and backend-neutral.** Dead-node reclaim is triggered by Coordination `NodeLeft` events plus a periodic liveness-snapshot reconcile, so it works on the EF/Postgres path WITHOUT Redis. Reclaim matches the dead `node@incarnation` exactly and never touches a restarted node's freshly-stamped rows or unowned-idle rows.
- **Recovery latency trade-off.** On the no-Redis path, fast-restart recovery is TTL-bounded: a predecessor incarnation is reclaimed only after its heartbeat expires and `NodeLeft` fires (previously the machine-name self-reclaim was immediate). Tune via the Coordination heartbeat/TTL and `DeadNodeReconcileInterval`.
- **Fail-stop on membership loss.** If the local node loses membership, the durable scheduler stops processing rather than stamping a stale owner.

### Cron-Expression Caching

Jobs cron-expression caching uses the host application's optional default `Headless.Caching.ICache`. Register a cache provider such as `Headless.Caching.InMemory`, `Headless.Caching.Redis`, or `Headless.Caching.Hybrid` before or alongside Jobs:

```csharp
builder.Services.AddRedisCache(redis => redis.ConnectionString = "localhost:6379");

builder.Services
    .AddHeadlessJobs()
    .AddOperationalStore(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
    });
```

When no `ICache` is registered, cron-expression reads fall through to the database and invalidation after cron-job writes is skipped. Cache read/write/remove failures are fail-open for Jobs; caller cancellation still propagates.

## Configuration

```csharp
builder.Services
    .AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler =>
        {
            // How often the durable path reconciles dead nodes from the liveness snapshot
            // to catch any NodeLeft signal missed while not subscribed. Default: 1 minute.
            scheduler.DeadNodeReconcileInterval = TimeSpan.FromMinutes(1);
        });
    })
    .AddOperationalStore(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
    });
```

## Side Effects

- Persists time-based and cron-based jobs in EF Core-mapped tables
- Stamps the `node@incarnation` owner on durable job rows
- Uses the optional default `Headless.Caching.ICache` for cron-expression caching when one is registered
- Registers the membership-recovery bridge (NodeLeft + periodic reconcile) and a registration-before-start gate (scheduler processing starts only after coordination registration completes)
- Requires a registered `Headless.Coordination` provider; fails fast at startup otherwise

## Error Handling and Retry Persistence

With EF enabled, Jobs persists retry and failure state across restarts:

- `Retries`, `RetryIntervals`, `RetryCount`
- Final status (`Failed`, `Cancelled`, `Skipped`, `Done`)
- Exception details (`ExceptionMessage`) and skip reason (`SkippedReason`)
- Execution timing (`ExecutionTime`, `ExecutedAt`, `ElapsedTime`)

This allows reliable post-mortem analysis and dashboard visibility for failed or unstable jobs.
