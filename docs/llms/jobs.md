---
domain: Jobs (Background Jobs)
packages: Jobs.Abstractions, Jobs.Core, Jobs.Dashboard, Jobs.SourceGenerator, Jobs.OpenTelemetry, Jobs.EntityFramework, Jobs.EntityFramework.PostgreSql, Jobs.EntityFramework.SqlServer
---

# Jobs (Background Jobs)

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Job Types](#job-types)
    - [The `[JobFunction]` Attribute and Source Generator](#the-jobfunction-attribute-and-source-generator)
    - [Lease Model and Sliding Renewal](#lease-model-and-sliding-renewal)
    - [Distributed Coordination and Node Identity](#distributed-coordination-and-node-identity)
    - [Commit-Coordinated Enqueue (Atomic Enqueue)](#commit-coordinated-enqueue-atomic-enqueue)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Jobs.Abstractions](#headlessjobsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Jobs.Core](#headlessjobscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Jobs.Dashboard](#headlessjobsdashboard)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Jobs.SourceGenerator](#headlessjobssourcegenerator)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Jobs.OpenTelemetry](#headlessjobsopentelemetry)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Jobs.EntityFramework](#headlessjobsentityframework)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
    - [Error Handling and Retries](#error-handling-and-retries)
- [Headless.Jobs.EntityFramework.PostgreSql](#headlessjobsentityframeworkpostgresql)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Jobs.EntityFramework.SqlServer](#headlessjobsentityframeworksqlserver)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)

> High-performance background job scheduler for .NET with cron expressions, time-based scheduling, compile-time source-generated registration, and distributed coordination.

## Quick Orientation

Required packages: `Jobs.Core` + `Jobs.EntityFramework` (persistence) + `Jobs.SourceGenerator` (compile-time job registration). Add the PostgreSQL or SQL Server Jobs EF provider package for native atomic claims; otherwise the EF package uses its portable optimistic-CAS fallback.

Optional add-ons:
- `Jobs.Dashboard` — monitoring UI with authentication (basic, API key, host auth) plus live-cluster node view
- `Jobs.OpenTelemetry` — distributed tracing and structured logging
- `Jobs.Abstractions` — interfaces only; pulled in transitively by `Jobs.Core`; install directly only when building a library on top

Minimum wiring (in-memory storage, no persistence):

```csharp
using Polly;
using Polly.Retry;

builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });
});
// No app.UseJobs() required — scheduling starts via IHostedService automatically.
```

For durable persistence register a coordination provider first, then add the EF operational store:

```csharp
builder.Services.AddHeadlessCoordination(c => c.UseSqlServer(conn));
builder
    .Services.AddHeadlessJobs()
    .UseEntityFramework(ef => ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn)));
```

Mark job methods with `[JobFunction("name")]` (or `[JobFunction("name", cronExpression: "* * * * *")]` for cron) and add `Jobs.SourceGenerator` for compile-time zero-reflection discovery.

## Agent Instructions

- Do NOT use Hangfire or Quartz — use `Headless.Jobs` for all background jobs in this framework.
- The registration attribute is `[JobFunction]` (`JobFunctionAttribute` in `Headless.Jobs.Base`). The first positional argument is the function name; `cronExpression` is a named parameter. Add `Headless.Jobs.SourceGenerator` to the project for compile-time registration.
- Call `AddHeadlessJobs()` on `IServiceCollection`. There is no `app.UseJobs()` call — the scheduler starts automatically through `IHostedService` registered by `AddHeadlessJobs`.
- Use `Jobs.EntityFramework` for durable persistence. Without it, jobs live in memory and are lost on restart.
- Configure `UsePostgreSqlClaims()` or `UseSqlServerClaims()` inside the existing `UseEntityFramework` builder when the matching provider package is installed. Configure only one. Omitting both deliberately keeps the portable EF optimistic-CAS claim path.
- For the durable operational store, register `AddHeadlessCoordination(c => c.Use…(conn))` BEFORE `AddHeadlessJobs(o => o.UseEntityFramework(…))`. Without coordination, startup throws `InvalidOperationException` naming `AddHeadlessCoordination`.
- On the durable path, node identity is `node@incarnation` (store-allocated by Coordination), not `Environment.MachineName`. `SchedulerOptionsBuilder.NodeId` is only a pre-registration display fallback — it is NOT the row owner on the durable path.
- Running jobs slide their pickup lease forward on the `LeaseRenewalInterval` cadence (default ≈ `LeaseDuration / 3`), so `LeaseDuration` (default 5 min) no longer needs to exceed the longest job runtime. Keep `LeaseDuration` ≥ `FallbackIntervalChecker` to avoid spurious re-claims of rows that are claimed but not yet started.
- Set `OnNodeDeath = NodeDeathPolicy.MarkFailed` or `Skip` on non-idempotent jobs — default `Retry` will re-run the job after a node crash.
- Do NOT install a Jobs-specific cache package. Jobs cron-expression caching reuses the host's `ICache` (`Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`). Without a registered `ICache`, cron expressions are read directly from the database.
- Atomic enqueue: call `IJobScheduler` or the low-level manager inside `db.ExecuteCoordinatedTransactionAsync(...)` to commit domain writes and the job row as one unit. The facade persists through the same managers and inherits their deferred post-commit side effects. Requires a `Headless.CommitCoordination` provider (`AddPostgreSqlCommitCoordination()` / `AddSqlServerCommitCoordination()`) — a different subsystem from `AddHeadlessCoordination`. The coordinated path throws on any failure; wrap in `try/catch`.
- Establish commit coordination synchronously before entering asynchronous work. The provided `ExecuteCoordinatedTransactionAsync` helpers do this correctly; once established, the scope flows across awaits inside the operation, so domain writes and message publishes may be awaited before `AddAsync`.
- Use `[JobsConstructor]` (`JobsConstructorAttribute`) on the constructor the source generator should use when a class has multiple constructors.
- Use `IJobScheduler` for routine immediate, delayed, and recurring scheduling. Typed overloads resolve generated metadata from `typeof(TArgs)`; requestless overloads require a generated `JobFunctionDescriptor` from `JobFunctionProvider.JobFunctionDescriptors`.
- `EnqueueOptions` / `RecurringJobOptions` support description, durable retry count/intervals, and node-death policy only. Execution time and cron expression are method arguments. Do not add priority to scheduling options; priority remains immutable `[JobFunction]` / descriptor metadata.
- For testing, call `options.DisableBackgroundServices()` to suppress background scheduler execution.
- To use `JobsStartMode.Manual`, set `scheduler.StartMode = JobsStartMode.Manual` inside `ConfigureScheduler`.
- Managers remain supported: inject `ITimeJobManager<TTimeJob>` / `ICronJobManager<TCronJob>` for CRUD, batching, seeding, custom entities, chains, and advanced persistence workflows.

---

## Core Concepts

### Job Types

Jobs supports two first-class job types:

**Time jobs** (`TimeJobEntity`) — one-off jobs scheduled to run at a specific UTC `ExecutionTime`. Managed via `ITimeJobManager<TTimeJob>`. Supports parent–child chains (up to 3 levels, 5 children per level) using `FluentChainJobBuilder<TTimeJob>`.

**Cron jobs** (`CronJobEntity`) — recurring jobs defined by a cron expression (`Expression` property). Each firing generates a `CronJobOccurrenceEntity` that is claimed and executed by a scheduler worker. Managed via `ICronJobManager<TCronJob>`.

Both types share `BaseJobEntity` (`Id`, `Function`, `Description`, `CreatedAt`, `UpdatedAt`) and expose `Retries`, `RetryIntervals`, and `OnNodeDeath` policy.

### The `[JobFunction]` Attribute and Source Generator

The source generator (`Headless.Jobs.SourceGenerator`) scans for `JobFunctionAttribute` (`[JobFunction]`) on methods and generates:
- A module initializer that auto-registers job delegates with the Jobs runtime before `Main` runs.
- A delegate-free `JobFunctionDescriptor` for every function, frozen by `JobFunctionProvider` into name and typed-request indexes.
- Factory delegates for every job method.
- Constructor injection code (using the `[JobsConstructor]` constructor if present, otherwise the first public constructor).

Attribute signatures (from `Headless.Jobs.Base.JobFunctionAttribute`):

```csharp
// Cron job (cronExpression is optional — omit for time/programmatic jobs)
[JobFunction("DailyReport", cronExpression: "0 0 * * *", taskPriority: JobPriority.High)]
public static Task ExecuteAsync(IServiceProvider sp, CancellationToken ct) { ... }

// Time job or named function for programmatic enqueue
[JobFunction("ProcessOrder")]
public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct) { ... }
```

The first positional argument is the durable function identity. `IJobScheduler` obtains it from the generated descriptor, while low-level manager callers set the entity `Function` directly. Priority (`JobPriority.Normal` / `High` / `Low` / `LongRunning`) and max-concurrency are optional attribute parameters.

Typed functions are indexed by both function name and exact request `Type`; requestless descriptors have `RequestType = null` and do not appear in the inverse type index. HF005 rejects duplicate function names and HF011 rejects duplicate typed request mappings in one compilation. Cross-assembly collisions fail `JobFunctionProvider.Build()` with a deterministic ordinal-sorted report rather than choosing the first initializer.

### Lease Model and Sliding Renewal

Every claim of a job or cron-occurrence row stamps a pickup lease: `LockedUntil = now + SchedulerOptionsBuilder.LeaseDuration` (default 5 minutes). In-memory uses the injected `TimeProvider`. EF translates `DateTime.UtcNow` inside the claim statement, so lease-expiry comparison and stamping use the database UTC clock without a separate scalar query.

**Sliding lease for running jobs (#316):** before invoking user code, a job verifies that the current node still owns the row. A running job then renews its lease on the `LeaseRenewalInterval` cadence (defaults to `LeaseDuration / 3`; an explicit value must be positive and strictly less than `LeaseDuration`). On the EF storage path, renewals compare against the **database clock** (`now()`/`GETUTCDATE()`), not a node's local clock, so cross-node clock skew cannot reclaim a healthy renewing job. If a renewal affects zero rows (the row was reclaimed or its owner changed), or if the renewal cannot complete within the cadence (a hung store), the worker cancels that job's `CancellationToken` (cancel-on-loss). If the start-time check loses ownership, user code is not invoked and the row is left `InProgress` for stalled reclaim.

Consequences:
- `LeaseDuration` no longer needs to exceed the longest job runtime; a healthy long job keeps renewing.
- A job stuck `InProgress` whose lease lapses (stopped renewing) is reclaimed per its `OnNodeDeath` policy within ≈ one `LeaseDuration` — independent of node death.
- The dead-node sweep defers `InProgress` rows to the lease: a busy node's still-leased running jobs survive membership blips and are only recovered once their lease lapses.

### Distributed Coordination and Node Identity

The durable operational store (EF provider) uses `Headless.Coordination` for:
- **Node identity**: the node owner stamped on job rows is `node@incarnation` (a store-allocated incarnation ID), not `Environment.MachineName`. K8s pod-collision handling via `POD_NAME`/`POD_NAMESPACE` is configured on `Headless.Coordination`, not on `SchedulerOptionsBuilder`.
- **Dead-node recovery**: triggered by `Coordination` `NodeLeft` events plus a periodic liveness-snapshot reconcile (`DeadNodeReconcileInterval`, default 1 minute). Backend-neutral — works without Redis. Reclaim matches the dead `node@incarnation` exactly; it never touches rows owned by a restarted node's fresh incarnation.
- **Fail-stop on membership loss**: if the local node loses coordination membership, the durable scheduler stops processing rather than stamping stale owners.

### Commit-Coordinated Enqueue (Atomic Enqueue)

When a `Headless.CommitCoordination` provider is registered (`services.AddPostgreSqlCommitCoordination()` or `services.AddSqlServerCommitCoordination()`), `ITimeJobManager.AddAsync` / `AddBatchAsync` and `ICronJobManager.AddAsync` / `AddBatchAsync` write the job row inside the caller's ambient transaction and defer dispatch, scheduler restart, notifications, and cron-cache invalidation to post-commit.

```csharp
// db is a relational DbContext; services is the DI scope; both are enlisted by the helper.
await db.ExecuteCoordinatedTransactionAsync(
    async (ctx, ct) =>
    {
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);

        await outboxBus.PublishAsync(new OrderPlaced(order.Id), ct); // outbox publish

        await timeJobManager.AddAsync(
            new TimeJobEntity // enlists in same transaction
            {
                Function = "SendOrderReminder",
                ExecutionTime = DateTime.UtcNow.AddHours(24),
                Request = JobsHelper.SerializeRequest(new { order.Id }),
            },
            ct
        );
    },
    services,
    ct
);
// On commit: order row + outbox message + job row all persist; dispatch fires post-commit.
// On rollback: none persist, no dispatch.
```

**Footguns:**
- The ambient scope must be established synchronously; do not create a custom async factory that sets `ICurrentCommitCoordinator`. Use `ExecuteCoordinatedTransactionAsync` or a synchronous enlistment API. After enlistment, normal awaits inside the coordinated operation preserve the scope.
- Coordinated enqueues in one scope must be sequential — the scope's DB connection/transaction is not thread-safe.
- `AddAsync` / `AddBatchAsync` **throw** on failure (validation, dead/completed transaction, mis-wire). `Update` / `Delete` return `JobResult<T>` and do not throw.
- A returned entity on the coordinated path means the row was **enlisted** (commits with the transaction), not that dispatch ran. Post-commit side effects are bounded by `PostCommitDrainTimeout` (default 30s; valid range `> 0` through `5m`); timeout releases the commit thread and the fallback poll sweep recovers dispatch.
- The durable coordinated path needs **two separate registrations**: `AddHeadlessCoordination(...)` (the `Headless.Coordination` distributed-lock/membership subsystem for the operational store) AND a `Add{Provider}CommitCoordination()` (the `Headless.CommitCoordination` transactional scope subsystem). Similar names, different systems.

## Choosing a Provider

The base EF package is the compatibility layer. Native claim packages optimize pickup without changing the scheduler contract, lease rules, descendant stamping, or fallback-window behavior.

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| EF optimistic CAS | The EF database is unsupported by a native package, or contention is low | Many workers regularly race for the same due rows | Zero extra provider package, but losing workers perform failed compare-and-swap work |
| PostgreSQL atomic claims | PostgreSQL 14+ hosts contend for due work | The operational store is not PostgreSQL | `FOR UPDATE SKIP LOCKED` lets claimers select disjoint unlocked candidates in one update-and-return transaction |
| SQL Server atomic claims | SQL Server 2019+ or Azure SQL hosts contend for due work | Page-lock contention or escalation dominates and cannot be operationally addressed | `READPAST` skips row locks, but page locks can still block; `ROWLOCK` is not a guarantee |

Native selection belongs inside `UseEntityFramework`; do not add a standalone service registration. Configure exactly one native claim provider. Selecting both is rejected during registration, while selecting neither retains the CAS fallback.

The PostgreSQL and SQL Server packages are EF optimization extensions, not independent persistence providers. `Jobs.EntityFramework` retains job storage, mapping definitions, recovery, the persistence contract, and provider-neutral claim transaction lifecycle primitives. Each extension owns provider-specific claim execution, including SQL, parameters, and locking behavior.

---

## Headless.Jobs.Abstractions

Contracts, entity types, manager interfaces, and execution primitives for the Jobs system.

### Problem Solved

Provides the shared contracts — `IJobScheduler`, `ITimeJobManager<TTimeJob>`, `ICronJobManager<TCronJob>`, descriptors, entity types, options, enums, exception types, and execution context — that decouple job enqueueing code from any specific Jobs persistence provider or scheduler implementation. Consumer contracts do not depend on `Jobs.EntityFramework`.

### Key Features

- **Routine scheduling facade**: `IJobScheduler` resolves generated `[JobFunction]` metadata, serializes typed requests, and schedules immediate, delayed, and recurring jobs without copied function strings or entity construction.
- **Generated descriptors**: immutable `JobFunctionDescriptor` values expose function identity, nullable request type, cron metadata, priority, and maximum concurrency without exposing execution delegates.
- **Scheduling options**: `EnqueueOptions` and `RecurringJobOptions` map description, durable retry count/intervals, and node-death policy. Priority remains generated function metadata.
- **Manager interfaces**: `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` with `AddAsync`, `AddBatchAsync`, `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, `DeleteBatchAsync`.
- **Entity types**: `TimeJobEntity` / `TimeJobEntity<TTicker>` (parent–child chains), `CronJobEntity`, `CronJobOccurrenceEntity`, and `BaseJobEntity`.
- **Execution context**: `JobFunctionContext` and `JobFunctionContext<TRequest>` — exposes `Id`, `Type`, `RetryCount`, `IsDue`, `ScheduledFor`, `FunctionName`, `CronOccurrenceOperations`, and `RequestCancellation()`.
- **Attribute types**: `JobFunctionAttribute` (`[JobFunction]`) for function/cron registration; `JobsConstructorAttribute` (`[JobsConstructor]`) for custom DI injection.
- **Retry primitives**: `TimeJobEntity.Retries`, `RetryIntervals`, `RetryCount`; `CronJobEntity.Retries`, `RetryIntervals`.
- **Node-death policy**: `NodeDeathPolicy` enum (`Retry` / `MarkFailed` / `Skip`) on both entity types; propagated from `CronJobEntity` to every generated occurrence.
- **Exception types**: `JobValidatorException` (with `Errors` list for batch failures); `TerminateExecutionException` (stop without retry, optional final `JobStatus`).
- **Fluent chain builder**: `FluentChainJobBuilder<TTimeJob>` for defining parent–child–grandchild job chains up to 3 levels / 5 siblings per level.
- **Global exception handler**: `IJobExceptionHandler` with `HandleExceptionAsync` and `HandleCanceledExceptionAsync`.
- **Job status**: `JobStatus` enum: `Idle`, `Queued`, `InProgress`, `Succeeded`, `DueDone`, `Failed`, `Cancelled`, `Skipped`.

### Installation

```bash
dotnet add package Headless.Jobs.Abstractions
```

Pulled in transitively by `Headless.Jobs.Core`. Install directly only when building a library that targets Jobs interfaces without depending on the Core implementation.

### Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

public sealed record OrderReminderRequest(string OrderId);

public sealed class OrderService(IJobScheduler jobs)
{
    public async Task ScheduleReminderAsync(string orderId, CancellationToken ct)
    {
        var jobId = await jobs.EnqueueAsync(
            new OrderReminderRequest(orderId),
            new EnqueueOptions
            {
                Description = $"order-reminder-{orderId}",
                Retries = 3,
                RetryIntervals = [30, 60, 120],
            },
            ct
        );

        Console.WriteLine($"Scheduled {jobId}");
    }
}

// Mark a method for registration (requires Jobs.SourceGenerator)
[JobFunction("SendOrderReminder")]
public static Task ExecuteAsync(
    JobFunctionContext<OrderReminderRequest> context,
    CancellationToken ct)
{
    // context.Request.OrderId, context.RetryCount, and context.ScheduledFor are available.
    return Task.CompletedTask;
}
```

Requestless scheduling resolves a generated descriptor and passes it to the matching overload:

```csharp
var descriptor = JobFunctionProvider.JobFunctionDescriptors["Cleanup"];
var jobId = await scheduler.EnqueueAsync(descriptor, cancellationToken: ct);

var delayedId = await scheduler.ScheduleAsync(
    new OrderReminderRequest(orderId),
    DateTime.UtcNow.AddHours(24),
    cancellationToken: ct
);

var recurringId = await scheduler.ScheduleRecurringAsync(
    new OrderReminderRequest(orderId),
    "0 0 * * *",
    new RecurringJobOptions { Description = "daily-reminder" },
    ct
);
```

All facade methods return the persisted entity `Guid`; recurring scheduling returns the persisted cron-definition ID. Unknown request types or descriptor names throw `JobFunctionNotFoundException` before persistence. Low-level managers remain supported for CRUD, batching, seeding, custom entity types, chains, and advanced scenarios.

### Configuration

None at the abstractions layer. All configuration is done in `Headless.Jobs.Core` via `AddHeadlessJobs(options => ...)`.

### Dependencies

- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

None.

---

## Headless.Jobs.Core

Core implementation of the Jobs scheduler: in-memory persistence provider, execution task handler, background services, custom thread pool, and the `AddHeadlessJobs` DI extension.

### Problem Solved

Provides reliable background job scheduling with cron expressions, delayed execution, custom task scheduling, retry logic, and an in-process thread pool without any external job scheduler dependencies (Hangfire, Quartz, etc.). The in-memory path works standalone; the durable path composes with `Jobs.EntityFramework`.

### Key Features

- **`AddHeadlessJobs()`**: single DI entry point; registers managers, background services, and the in-memory persistence provider.
- **`IJobScheduler` facade**: schedules typed or requestless `[JobFunction]` methods through generated descriptor indexes, maps supported options, and returns persisted entity IDs.
- **Scheduler background service**: polls for due time jobs and cron occurrences on `FallbackIntervalChecker` cadence (default 30s); also driven by soft-notification signals for near-zero latency.
- **Custom thread pool** (`JobsTaskScheduler`): bounds active async executions by `MaxConcurrency` (default `Environment.ProcessorCount`), honors `High` → `Normal` → `Low` dequeue order, and gives `LongRunning` work a dedicated thread.
- **Sliding lease renewal** (#316): jobs verify ownership immediately before user code starts, then extend `LockedUntil` on `LeaseRenewalInterval` cadence; cancel-on-loss if renewal affects zero rows or errors.
- **`DisableBackgroundServices()`**: suppresses background execution; only the managers are registered (useful for worker-side-only nodes and test projects).
- **Seeder API**: `UseJobsSeeder(Func<ITimeJobManager<TTimeJob>, Task>)` and `UseJobsSeeder(Func<ICronJobManager<TCronJob>, Task>)` for startup data seeding; `IgnoreSeedDefinedCronJobs()` to skip auto-seeding of attribute-defined cron jobs.
- **GZip request payloads**: `UseGZipCompression()` on `JobsOptionsBuilder` compresses serialized request bytes.
- **Exception handler**: `SetExceptionHandler<THandler>()` registers an `IJobExceptionHandler` singleton.
- **Node-death policy enforcement**: claim predicate gates the lease-expiry re-claim arm on `OnNodeDeath == Retry`; clock skew cannot speculatively re-run `Skip` or `MarkFailed` jobs.
- **Startup mode**: `SchedulerOptionsBuilder.StartMode` (`JobsStartMode.Immediate` default / `JobsStartMode.Manual`).

### Design Notes

The in-memory pickup lease uses the injected `TimeProvider`. The EF operational store uses the **database clock** for acquisition, renewal, and reclaim. Claim predicates and stamps are translated into the existing SQL statement, avoiding both cross-node clock skew and a separate clock round trip.

`SchedulerOptionsBuilder.NodeId` is used as the row owner only on the in-memory single-process path (defaults to `Environment.MachineName`). On the durable path this value is overridden by `JobsOwnerIdentityAdapter` which reads the `node@incarnation` string from `Headless.Coordination`; `NodeId` becomes a pre-registration display fallback only.

Jobs remain `Queued` while waiting for worker and per-function concurrency capacity. The worker performs the owned `Queued` → `InProgress` write immediately before execution, then the execution handler performs one more lease check before invoking user code. If ownership expired while queued, the worker skips the delegate instead of starting an unowned job. Because that transition must happen at admission time, each admitted job issues its own single-row claim write — a tick with N co-due functions performs N claim round trips instead of one batched write; this is the deliberate cost of the single-winner fence.

Claiming a chained time job leases its direct children and grandchildren to the same owner while leaving their status `Idle`; each child transitions to `InProgress` only when its `RunCondition` is satisfied. Reclaimed time jobs and cron occurrences preserve `RetryCount`, so execution resumes from the persisted attempt instead of resetting the retry budget.

Only cancellation tied to the job's cancellation token (including `context.RequestCancellation()`) is classified as `Cancelled`. A detected lease loss writes no terminal status — the row stays `InProgress` so the stalled-reclaim sweep recovers it per `OnNodeDeath`. An unrelated `OperationCanceledException` is handled as a failure and follows the configured retry policy.

Cron expressions are evaluated in `SchedulerTimeZone`. A spring-forward occurrence inside an invalid local-time gap is shifted forward by the gap; an ambiguous fall-back occurrence runs once at the later UTC instant (the standard-time offset).

### Installation

```bash
dotnet add package Headless.Jobs.Core
```

### Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

// 1. Register Jobs
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
        scheduler.LeaseDuration = TimeSpan.FromMinutes(5);
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(30);
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

// 2. Define a cron job (requires Jobs.SourceGenerator)
[JobFunction("Cleanup", cronExpression: "*/5 * * * *")]
public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Running cleanup");
    await Task.CompletedTask;
}

// 3. Define a time job with DI
[JobFunction("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
        => await orders.ProcessAsync(context.Request, ct);
}

// 4. Schedule through generated typed metadata.
public sealed class OrderService(IJobScheduler scheduler)
{
    public Task<Guid> ScheduleAsync(OrderRequest request, CancellationToken ct) =>
        scheduler.EnqueueAsync(request, new EnqueueOptions { Description = "process-order" }, ct);
}

// The scheduler starts via IHostedService — no app.UseJobs() call needed.
```

Typed facade calls resolve `typeof(TArgs)`, serialize through the configured Jobs JSON/GZip pipeline, and persist through the configured manager. Requestless calls accept a descriptor from `JobFunctionProvider.JobFunctionDescriptors`. Immediate, delayed, and recurring methods return the persisted time-job or cron-definition ID. Unknown or stale identities fail before serialization or persistence.

`EnqueueOptions` and `RecurringJobOptions` expose only description, durable retries/intervals, and node-death policy. Execution time and cron expression remain explicit method arguments; priority remains immutable `[JobFunction]` / descriptor metadata. Managers remain public and supported for CRUD, batching, seeding, custom entities, chains, and advanced persistence workflows.

Facade calls use those managers internally, so they enlist in an established `Headless.CommitCoordination` scope and retain the same deferred post-commit dispatch/restart/notification behavior.

### Configuration

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

### Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Coordination.Abstractions`
- `Headless.Coordination.Core`
- `Headless.DistributedLocks.Abstractions`
- `Headless.Extensions`
- `NCrontab.Signed`
- `Polly.Core`

### Side Effects

- Registers `ITimeJobManager<TimeJobEntity>` and `ICronJobManager<CronJobEntity>` as singletons.
- Registers one non-generic `IJobScheduler` facade bound to the same configured time/cron entity pair.
- Registers background hosted services: `JobsInitializationHostedService` (always), `JobsSchedulerBackgroundService`, `JobsFallbackBackgroundService`, and `JobsExecutionTaskHandler` (unless `DisableBackgroundServices()` is called).
- Registers `JobsTaskScheduler` (custom thread pool bounded by active async `MaxConcurrency`).
- Sets global `CronScheduleCache.TimeZoneInfo` and `JobsHelper` JSON/compression settings.

---

## Headless.Jobs.Dashboard

Embedded web monitoring UI for `Headless.Jobs` with pluggable authentication and real-time cluster updates.

### Problem Solved

Provides operational visibility into the Jobs scheduler — job queues, execution history, live cluster nodes, retry/failure details — without requiring a separate monitoring service. The dashboard is embedded in the host application and mounted under a configurable URL path.

### Key Features

- **Embedded SPA**: served from the host process, no separate deployment.
- **Authentication options**: `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, `WithHostAuthentication(policy?)` (delegates to host app's auth), or explicit no-auth mode for isolated development dashboards.
- **Live cluster view**: `GET /api/nodes` returns live node projections from `Headless.Coordination` membership; `NodeJoined` / `NodeLeft` / `NodeSuspected` push updates over SignalR — no polling required.
- **Error monitoring**: surfaces failed, cancelled, and skipped jobs; retry counts; execution timings; exception messages.
- **Storage-reduced cron graphs**: bundled providers select distinct UTC dates and aggregate status counts in storage;
  the dashboard does not load a cron job's lifetime occurrence entities to render its bounded history graph.
- **Fluent builder**: `SetBasePath(path)`, `SetBackendDomain(domain)`, `SetCorsOrigins(origins)`, `SetCorsPolicy(policy)`.
- **Pair with OpenTelemetry**: Dashboard for operational triage; `Jobs.OpenTelemetry` for trace-level diagnostics.

### Design Notes

The dashboard exposes operational endpoints that can create, update, delete, run, cancel, start, stop, and restart jobs. Authentication must be chosen explicitly — if no auth method (including `WithNoAuth()`) is called, the host fails to start, so the dashboard never ships publicly by omission. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls; production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, or `WithApiKey(...)`. No CORS policy is applied by default (same-origin only); use `SetCorsOrigins(...)` when the SPA is served cross-origin.

Cron-occurrence graph selection remains history-derived: it first chooses the same inclusive UTC date window from
distinct occurrence dates, then zero-fills gaps. `IJobPersistenceProvider.GetCronOccurrenceGraphStatusCountsAsync`
is additive and has a compatibility implementation for third-party providers. A custom durable provider should
override it so distinct-date selection and date/status aggregation happen in storage; otherwise the default
implementation preserves behavior by projecting through the existing occurrence-list API.

### Installation

```bash
dotnet add package Headless.Jobs.Dashboard
```

### Quick Start

```csharp
using Headless.Jobs;

builder
    .Services.AddHeadlessJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs-dashboard");
        dashboard.WithHostAuthentication(); // or WithBasicAuth / WithApiKey
    });

// No app.MapJobs() or app.UseJobs() — the dashboard middleware is injected via IStartupFilter.
var app = builder.Build();
app.Run();
```

### Configuration

```csharp
builder
    .Services.AddHeadlessJobs()
    .AddDashboard(dashboard =>
    {
        // Path and domain
        dashboard.SetBasePath("/jobs");
        dashboard.SetBackendDomain("https://api.example.com");
        dashboard.SetCorsOrigins("https://admin.example.com"); // needed only when the SPA is cross-origin

        // Authentication — required, pick one:
        dashboard.WithBasicAuth("admin", "secret"); // username/password
        dashboard.WithApiKey("my-api-key"); // Bearer token / query param
        dashboard.WithHostAuthentication(); // delegate to host auth
        dashboard.WithHostAuthentication("AdminPolicy"); // host auth + policy
        // Or opt out explicitly with dashboard.WithNoAuth() — isolated development environments only.
    });
```

Auth detection is automatic: explicit `WithNoAuth()` → public; basic auth → username/password login UI; API key → bearer token; host auth → delegates to the host's authentication middleware.

### Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Dashboard.Authentication` (shared with `Headless.Messaging.Dashboard`)

### Side Effects

- Mounts dashboard HTTP API and SignalR hub under `SetBasePath` path via `IStartupFilter` (no explicit `app.Use…` call needed).
- Subscribes to `Headless.Coordination` membership events for live-node push updates.
- Serves embedded frontend SPA assets; requires Node 22 on `PATH` when building from source (build target `make dashboards`).
- Exposes mutating operational endpoints; configure authentication and CORS before exposing the dashboard outside an isolated development environment.

---

## Headless.Jobs.SourceGenerator

Roslyn incremental source generator that eliminates reflection and manual job registration for the Jobs scheduler.

### Problem Solved

Without the source generator, every job class or method must be manually registered with the Jobs runtime at startup, and job dispatch uses reflection to invoke methods. The source generator scans for `[JobFunction]` attributes at compile time and emits a module initializer that auto-registers all discovered jobs before `Main` runs, with zero reflection at runtime.

### Key Features

- **Zero reflection**: all dispatch delegates are generated as strongly-typed lambdas.
- **Auto-registration**: a `[ModuleInitializer]` in the generated file (`JobsInstanceFactory.g.cs`) registers job delegates before any host startup code runs.
- **Descriptor indexes**: generates delegate-free descriptors for every typed and requestless function; the provider exposes frozen indexes by name and by typed request `Type`.
- **Type safety**: compile-time validation of job method signatures and cron expression syntax.
- **DI constructor injection**: generates constructor factory methods; uses `[JobsConstructor]` constructor when present, otherwise the first public constructor.
- **Incremental**: only re-generates when marked methods change (fast on large solutions).
- **Collision safety**: HF005 rejects duplicate function names and HF011 rejects duplicate typed request mappings within a compilation. Provider construction reports cross-assembly conflicts deterministically.
- **Rich diagnostics**: compile-time errors for unknown function names, ambiguous constructors, invalid cron expressions, mismatched context types, and ambiguous scheduling identities.

### Installation

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

### Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Enums;

// Static cron job (no DI)
[JobFunction("Cleanup", cronExpression: "*/5 * * * *")]
public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
{
    sp.GetRequiredService<ILogger<Program>>().LogInformation("Cleaning up");
    await Task.CompletedTask;
}

// Instance job with primary constructor DI
[JobFunction("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
        => await orders.ProcessAsync(context.Request, ct);
}

// Multiple constructors — mark the target with [JobsConstructor]
public sealed class ComplexJob
{
    [JobsConstructor]
    public ComplexJob(ILogger<ComplexJob> logger, IConfiguration config) { ... }

    public ComplexJob() { } // default ctor ignored by generator

    [JobFunction("ComplexTask")]
    public async Task ExecuteAsync(CancellationToken ct) { ... }
}

// High-priority cron
[JobFunction("DailyReport", cronExpression: "0 0 * * *", taskPriority: JobPriority.High)]
public static Task ExecuteAsync(IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;
```

### Configuration

No runtime configuration. Attributes are the sole interface. Generated output file: `JobsInstanceFactory.g.cs` (a `[ModuleInitializer]` in the consuming assembly).

`[JobFunction]` remains the sole handler discovery model. Requestless descriptors use `RequestType = null`; typed functions are indexed by both durable function name and exact request `Type`. Attribute priority and maximum concurrency remain descriptor metadata, not per-schedule options.

### Dependencies

- `Microsoft.CodeAnalysis.CSharp` (build-time Roslyn API; not a runtime dependency)

### Side Effects

Emits `JobsInstanceFactory.g.cs` at compile time. The generated file:
- Contains a `[ModuleInitializer]` that registers job delegates, request-type mappings, and delegate-free descriptors with the Jobs runtime.
- Contains constructor factory lambdas for each discovered job class.
- Has no effect at runtime beyond the one-time module initializer invocation.

---

## Headless.Jobs.OpenTelemetry

OpenTelemetry instrumentation for `Headless.Jobs` — activity tracing for the full job execution lifecycle plus structured logging.

### Problem Solved

Provides distributed tracing (OpenTelemetry activities/spans), structured log events, and execution metrics for every Jobs job execution without modifying job code. Replaces the default `LoggerInstrumentation` with a full `ActivitySource`-based implementation.

### Key Features

- **Activity tracing**: spans for job execution, enqueue, completion, failure, cancellation, skip, and data seeding.
- **Retry tracking**: activity tags for `current_attempt` and `final_retry_count`.
- **Error telemetry**: `error_type`, `error_message`, `error_stack_trace` tags on failed spans.
- **Caller information**: `enqueued_from` tag captures the call site where the job was enqueued.
- **Parent–child trace linking**: `parent_id` tag links child job spans to their parent.
- **Structured log events**: correlated with trace context for Serilog, NLog, and any `ILogger`-backed sink.

### Installation

```bash
dotnet add package Headless.Jobs.OpenTelemetry
```

### Quick Start

```csharp
using OpenTelemetry.Trace;

// 1. Add Jobs with OpenTelemetry instrumentation
builder.Services.AddHeadlessJobs().AddOpenTelemetryInstrumentation(); // replaces LoggerInstrumentation with OTel

// 2. Configure the OpenTelemetry pipeline to include the Jobs ActivitySource
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Headless.Jobs") // the Jobs ActivitySource name
            .AddConsoleExporter(); // or Jaeger, OTLP, Azure Monitor, etc.
    });
```

### Configuration

`AddOpenTelemetryInstrumentation()` takes no options — it replaces the default `LoggerInstrumentation` singleton with `OpenTelemetryInstrumentation`. The `ActivitySource` name is `"Headless.Jobs"`. Add it to your tracing pipeline's `AddSource(...)` call to activate spans.

Activity tag reference:

| Tag | Example |
|-----|---------|
| `headless.jobs.job.id` | `123e4567-…` |
| `headless.jobs.job.type` | `TimeJob`, `CronJob` |
| `headless.jobs.job.function` | `ProcessOrder` |
| `headless.jobs.job.priority` | `Normal`, `High`, `Low`, `LongRunning` |
| `headless.jobs.job.machine` | `web-01` |
| `headless.jobs.job.parent_id` | parent job GUID |
| `headless.jobs.job.enqueued_from` | `OrderController.Create (Program.cs:42)` |
| `headless.jobs.job.retries` | `3` |
| `headless.jobs.job.current_attempt` | `2` |
| `headless.jobs.job.final_status` | `Succeeded`, `Failed`, `Cancelled`, `Skipped` |
| `headless.jobs.job.execution_time_ms` | `1250` |
| `headless.jobs.job.error_type` | `SqlException` |
| `headless.jobs.job.error_message` | `Connection timeout` |

Note: `headless.jobs.job.final_status` emits `Succeeded` (not the former `Done` value). Update dashboards or alerts that matched the literal `Done`.

### Dependencies

- `Headless.Jobs.Abstractions`
- `OpenTelemetry` (≥ 1.7.0 recommended)

### Side Effects

Registers `OpenTelemetryInstrumentation` as the singleton `IJobsInstrumentation`, replacing the default `LoggerInstrumentation`. No other registrations.

---

## Headless.Jobs.EntityFramework

Entity Framework Core persistence provider for `Headless.Jobs` — durable, distributed, multi-node job storage with database-clock lease authority.

### Problem Solved

Provides persistence of time jobs and cron occurrences across restarts and across multiple nodes, using EF Core-mapped tables. Integrates with `Headless.Coordination` for distributed node identity (`node@incarnation`), dead-node recovery, and fail-stop on membership loss.

### Key Features

- **Durable storage**: persists `TimeJobEntity`, `CronJobEntity`, and `CronJobOccurrenceEntity` in EF Core-mapped tables (default schema: `jobs`).
- **`UseEntityFramework(ef => …)`**: the EF registration extension on `JobsOptionsBuilder`.
- **`UseJobsDbContext<TDbContext>(dbOptions, schema?)`**: registers a dedicated `JobsDbContext` with configurable schema.
- **`UseApplicationDbContext<TDbContext>(ConfigurationType)`**: shares an existing application `DbContext` instead of a dedicated one.
- **Database-clock lease authority**: on the EF path, lease renewal comparisons (`LockedUntil`) use the database server clock (`now()`/`GETUTCDATE()`), not the node's `TimeProvider`. Cross-node clock skew cannot reclaim a healthy renewing job.
- **Atomic chain claims**: a root time-job claim leases its direct children and grandchildren to the same owner in one database update; fallback recovery uses the same tree claim and never steals a live queued lease.
- **Bounded compatibility recovery**: EF CAS fallback orders overdue roots by execution time and ID and processes at
  most 100 candidates per sweep, matching the native provider claim ceiling while retaining each row's CAS fence.
- **Durable retry state**: root jobs, descendants, and cron occurrences retain their persisted `RetryCount` when projected for execution.
- **Node identity and recovery**: stamps `node@incarnation` as the row owner; dead-node reclaim driven by `NodeLeft` events plus periodic reconcile (`DeadNodeReconcileInterval`).
- **Fail-fast coordination check**: startup throws `InvalidOperationException` when no coordination provider is registered.
- **Cron-expression caching**: reuses the host's `ICache` (optional). No `ICache` → reads from DB, cache invalidation is skipped. Cache failures are fail-open.
- **DbContext pool**: configurable via `SetDbContextPoolSize(n)` (default 1024).
- **Custom schema**: `SetSchema("custom_schema")` or the `schema` parameter on `UseJobsDbContext`.

### Design Notes

Lease acquisition, renewal, and reclaim on the EF path anchor `LockedUntil` to the **database clock** (`now()` on PostgreSQL, `GETUTCDATE()` on SQL Server), not the node's injected `TimeProvider`. Claims translate the clock expression inside the existing update statement; they do not execute a separate scalar query. In-memory has no database server and uses `TimeProvider`, so EF tests must not assume fake application time controls lease deadlines.

The `JobsDbContext<TTimeJob, TCronJob>.DbContextOptions` constructor must be `public` for the EF pool to resolve it at startup. Validation fails fast at DI build time.

Install `Headless.Jobs.EntityFramework.PostgreSql` or `Headless.Jobs.EntityFramework.SqlServer` and select it inside the same `UseEntityFramework` builder to replace the CAS pickup path with a provider-native atomic claim-and-return operation. The scheduler and persistence contract remain database-agnostic. Register exactly one native claim provider; selecting both fails during registration.

These packages are EF optimization extensions, not standalone persistence providers. The base package owns the full persistence contract plus provider-neutral mapping definitions and claim-transaction lifecycle primitives; each extension owns provider-specific claim execution, including SQL, parameters, and locking semantics.

### Installation

```bash
dotnet add package Headless.Jobs.EntityFramework
```

### Quick Start

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
    });

// Optional: cron-expression caching via ICache
builder.Services.AddHeadlessCaching(setup =>
    setup.UseRedis(o => o.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379"))
);
```

Without a registered coordination provider the durable path throws at startup:
```
InvalidOperationException: The durable Jobs operational store requires a coordination provider.
Register one with AddHeadlessCoordination(...) before AddHeadlessJobs(... UseEntityFramework(...)).
```

### Configuration

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
        ef.SetDbContextPoolSize(512); // default: 1024
        ef.SetSchema("background"); // default: "jobs"
    });
```

### Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Coordination.Abstractions`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Replaces the in-memory `IJobPersistenceProvider` with `JobsEFCorePersistenceProvider`.
- Registers `JobsOwnerIdentityAdapter` (overrides the default `DefaultJobsOwnerIdentity`).
- Registers `JobsDeadOwnerReclaimer`, `DeadOwnerRecoveryBridge`, and `JobsCoordinationStartupGate` hosted services.
- Persists job rows in EF Core-mapped tables under the configured schema.
- Consumes the optional default `ICache` for cron-expression caching.
- Fails fast at startup if no coordination provider is registered.

---

### Error Handling and Retries

#### Retry Configuration

`Retries`, `RetryCount`, and `RetryIntervals` remain the durable retry representation. `Retries` excludes the original execution. `RetryCount` is persisted monotonically before each wait so a recovered process resumes from the consumed budget. Set `Retries` and optional `RetryIntervals` (seconds between attempts) on the entity:

```csharp
await timeJobManager.AddAsync(
    new TimeJobEntity
    {
        Function = "ProcessPayment",
        ExecutionTime = DateTime.UtcNow,
        Request = JobsHelper.SerializeRequest(new { PaymentId = "pay_123" }),
        Retries = 3,
        RetryIntervals = [30, 60, 120], // seconds between attempts
    },
    ct
);
```

- Retries run automatically when a job method throws.
- Status remains `InProgress` during retries; becomes `Failed` after exhaustion.
- `JobFunctionContext.RetryCount` carries the current attempt number.
- If `RetryIntervals` is shorter than `Retries`, the last interval is reused.
- If `RetryIntervals` is null or empty, default is 30 seconds.

Runtime execution uses Polly.Core directly. Configure the reusable pipeline through `JobsOptionsBuilder.ConfigureRetries`:

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureRetries(retry =>
    {
        retry.RetryStrategy = new RetryStrategyOptions
        {
            MaxRetryAttempts = int.MaxValue, // optional global cap; row Retries remains durable
            Delay = TimeSpan.FromSeconds(30),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            MaxDelay = TimeSpan.FromMinutes(5),
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is TimeoutException or HttpRequestException
            ),
        };
        retry.OnExhaustedTimeout = TimeSpan.FromSeconds(30);
        retry.OnExhausted = (context, ct) =>
        {
            context.ServiceProvider.GetRequiredService<ILogger<Program>>()
                .LogError(context.Exception, "Job {JobId} exhausted", context.JobId);
            return Task.CompletedTask;
        };
    });
});
```

`ShouldHandle` is always explicit; cancellation and `TerminateExecutionException` are excluded by default, and that default classification is exposed as `JobsRetryOptions.DefaultShouldHandle` for reuse when replacing `RetryStrategy`. Per-row `RetryIntervals` override Polly delay generation and retain fixed-schedule/final-interval reuse semantics. Otherwise Polly owns fixed, linear, exponential, jittered, capped, or custom delays. Jobs owns leases, durable counters, scheduling, and terminal state. The exhausted callback runs in a fresh DI scope only after an atomic owned transition to `Failed`; timeout or callback failure is logged and contained. Lease renewal remains active during attempts and delays; lease loss cancels the pipeline and prevents stale writes.

Never serialize `RetryStrategyOptions`, `ResiliencePipeline`, `ResilienceContext`, predicates, delay generators, or delegates.

#### Global Exception Handler

`HandleExceptionAsync` fires once per failed attempt — after each attempt's durable retry state is persisted (and once more at final failure) — not only once per job. Use it for per-attempt side effects (alerting, metrics, log sinks); use `JobsRetryOptions.OnExhausted` for the once-only notification after the retry budget is consumed. Each handler invocation is bounded by `OnExhaustedTimeout`; a hanging handler is logged and orphaned so it cannot stall retry progression.

```csharp
public sealed class MyJobExceptionHandler(ILogger<MyJobExceptionHandler> logger)
    : IJobExceptionHandler
{
    public Task HandleExceptionAsync(Exception ex, Guid jobId, JobType jobType, CancellationToken cancellationToken = default)
    {
        logger.LogError(ex, "Job {JobId} ({JobType}) failed", jobId, jobType);
        return Task.CompletedTask;
    }

    public Task HandleCanceledExceptionAsync(Exception ex, Guid jobId, JobType jobType, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Job {JobId} ({JobType}) was cancelled", jobId, jobType);
        return Task.CompletedTask;
    }
}

// Register:
builder.Services.AddHeadlessJobs(options =>
{
    options.SetExceptionHandler<MyJobExceptionHandler>();
});
```

#### Job-Level Error Handling

```csharp
[JobFunction("ProcessOrder")]
public sealed class ProcessOrderJob(ILogger<ProcessOrderJob> logger)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(context.Request, ct);
        }
        catch (HttpRequestException ex) when (context.RetryCount < 3)
        {
            logger.LogWarning(ex, "Transient failure on attempt {Attempt}", context.RetryCount + 1);
            throw; // triggers retry
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Permanent failure, not retrying");
            return; // completes without retry
        }
    }
}
```

#### TerminateExecutionException and Status Control

Throw `TerminateExecutionException` to stop execution immediately without consuming retry budget:

```csharp
using Headless.Jobs.Core.Exceptions;
using Headless.Jobs.Enums;

