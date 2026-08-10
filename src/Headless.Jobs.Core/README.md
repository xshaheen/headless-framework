# Headless.Jobs.Core

Core implementation of the Jobs scheduler: in-memory persistence provider, execution task handler, background services, bounded task scheduler, and the `AddHeadlessJobs` DI extension.

## Problem Solved

Provides reliable background job scheduling with cron expressions, delayed execution, custom task scheduling, retry logic, and bounded in-process execution without any external job scheduler dependencies (Hangfire, Quartz, etc.). The in-memory path works standalone; the durable path composes with `Headless.Jobs.EntityFramework`.

Stored requests may use GZip compression through `UseGZipCompression()`. Decompression is capped at 64 MiB by default; use `UseGZipCompression(maxDecompressedBytes)` when an application deliberately supports a different bounded payload size.

## Key Features

- **`AddHeadlessJobs()`**: single DI entry point; registers managers, background services, and the in-memory persistence provider.
- **`IJobScheduler` facade**: schedules typed or requestless `[JobFunction]` methods through generated descriptor indexes, maps supported options, controls cron pause/resume, and returns persisted entity IDs or locked-transition results.
- **Typed chain enqueue**: `IJobScheduler.EnqueueAsync(JobChain, …)` resolves every node's descriptor, enforces `SchedulerOptionsBuilder.MaxChainDepth` (default 10) before persistence naming the configured limit, and persists the root plus its whole descendant tree atomically through the manager add path.
- **Injected identity and app time**: managers assign persisted IDs through `IGuidGenerator` and stamp audit/scheduling time through `TimeProvider`, including every descendant in a persisted job chain.
- **Scheduler background service**: polls for due time jobs and cron occurrences on `FallbackIntervalChecker` cadence (default 30s); also driven by soft-notification signals for near-zero latency.
- **Bounded task scheduler** (`JobsTaskScheduler`): runs normal jobs as logical worker slots on the shared .NET thread pool, bounds active async executions by `MaxConcurrency` (default `Environment.ProcessorCount`), and honors `High` → `Normal` → `Low` dequeue order. `LongRunning` work receives a dedicated thread within the separate `MaxLongRunningConcurrency` budget (default: the smaller of `MaxConcurrency` and 4). Long-running admission is queued on a detached lane (capped at two parked admissions per slot), so a saturated budget never blocks the dispatch loop; an admission rejected at the cap or dropped by cancellation/shutdown is recovered by the fallback reclaim sweep when its pickup lease lapses.
- **Sliding lease renewal** (#316): jobs verify ownership immediately before user code starts, then extend `LockedUntil` on `LeaseRenewalInterval` cadence; cancel-on-loss if renewal affects zero rows or errors.
- **`DisableBackgroundServices()`**: suppresses background execution; only the managers are registered (useful for enqueue-only nodes and test projects).
- **Seeder API**: `UseJobsSeeder(...)` for startup data seeding; `IgnoreSeedDefinedCronJobs()` to skip auto-seeding of attribute-defined cron jobs.
- **GZip request payloads**: `UseGZipCompression()` compresses serialized request bytes.
- **Exception handler**: `SetExceptionHandler<THandler>()` registers an `IJobExceptionHandler` singleton.
- **Node-death policy enforcement**: claim predicate gates lease-expiry re-claim on `OnNodeDeath == Retry`; clock skew cannot re-run `Skip` or `MarkFailed` jobs.
- **Startup mode**: `SchedulerOptionsBuilder.StartMode` (`JobsStartMode.Immediate` default / `JobsStartMode.Manual`).
- **Tenancy seam**: `HeadlessTenancyBuilder.Jobs(...)` exposes `PropagateTenant()`, `RequireTenantOnEnqueue()`, and `RejectCrossTenantEnqueue()`; time jobs capture the ambient tenant at schedule time and restore it around every execution attempt. See [Tenancy](#tenancy).

## Design Notes

The in-memory provider uses the injected `TimeProvider` for pickup leases. The EF operational store translates `DateTime.UtcNow` inside each claim statement, so both lease-expiry comparison and `LockedUntil` stamping use the **database clock** without a separate clock query. EF renewal and reclaim use the same authority, preventing application/database clock skew from shortening or extending the initial lease.

`AddHeadlessJobs` supplies `TimeProvider.System` and the Version 7 `IGuidGenerator` only as replaceable DI defaults. Runtime services never fall back to ambient static clocks or random GUID creation. A `JobChain` therefore carries no persisted identity or time: `IJobScheduler.EnqueueAsync(JobChain, …)` maps it to an unstamped `TimeJobEntity` tree, and the manager add path assigns missing IDs, parent IDs, and one injected-clock timestamp across the complete graph before persistence.

`SchedulerOptionsBuilder.NodeId` is used as the row owner only on the in-memory single-process path. On the durable path it is overridden by `JobsOwnerIdentityAdapter` (reads `node@incarnation` from `Headless.Coordination`); `NodeId` becomes a pre-registration display fallback only.

On the `DeadNodeReconcileInterval` cadence, the durable fallback also reclaims rows stamped by an owner identity that is absent from the liveness snapshot entirely, including superseded incarnations that were never classified dead and dead identities pruned past retention. This orphaned-owner sweep is the recovery path for owner-stamped `Idle`/`Queued` rows with no execution time, including non-timed chain descendants left behind after an ungraceful restart.

Generated module initializers populate one process-wide canonical catalog. `AddHeadlessJobs` invokes the options callback first so every `AddJobsDiscovery(...)` assembly is loaded, then freezes that catalog exactly once. Repeated builds are idempotent; registrations attempted after discovery or freeze fail deterministically instead of disappearing. `JobFunctionProvider.JobFunctionDescriptors` remains the public configuration-independent descriptor lookup for requestless scheduling.

Each `IHost` receives its own immutable runtime registry projected from the canonical catalog and that host's `IConfiguration`. Cron configuration tokens are resolved only in this host-owned registry. Scheduling, execution, seeding, fallback, managers, and Dashboard operations all consume the injected registry, so multiple hosts in one process can use different configuration without resetting or replacing one another.

Jobs remain `Queued` while waiting for worker and per-function concurrency capacity. The worker performs the owned `Queued` → `InProgress` write immediately before execution, then the execution handler performs one more lease check before invoking user code. If ownership expired while queued, the worker skips the delegate instead of starting an unowned job. Because that transition must happen at admission time, each admitted job issues its own single-row claim write — a tick with N co-due functions performs N claim round trips instead of one batched write; this is the deliberate cost of the single-winner fence.

Claiming a chained time job leases its non-timed descendants down to the configured chain depth (`SchedulerOptionsBuilder.MaxChainDepth`, default 10) to the same owner while leaving their status `Idle`; each child transitions to `InProgress` only when its `RunCondition` is satisfied by the parent's terminal state. A descendant carrying its own execution time is not claimed with the parent — it becomes claimable independently at the later of the parent's matching terminal state and its own time. Recovery keeps the retry budget crash-durable: reclaiming a **started** attempt (an `InProgress` row whose lease lapsed, under `OnNodeDeath.Retry`) increments the persisted `RetryCount` — the interrupted attempt is consumed, per the `NodeDeathPolicy.Retry` contract — while releasing a claimed-but-unstarted (`Idle`/`Queued`) row leaves the count untouched. Execution resumes from the persisted attempt, and a row whose persisted count already exceeds the budget is terminalized `Failed` (with the exhausted callback) instead of running the handler again, so a handler that reliably kills its host cannot re-run forever.

The whole chain executes in-process under the root's single pickup lease. If the owning node crashes mid-chain after the root already completed, the running tail can be orphaned (reclaim returns a non-timed descendant to idle with no execution time and nothing re-picks it up); per-node `OnNodeDeath` policies still apply, and per-node independent pickup is deferred hardening. Lowering `MaxChainDepth` after deeper chains were persisted truncates runtime traversal for those chains.

Deleting a time job deletes its whole descendant chain. The parent/child foreign key is deliberately non-cascading, so both the in-memory and EF providers resolve the subtree explicitly and delete it deepest-first (the EF provider does so inside one transaction); the returned count includes every removed descendant. Deleting a non-root node removes only that node's subtree and leaves its ancestors intact.

A typed job function's stored request is read immediately before the handler runs. A read or deserialization failure fails that attempt and is classified by the normal retry pipeline; the handler is never invoked with a default payload, and cancellation stays cancellation. `JobsRequestProvider.GetRequestAsync` therefore returns `default` only when the job genuinely stored no request.

Dashboard SignalR notifications are best-effort on the whole scheduling path: a hub failure is logged and never aborts a claim enumeration, so a dashboard or backplane outage cannot delay job dispatch. If a claim enumeration does abort for another reason, the rows already claimed in that batch are released back to `Idle` instead of waiting out their lease.

Time-job cancellation is durable and job-ID-only through `IJobScheduler.CancelAsync` or `context.RequestCancellationAsync()`. Idle jobs become `Cancelled` atomically; queued and in-progress jobs retain their status and set `CancelRequested`. The owning execution observes that flag immediately before user code and then on `CancellationObservationInterval`, using the same owner/status fence as lease renewal.

Cron pause/resume is durable and definition-specific. Pause atomically marks the definition and skips pending `Idle` / `Queued` occurrences while preserving `InProgress` work. Resume uses a schedule revision fence so concurrent nodes create at most one occurrence strictly after the injected `TimeProvider` instant, and rebases the definition's schedule watermark to the resume instant — which is what keeps the paused interval from being replayed once misfire recovery exists. Catch-up is no longer outside this contract: see [Misfire recovery](#misfire-recovery).

Ordinary cron dispatch first commits the expected schedule position and its occurrence outcome through one persistence operation. A newly materialized occurrence is `Idle`, unowned, and unleased; only the later claim stamps `Queued`, owner, and lease using the provider's time authority. A crash after materialization therefore leaves exactly one claimable occurrence rather than an advanced position with a missing tick.

Each host owns an independent execution-cancellation registry. Durable cancellation, host shutdown, and lease loss are distinct causes tied to one opaque execution handle. Only a cooperative exit with that execution's exact token after durable observation writes terminal `Cancelled`. Lease loss and cooperative host shutdown leave the row `InProgress` for recovery; an uncooperative handler keeps its natural success/failure result while `CancelRequested` remains audit data. An unrelated `OperationCanceledException` remains a failure and follows retry policy.

Before deploying this version with a relational Jobs store, apply the Jobs migrations for cancellation, cron control, the live-occurrence index, and the cron watermark/recovery fields (`ReconciledThroughUtc`, `NextDueUtc`, `MissedRunGraceSeconds`, `OnMissedRun`, `EvaluationFingerprint`, `FingerprintFailureCount`, `FingerprintRetryAfterUtc`, and occurrence `RecoveredFromUtc`). Quiesce every scheduler node for the migration and start only new binaries afterward; mixed versions do not share the atomic materialization contract. Legacy positions backfill to the uninitialized sentinel and are anchored at store time on first wake, avoiding synthetic backlog. The PostgreSQL demos and SQL Server conformance project contain reference migrations; custom schemas require equivalent DDL plus the fingerprint retry/keyset index. Once watermark, recovery, or fingerprint-defer state is written, downgrade is state-losing and must be blocked unless operators intentionally export or discard that state.

Cron expressions use `RecurringJobOptions.TimeZoneId` when present and otherwise fall back to `SchedulerTimeZone`. Only validated IANA identifiers are accepted. Occurrences remain UTC; a spring-forward occurrence inside an invalid local-time gap is shifted forward by the gap, and an ambiguous fall-back occurrence runs once at the later UTC instant (the standard-time offset).

Jobs uses reusable Polly.Core `ResiliencePipeline` instances for runtime retry execution. `JobsRetryOptions.RetryStrategy` is the public Polly configuration surface, while `Retries`, `RetryCount`, and `RetryIntervals` remain the durable authority. `RetryCount` is persisted before every wait; lease renewal stays active across attempts and delays, and a lost lease cancels the pipeline and fences terminal writes. Per-row `RetryIntervals` override Polly delay generation and reuse their last value when shorter than `Retries`. Polly configuration and delegates are never serialized.

There is no `app.UseJobs()` call — the scheduler starts automatically through the `IHostedService` registrations added by `AddHeadlessJobs`.

## Misfire recovery

A cron definition carries a durable **schedule watermark** — the instant through which its schedule has been
reconciled — plus a **projection** of the first occurrence after it. The watermark records what was *accounted for*
rather than what was promised, so it stays true when a rule change invalidates the derived projection, and a skip
advances it without anything firing.

That record is what makes a missed occurrence detectable at all. Before it, reconciliation state lived only as an
in-memory sleep timer: a process that died mid-sleep left no trace, and on restart simply recomputed from the current
time. The occurrence was gone with nothing to notice it had ever been due.

### When a definition enters recovery

An instant is **pending** when it falls at or before now and the watermark has not passed it — whether or not an
occurrence row exists for it. A definition enters recovery when more than one instant is pending, or when its single
pending instant is older than that definition's grace threshold.

The grace threshold separates ordinary lateness from a genuine miss. It defaults to 60 seconds (matching Quartz) and
is resolved once at creation and persisted **per definition**, so every node evaluates the same threshold. A locally
configured value must never decide whether an instant misfired, or two nodes would disagree about the same tick.

### Policies

| Policy | Behaviour |
|---|---|
| `Coalesce` (default) | Materializes exactly **one** run for the whole unresolved missed window, reporting the first unaccounted-for missed instant as its scheduled instant. |
| `Skip` | Materializes **no** run and simply carries the watermark past the backlog. |

Both leave the watermark at the recovery instant, so a resolved backlog is never reconsidered. A schedule whose
interval is shorter than the scheduler's wake latency will legitimately re-enter recovery on the following wake —
that is the correct outcome, not a fault.

The default matches what Hangfire, Quartz, and systemd independently converged on. Bounded catch-up — replaying more
than one missed occurrence — is deliberately not offered.

Recovery never runs an instant twice and never leaves two live occurrences for one instant. An occurrence already
executing or already finished is stepped past untouched; one that has not begun executing is either repurposed as the
coalesced run or transitioned to `Skipped`.

### Configuring it

```csharp
// Declared in code: seeds the definition when it is first created.
[JobFunction("reports.nightly", "0 0 2 * * *", OnMissedRun = MissedRunPolicy.Skip, MissedRunGraceSeconds = 300)]
public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
```

```csharp
// Scheduler-wide defaults for definitions that declare neither.
builder.ConfigureScheduler(scheduler =>
{
    scheduler.DefaultMissedRunPolicy = MissedRunPolicy.Coalesce;
    scheduler.DefaultMissedRunGraceSeconds = 60;
});
```

**The persisted value is the authority.** The attribute seeds a definition only when it is created and is never
reapplied during startup reconciliation, so a value later changed through `ICronJobManager.UpdateAsync` stays in force
across restarts and redeploys. That single rule is what makes an operator override self-evident without persisting a
provenance marker — and it means changing the attribute in code does **not** change an existing definition.

### What an executing job sees

```csharp
public async Task RunAsync(JobFunctionContext context, CancellationToken cancellationToken)
{
    if (context.IsRecoveryRun)
    {
        // One coalesced run stands in for EVERY occurrence missed during the outage, not just this instant.
        // Treat RecoveredFromUtc as the lower bound of the window to process.
        await ProcessSinceAsync(context.RecoveredFromUtc!.Value, cancellationToken);
        return;
    }

    await ProcessSinceAsync(context.ScheduledFor, cancellationToken);
}
```

`Lateness` reports how late the run actually started. For a recovery run it measures from the first unaccounted-for
missed instant, so it spans the unresolved part of the outage rather than the dispatch delay. It never goes negative.

### Schedule-interpretation drift

An expression and a timezone identifier can stay byte-identical while the instant they resolve to moves — a tzdata
update shifts a zone's transitions, or the cron library changes how it reads a field. Each definition therefore stores
an opaque **evaluation fingerprint** of the rules its projection was derived under; only equality is meaningful.

A background sweep rebases definitions whose fingerprint no longer matches, independently of whether their projection
is due. That independence is the point: a rule change that moves an occurrence *earlier* hides behind the stale later
projection, so a sweep keyed on due-ness would skip exactly the definitions that need it. The rebase anchors the new
projection at or after the current instant, so a tick the changed rules moved into the past is surfaced rather than
replayed as a misfire.

Startup drains one fixed high-water snapshot before scheduler pickup is enabled. Deterministically invalid definitions
are durably deferred with a provider-time exponential retry (`FingerprintFailureCount` / `FingerprintRetryAfterUtc`,
capped at 24h); storage, provider, and unknown failures fail startup closed. The periodic sweep runs every
`FingerprintSweepInterval` (default 1h) in `FingerprintSweepBatchSize` pages (default 100), drains up to 100 consecutive
full pages, performs one bounded keyset wrap, and retains its cursor when that pass bound is reached. Custom providers
must implement the stale-page, fenced-defer, and compare-and-advance SPI with the same store-time and lost-fence rules.

Recovery and rebase outcomes are reported through the framework's existing logging instrumentation. A missed count is
always accompanied by whether it is exact or a lower bound — a long outage on a seconds-resolution schedule stops
counting at a ceiling, and "at least 1000" calls for a different response than "exactly 1000".

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
var recurringId = await scheduler.ScheduleRecurringAsync(
    request,
    "0 0 * * *",
    new RecurringJobOptions { TimeZoneId = "America/New_York" },
    ct
);
var pauseAccepted = await scheduler.PauseCronAsync(recurringId, ct);
var resumeAccepted = await scheduler.ResumeCronAsync(recurringId, ct);

var cleanup = JobFunctionProvider.JobFunctionDescriptors["Cleanup"];
var cleanupId = await scheduler.EnqueueAsync(cleanup, cancellationToken: ct);
var cancellationAccepted = await scheduler.CancelAsync(delayedId, ct);
```

`EnqueueOptions` and `RecurringJobOptions` expose description, durable retries/intervals, and node-death policy; recurring options also expose nullable IANA `TimeZoneId`. Execution time and cron expression are explicit method arguments. Priority remains immutable `[JobFunction]` / descriptor metadata.

Low-level managers are not deprecated. Continue using `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` for CRUD, batching, seeding, custom entities, chains, and advanced persistence workflows.

## Middleware

Jobs middleware uses stage-specific generic attributes. Assembly placement is global; method placement beside `[JobFunction]` targets that function without repeating its durable identity. `Schedule` middleware runs in the manager exactly once per submitted entity, before validation, persistence, or coordinated post-commit work; `Execute` middleware runs inside every retry attempt. Middleware is resolved from a bounded DI scope and must invoke `next` to accept the operation.

```csharp
[assembly: JobScheduleMiddleware<AuditScheduleMiddleware>(Priority = JobMiddlewarePriority.Early)]

// Uncommon fallback: target a descriptor generated by a referenced assembly.
[assembly: JobExecuteMiddleware<ExternalInvoiceMiddleware>(Function = "external.invoice.create")]

public static class InvoiceJobs
{
    [JobFunction("invoice.create")]
    [JobExecuteMiddleware<InvoiceExecutionMiddleware>]
    public static Task CreateAsync(JobFunctionContext<InvoiceRequest> context) => Task.CompletedTask;
}
```

Register each middleware type with DI using the lifetime it needs; the generated dispatcher resolves it from the bounded scheduling or execution scope.

```csharp
builder.Services.AddScoped<AuditScheduleMiddleware>();
builder.Services.AddScoped<ExternalInvoiceMiddleware>();
builder.Services.AddScoped<InvoiceExecutionMiddleware>();
```

The generic constraint requires schedule and execute middleware to implement their matching interfaces. Declarations are ordered by ascending priority, then middleware type identity. The generated dispatcher is direct-call/AOT-safe: runtime plugin discovery, class-handler targeting, and registration after `JobFunctionProvider.Build()` are unsupported. Include function or middleware-only assemblies in `AddJobsDiscovery` when the runtime would not otherwise load them before startup registration freezes.

Facade calls still flow through those managers, so scheduling inside an established `Headless.CommitCoordination` scope preserves the same atomic row write and deferred post-commit side effects.

## Tenancy

Time jobs carry a persisted, length-bounded `TenantId` (`BaseJobEntity.TenantId`, max `JobsTenancyOptions.TenantIdMaxLength = 200`). Enable propagation through the root tenancy seam contributed by this package:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
);
```

`PropagateTenant()` captures the ambient `ICurrentTenant.Id` onto a time job at schedule time (an explicit `EnqueueOptions.TenantId` wins — even when it differs from the ambient tenant, in which case the mismatch logs a warning; opt in to `RejectCrossTenantEnqueue()` to reject that lateral path instead, while explicit values from system scope stay honored for cron fan-out). A persisted `TenantId` is restored around every execution attempt — and around the failure callbacks (`IJobExceptionHandler`, cancellation handler, `OnExhausted`) — including retries, which Polly re-dispatches per attempt. Restoration happens whenever the row carries a tenant, **even on a host with `PropagateTenant()` off**: an explicit `EnqueueOptions.TenantId` is persisted regardless of the flag, so such a job always runs under its tenant rather than silently system-scope; manual restoration is only needed for work that runs outside the Jobs execute pipeline. `RequireTenantOnEnqueue()` rejects a tenantless, non-system enqueue with `Headless.Abstractions.MissingTenantContextException`. Set `EnqueueOptions.IsSystemJob = true` for a deliberate tenantless job; it is rejected when an ambient tenant is present so tenant code cannot escalate to system scope, and it is never persisted. Structural validation (cron-scope rejection, system-job contradictions, blank / over-length bounds) always runs; only ambient capture and strict enforcement are gated by the seam flags. Register a real `ICurrentTenant` source (HTTP claim resolution, `AddHeadlessDbContextServices()`, or a custom implementation) before `AddHeadlessJobs` so propagation resolves a live tenant instead of the `NullCurrentTenant` fallback.

Cron is always system-scope — a cron definition carrying a tenant is rejected with `JobValidatorException`. Run tenant-scoped recurring work by fanning out one tenant-scoped time job per tenant from a system-scope cron handler, passing an explicit `EnqueueOptions.TenantId`:

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

// A tenant-scoped time job. When it runs, the execute middleware has already restored
// ICurrentTenant to the job's TenantId, so tenant-scoped services (EF global filters,
// permission cache) observe the right tenant automatically.
public sealed record TenantReportRequest(string ReportKind);

[JobFunction("GenerateTenantReport")]
public sealed class GenerateTenantReport(IReportService reports)
{
    public Task ExecuteAsync(JobFunctionContext<TenantReportRequest> context, CancellationToken ct) =>
        reports.BuildAsync(context.Request.ReportKind, ct);
}

// A system-scope cron that fans out one tenant-scoped time job per tenant.
[JobFunction("NightlyReportFanOut", cronExpression: "0 2 * * *")]
public static async Task FanOutAsync(IServiceProvider sp, CancellationToken ct)
{
    var scheduler = sp.GetRequiredService<IJobScheduler>();
    var tenants = sp.GetRequiredService<IAppTenantDirectory>(); // application-owned enumeration

    foreach (var tenantId in await tenants.ListActiveTenantIdsAsync(ct))
    {
        // Explicit TenantId is REQUIRED: the cron handler runs system-scope, so there is no
        // ambient tenant for PropagateTenant() to capture here. Relying on ambient capture
        // inside a cron handler would silently persist tenantless jobs.
        await scheduler.EnqueueAsync(
            new TenantReportRequest("nightly"),
            new EnqueueOptions { TenantId = tenantId, Description = $"nightly-report-{tenantId}" },
            ct
        );
    }
}
```

The framework owns no tenant enumeration — fan-out is application code by design (`IReportService` and `IAppTenantDirectory` above are application-owned). The full resolution rules, chain-propagation semantics, and startup diagnostics are documented in `Headless.MultiTenancy`'s domain docs.

## Configuration

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeId = "my-node"; // in-memory path only
        scheduler.MaxConcurrency = 10; // default: processor count
        scheduler.MaxLongRunningConcurrency = 4; // default: min(MaxConcurrency, 4)
        scheduler.IdleWorkerTimeOut = TimeSpan.FromMinutes(1); // default: 1 min
        scheduler.LeaseDuration = TimeSpan.FromMinutes(5); // default: 5 min
        scheduler.LeaseRenewalInterval = null; // null → LeaseDuration / 3
        scheduler.CancellationObservationInterval = null; // null → effective lease-renewal interval
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(30); // default: 30s
        scheduler.PostCommitDrainTimeout = TimeSpan.FromSeconds(30); // default: 30s; > 0, max: 5 min
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc; // default: UTC — never Local (fleet-divergent cron dedup)
        scheduler.DeadNodeReconcileInterval = TimeSpan.FromMinutes(1); // durable path; default: 1 min
        scheduler.StartMode = JobsStartMode.Immediate; // or Manual
        scheduler.MaxChainDepth = 10; // default: 10; range 1..JobChain.MaxStructuralDepth (64)
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

## Observability

Emits OpenTelemetry activity spans under the instrumentation name `Headless.Jobs`, exposed as `JobsDiagnostics.SourceName`. The default `IJobsInstrumentation` emits natively — subscribing the tracing pipeline with `TracerProviderBuilder.AddJobsInstrumentation()` (typed helper, `OpenTelemetry.Api` only — no SDK dependency) or `AddSource(JobsDiagnostics.SourceName)` is the single opt-in; without a listener the activities short-circuit and only the structured log events remain. Spans cover job execution, enqueue, completion, failure, cancellation, skip, and data seeding; framework tags are namespaced `headless.job.*` / `headless.seeding.*` with `snake_case` segments (`headless.job.retry_count`, `headless.job.parent_id`, ...). See [docs/llms/jobs.md](../../docs/llms/jobs.md) for the full tag table.

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Coordination.Abstractions`
- `Headless.Coordination.Core`
- `Headless.DistributedLocks.Abstractions`
- `Headless.MultiTenancy`
- `Headless.Extensions`
- `NCrontab.Signed`
- `Polly.Core`

## Side Effects

- Registers `ITimeJobManager<TimeJobEntity>` and `ICronJobManager<CronJobEntity>` as singletons.
- Registers the non-generic `IJobScheduler` facade against the same configured time/cron entity pair as the managers.
- Registers background hosted services: `JobsInitializationHostedService` (always), `JobsSchedulerBackgroundService`, `JobsFallbackBackgroundService`, and `JobsExecutionTaskHandler` (unless `DisableBackgroundServices()` is called).
- Registers `JobsTaskScheduler` (shared-thread-pool logical workers bounded by active async `MaxConcurrency`; dedicated threads only for `LongRunning`).
- Registers a scheduler-scoped cron schedule cache and the per-host `JobsRequestSerializationOptions` singleton (request JSON options, GZip, decompression cap) consumed by `JobsHelper` — no process-global serializer state.
- Registers the Jobs tenancy primitives: `TenantPropagationScheduleMiddleware` / `TenantRestoreExecuteMiddleware` (`TryAddSingleton`), an `AsyncLocal`-backed `ICurrentTenantAccessor`, and the `ICurrentTenant` fallback (`NullCurrentTenant`, replaced by a real `CurrentTenant` once an HTTP / EF / consumer seam registers one). Inserts the schedule and execute tenancy middleware into the process-global registry once per process; both no-op until the tenancy seam enables `JobsTenancyOptions`.
