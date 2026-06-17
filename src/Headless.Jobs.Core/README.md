# Headless.Jobs.Core

Core implementation of Jobs, a high-performance background job scheduler for .NET with cron expressions and time-based scheduling.

## Problem Solved

Provides reliable, distributed background job scheduling with cron expressions, delayed execution, custom task scheduling, and real-time monitoring without external dependencies like Hangfire or Quartz.

## Key Features

- **Cron Scheduling**: Full cron expression support with timezone handling
- **Time-Based Jobs**: Schedule jobs at specific times or intervals
- **Custom Thread Pool**: Optimized task scheduler for background jobs
- **Persistence**: In-memory or database-backed job storage
- **Fallback**: Automatic recovery and retry for failed jobs
- **Zero Allocations**: High-performance execution with minimal GC pressure
- **Hot Reload**: Dynamic job registration and configuration updates

## Installation

```bash
dotnet add package Headless.Jobs.Core
```

## Quick Start

```csharp
// Register Jobs
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    });
});

// Define cron job
[Jobs("*/5 * * * *")] // Every 5 minutes
public static class CleanupJob
{
    public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<CleanupJob>>();
        logger.LogInformation("Running cleanup job");

        await Task.CompletedTask;
    }
}

// Schedule time-based job programmatically
public sealed class OrderService(ITimeJobManager<TimeJobEntity> job)
{
    public async Task SendReminderAsync(string orderId, CancellationToken ct)
    {
        await job.AddAsync(new TimeJobEntity
        {
            Function = "SendOrderReminder",
            Description = $"order-reminder-{orderId}",
            ExecutionTime = DateTime.UtcNow.AddHours(24),
            Request = JobsHelper.SerializeRequest(new { OrderId = orderId })
        }, ct);
    }
}
```

## Configuration

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.IdleWorkerTimeOut = TimeSpan.FromMinutes(5);
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });

    // Exception handling
    options.SetExceptionHandler<CustomJobExceptionHandler>();

    // Disable background services (for testing)
    options.DisableBackgroundServices();

    // Start modes
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.StartMode = JobsStartMode.Immediate; // default
        scheduler.StartMode = JobsStartMode.Manual;    // wait for manual trigger
    });
});
```

### Node Identity (durable path)

On the in-memory single-process path, the scheduler uses `SchedulerOptionsBuilder.NodeId` (defaults to the machine name) as the row owner. On the durable operational-store path (`Headless.Jobs.EntityFramework` + `AddHeadlessCoordination`), `SchedulerOptionsBuilder.NodeId` is **not** the owner: node identity is `node@incarnation` (store-allocated by Coordination) and durable job rows are stamped with it. Node identity for that path — including K8s pod-collision handling (via `POD_NAME`/`POD_NAMESPACE`) — is configured on `Headless.Coordination`, not on this option; `SchedulerOptionsBuilder.NodeId` only acts as a display fallback before membership registration completes. If the local node loses membership, the durable scheduler fail-stops (stops processing) rather than stamping a stale owner. See `Headless.Jobs.EntityFramework` for setup.

### Pickup Lease and Node-Death Policy

Every claim of a time-job or cron-occurrence row stamps a pickup lease: `LockedUntil = now + SchedulerOptionsBuilder.LeaseDuration` (default 5 minutes). The lease deadline is written and compared using the injected application `TimeProvider`, never the database server clock — this mirrors `Headless.Messaging` so the InMemory and SQL providers share identical pickup semantics and a fake clock in tests stays honest.

The lease is a **duplicate-suppression / self-heal floor, not the liveness authority.** A dead node's rows are recovered by Coordination's incarnation + heartbeat sweep (see `Headless.Jobs.EntityFramework`); lease expiry only lets a *stalled but not-yet-declared-dead* `Idle`/`Queued` row be re-claimed. Contract: `LeaseDuration` must exceed the longest expected job runtime. If it is shorter, a still-running job's lease can expire and — for `OnNodeDeath = Retry` jobs only — be speculatively re-claimed and run a second time.

`NodeDeathPolicy` (the `OnNodeDeath` property on `TimeJobEntity` / `CronJobOccurrenceEntity`) decides what happens to a row whose owning node dies mid-execution:

| Policy | On node death | Use when |
|--------|---------------|----------|
| `Retry` (default) | Row is released for re-claim; the attempt counts toward the retry budget. | Job is idempotent — safe to run again. |
| `MarkFailed` | Row transitions to terminal `Failed`; never re-run. | A second run is wrong; surface the failure. |
| `Skip` | Row transitions to terminal `Skipped`; never re-run. | Idempotency-critical jobs that must run at most once. |

Safety interaction: the claim predicate's lease-expiry arm is gated on `OnNodeDeath == Retry`. Clock skew can therefore never speculatively re-run a `Skip` or `MarkFailed` job — #315's per-job policy and the #268 lease deadline are the same safety mechanism viewed from two angles. The unowned arm (never leased) and the self-owned arm (crash-recovery re-pickup of your own row) are unaffected by the policy.

### Cron-Expression Caching (durable path)

Jobs does not ship a Jobs-specific Redis cache package. The durable EF path uses the host application's optional default `Headless.Caching.ICache` for cron-expression caching. Register `Headless.Caching.InMemory`, `Headless.Caching.Redis`, or `Headless.Caching.Hybrid` when caching is desired; without an `ICache`, Jobs reads cron expressions from the database and cache invalidation is skipped.

### Distributed Lock Hardening (optional, off by default)

Two startup/recovery operations are safe to run on every node but redundant when many nodes run them at once:

- **Cron-seed migration** (`jobs.cron-seed-migration`) — every node, on boot, scans code-defined cron jobs and upserts them. On a rolling deploy of N nodes that is N full scans + N write storms.
- **Dead-node resource reclaim** (`jobs.dead-node-sweep`) — `NodeLeft` events and the periodic liveness reconcile can both fire on every survivor; each runs the full reclaim batch.

Both are already correct without a lock — the cron upsert/constraints and the exact-owner reclaim predicates make repeated runs idempotent (a second run touches zero rows). The optional Jobs-scoped distributed lock only removes the *redundant cross-node work and contention*; it is **never** the correctness boundary for job-row ownership (per-row predicates, `node@incarnation` ownership, and per-job leases stay that boundary).

Wire it by passing any `Headless.DistributedLocks.IDistributedLock` — the same provider you already use elsewhere works:

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>());
    // or: options.UseDistributedLock(myLockInstance);
});
```