if (!IsConfigurationValid())
{
    // Defaults to JobStatus.Skipped
    throw new TerminateExecutionException("Configuration invalid");
}

if (isPermamentFailure)
{
    // Explicit final status
    throw new TerminateExecutionException(JobStatus.Failed, "Permanent data error");
}
```

Overloads:
- `TerminateExecutionException("message")` → final status `Skipped`
- `TerminateExecutionException(JobStatus status, "message")` → explicit status
- Both overloads have a variant accepting an `innerException` for diagnostic details

#### Cron Occurrence Skipping

Prevent overlapping cron runs:

```csharp
[JobFunction("LongCron", cronExpression: "0 * * * *")]
public sealed class LongRunningCronJob
{
    public async Task ExecuteAsync(JobFunctionContext context, CancellationToken ct)
    {
        context.CronOccurrenceOperations.SkipIfAlreadyRunning();
        await RunLongTaskAsync(ct);
    }
}
```

`SkipIfAlreadyRunning()` transitions the occurrence to `Skipped` status if another occurrence of the same cron job is currently `InProgress`.

#### Job Status Reference

| Status | Meaning |
|--------|---------|
| `Idle` | Queued, not yet claimed |
| `Queued` | Claimed, waiting for a worker thread |
| `InProgress` | Actively executing (lease renewing) |
| `Succeeded` | Completed successfully |
| `DueDone` | Cron occurrence completed within its due window |
| `Failed` | Retries exhausted or unhandled exception |
| `Cancelled` | Job token cancelled or `context.RequestCancellation()` called; a detected lease loss instead leaves the row `InProgress` for stalled reclaim |
| `Skipped` | `TerminateExecutionException` or `SkipIfAlreadyRunning()` |

#### Node-Death Policy (OnNodeDeath)

When the owning node dies mid-execution, `NodeDeathPolicy` determines the row's fate (default `Retry`):

| Policy | On node death | Use when |
|--------|---------------|----------|
| `Retry` (default) | Row released for re-claim; counts toward retry budget | Job is idempotent — safe to re-run |
| `MarkFailed` | Terminal `Failed`; never re-run | Second run is wrong; surface the failure |
| `Skip` | Terminal `Skipped`; never re-run | Must run at most once |

Set it on the entity or via the fluent builder:

```csharp
// On the entity directly
await timeJobManager.AddAsync(
    new TimeJobEntity
    {
        Function = "ChargeCard",
        OnNodeDeath = NodeDeathPolicy.MarkFailed,
        ExecutionTime = DateTime.UtcNow,
    },
    ct
);

