---
domain: Jobs (Background Jobs)
packages: Jobs
---

# Jobs (Background Jobs)

> Background job scheduler for .NET: one-off, delayed, and cron jobs declared as classes, registered at compile time by a source generator, stored in memory or in your EF Core database, and coordinated across nodes.

## Orientation

Use `Headless.Jobs` for every background job in an app built on Headless. Do not add Hangfire or Quartz.

| Package | Role | Install when |
| --- | --- | --- |
| `Headless.Jobs` | The scheduler, `AddHeadlessJobs`, the in-memory store, health checks, the host manifest section, and OpenTelemetry | Always |
| `Headless.Jobs.SourceGenerator` | Emits each assembly's `JobsModule` from its `[Job]` classes | In every project that declares jobs or Jobs middleware |
| `Headless.Jobs.EntityFramework` | The durable EF Core store | Jobs must survive a restart or run on several nodes |
| `Headless.Jobs.EntityFramework.PostgreSql` / `.SqlServer` | Native atomic claims and a one-call setup on your application `DbContext` | The database is PostgreSQL or SQL Server (the usual case) |
| `Headless.Jobs.Dashboard` | The Jobs module of the unified dashboard | Operators need to see and act on runs |
| `Headless.Jobs.Abstractions` | The contracts | Only in a library that targets the Jobs interfaces without the scheduler |

The default path:

1. Declare a job as a class that implements `IJob` (no arguments) or `IJob<TArgs>` and carries `[Job("owner.name")]`. Add `Cron = "0 0 2 * * *"` for a recurring job: six fields, seconds first.
2. Register Jobs with `AddHeadlessJobs`, add each assembly's generated module with `options.AddModule<MyApp.JobsModule>()`, and pick the store with `UsePostgreSql<AppDbContext>` or `UseSqlServer<AppDbContext>`.
3. Schedule work with the injected `IJobScheduler`, or with `unit.Jobs` when the job must commit with your business writes.
4. Run `seed` (or `deploy`) as a deploy step so declared cron jobs exist.

## Agent Rules