Semantics:

- **Off by default.** Without `UseDistributedLock`, both operations run on every node, unchanged. A `NullDistributedLock` fallback is always registered so the guard sites resolve cleanly.
- **Skip-on-contention.** Each guard tries to acquire once with no wait (`AcquireTimeout = TimeSpan.Zero`). If another node holds the lock — or acquisition faults — the node skips the work (debug-logged) and another node carries it. Cancellation during acquisition propagates rather than skipping.
- **Self-healing TTL.** The lease has a generous finite TTL with `Monitor` mode, so a holder that dies mid-operation releases via expiry; the next boot or reconcile tick re-runs.
- **Jobs-isolated, keyed registration.** The provider is kept under a Jobs-private keyed-DI slot, so it never conflicts with an unrelated app-level `IDistributedLock`. The two resource names are stable and Jobs-specific.

Not guarded: **cron-occurrence creation** is intentionally left without a `jobs.cron-occurrence-creation` lock — occurrences carry deterministic ids and are created via an id-keyed upsert, so storage-level dedup is already the correctness boundary and a coarse lock would add nothing. This mirrors the `Headless.Messaging` `UseDistributedLock` retry-pickup pattern.

### Retry Configuration

```csharp
await timeJobs.AddAsync(new TimeJobEntity
{
    Function = "ProcessPayment",
    Description = "payment-processing",
    ExecutionTime = DateTime.UtcNow,
    Request = JobsHelper.SerializeRequest(new { PaymentId = "pay_123" }),
    Retries = 3,
    RetryIntervals = [30, 60, 120],
}, cancellationToken);
```

- Retries are automatic when job execution throws.
- Status remains `InProgress` during retries.
- After retries are exhausted, final status becomes `Failed`.
- If `RetryIntervals` is null/empty, default interval is 30 seconds.
- If fewer intervals are provided than retries, the last interval is reused.

### Global Exception Handler

```csharp
public sealed class CustomJobExceptionHandler(ILogger<CustomJobExceptionHandler> logger)
    : IJobExceptionHandler
{
    public Task HandleExceptionAsync(Exception exception, Guid jobId, JobType jobType)
    {
        logger.LogError(exception, "Job {JobId} ({JobType}) failed", jobId, jobType);
        return Task.CompletedTask;
    }

    public Task HandleCanceledExceptionAsync(Exception exception, Guid jobId, JobType jobType)
    {
        logger.LogWarning("Job {JobId} ({JobType}) cancelled", jobId, jobType);
        return Task.CompletedTask;
    }
}
```

Register it with:

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.SetExceptionHandler<CustomJobExceptionHandler>();
});
```

### Job-Level Controls

- Throw to trigger retry.
- Catch and return to stop retry for permanent failures.
- Use `context.RetryCount` from `JobFunctionContext` for attempt-aware behavior.
- Call `context.RequestCancellation()` to mark as `Cancelled`.
- Call `context.CronOccurrenceOperations.SkipIfAlreadyRunning()` for overlap-safe cron jobs.

### TerminateExecutionException

Use `TerminateExecutionException` to stop execution without retries and optionally set final status:

```csharp
throw new TerminateExecutionException(JobStatus.Failed, "Configuration is invalid for this job");
```

- `TerminateExecutionException("message")` -> `Skipped`
- `TerminateExecutionException(JobStatus status, "message")` -> explicit status
- Overloads with `innerException` keep details for diagnostics

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.DistributedLocks.Abstractions` — optional distributed-lock hardening (off by default)
- `Headless.Extensions`

## Side Effects

- Starts background hosted services for job scheduling and execution
- Creates in-memory job storage (or database tables with persistence providers)
- Runs custom thread pool for job execution
- Periodically scans for due jobs and executes them