// Via FluentChainJobBuilder
var job = FluentChainJobBuilder<TimeJobEntity>.BeginWith(p =>
    p.SetFunction("ChargeCard").SetOnNodeDeath(NodeDeathPolicy.MarkFailed).SetExecutionTime(DateTime.UtcNow)
);
await timeJobManager.AddAsync(job, ct);

// On a cron job (propagates to all occurrences)
await cronJobManager.AddAsync(
    new CronJobEntity
    {
        Function = "NightlyReport",
        Expression = "0 2 * * *",
        OnNodeDeath = NodeDeathPolicy.Skip,
    },
    ct
);
```

The claim predicate's lease-expiry re-claim arm is gated on `OnNodeDeath == Retry`, so clock skew cannot speculatively re-run `Skip` or `MarkFailed` jobs.

---

## Headless.Jobs.EntityFramework.PostgreSql

### Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with PostgreSQL-native atomic claim-and-return operations under scheduler contention.

This is an optimization extension for `Headless.Jobs.EntityFramework`, not an independent Jobs persistence provider. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns PostgreSQL-specific claim execution, including SQL, parameters, and locking behavior.

### Key Features

- Claims existing time jobs and cron occurrences with `UPDATE ... RETURNING` over a `FOR UPDATE SKIP LOCKED` candidate query.
- Bounds set-based root and fallback-occurrence selection to 100 winners per transaction; skipped or excess work remains eligible for the next scheduler pass.
- Creates cron occurrences with `INSERT ... ON CONFLICT DO NOTHING ... RETURNING` to deduplicate each execution-time and cron-job pair.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.

### Design Notes

`SKIP LOCKED` lets concurrent workers move past candidates locked by another claim transaction. The update, descendant stamping, and returned winners share one explicit transaction, so a rolled-back claim exposes no executable work. PostgreSQL 14 or later is the supported baseline; the underlying primitive exists on older releases, but they are outside this package's tested support target.

### Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.PostgreSql
```