- **A job is a class, never a method.** It is a top-level, non-abstract, non-generic `public` or `internal` class that implements exactly one of `IJob` or `IJob<TArgs>` and carries `[Job("owner.name")]`. The identity is durable and stored with every run.
- **Add every module explicitly.** Call `options.AddModule<TAssembly.JobsModule>()` for each assembly that declares jobs, including the host's own. Nothing registers implicitly. A library contributes its module with `services.ConfigureJobs(jobs => jobs.AddModule<...>())`, never with `AddHeadlessJobs`. See [declaring.md](jobs/declaring.md#register-modules).
- **There is no `app.UseJobs()`.** `AddHeadlessJobs` registers hosted services that start the scheduler.
- **Schedule through `IJobScheduler`, addressed by type.** `EnqueueAsync(new CloseDayArgs(day), ct)` for a job with arguments, `EnqueueAsync<CloseDayJob>(ct)` for one without. There is no function-name or descriptor overload. Absolute times are `DateTimeOffset`; a `DateTime` does not compile.
- **Pick the receiver for the transaction.** Schedule through `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()` to commit the job with the unit's business writes. The injected `IJobScheduler` and managers never enlist, whatever unit is open around them. See [atomic-enqueue.md](jobs/atomic-enqueue.md).
- **Write handlers for at-least-once execution.** A retry, a crash re-run under `NodeDeathPolicy.Retry`, or a requeue runs the handler again. Use `JobContext.Id`, stable across those runs, as the deduplication key for side effects. A follow-up job scheduled through `unit.Jobs` commits with your writes, not with the job's completion, so key it with `JobOptions.IdempotencyKey` built from `JobContext.Id` plus the step name.
- **Choose `OnNodeDeath` for non-idempotent jobs.** Set `NodeDeathPolicy.MarkFailed` or `Skip`. The default `Retry` re-runs a job after its node crashes.
- **Retries come from a failure policy.** Declare `[Job(id, FailurePolicy = typeof(TPolicy))]`, replace it with `Tune(id, j => j.FailurePolicy<TPolicy>())`, or set `options.DefaultFailurePolicy(...)`. Without any of these a job does not retry. Startup policies (`ConfigureDefaults`, `ConfigureJob<TRequest>`, `Tune(...).Options(...)`) reject `Retries`. See [failures.md](jobs/failures.md#failure-policy).
- **Declare startup-known cron jobs in code.** Use `[Job(..., Cron = ...)]` or `options.SeedRecurringJob(key, ...)`, applied by the `seed` deploy step. `ScheduleRecurringAsync` and `ICronJobManager.AddAsync` insert a new definition on every call, so calling them at startup on N replicas creates N definitions that each fire. See [recurring.md](jobs/recurring.md#seeded-recurring-jobs).
- **Set a distinct `options.SeedOwner("...")` per app** when apps share one Jobs store, and the same owner on every host of one app. Otherwise apps delete each other's seeded rows.
- **Give cron definitions no tenant.** Cron is always system scope. Fan out to tenant-scoped time jobs with an explicit `JobOptions.TenantId`. See [tenancy.md](jobs/tenancy.md).
- **Treat `(Function, ContractVersion, Request bytes)` as a durable contract.** Bump `[Job(ContractVersion = ...)]` when the argument schema changes. Nothing infers it from CLR names.
- **Use a durable store for real work.** Without `Headless.Jobs.EntityFramework` jobs live in memory and are lost on restart.
- **Register coordination on the advanced EF path.** `UsePostgreSql<TContext>` and `UseSqlServer<TContext>` register coordination for you: do not also call `AddHeadlessCoordination`. With `UseEntityFramework(...)`, call `AddHeadlessCoordination(c => c.Use…(conn))` first. Without it, startup throws `InvalidOperationException` naming `AddHeadlessCoordination`.
- **Do not install a Jobs cache package.** Cron-expression caching reuses the host's `ICache` when one is registered.
- **Use `ClusterMaxConcurrency` for a cluster-wide limit.** `MaxConcurrency` limits one node, so N nodes run N times the limit. A cluster limit needs a native claim package. See [execution.md](jobs/execution.md#cluster-wide-concurrency).
- **Prevent overlapping cron runs with `OnOverlap = CronOverlapPolicy.Skip`.** A check inside the job sees only its own node.
- **Keep `ShutdownTimeout` equal to `HostOptions.ShutdownTimeout`.** Jobs cannot read the host's timeout.
- **In tests, call `options.DisableBackgroundServices()`** to schedule without executing, and add `DisableStartupInitialization()` to skip store access at startup. See [testing.md](jobs/testing.md).

## Choose an API

| Task | Use | Not |
| --- | --- | --- |
| Run a job now, after a delay, or at an instant | `IJobScheduler.EnqueueAsync`, `ScheduleAfterAsync`, `ScheduleAsync` | `ITimeJobManager.AddAsync` with a hand-built entity |
| Commit a job with business writes | `unit.Jobs` inside `IUnitOfWorkFactory.RunAsync(db, ...)` | The injected `IJobScheduler` |
| A recurring job known at startup | `[Job(Cron = ...)]` or `SeedRecurringJob`, plus the `seed` step | `ScheduleRecurringAsync` at startup |
| A recurring job a user creates | `ScheduleRecurringAsync` | A seeded declaration |
| Deduplicate repeated enqueues for a while | `JobOptions.IdempotencyKey` plus `IdempotencyTtl` | A keyed schedule |
| One deadline per business key, replaceable | `ScheduleKeyedAsync`, `ReplaceKeyedAsync`, `CancelKeyedAsync` | An idempotency key |
| Run step B after A succeeds or fails | `JobChain` with `Then` and `Catch` | Scheduling B from inside A |
| Bring back a failed run | `RequeueAsync` or `RequeueOccurrenceAsync` | Re-enqueueing a copy |
| Stop without retrying | `throw new TerminateExecutionException(...)` | Swallowing the exception and returning |

## Headless.Jobs

The scheduler: `AddHeadlessJobs`, background services, the bounded worker pool, the in-memory store, health checks, the host manifest section, and OpenTelemetry instrumentation.

### Setup

```bash
dotnet add package Headless.Jobs
dotnet add package Headless.Jobs.SourceGenerator
dotnet add package Headless.Jobs.EntityFramework.PostgreSql
```

Declare a job, register Jobs on your application's PostgreSQL context, and schedule:

```csharp
using Headless.Jobs;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHeadlessJobs(options =>
{
    // One generated module per assembly that declares [Job] classes or Jobs middleware.
    options.AddModule<MyApp.JobsModule>();
    options.UsePostgreSql<AppDbContext>(coordination => coordination.Configure(o => o.ClusterName = "orders"));
});

// A recurring job without arguments: six cron fields, seconds first.
[Job("maintenance.cleanup", Cron = "0 */5 * * * *")]
public sealed class CleanupJob(ILogger<CleanupJob> logger) : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken ct)
    {
        logger.LogInformation("Running cleanup");
        return ValueTask.CompletedTask;
    }
}

// A job with an argument; OrderRequest addresses it.
[Job("orders.process")]
public sealed class ProcessOrderJob(IOrderService orders) : IJob<OrderRequest>
{
    public async ValueTask ExecuteAsync(JobContext<OrderRequest> context, CancellationToken ct) =>
        await orders.ProcessAsync(context.Request, ct);
}

public sealed class OrderService(IJobScheduler scheduler)
{
    public Task<Guid> ScheduleAsync(OrderRequest request, CancellationToken ct) =>
        scheduler.EnqueueAsync(request, new JobOptions { Description = "process-order" }, ct);
}
```

Create the Jobs tables from your `DbContext` model before workers start, and run `dotnet MyApp.dll seed` (or `deploy`) so `maintenance.cleanup` gets its cron row. Use `UseSqlServer<AppDbContext>` on SQL Server. [storage.md](jobs/storage.md) covers the dedicated-context path. For in-memory jobs (tests, local development), omit the store call.

**Verify:**

- `dotnet MyApp.dll describe` lists `maintenance.cleanup` and `orders.process` in the `jobs` section, with `runState` `runs-here`.
- The `jobs` and `jobs-storage-{AppDbContext full name}` readiness checks report `Healthy` once the host has started.
- An `EnqueueAsync` call returns an id, and the handler runs. An argument type no added module declares throws `JobFunctionNotFoundException`.

### Configuration

Scheduler settings, with their defaults:

```csharp
using System.Text.Json;

builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10; // default: processor count
        scheduler.IdleWorkerTimeOut = TimeSpan.FromMinutes(1); // default: 1 min
        scheduler.LeaseDuration = TimeSpan.FromMinutes(5); // default: 5 min
        scheduler.LeaseRenewalInterval = null; // null → LeaseDuration / 3
        scheduler.ShutdownTimeout = TimeSpan.FromSeconds(30); // default: 30s; keep equal to HostOptions.ShutdownTimeout
        scheduler.ProgressReportInterval = TimeSpan.FromSeconds(2); // default: 2s; least time between progress writes
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(30); // default: 30s
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc; // default: UTC; must be an IANA zone, the same on every node
        scheduler.DeadNodeReconcileInterval = null; // durable store; null → coordination DeadRetentionWindow / 2 (15s)
        scheduler.StartMode = JobsStartMode.Immediate; // or Manual
        scheduler.MaxChainDepth = 10; // default: 10; range 1..JobChain.MaxStructuralDepth (64)
        scheduler.NodeId = "my-node"; // in-memory store only; the durable store uses the coordination owner tag
    });

    options.ConfigureStorage(storage => storage.Schema = "background"); // default: "headless"
    options.SetExceptionHandler<MyJobExceptionHandler>();
    options.UseGZipCompression(); // compress stored arguments; decompression capped at 64 MiB
    options.SeedOwner("billing"); // default: "default"; distinct per app that shares the Jobs store
    options.ConfigureRequestJsonOptions(json =>
    {
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
});
```

| Setting | Where it is described |
| --- | --- |
| `MaxConcurrency`, `MaxLongRunningConcurrency`, priorities | [execution.md](jobs/execution.md#concurrency-and-priority-on-one-node) |
| `LeaseDuration`, `LeaseRenewalInterval`, `FallbackIntervalChecker` | [execution.md](jobs/execution.md#leases) |
| `ShutdownTimeout` | [execution.md](jobs/execution.md#shutdown-and-drain) |
| `SchedulerTimeZone`, `DefaultMissedRunPolicy`, `DefaultMissedRunGraceSeconds`, `DefaultOverlapPolicy`, `FingerprintSweepInterval`, `FingerprintSweepBatchSize` | [recurring.md](jobs/recurring.md) |
| `MaxChainDepth` | [scheduling.md](jobs/scheduling.md#typed-job-chains) |
| `DefaultFailurePolicy`, `ConfigureRetries`, `SetExceptionHandler` | [failures.md](jobs/failures.md) |
| `ConfigureDefaults`, `ConfigureJob<TRequest>` | [scheduling.md](jobs/scheduling.md#set-startup-defaults) |
| `Tune`, `RunOnly`, `Headless:Jobs:Jobs:{identity}` | [declaring.md](jobs/declaring.md#tune-jobs-and-filter-what-a-host-runs) |
| `SeedRecurringJob`, `SeedOwner`, `IgnoreSeedDefinedCronJobs`, `UseJobsSeeder` | [recurring.md](jobs/recurring.md#seeded-recurring-jobs) |
| `ConfigureStorage`, `UseEntityFramework` | [storage.md](jobs/storage.md) |
| `DisableBackgroundServices`, `DisableStartupInitialization` | [testing.md](jobs/testing.md) |

- `UseGZipCompression()` caps decompression at 64 MiB. Use `UseGZipCompression(maxDecompressedBytes)` only when the app deliberately supports a different bounded size.
- `ConfigureStorage(configuration.GetSection("Headless:Jobs:Storage"))` binds the storage options from configuration.
- Configure Jobs tenancy only through `AddHeadlessTenancy(tenancy => tenancy.Jobs(...))`.

### Runtime behavior

- Registers `IJobScheduler`, `ITimeJobManager<TimeJobEntity>`, and `ICronJobManager<CronJobEntity>` as singletons, plus the `unit.Jobs`, `unit.TimeJobs<T>()`, and `unit.CronJobs<T>()` accessors on `IUnitOfWork`.
- Registers the scheduler, fallback, and execution hosted services unless `DisableBackgroundServices()` is called, and a post-commit worker always.
- Registers `TimeProvider.System` and a Version 7 `IGuidGenerator` as replaceable defaults. Jobs never reads an ambient clock or creates random GUIDs itself.
- Builds one frozen job registry per host from the modules that host added, resolved against that host's `IConfiguration`, so several hosts in one process do not interfere.
- Adds the `jobs` and `jobs-scheduler` readiness checks when background services run, and the `jobs` host manifest section always ([operations.md](jobs/operations.md)).
- Adds the tenancy schedule and execute middleware, which do nothing until tenancy is enabled ([tenancy.md](jobs/tenancy.md)).

## Generator diagnostics

Every `Headless.Jobs.SourceGenerator` rule is a compile error in category `Headless.Jobs.SourceGenerator`. Each rule's help link points to its row here.

| Rule | Reported when | Fix |
| --- | --- | --- |
| <a id="hf001"></a>HF001 | The `[Job]` class is neither public nor internal, or is `file`-local. | Make the class `public` or `internal` (no modifier on a top-level class is internal). |
| <a id="hf003"></a>HF003 | The `Cron` value is not a valid six-field (seconds-first) expression. A value starting with `%` names a configuration key and is checked at startup instead. | Use six fields, for example `"0 */5 * * * *"`, or a `%Section:Key` configuration key. |
| <a id="hf004"></a>HF004 | The identity is not in `owner.name` form: it is empty, longer than 200 characters, has no `.` separator or an empty segment, has surrounding white space, or contains a control character. | Pass an identity such as `"billing.close-day"`, whose first segment names the owning module. |
| <a id="hf005"></a>HF005 | Two `[Job]` classes in one compilation use the same identity. Nothing is generated. | Give each job a unique identity; use `ContractVersion` to version one job, not a second identity. |
| <a id="hf007"></a>HF007 | The `[Job]` class is abstract or generic, so a run cannot construct it. | Put `[Job]` on a concrete, non-generic class. |
| <a id="hf008"></a>HF008 | The `[Job]` class is nested in another type. | Move the class to the top level. |
| <a id="hf009"></a>HF009 | The `[Job]` class implements neither `IJob` nor `IJob<TArgs>`, or implements more than one of them. | Implement exactly one: `IJob` for a job without arguments, `IJob<TArgs>` for a job with arguments. |
| <a id="hf011"></a>HF011 | Two `[Job]` classes in one compilation implement `IJob<TArgs>` with the same `TArgs`. Nothing is generated. | Give each job its own argument type; scheduling finds a job by its argument type. |
| <a id="hf012"></a>HF012 | `Priority` is not a defined `JobPriority` value. | Pass a `JobPriority` member. |
| <a id="hf013"></a>HF013 | `MaxConcurrency` or `ClusterMaxConcurrency` is negative. | Pass `0` for unlimited or a positive limit. |
| <a id="hf014"></a>HF014 | An assembly-level middleware `Function` names no job identity published by a referenced assembly. | Correct the identity, or reference the assembly that declares the job. |
| <a id="hf015"></a>HF015 | The same middleware type is declared twice for the same stage, target, and priority. | Remove the duplicate declaration. |
| <a id="hf016"></a>HF016 | Class-level middleware is declared on a class without `[Job]`. | Put the middleware attribute on a `[Job]` class, or move it to the assembly. |
| <a id="hf017"></a>HF017 | Class-level middleware sets `Function`. | Remove `Function`; the decorated `[Job]` class is the target. |
| <a id="hf018"></a>HF018 | Assembly-level middleware `Function` names a job declared in the same assembly. | Declare the middleware on that `[Job]` class instead. |
| <a id="hf019"></a>HF019 | The middleware type, or a type containing it, is private, protected, or `file`-local, so generated code cannot resolve it. | Make the type `public` or `internal`. |
| <a id="hf020"></a>HF020 | `OnMissedRun` is not a defined `MissedRunPolicy` value. | Pass a `MissedRunPolicy` member. |
| <a id="hf021"></a>HF021 | `MissedRunGraceSeconds` is zero or negative. | Pass a positive number of seconds, or leave it unset to inherit the scheduler default. |
| <a id="hf022"></a>HF022 | `OnOverlap` is not a defined `CronOverlapPolicy` value. | Pass a `CronOverlapPolicy` member. |
| <a id="hf023"></a>HF023 | The `FailurePolicy` type does not derive from `Headless.Reliability.FailurePolicy`, is not a class, is abstract, still has an open type parameter (its own or a containing type's), is private, protected, or `file`-local, or has no public parameterless constructor, so the generated factory cannot construct it. A closed generic such as `RetryTwice<Payments>` is accepted. Nothing is generated. | Point `FailurePolicy` at a concrete, closed `public` or `internal` class derived from `FailurePolicy` with a public parameterless constructor. |

## Topics

Read this file on every Jobs task. Read a topic file when its row matches the task.

| Read when | Topic |
| --- | --- |
| Declaring a job class, adding the source generator, registering a library's module, tuning a job per host, splitting jobs between hosts with `RunOnly`, versioning job arguments, adding Jobs middleware, or publishing trimmed or native AOT | [declaring.md](jobs/declaring.md) |
| Scheduling a job now, later, or at an instant; setting per-call or startup options; deduplicating enqueues; scheduling a keyed deadline; cancelling a job; building a job chain; or using `ITimeJobManager` or `ICronJobManager` | [scheduling.md](jobs/scheduling.md) |
| Committing a job in the same transaction as business writes, or deciding between `unit.Jobs` and the injected scheduler | [atomic-enqueue.md](jobs/atomic-enqueue.md) |
| Running jobs in a multi-tenant host, or fanning a cron job out per tenant | [tenancy.md](jobs/tenancy.md) |
| Adding a cron job, seeding recurring jobs, creating one at run time, pausing or resuming one, or handling missed runs, overlap, time zones, or DST | [recurring.md](jobs/recurring.md) |
| Configuring retries and failure policies, handling exceptions, alerting on failed runs, choosing what a node crash does, or requeueing a failed run | [failures.md](jobs/failures.md) |
| Limiting concurrency per node or across the cluster, reporting progress, sizing leases, draining on shutdown, or diagnosing node identity and dead-node recovery | [execution.md](jobs/execution.md) |
| Choosing or wiring the durable store, the PostgreSQL or SQL Server claim package, the schema, or a model you manage yourself | [storage.md](jobs/storage.md) |
| Wiring readiness checks, reading the `describe` manifest, adding OpenTelemetry, reading attempt history or failure fingerprints, or adding the Jobs dashboard | [operations.md](jobs/operations.md) |
| Unit-testing a job handler or configuring a test host that uses Jobs | [testing.md](jobs/testing.md) |
| Implementing your own `IJobPersistenceProvider` | [custom-persistence.md](jobs/custom-persistence.md) |
