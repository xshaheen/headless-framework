---
domain: Jobs (Background Jobs)
packages: Jobs.SourceGenerator
---

# Declare and register jobs

> Part of [Jobs](../jobs.md). Declare job classes, register their generated modules on each host, tune jobs per host, version their arguments, and attach middleware.

## Declaring a job

A job is a class that implements `IJob` or `IJob<TArgs>` and carries `[Job(identity)]`. `ExecuteAsync` receives a `JobContext` (or `JobContext<TArgs>`, whose `Request` holds the deserialized argument) and the cancellation token, and returns `ValueTask`.

```csharp
using Headless.Jobs;

public sealed record ProcessOrderArgs(string OrderId);

// Recurring job without arguments: six cron fields, seconds first.
[Job("reports.daily", Cron = "0 0 0 * * *", Priority = JobPriority.High)]
public sealed class DailyReportJob(IReportService reports) : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
        await reports.BuildDailyAsync(cancellationToken);
}

// Job with a typed argument, scheduled on demand.
[Job("orders.process")]
public sealed class ProcessOrderJob(IOrderService orders) : IJob<ProcessOrderArgs>
{
    public async ValueTask ExecuteAsync(JobContext<ProcessOrderArgs> context, CancellationToken cancellationToken) =>
        await orders.ProcessAsync(context.Request.OrderId, cancellationToken);
}
```