### Quick Start

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

### Configuration

`UsePostgreSqlClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback.

### Dependencies

- `Headless.Jobs.EntityFramework`
- `Npgsql.EntityFrameworkCore.PostgreSQL`

### Side Effects

- Replaces the default Jobs EF claim strategy with the PostgreSQL atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change scheduler cadence, leases, retry policy, or the public persistence contract.

---

## Headless.Jobs.EntityFramework.SqlServer

### Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with SQL Server-native atomic claim-and-output operations under scheduler contention.

This is an optimization extension for `Headless.Jobs.EntityFramework`, not an independent Jobs persistence provider. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns SQL Server-specific claim execution, including SQL, parameters, and locking behavior.

### Key Features

- Selects claim candidates with `UPDLOCK`, `READPAST`, and `ROWLOCK`, then returns winners from the same update through `OUTPUT inserted...`.
- Bounds set-based root and fallback-occurrence selection to 100 winners per transaction to limit lock footprint and escalation risk; skipped or excess work remains eligible for the next scheduler pass.
- Adds `READCOMMITTEDLOCK` when `READ_COMMITTED_SNAPSHOT` is enabled, as required for `READPAST` under read-committed snapshot isolation.
- Creates cron occurrences atomically against the unique execution-time and cron-job key.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.

### Design Notes

`READPAST` skips row locks, not page locks. Page locking or lock escalation can therefore block competing claimers even with `ROWLOCK`, which is a preference rather than a guarantee. The package does not change `LOCK_ESCALATION`; operators should measure contention, lock memory, and workload behavior before applying database-level changes. SQL Server 2019 or later and Azure SQL are the supported targets.

### Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.SqlServer
```

### Quick Start

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

### Configuration

`UseSqlServerClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback. The strategy detects `READ_COMMITTED_SNAPSHOT` and adjusts its locking hints.

### Dependencies

- `Headless.Jobs.EntityFramework`
- `Microsoft.EntityFrameworkCore.SqlServer`

### Side Effects

- Replaces the default Jobs EF claim strategy with the SQL Server atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change lock-escalation settings, scheduler cadence, leases, retry policy, or the public persistence contract.
