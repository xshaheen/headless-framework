---
domain: Jobs (Background Jobs)
packages: Jobs.Abstractions, Jobs.Core, Jobs.Dashboard, Jobs.SourceGenerator, Jobs.OpenTelemetry, Jobs.EntityFramework
---

# Jobs (Background Jobs)

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Error Handling and Retries](#error-handling-and-retries)
  - [Retry Configuration](#retry-configuration)
  - [Global Exception Handler](#global-exception-handler)
  - [Job-Level Error Handling](#job-level-error-handling)
  - [TerminateExecutionException and Status Control](#terminateexecutionexception-and-status-control)
  - [Cron Occurrence Skipping](#cron-occurrence-skipping)
  - [Job Status After Errors](#job-status-after-errors)
  - [Node-Death Policy (OnNodeDeath)](#node-death-policy-onnodedeath)
  - [Best Practices](#best-practices)
- [Headless.Jobs.Abstractions](#headlessjobsabstractions)
  - [Installation](#installation)
  - [Commit Coordination (Atomic Enqueue)](#commit-coordination-atomic-enqueue)
- [Headless.Jobs.Core](#headlessjobscore)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Jobs.Dashboard](#headlessjobsdashboard)
  - [Installation](#installation-2)
  - [Minimal Setup](#minimal-setup)
  - [🚀 Quick Examples](#-quick-examples)
    - [No Authentication (Public Dashboard)](#no-authentication-public-dashboard)
    - [Basic Authentication](#basic-authentication)
    - [API Key Authentication](#api-key-authentication)
    - [Use Host Application's Authentication](#use-host-applications-authentication)
    - [Use Host Authentication with Custom Policy](#use-host-authentication-with-custom-policy)
  - [🔧 Fluent API Methods](#-fluent-api-methods)
  - [Live Nodes View](#live-nodes-view)
  - [🔒 How It Works](#-how-it-works)
  - [🌐 Frontend Integration](#-frontend-integration)
- [Headless.Jobs.SourceGenerator](#headlessjobssourcegenerator)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Jobs.OpenTelemetry](#headlessjobsopentelemetry)
  - [Features](#features)
  - [Installation](#installation-4)
  - [Usage](#usage)
    - [Basic Setup](#basic-setup)
    - [With Jaeger](#with-jaeger)
    - [With Application Insights](#with-application-insights)
  - [Trace Structure](#trace-structure)
    - [Job Execution Activities](#job-execution-activities)
    - [Tags Added to Activities](#tags-added-to-activities)
  - [Logging Output](#logging-output)
  - [Integration with Logging Frameworks](#integration-with-logging-frameworks)
    - [Serilog](#serilog)
    - [NLog](#nlog)
  - [Performance Impact](#performance-impact)
  - [Requirements](#requirements)
- [Headless.Jobs.EntityFramework](#headlessjobsentityframework)
  - [📦 Installation](#-installation)
  - [Quick Start](#quick-start-2)
    - [Node Identity and Recovery Model](#node-identity-and-recovery-model)
    - [Cron-Expression Caching](#cron-expression-caching)
  - [Configuration](#configuration-2)
  - [Side Effects](#side-effects-2)

> High-performance background job scheduler for .NET with cron expressions, time-based scheduling, source-generated registration, and distributed coordination.

## Quick Orientation

Minimum setup: `Jobs.Core` + `Jobs.EntityFramework` (for persistence) + `Jobs.SourceGenerator` (for compile-time job registration).

Additional packages:
- `Jobs.Dashboard` -- monitoring UI with authentication (basic, API key, host auth) plus a live-nodes cluster view
- `Jobs.OpenTelemetry` -- distributed tracing and structured logging
- `Jobs.Abstractions` -- interfaces only (pulled in transitively by Core)

Clustering: the durable operational-store path (`AddOperationalStore`) requires a `Headless.Coordination` provider. Register it with `AddHeadlessCoordination(...)` BEFORE `AddHeadlessJobs(...)`. Node identity on this path is `node@incarnation` (store-allocated), and dead-node recovery is driven by Coordination `NodeLeft` events plus a periodic reconcile -- backend-neutral, no Redis required. The in-memory single-process path (no `AddOperationalStore`) needs no coordination.

Cron-expression caching: Jobs reads the optional default `ICache` registered by the host application. Register `Headless.Caching.InMemory`, `Headless.Caching.Redis`, or `Headless.Caching.Hybrid` when cron-expression caching is desired. Without an `ICache`, Jobs reads cron expressions from the durable store and cache invalidation is a no-op.

Wiring:
```csharp
builder.Services.AddJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });
});
app.UseJobs();
```

Mark job classes with `[Jobs("cron-expression")]` and add the SourceGenerator package for zero-reflection auto-discovery.

## Agent Instructions

- Do NOT use Hangfire or Quartz -- use Headless.Jobs (Jobs) for all background jobs in this framework.
- Mark job methods with `[Jobs("cron-expression")]` attribute. Add `Headless.Jobs.SourceGenerator` to the project for compile-time job registration (eliminates reflection).
- Use `Jobs.EntityFramework` for job persistence. Without it, jobs are in-memory only and lost on restart.
- For the durable operational store, register a coordination provider with `AddHeadlessCoordination(c => c.UsePostgreSql(conn))` (or another provider) BEFORE `AddHeadlessJobs(o => o.AddOperationalStore(...))`. Without it, the durable path throws `InvalidOperationException` naming `AddHeadlessCoordination`. The in-memory path (no `AddOperationalStore`) needs no coordination.
- On the durable path, node identity is `node@incarnation` (store-allocated by Coordination), not the machine name, and `SchedulerOptionsBuilder.NodeId` is not used as the owner there (it is only a pre-registration display fallback). Node identity for the durable path — including K8s pod-collision handling via `POD_NAME`/`POD_NAMESPACE` — is owned by `Headless.Coordination`. On the in-memory single-process path, the row owner comes from `SchedulerOptionsBuilder.NodeId` (defaults to the machine name).
- Running jobs slide their pickup lease forward on the `SchedulerOptionsBuilder.LeaseRenewalInterval` cadence (#316; defaults to ≈ `LeaseDuration / 3`, must be positive and `< LeaseDuration` if set), so `LeaseDuration` (default 5 min) **no longer needs to exceed the longest job runtime** — only the `Idle`/`Queued` claim→start window does (keep `LeaseDuration` ≥ `FallbackIntervalChecker`). A running job that stops renewing (crashed/wedged) is reclaimed per its `OnNodeDeath` policy within ≈ one `LeaseDuration`. Still set `OnNodeDeath = MarkFailed`/`Skip` on non-idempotent jobs so neither the claim predicate nor the stalled-job reclaim ever re-runs them.
- Do NOT install a Jobs-specific Redis cache package. Jobs cron-expression caching uses the host application's default `Headless.Caching.ICache`; pick `Headless.Caching.InMemory`, `Headless.Caching.Redis`, or `Headless.Caching.Hybrid` based on deployment needs.
- No-Redis dead-node recovery is TTL-bounded, not immediate: a predecessor incarnation is reclaimed only after its heartbeat expires and `NodeLeft` fires (plus a periodic reconcile backstop). Tune via the Coordination heartbeat/TTL and `SchedulerOptionsBuilder.DeadNodeReconcileInterval`.
- Call `app.UseJobs()` after `builder.Build()` to start the scheduler. Use `JobsStartMode.Manual` if you need delayed startup.
- For time-based (one-off) jobs, inject `ITimeJobManager<TimeJobEntity>` and call `AddAsync()`.
- To enqueue a job atomically with a domain write (and/or an outbox publish), run them inside a relational commit-coordinated scope (`Headless.CommitCoordination`, e.g. `db.ExecuteCoordinatedTransactionAsync(...)`). `AddAsync` then writes the row inside the caller's transaction and defers dispatch/scheduler/notify/cron-cache-invalidation to post-commit. This needs **two** registrations on the durable path: `AddHeadlessCoordination(...)` for the operational store (the `Headless.Coordination` distributed-lock subsystem) **and** a commit-coordination provider — `services.AddPostgreSqlCommitCoordination()` or `AddSqlServerCommitCoordination()` (the separate `Headless.CommitCoordination` subsystem) — to activate the coordinated scope. Similar names, different systems; register both. Capture is synchronous (pre-await): the coordinator scope is an `AsyncLocal` captured when `AddAsync` is entered, so never put `AddAsync` behind an intermediate `async` method that `await`s first — the captured scope is lost and the enqueue silently falls back to direct insert that auto-commits even if the outer transaction rolls back. Keep coordinated enqueues in one scope sequential — the scope's single DB connection/transaction is not thread-safe. On the coordinated path `AddAsync` / `AddBatchAsync` (time **and** cron) return the persisted entity and **throw** on any failure — wrap the call in `try/catch`. A thrown failure rolls the caller's transaction back rather than committing without the job row, which is the whole point; a returned entity means the row was enlisted (it commits with the transaction), not that dispatch ran (the fallback poll sweep — `FallbackIntervalChecker`, default 30s — recovers a post-commit dispatch failure). Failures that throw: validation (`JobValidatorException` — its `Errors` lists every failure for a batch), a relational transaction offered but dead/completed, or a relational coordinator active but the provider can't write coordinated (`InvalidOperationException`); a coordinated scope with no relational capability falls back to direct insert. `Update`/`Delete` keep returning `JobResult` — only the transaction-enlisting Add path throws. Requires the EF operational store (`AddOperationalStore`) whose `DbContext` must expose a `public MyContext(DbContextOptions<MyContext> options)` constructor; the in-memory path is always direct.
- For cron jobs, the `[Jobs]` attribute takes a cron expression and optional `Priority` parameter (`JobPriority.High`, `Normal`, `LongRunning`).
- Use `[JobsConstructor]` attribute on constructors when you need custom DI injection in job classes.
- Dashboard authentication: call `dashboard.WithBasicAuth()`, `WithApiKey()`, or `WithHostAuthentication()` inside `config.AddDashboard()`.
- OpenTelemetry: call `.AddOpenTelemetryInstrumentation()` on the Jobs builder and add `"Jobs"` as a source to your tracing config.
- Exception handling: register a custom handler via `options.SetExceptionHandler<CustomJobExceptionHandler>()`.
- For testing, call `options.DisableBackgroundServices()` to prevent the scheduler from running.

### Retry Configuration

Configure retries when creating jobs:

```csharp
await timeJobs.AddAsync(new TimeJobEntity
{
    Function = "ProcessPayment",
    Description = "Process payment for order #A-1024",
    ExecutionTime = DateTime.UtcNow,
    Request = JobsHelper.SerializeRequest(new { PaymentId = "pay_123" }),
    Retries = 3,                           // Total retry attempts after first failure
    RetryIntervals = [30, 60, 120],        // Seconds between retries
}, cancellationToken);
```

Retry behavior:
- Retries run automatically when a job throws an exception.
- Job status stays `InProgress` while retrying.
- After retries are exhausted, status becomes `Failed`.
- `JobFunctionContext.RetryCount` tracks the current attempt.

Retry interval strategies:
- Fixed delay: `RetryIntervals = [60, 60, 60]`
- Exponential backoff: `RetryIntervals = [1, 2, 4, 8, 16, 32]`
- Progressive backoff: `RetryIntervals = [30, 60, 300, 900, 3600]`
- Immediate retry: `RetryIntervals = [0, 0, 0]`

Default behavior:
- If `RetryIntervals` is null/empty, Jobs defaults to `30` seconds.
- If `RetryIntervals` is shorter than `Retries`, the last interval is reused.

### Global Exception Handler

Implement `IJobExceptionHandler`:

```csharp
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;

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

Register it:

```csharp
builder.Services.AddJobs(options =>
{
    options.SetExceptionHandler<CustomJobExceptionHandler>();
});
```

### Job-Level Error Handling

Use `try/catch` in job methods and choose whether to retry:

```csharp
[Jobs("ProcessOrder")]
public sealed class ProcessOrderJob(ILogger<ProcessOrderJob> logger)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
    {
        try
        {
            await ProcessOrderAsync(context.Request, ct);
        }
        catch (HttpRequestException ex) when (context.RetryCount < 3)
        {
            logger.LogWarning(ex, "Transient failure. Attempt {Attempt}", context.RetryCount + 1);
            throw; // Retry
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Permanent validation failure. No retry.");
            return; // Complete without retry
        }
    }

    private static Task ProcessOrderAsync(OrderRequest request, CancellationToken ct) => Task.CompletedTask;
}
```

### TerminateExecutionException and Status Control

Use `TerminateExecutionException` to stop execution without retries and optionally set final status:

```csharp
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;

if (!IsConfigurationValid())
{
    throw new TerminateExecutionException(
        JobStatus.Failed,
        "Configuration is invalid for this job"
    );
}
```

Available patterns:
- `new TerminateExecutionException("message")` -> `Skipped`
- `new TerminateExecutionException(JobStatus status, "message")` -> explicit status
- `new TerminateExecutionException("message", innerException)` -> `Skipped` + inner exception details
- `new TerminateExecutionException(JobStatus status, "message", innerException)` -> explicit status + inner details

### Cron Occurrence Skipping

Prevent overlapping cron runs:

```csharp
[Jobs("0 * * * *")]
public sealed class LongRunningCronJob
{
    public async Task ExecuteAsync(JobFunctionContext context, CancellationToken ct)
    {
        context.CronOccurrenceOperations.SkipIfAlreadyRunning();
        await RunLongTaskAsync(ct);
    }

    private static Task RunLongTaskAsync(CancellationToken ct) => Task.CompletedTask;
}
```

### Job Status After Errors

- `Succeeded`: job completed without error (the terminal-success status; retry-success retains `DueDone`).
- `Failed`: retries exhausted or unhandled exception.
- `Cancelled`: cancellation requested via token or `context.RequestCancellation()`.
- `Skipped`: terminated explicitly (`TerminateExecutionException`) or overlapping cron occurrence skipped.

### Node-Death Policy (OnNodeDeath)

When the node that owns an in-flight job row dies, the per-job `NodeDeathPolicy` decides the row's fate (default `Retry`):

| Policy | On node death | Use when |
|--------|---------------|----------|
| `Retry` (default) | Row is released for re-claim; the attempt counts toward the retry budget. | Job is idempotent — safe to run again. |
| `MarkFailed` | Row transitions to terminal `Failed` (lease cleared, owner retained for audit, `ExceptionMessage` set); never re-run. | A second run is wrong; surface the failure. |
| `Skip` | Row transitions to terminal `Skipped` (lease cleared, owner retained); never re-run. | Idempotency-critical jobs that must run at most once. |

Select the policy with `SetOnNodeDeath(NodeDeathPolicy)` on the fluent job builder (available on the parent, child, and grandchild builders), or by assigning the `OnNodeDeath` property directly on the `TimeJobEntity` you pass to `ITimeJobManager.AddAsync`. For cron jobs, set `OnNodeDeath` on the `CronJobEntity` you register with `ICronJobManager.AddAsync` — it propagates to every generated occurrence.

```csharp
FluentChainJobBuilder<TimeJobEntity>
    .BeginWith(p => p.SetFunction("charge-card").SetOnNodeDeath(NodeDeathPolicy.MarkFailed));

// or directly
await cronJobManager.AddAsync(new CronJobEntity { Function = "nightly-report", Expression = "0 2 * * *", OnNodeDeath = NodeDeathPolicy.Skip });
```

This couples with the pickup lease (`SchedulerOptionsBuilder.LeaseDuration`, default 5 min). Every claim stamps `LockedUntil = now + LeaseDuration`, written and compared using the injected application `TimeProvider`, not the DB server clock (mirrors `Headless.Messaging` for InMemory↔SQL parity). The lease is a **duplicate-suppression / self-heal floor, not the liveness authority** — a dead node's rows are recovered by Coordination's incarnation + heartbeat sweep; lease expiry only lets a *stalled but not-yet-declared-dead* `Idle`/`Queued` row be re-claimed.

The claim predicate's lease-expiry arm is gated on `OnNodeDeath == Retry`, so clock skew can never speculatively re-run a `Skip` or `MarkFailed` job. The unowned and self-owned (crash-recovery) claim arms are unaffected by the policy.

**Sliding lease for running jobs (#316).** A running job renews its lease on the `SchedulerOptionsBuilder.LeaseRenewalInterval` cadence (≈ `LeaseDuration / 3` by default; an explicit value must be positive and `< LeaseDuration`, validated at startup). Each renewal slides `LockedUntil` forward, fenced on still-owned + **running** (`InProgress`) status; a renewal affecting **zero rows** means the lease was lost, and a renewal that errors or cannot complete within the renewal cadence (a hung or unreachable store, #463) is treated the same — in either case the worker cancels that job's `CancellationToken` (cancel-on-loss, best-effort). On EF storage the lease deadline compares against the **database clock** (`now()`/`GETUTCDATE()`), not any one node's `TimeProvider`, so cross-node clock skew cannot reclaim a healthy renewing job. Consequences:

- **`LeaseDuration` no longer needs to exceed the longest job runtime** — a healthy long job keeps renewing. It sizes only the `Idle`/`Queued` claim→start window and the stalled-job recovery latency.
- A job stuck `InProgress` whose lease lapses (stopped renewing) is reclaimed per its `OnNodeDeath` policy on the fallback cadence — `Retry` → released to `Idle`, `MarkFailed` → `Failed`, `Skip` → `Skipped` — **independent of node death**, closing the gap where a job wedged on a still-heartbeating node was recovered by neither the claim predicate nor the dead-node sweep.
- The dead-node sweep now **defers `InProgress` rows to the lease**: a busy node's still-leased running jobs survive a membership blip and are recovered only once their lease lapses; `Idle`/`Queued` rows are still reclaimed immediately on node death.

### Best Practices

- Treat transient and permanent failures differently.
- Use retry intervals that match dependency behavior.
- Log `FunctionName`, `Id`, and `RetryCount` for every failure.
- Monitor failed/cancelled/skipped jobs through Dashboard and OpenTelemetry.

---
# Headless.Jobs.Abstractions

Simple utilities for queuing and executing cron/time-based jobs in the background.

---

## Installation
[Headless.Jobs.Abstractions.csproj](Headless.Jobs.Abstractions.csproj)
```bash
dotnet add package Headless.Jobs.Abstractions
```

## Commit Coordination (Atomic Enqueue)

When a relational commit coordinator is active (see `Headless.CommitCoordination`), `ITimeJobManager.AddAsync` /
`AddBatchAsync` and `ICronJobManager.AddAsync` / `AddBatchAsync` write the job row **inside the caller's ambient
transaction** and defer dispatch / scheduler-restart / notify / cron-cache-invalidation to post-commit — mirroring the
messaging outbox. A domain write, an integration-event publish, and a job enqueue can commit (or roll back) as one unit:

```csharp
// db is a relational DbContext; services is the request scope; both are enlisted by the helper.
await db.ExecuteCoordinatedTransactionAsync(async (ctx, ct) =>
{
    ctx.Orders.Add(order);                                       // domain write
    await ctx.SaveChangesAsync(ct);

    await outboxBus.PublishAsync(new OrderPlaced(order.Id), ct); // message publish (outbox)

    await timeJobManager.AddAsync(new TimeJobEntity              // job enqueue, written into the same transaction
    {
        Function = "SendOrderReminder",
        ExecutionTime = DateTime.UtcNow.AddHours(24),
        Request = JobsHelper.SerializeRequest(new { order.Id }),
    }, ct);
}, services, ct);
// On commit: order row + outbox message + job row all persist; the job's dispatch/scheduler/notify fire post-commit.
// On rollback: none persist and no dispatch occurs.
```

**Required DI registration**: atomic enqueue activates only when a `Headless.CommitCoordination` provider is registered — `services.AddPostgreSqlCommitCoordination()` or `services.AddSqlServerCommitCoordination()` (core seam: `AddCommitCoordination()`). This is a **different subsystem** from `AddHeadlessCoordination(...)`, the `Headless.Coordination` distributed-lock / node-membership provider required by the durable operational store (`AddOperationalStore`). The durable atomic-enqueue path needs **both**: `AddHeadlessCoordination(...)` for the operational store, **and** a `Add{Provider}CommitCoordination()` for the commit-coordination scope. The similar names name two distinct systems — register both.

**Capture is synchronous (pre-await)**: the ambient commit-coordinator scope is an `AsyncLocal` captured at the point `AddAsync` is entered. Do **not** wrap `AddAsync` behind an intermediate `async` method that `await`s before reaching `AddAsync` — any `await` before that call executes outside the captured scope, so the enqueue silently falls back to the direct path and auto-commits even if the outer transaction rolls back.

**Concurrency**: coordinated enqueues within a single coordinated scope must not run concurrently — the scope's single DB connection / transaction is not thread-safe (the same constraint as any code sharing one EF `DbContext` / connection). Keep enqueue calls in one scope sequential.

- **No coordinator (or no `AddOperationalStore`)**: unchanged — direct insert + in-band dispatch.
- **Coordinated scope with no relational capability** (messaging-only scope): falls back to the direct path — coordination is not made infectious.
- **Fail loud**: `AddAsync` / `AddBatchAsync` (time and cron) **throw** on any failure — validation (`JobValidatorException`), a relational transaction offered but unusable (dead/completed), or a relational coordinator active but the provider cannot write coordinated (`InvalidOperationException`). Wrap coordinated enqueues in `try/catch`; a thrown failure rolls the caller's transaction back.
- **Return-contract (SLA)**: on the coordinated path Add returns the persisted entity, meaning the row was **enlisted** (it commits with the caller's transaction) — not that deferred dispatch ran; a post-commit dispatch failure is swallowed and recovered by the fallback poll sweep (`FallbackIntervalChecker`, default 30s). `Update`/`Delete` keep returning `JobResult`; only the Add path throws.
- **Tenancy** stamping inside the coordinated write is out of scope until a tenant column exists (issue #278).

---
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
builder.Services.AddJobs(options =>
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

// Initialize Jobs
app.UseJobs();

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
builder.Services.AddJobs(options =>
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
});

// Start modes
app.UseJobs(JobsStartMode.Immediate); // Start immediately (default)
app.UseJobs(JobsStartMode.Manual);    // Wait for manual trigger
```

### Distributed Lock Hardening (optional, off by default)

**Startup cron-seed migration** (`jobs.cron-seed-migration`) runs on every node — each scans code-defined cron jobs and upserts them. Seeded rows carry a **deterministic primary key derived from the function name**, so simultaneous first-boot across N nodes converges on a single row (primary-key dedup) — no duplicate schedules (the durable provider catches the unique-violation and discards the redundant insert). An optional Jobs-scoped `IDistributedLock` then only removes the redundant cross-node scan/write storm. It is never the correctness boundary for job-row ownership/execution — per-row predicates, `node@incarnation` ownership, and per-job leases remain that boundary.

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    // Pass any already-composed IDistributedLock (instance or factory). Off unless this is called.
    options.UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>());
});
```

- **Off by default** — without `UseDistributedLock`, the seed runs on every node, unchanged; a `NullDistributedLock` fallback is always registered.
- **Skip-on-contention** — the guard tries once with no wait (`AcquireTimeout = TimeSpan.Zero`); if another node holds the lock or acquisition faults, the node skips. Cancellation during acquisition propagates.
- **Recovery is next-boot, not periodic** — a generous finite TTL (`None` monitoring; nothing observes lease-loss, so the TTL is the only safety net) lets a holder that dies mid-seed release via expiry. The seed only runs at startup, so if the lock store is down for *all* nodes at first boot, the schedule stays unseeded until the next process restart (warning-logged on acquire-fault) — it is not retried on a timer.
- **Jobs-isolated** — kept under a Jobs-private keyed-DI slot, so it never clashes with an app-level `IDistributedLock`. The resource name is stable and Jobs-specific.
- **Dead-node reclaim is intentionally NOT lock-guarded** — the shared `DeadOwnerRecoveryBridge` marks each dead owner reclaimed *before* the sweep and only retries it (next reconcile tick) when the sweep *throws*. A skip-on-contention that returned normally would pin the owner and strand its dead-node `InProgress` rows (`ReleaseDeadNodeResources` is their sole terminal-transition path). The exact-owner predicates make a repeated sweep touch zero rows, so every survivor sweeping is cheap and self-healing (periodic reconcile is the authoritative backstop); a lock here would be the correctness boundary it must never be.
- **Not guarded: cron-occurrence creation** — occurrences carry deterministic ids and are created via an id-keyed upsert, so storage-level dedup is already the correctness boundary; a `jobs.cron-occurrence-creation` lock would add nothing and is intentionally omitted. Mirrors the `Headless.Messaging` `UseDistributedLock` retry-pickup pattern.

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.DistributedLocks.Abstractions` — optional distributed-lock hardening (off by default)
- `Headless.Extensions`

## Side Effects

- Starts background hosted services for job scheduling and execution
- Creates in-memory job storage (or database tables with persistence providers)
- Runs custom thread pool for job execution
- Periodically scans for due jobs and executes them
---
# Headless.Jobs.Dashboard

Monitoring dashboard for Headless.Jobs with built-in authentication options and real-time updates.

## Installation

```bash
dotnet add package Headless.Jobs.Dashboard
```

## Minimal Setup

```csharp
builder.Services
    .AddJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs-dashboard");
        dashboard.WithHostAuthentication();
    });

app.UseJobs();
```

## 🚀 Quick Examples

### No Authentication (Public Dashboard)
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        // No authentication setup = public dashboard
    });
});
```

### Basic Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});
```

### API Key Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication();
    });
});
```

### Use Host Authentication with Custom Policy
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

## 🔧 Fluent API Methods

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy)` - Use your app's existing auth with optional policy (e.g., "AdminPolicy")
- `SetBasePath(path)` - Set dashboard URL path
- `SetBackendDomain(domain)` - Set backend API domain
- `SetCorsPolicy(policy)` - Configure CORS

## Live Nodes View

The dashboard surfaces the cluster's live nodes (identity, role, state, last-beat) from the `Headless.Coordination` membership liveness snapshot:

- `GET /api/nodes` returns the current node projection.
- Membership changes (node joined, suspected, recovered, left) push live updates over the existing SignalR hub, so the nodes panel updates without polling.

This replaces the former Redis-driven node feed; node liveness now comes from the coordination substrate.

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** -> Public dashboard
2. **Basic auth configured** -> Username/password login
3. **Bearer token configured** -> API key authentication
4. **Host auth configured** -> Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication
- Supports both header and query parameter auth (for WebSockets)

That's it! Simple and clean. 🎉
---
# Headless.Jobs.SourceGenerator

C# source generator for Jobs that generates boilerplate code for background job registration and execution.

## Problem Solved

Eliminates reflection overhead and manual job registration by generating compile-time code for Jobs job functions marked with `[Jobs]` attribute.

## Key Features

- **Zero Reflection**: Compile-time code generation
- **Auto-Registration**: Automatic job discovery and registration
- **Type Safety**: Compile-time validation of job signatures
- **DI Integration**: Generates constructor injection code
- **Incremental**: Fast rebuild with incremental generation
- **Diagnostics**: Rich compile-time error messages

## Installation

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

## Quick Start

```csharp
// Define job with attribute
[Jobs("*/5 * * * *")] // Every 5 minutes
public static class CleanupJob
{
    public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<CleanupJob>>();
        logger.LogInformation("Running cleanup");
    }
}

// Source generator creates:
// - Job registration code
// - Execution delegates
// - Constructor injection
// - Request type mapping

// No manual registration needed - jobs auto-discovered at compile time
```

## Configuration

No runtime configuration. Uses attributes:

```csharp
// Cron job
[Jobs("0 0 * * *", Priority = JobPriority.High)]
public static class DailyReport { /* ... */ }

// Job with request payload
[Jobs("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(
        JobFunctionContext<OrderRequest> context,
        CancellationToken ct)
    {
        await orders.ProcessAsync(context.Request, ct);
    }
}

// Custom constructor
public sealed class ComplexJob
{
    [JobsConstructor]
    public ComplexJob(ILogger<ComplexJob> logger, IConfiguration config)
    {
        // Custom initialization
    }

    [Jobs("ComplexTask")]
    public async Task ExecuteAsync(/* ... */) { }
}
```

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (analyzer/generator)

## Side Effects

Generates `JobsInstanceFactoryExtensions.g.cs` at compile time with:
- Module initializer for auto-registration
- Job execution delegates
- Constructor factory methods
- Request type registrations
---
# Headless.Jobs.OpenTelemetry

OpenTelemetry instrumentation package for Jobs job scheduler with distributed tracing support.

## Features

- **Distributed Tracing**: Full OpenTelemetry activity/span creation for job execution lifecycle
- **Structured Logging**: Rich logging with job context through ILogger integration
- **Parent-Child Relationships**: Maintains trace relationships between parent and child jobs
- **Retry Tracking**: Tracks retry attempts with detailed context
- **Performance Metrics**: Comprehensive execution time and outcome tracking
- **Error Tracking**: Detailed exception and cancellation tracking
- **Caller Information**: Automatic detection of where jobs are enqueued from

## Installation

```bash
dotnet add package Headless.Jobs.OpenTelemetry
```

## Usage

### Basic Setup

```csharp
using Headless.Jobs;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry with Jobs ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Headless.Jobs") // Add Jobs ActivitySource
               .AddConsoleExporter()
               .AddJaegerExporter();
    });

// Add Jobs with OpenTelemetry instrumentation
builder.Services.AddJobs<MyTimeJob, MyCronJob>(options => { })
    .AddOperationalStore(ef => { })
    .AddOpenTelemetryInstrumentation(); // Enable tracing

var app = builder.Build();
app.Run();
```

### With Jaeger

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddJobsInstrumentation()
               .AddJaegerExporter(options =>
               {
                   options.Endpoint = new Uri("http://localhost:14268/api/traces");
               });
    });
```

### With Application Insights

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddJobsInstrumentation()
               .AddAzureMonitorTraceExporter();
    });
```

## Trace Structure

### Job Execution Activities
```
headless.jobs.job.execute.timeticker (main job execution span)
├── headless.jobs.job.enqueued (when job starts execution)
├── headless.jobs.job.completed (on successful completion)
├── headless.jobs.job.failed (on failure)
├── headless.jobs.job.cancelled (on cancellation)
├── headless.jobs.job.skipped (when skipped)
├── headless.jobs.seeding.started (for data seeding)
└── headless.jobs.seeding.completed (seeding completion)
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `headless.jobs.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `headless.jobs.job.type` | Type of job | `TimeJob`, `CronJob` |
| `headless.jobs.job.function` | Function name being executed | `ProcessEmails` |
| `headless.jobs.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `headless.jobs.job.machine` | Machine executing the job | `web-server-01` |
| `headless.jobs.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `headless.jobs.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `headless.jobs.job.is_due` | Whether the job was due | `true`, `false` |
| `headless.jobs.job.is_child` | Whether this is a child job | `true`, `false` |
| `headless.jobs.job.retries` | Maximum retry attempts | `3` |
| `headless.jobs.job.current_attempt` | Current retry attempt | `1`, `2`, `3` |
| `headless.jobs.job.final_status` | Final execution status | `Succeeded`, `Failed`, `Cancelled`, `Skipped` |
| `headless.jobs.job.final_retry_count` | Final retry count reached | `2` |
| `headless.jobs.job.execution_time_ms` | Execution time in milliseconds | `1250` |
| `headless.jobs.job.success` | Whether execution was successful | `true`, `false` |
| `headless.jobs.job.error_type` | Exception type for failures | `SqlException`, `TimeoutException` |
| `headless.jobs.job.error_message` | Error message | `Connection timeout` |
| `headless.jobs.job.error_stack_trace` | Full stack trace | `at MyService.ProcessData()...` |
| `headless.jobs.job.cancellation_reason` | Reason for cancellation | `Task was cancelled` |
| `headless.jobs.job.skip_reason` | Reason for skipping | `Another instance is already running` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] Jobs Job enqueued: TimeJob - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from ExecutionTaskHandler
[INF] Jobs Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] Jobs Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[INF] Jobs Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 2500ms - Success: False
[WRN] Jobs Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Task was cancelled
[INF] Jobs Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another CronOccurrence is already running!
[INF] Jobs start seeding data: TimeJob (production-node-01)
[INF] Jobs completed seeding data: TimeJob (production-node-01)
```

## Integration with Logging Frameworks

This package works seamlessly with any logging framework that integrates with `ILogger`:

### Serilog
```csharp
builder.Host.UseSerilog((context, config) =>
{
    config.WriteTo.Console()
          .WriteTo.File("logs/jobs-.txt", rollingInterval: RollingInterval.Day)
          .Enrich.FromLogContext();
});
```

### NLog
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddNLog();
```

## Performance Impact

- **Minimal Overhead**: Activities are only created when OpenTelemetry listeners are active
- **Efficient Logging**: Uses structured logging with minimal string allocations
- **Conditional Tracing**: No performance impact when tracing is disabled

## Requirements

- .NET 8.0 or later
- OpenTelemetry 1.7.0 or later
- Headless.Jobs.Abstractions (automatically included)

---
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

app.UseJobs();
```

Without a registered coordination provider, the durable path throws `InvalidOperationException` naming `AddHeadlessCoordination`.

### Node Identity and Recovery Model

- **Owner identity is `node@incarnation`** (store-allocated incarnation), not the machine name. Each durable job row is stamped with the current node's `node@incarnation` owner.
- **Recovery is event-driven and backend-neutral.** Dead-node reclaim is triggered by Coordination `NodeLeft` events plus a periodic liveness-snapshot reconcile, so it works on the EF/Postgres path WITHOUT Redis. Reclaim matches the dead `node@incarnation` exactly and never touches a restarted node's freshly-stamped rows or unowned-idle rows.
- **Recovery latency trade-off.** On the no-Redis path, fast-restart recovery is TTL-bounded: a predecessor incarnation is reclaimed only after its heartbeat expires and `NodeLeft` fires (previously the machine-name self-reclaim was immediate). Tune via the Coordination heartbeat/TTL and `DeadNodeReconcileInterval`.
- **Fail-stop on membership loss.** If the local node loses membership, the durable scheduler stops processing rather than stamping a stale owner.

### Cron-Expression Caching

Jobs cron-expression caching is provider-neutral. `Headless.Jobs.EntityFramework` uses the host application's optional default `ICache` from `Headless.Caching.Abstractions`:

```csharp
// Choose one cache provider before or alongside Jobs.
var redis = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddHeadlessCaching(setup => setup.UseRedis(options => options.ConnectionMultiplexer = redis));

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
