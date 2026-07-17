# Headless.Jobs.Core

Core implementation of the Jobs scheduler: in-memory persistence provider, execution task handler, background services, bounded task scheduler, and the `AddHeadlessJobs` DI extension.

## Problem Solved

Provides reliable background job scheduling with cron expressions, delayed execution, custom task scheduling, retry logic, and bounded in-process execution without any external job scheduler dependencies (Hangfire, Quartz, etc.). The in-memory path works standalone; the durable path composes with `Headless.Jobs.EntityFramework`.

## Key Features

- **`AddHeadlessJobs()`**: single DI entry point; registers managers, background services, and the in-memory persistence provider.
- **`IJobScheduler` facade**: schedules typed or requestless `[JobFunction]` methods through generated descriptor indexes, maps supported options, and returns persisted entity IDs.
- **Scheduler background service**: polls for due time jobs and cron occurrences on `FallbackIntervalChecker` cadence (default 30s); also driven by soft-notification signals for near-zero latency.
- **Bounded task scheduler** (`JobsTaskScheduler`): runs normal jobs as logical worker slots on the shared .NET thread pool, bounds active async executions by `MaxConcurrency` (default `Environment.ProcessorCount`), and honors `High` → `Normal` → `Low` dequeue order. Only `LongRunning` work receives a dedicated thread.
- **Sliding lease renewal** (#316): jobs verify ownership immediately before user code starts, then extend `LockedUntil` on `LeaseRenewalInterval` cadence; cancel-on-loss if renewal affects zero rows or errors.
- **`DisableBackgroundServices()`**: suppresses background execution; only the managers are registered (useful for enqueue-only nodes and test projects).
- **Seeder API**: `UseJobsSeeder(...)` for startup data seeding; `IgnoreSeedDefinedCronJobs()` to skip auto-seeding of attribute-defined cron jobs.
- **GZip request payloads**: `UseGZipCompression()` compresses serialized request bytes.
- **Exception handler**: `SetExceptionHandler<THandler>()` registers an `IJobExceptionHandler` singleton.
- **Node-death policy enforcement**: claim predicate gates lease-expiry re-claim on `OnNodeDeath == Retry`; clock skew cannot re-run `Skip` or `MarkFailed` jobs.
- **Startup mode**: `SchedulerOptionsBuilder.StartMode` (`JobsStartMode.Immediate` default / `JobsStartMode.Manual`).

## Design Notes

The in-memory provider uses the injected `TimeProvider` for pickup leases. The EF operational store translates `DateTime.UtcNow` inside each claim statement, so both lease-expiry comparison and `LockedUntil` stamping use the **database clock** without a separate clock query. EF renewal and reclaim use the same authority, preventing application/database clock skew from shortening or extending the initial lease.

`SchedulerOptionsBuilder.NodeId` is used as the row owner only on the in-memory single-process path. On the durable path it is overridden by `JobsOwnerIdentityAdapter` (reads `node@incarnation` from `Headless.Coordination`); `NodeId` becomes a pre-registration display fallback only.

Jobs remain `Queued` while waiting for worker and per-function concurrency capacity. The worker performs the owned `Queued` → `InProgress` write immediately before execution, then the execution handler performs one more lease check before invoking user code. If ownership expired while queued, the worker skips the delegate instead of starting an unowned job. Because that transition must happen at admission time, each admitted job issues its own single-row claim write — a tick with N co-due functions performs N claim round trips instead of one batched write; this is the deliberate cost of the single-winner fence.

Claiming a chained time job leases its direct children and grandchildren to the same owner while leaving their status `Idle`; each child transitions to `InProgress` only when its `RunCondition` is satisfied. Reclaimed time jobs and cron occurrences preserve `RetryCount`, so execution resumes from the persisted attempt instead of resetting the retry budget.

Only cancellation tied to the job's cancellation token (including `context.RequestCancellation()`) is classified as `Cancelled`. A detected lease loss writes no terminal status — the row stays `InProgress` so the stalled-reclaim sweep recovers it per `OnNodeDeath`. An unrelated `OperationCanceledException` is handled as a failure and follows the configured retry policy.

Cron expressions are evaluated in `SchedulerTimeZone`. A spring-forward occurrence inside an invalid local-time gap is shifted forward by the gap; an ambiguous fall-back occurrence runs once at the later UTC instant (the standard-time offset).

Jobs uses reusable Polly.Core `ResiliencePipeline` instances for runtime retry execution. `JobsRetryOptions.RetryStrategy` is the public Polly configuration surface, while `Retries`, `RetryCount`, and `RetryIntervals` remain the durable authority. `RetryCount` is persisted before every wait; lease renewal stays active across attempts and delays, and a lost lease cancels the pipeline and fences terminal writes. Per-row `RetryIntervals` override Polly delay generation and reuse their last value when shorter than `Retries`. Polly configuration and delegates are never serialized.

There is no `app.UseJobs()` call — the scheduler starts automatically through the `IHostedService` registrations added by `AddHeadlessJobs`.

## Installation

```bash
dotnet add package Headless.Jobs.Core
```

## Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Polly;
using Polly.Retry;

// 1. Register Jobs
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });
    options.SetExceptionHandler<MyJobExceptionHandler>();
    options.ConfigureRetries(retry =>
    {
        retry.RetryStrategy.ShouldHandle = args =>
            ValueTask.FromResult(args.Outcome.Exception is HttpRequestException);
        retry.RetryStrategy.Delay = TimeSpan.FromSeconds(30);
        retry.RetryStrategy.BackoffType = DelayBackoffType.Exponential;
        retry.RetryStrategy.UseJitter = true;
        retry.RetryStrategy.MaxDelay = TimeSpan.FromMinutes(5);
    });
});