- **Class shape.** The class is top-level, non-abstract, non-generic, `public` or `internal`, and implements exactly one of `IJob` or `IJob<TArgs>`. The generator rejects any other shape at compile time ([diagnostics](../jobs.md#generator-diagnostics)).
- **Identity.** The identity is `owner.name`, at most 200 characters, and is the durable function name stored with every run. Its first segment names the owning module. It compares ordinally and case-sensitively, so `Invoice.Send` and `invoice.send` are two jobs.
- **Attribute properties.** `Cron`, `TimeZone`, `Priority`, `MaxConcurrency`, `ClusterMaxConcurrency`, `ContractVersion`, `FailurePolicy`, `OnMissedRun`, `MissedRunGraceSeconds`, and `OnOverlap` are named properties of `JobAttribute`. Priority (`JobPriority.Normal`, `High`, `Low`, `LongRunning`), `MaxConcurrency` (per node), and `ClusterMaxConcurrency` (across nodes) are the job's defaults. A host overrides them with [`Tune`](#tune-jobs-and-filter-what-a-host-runs).
- **Addressing.** A job with arguments is addressed by its argument type, because each argument type belongs to exactly one job. A job without arguments is addressed by its class.
- **Construction.** Each run builds the class from that run's DI scope with `ActivatorUtilities.CreateInstance`, so constructor parameters resolve like scoped services, including `[FromKeyedServices]`. When a class has several public constructors, mark the one to use with `[ActivatorUtilitiesConstructor]`.

`JobContext` exposes `Id`, `Type`, `RetryCount`, `IsDue`, `ScheduledFor`, `FunctionName`, `Lateness`, recovery metadata (`IsRecoveryRun`, `RecoveredFromUtc`), lineage (correlation and causation ids), `ReportProgress`, and `RequestCancellationAsync()` for time jobs. `Id` is the row id and stays the same across retries, crash re-runs, and requeues, so use it as the deduplication key for side effects.

**Verify:** the job's identity appears in the `jobs` section of `describe` output ([host manifest](operations.md#host-manifest)). A job missing there was not declared correctly or its module was not added.

---

## Headless.Jobs.SourceGenerator

A Roslyn incremental source generator that turns `[Job]` classes and Jobs middleware attributes into a module the host registers. It removes reflection and manual registration.

### Setup

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

Add the package to every project that declares `[Job]` classes or Jobs middleware. Then add that project's generated module on the host:

```csharp
using Headless.Jobs;

builder.Services.AddHeadlessJobs(options => options.AddModule<MyApp.JobsModule>());
```

The generator takes no configuration: the attributes are its only input.

**Verify:** the project compiles with no `HF` diagnostic, and `describe` lists the module's jobs in the `jobs` section.

### Generated module

- **Name.** The generator emits `JobsModule.g.cs`, which declares a public `JobsModule` (an `IJobsModule`) in the namespace named after the assembly: `MyApp.JobsModule` for assembly `MyApp`. An assembly name that is not a valid C# namespace is mapped one `.`-separated segment at a time. A character that cannot appear in an identifier becomes `_`, a segment that starts with a digit gets a `_` prefix, an empty segment becomes `_`, and a C# keyword is escaped with `@`. So `Orders-Api` gives `Orders_Api.JobsModule`, `2024.Billing` gives `_2024.Billing.JobsModule`, and `Orders.class` gives `Orders.@class.JobsModule`.
- **Explicit registration.** Nothing registers until a host calls `AddModule<…JobsModule>()`. There is no module initializer and no runtime assembly scanning. An assembly whose module is not added contributes no jobs or middleware, even when it is loaded. An assembly that declares no `[Job]` class or middleware gets no module.
- **Contents.** The module registers each job's descriptor (`JobFunctionDescriptor`: identity, argument type, cron metadata, priority, concurrency, contract version), its argument type, and its middleware. One typed invoker per job builds the class and calls `ExecuteAsync` without reflection.
- **No public catalog.** The generator emits no list of job names. Scheduling code addresses a job by its argument type or its class.
- **Errors.** Every generator rule is a compile error. The [diagnostics table](../jobs.md#generator-diagnostics) lists each rule with its fix.

---

## Register modules

Add every generated module with `options.AddModule<TAssembly.JobsModule>()`, including the host's own assembly.

A library or module that owns jobs contributes its generated module from its own `Add{Module}` entry point with `services.ConfigureJobs(...)`. It never calls `AddHeadlessJobs`, which the host owns:

```csharp
using Headless.Jobs;

public static class BillingSetup
{
    public static IServiceCollection AddBilling(this IServiceCollection services) =>
        services.ConfigureJobs(jobs =>
            jobs.AddModule<Billing.JobsModule>().Tune("billing.close-day", job => job.Concurrency(1))
        );
}

// Worker host: order does not matter, because contributions apply when the host's registry is built.
builder.Services.AddHeadlessJobs(options =>
{
    options.AddModule<MyApp.JobsModule>();
    options.Tune("orders.process", job => job.Concurrency(4).Priority(JobPriority.High));
    options.RunOnly("billing.*", "orders.process");
});
builder.Services.AddBilling();
```

- `JobsContributionBuilder` is not generic, so the module does not need the host's entity types. It offers `AddModule<TModule>()` and `Tune(identity, ...)`.
- Contributions apply in the order they were added, before or after `AddHeadlessJobs`. Contributing one module twice is harmless. A host that never calls `AddHeadlessJobs` ignores contributions.
- Each host builds its own registry from the modules it adds and freezes it when the registry is first resolved. Hosts in one process may add different modules and bind different configuration.
- Two modules that declare one identity, one argument type, or one job class fail startup with an error that names both modules. Within one compilation the generator reports the same conflicts as `HF005` and `HF011`.

**Verify:** a missing module surfaces as `JobFunctionNotFoundException` on the first `EnqueueAsync` for its argument type, before anything is persisted. A duplicate fails host startup.

## Tune jobs and filter what a host runs

Tune a declared job's deployment settings by identity with `Tune(identity, job => ...)` on `JobsOptionsBuilder` or `JobsContributionBuilder`. `JobTuningBuilder` sets:

- `Concurrency(int)`: the per-node limit.
- `ClusterConcurrency(int)`: the limit across every node (see [Cluster-wide concurrency](execution.md#cluster-wide-concurrency)).
- `Priority(JobPriority)`.
- `FailurePolicy<TPolicy>()` or `FailurePolicy(p => ...)` (see [Failure policy](failures.md#failure-policy)).
- `Options(...)`: the node-death override for that job.
- `UseExecuteMiddleware<T>()` and `UseScheduleMiddleware<T>()`: per-job middleware resolved from DI.

Tuning never declares a job or changes its identity, argument type, or cron schedule.

Configuration tunes the same settings without a redeploy and binds after every `Tune` call, from `Headless:Jobs:Jobs:{identity}`. Only `Concurrency`, `ClusterConcurrency`, `Priority`, and the `FailurePolicy` section (`ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, `DelayedMaxDelay`) are accepted:

```json
{
  "Headless": {
    "Jobs": {
      "Jobs": {
        "billing.close-day": {
          "Concurrency": 2,
          "ClusterConcurrency": 4,
          "Priority": "High",
          "FailurePolicy": { "DelayedRetries": 3 }
        }
      }
    }
  }
}
```

An unknown identity, an unknown setting, or an invalid value in a `Tune` call or in configuration fails startup.

**Split execution between hosts** that share modules with `RunOnly(...)` on `JobsOptionsBuilder`. Each entry is an exact identity or an `owner.*` pattern that matches the text before the first `.`. Calls accumulate. An entry that matches no registered job fails startup.

- A job outside the filter stays registered. This host still schedules it, seeds its cron definition, and shows it in the dashboard. It never claims or runs it, so a host without the filter must run it.
- A chain step without its own execution time runs with the root that claimed it, whatever the filter says.
- On the durable store each node publishes the functions it claims in its coordination metadata under `headless.jobs.claimed-functions`, and its host `MaxConcurrency` under `headless.jobs.max-concurrency` ([coordination.md](../coordination.md#node-metadata)). The run filter is fixed for the node's lifetime. The dashboard flags a function no live node claims.

**Verify:** the `jobs` section of `describe` shows the effective `maxConcurrency`, `clusterMaxConcurrency`, `priority`, and `failurePolicy` after tuning and configuration, and `runState` is `runs-here` or `registered-not-run-here` per the filter.

## Version job arguments

Treat `(Function, ContractVersion, Request bytes)` as a durable executable contract. Every stored run keeps its own copy, and a cron occurrence copies its definition's tuple when it is created, so editing the definition later does not change an existing occurrence.

- Declare the argument schema version with `[Job("invoice.create", ContractVersion = "schema-v2")]`. The default is `JobContract.InitialVersion` (`"1"`). Change it explicitly when the argument schema changes; nothing infers it from CLR names.
- A version holds 1 to 100 characters and an identity 1 to 200. Both compare ordinally. Leading or trailing whitespace, control characters, and invalid Unicode are rejected, never trimmed.
- A node runs a stored row only when the stored version equals its registered version. On a mismatch, or when the node does not register the function at all, it logs a diagnostic and returns the row to `Idle` with its retry budget unchanged, so a compatible node can claim it. The first incompatible node never fails the job. When no node registers the stored version, the row waits until you deploy one that does.
- One identity never has two versions registered at once. Use `ContractVersion` to version a job, not a second identity.

**Verify:** the `jobs` section of `describe` shows each function's `contractVersion`. A row stuck `Idle` with the execution span tag `headless.job.contract_version_error` means no running node registers its version.

## Middleware

Jobs middleware attaches to two stages:

- `Schedule` middleware runs once per submitted entity, before validation, persistence, or post-commit work.
- `Execute` middleware runs inside every attempt, so it runs again on each retry.

Declare it with stage-specific generic attributes. An assembly-level attribute applies to every job. A class-level attribute on a `[Job]` class targets that job. A host can also attach middleware to one job with `Tune(identity, job => job.UseExecuteMiddleware<T>())` or `UseScheduleMiddleware<T>()`.

```csharp
[assembly: JobScheduleMiddleware<AuditScheduleMiddleware>(Priority = JobMiddlewarePriority.Early)]

[Job("invoice.create")]
[JobExecuteMiddleware<InvoiceExecutionMiddleware>]
public sealed class CreateInvoiceJob : IJob<InvoiceRequest>
{
    public ValueTask ExecuteAsync(JobContext<InvoiceRequest> context, CancellationToken ct) => ValueTask.CompletedTask;
}
```

To target a job a referenced assembly declares, set `Function` on an assembly-level attribute. The generator reports `HF014` unless a referenced assembly publishes that identity:

```csharp no-compile
[assembly: JobExecuteMiddleware<ExternalInvoiceMiddleware>(Function = "external.invoice.create")]
```

Register each middleware type in DI with the lifetime it needs. It resolves from the scheduling or execution scope:

```csharp
builder.Services.AddScoped<AuditScheduleMiddleware>();
builder.Services.AddScoped<ExternalInvoiceMiddleware>();
builder.Services.AddScoped<InvoiceExecutionMiddleware>();
```

- Schedule middleware implements `IJobScheduleMiddleware` and execute middleware `IJobExecuteMiddleware`. Each must call `next` to accept the operation.
- Declarations run in ascending priority, then by middleware type identity.
- The module of the assembly that declares the middleware registers it, so add that module with `AddModule` even for a middleware-only assembly.
- Middleware cannot be added after the host's registry freezes. Runtime plugin discovery is unsupported.
- `AddHeadlessJobs` always adds the tenancy schedule and execute middleware at `JobMiddlewarePriority.Tenancy`. They do nothing until tenancy is enabled ([tenancy.md](tenancy.md)).

**Verify:** a test that schedules a job through `IJobScheduler` observes the schedule middleware's effect. A missing DI registration throws when the middleware first resolves.

## Trimming and native AOT

`Headless.Jobs.Abstractions` and `Headless.Jobs` are AOT-compatible. Generated modules call job classes and middleware directly. A trimmed or native AOT host must still:

- Supply JSON metadata for every job argument type. Without a `JsonSerializerContext`, the first enqueue or execution of a typed job throws `NotSupportedException` naming the type. When reflection-based serialization is allowed (the default outside trimmed and AOT publishing), a missing resolver falls back to reflection.

  ```csharp no-compile
  [JsonSerializable(typeof(CreateInvoice))]
  internal sealed partial class JobArgsJsonContext : JsonSerializerContext;

  options.ConfigureRequestJsonOptions(json => json.TypeInfoResolverChain.Insert(0, JobArgsJsonContext.Default));
  ```

- Use a store other than `Headless.Jobs.EntityFramework`. The EF package and its PostgreSQL and SQL Server packages are not AOT-compatible, because EF Core generates query code at run time.
- Leave `Headless.Jobs.Dashboard` out. Its endpoints rely on reflection.

**Verify:** a native AOT publish reports no trim or AOT warning from the Jobs packages, and the first enqueue of each argument type succeeds.