// 2. Define a cron job (requires Headless.Jobs.SourceGenerator)
[JobFunction("Cleanup", cronExpression: "*/5 * * * *")]
public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
{
    sp.GetRequiredService<ILogger<Program>>().LogInformation("Running cleanup");
    await Task.CompletedTask;
}

// 3. Define a time job with DI
[JobFunction("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
        => await orders.ProcessAsync(context.Request, ct);
}

// 4. Schedule by typed request; no function string or entity construction is needed.
public sealed class OrderService(IJobScheduler scheduler)
{
    public Task<Guid> ScheduleAsync(OrderRequest request, CancellationToken ct) =>
        scheduler.EnqueueAsync(
            request,
            new EnqueueOptions
            {
                Description = "process-order",
                Retries = 3,
                RetryIntervals = [30, 60, 120],
            },
            ct
        );
}

// The scheduler starts via IHostedService — no app.UseJobs() call needed.
```

`IJobScheduler` is the safe routine path: typed calls resolve `typeof(TArgs)`, use the configured Jobs serializer and optional GZip compression, and persist through the configured managers. Requestless calls accept a generated `JobFunctionDescriptor` from `JobFunctionProvider.JobFunctionDescriptors`. Immediate, delayed, and recurring methods return the persisted time-job or cron-definition `Guid`. Unknown or stale identities fail before serialization or persistence.

```csharp
var delayedId = await scheduler.ScheduleAsync(request, DateTime.UtcNow.AddHours(1), cancellationToken: ct);
var recurringId = await scheduler.ScheduleRecurringAsync(request, "0 0 * * *", cancellationToken: ct);

var cleanup = JobFunctionProvider.JobFunctionDescriptors["Cleanup"];
var cleanupId = await scheduler.EnqueueAsync(cleanup, cancellationToken: ct);
```

`EnqueueOptions` and `RecurringJobOptions` expose only description, durable retries/intervals, and node-death policy. Execution time and cron expression are explicit method arguments. Priority remains immutable `[JobFunction]` / descriptor metadata.

Low-level managers are not deprecated. Continue using `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` for CRUD, batching, seeding, custom entities, chains, and advanced persistence workflows.

Facade calls still flow through those managers, so scheduling inside an established `Headless.CommitCoordination` scope preserves the same atomic row write and deferred post-commit side effects.

## Configuration

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeId = "my-node"; // in-memory path only
        scheduler.MaxConcurrency = 10; // default: processor count
        scheduler.IdleWorkerTimeOut = TimeSpan.FromMinutes(1); // default: 1 min
        scheduler.LeaseDuration = TimeSpan.FromMinutes(5); // default: 5 min
        scheduler.LeaseRenewalInterval = null; // null → LeaseDuration / 3
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(30); // default: 30s
        scheduler.PostCommitDrainTimeout = TimeSpan.FromSeconds(30); // default: 30s; > 0, max: 5 min
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc; // default: local
        scheduler.DeadNodeReconcileInterval = TimeSpan.FromMinutes(1); // durable path; default: 1 min
        scheduler.StartMode = JobsStartMode.Immediate; // or Manual
    });

    options.SetExceptionHandler<MyJobExceptionHandler>();
    options.DisableBackgroundServices(); // test / enqueue-only nodes
    options.UseGZipCompression(); // compress request payloads
    options.IgnoreSeedDefinedCronJobs(); // skip auto-seeding of attribute cron jobs
    options.UseJobsSeeder(async manager => // startup time-job seeder
    {
        await manager.AddAsync(new TimeJobEntity { Function = "Init", ExecutionTime = DateTime.UtcNow });
    });
    options.ConfigureRequestJsonOptions(json =>
    {
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
});
```

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Coordination.Abstractions`
- `Headless.Coordination.Core`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Extensions`
- `NCrontab.Signed`
- `Polly.Core`

## Side Effects

- Registers `ITimeJobManager<TimeJobEntity>` and `ICronJobManager<CronJobEntity>` as singletons.
- Registers the non-generic `IJobScheduler` facade against the same configured time/cron entity pair as the managers.
- Registers background hosted services: `JobsInitializationHostedService` (always), `JobsSchedulerBackgroundService`, `JobsFallbackBackgroundService`, and `JobsExecutionTaskHandler` (unless `DisableBackgroundServices()` is called).
- Registers `JobsTaskScheduler` (shared-thread-pool logical workers bounded by active async `MaxConcurrency`; dedicated threads only for `LongRunning`).
- Registers a scheduler-scoped cron schedule cache and sets `JobsHelper` JSON/compression settings.
