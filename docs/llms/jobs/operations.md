---
domain: Jobs (Background Jobs)
packages: Jobs.Dashboard
---

# Operate and observe jobs

> Part of [Jobs](../jobs.md). Readiness checks, the `describe` host manifest, OpenTelemetry traces and metrics, attempt history, failure fingerprints, and the Jobs dashboard module.

## Readiness and host manifest

### Health checks

Jobs registers its readiness checks itself. There is no `AddJobsHealthChecks()` call. Each check carries the `ready`, `headless`, and `jobs` (`HeadlessHealthCheckTags.Jobs`) tags. `jobs` and `jobs-scheduler` also carry `host-state` (`HeadlessHealthCheckTags.HostState`), so the [`check` command](../utilities.md#health-check-command) skips them before the host starts. Each follows the rules in [Health checks](../utilities.md#health-checks), and `RemoveHealthChecks` removes one.

| Check | Registered when | Failure status | Fails when |
| --- | --- | --- | --- |
| `jobs` | Background services run (no `DisableBackgroundServices()`) | `Unhealthy` | Activation has not finished (the cron fingerprint drain after start is still running or retrying a failure), or host shutdown has begun. The host stops claiming work at shutdown, so a load balancer stops routing to it before the drain |
| `jobs-scheduler` | Background services run | `Degraded` | The scheduler loop is not running: someone called `IJobsHostScheduler.StopAsync`, or `StartMode = JobsStartMode.Manual` and nothing started it. The host stays in rotation while the check reports it dispatches nothing |
| `jobs-storage-{DbContext full name}` | `UseEntityFramework` with a Jobs context; also tagged `database` | `Unhealthy` | `Database.CanConnectAsync` fails on a context from the store's own pool. The probe writes nothing |

A scheduling-only host (`DisableBackgroundServices()`) gets only the storage check. To make `jobs-scheduler` fail readiness, call `ConfigureHeadlessHealthCheck("jobs-scheduler", o => o.FailureStatus = HealthStatus.Unhealthy)`, before or after `AddHeadlessJobs`.

### Host manifest

`AddHeadlessJobs` writes the `jobs` section of the host manifest that `RunHeadlessAsync(args)` prints for `describe` and that `MapHeadlessHostManifest` serves ([utilities.md](../utilities.md#host-manifest)). The section reads the host's frozen job registry, so it shows the tuning configuration applied and this host's `RunOnly` filter. A host without `AddHeadlessJobs` has no `jobs` section.

```json
{
  "functions": [
    {
      "name": "billing.close-day",
      "contractVersion": "1",
      "kind": "cron",
      "cronExpression": "0 0 2 * * *",
      "priority": "normal",
      "maxConcurrency": 2,
      "clusterMaxConcurrency": 4,
      "missedRunPolicy": "coalesce",
      "missedRunGraceSeconds": 60,
      "overlapPolicy": "skip",
      "failurePolicy": {
        "immediateRetries": 0,
        "delayedRetries": 3,
        "delayedInitialDelaySeconds": 10,
        "delayedMaxDelaySeconds": 300
      },
      "runState": "runs-here"
    }
  ]
}
```

- **One entry per registered job,** ordered by name. `kind` is `cron` for a job with a schedule and `time` for one that runs only when scheduled. `cronExpression`, `missedRunPolicy`, `missedRunGraceSeconds`, and `overlapPolicy` appear only on cron jobs. An undeclared policy shows the scheduler default the seed would apply.
- **Effective tuning.** `priority` is `normal`, `high`, `low`, or `long-running`. `maxConcurrency` and `clusterMaxConcurrency` are the values after every `Tune` call and configuration entry, so `Headless:Jobs:Jobs:billing.close-day:Concurrency = 2` shows as `"maxConcurrency": 2`.
- **`failurePolicy`** always carries `immediateRetries` and `delayedRetries`. The delay fields appear only when `delayedRetries` is above zero, `failOn` (exception type names) only when the policy lists types, and `failWhenRules` (a count) only when it has predicates.
- **`runState`** is `runs-here` when this host runs background services and its `RunOnly` filter admits the job, and `registered-not-run-here` otherwise.
- **Not described:** the store, connection strings, and runtime state such as paused definitions or queued rows.

## OpenTelemetry instrumentation

Tracing and metrics are built into `Headless.Jobs`. There is no separate instrumentation package and no options. The spans and the meter share one name, `JobsDiagnostics.SourceName` (`"Headless.Jobs"`). Job code needs no changes.

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddHeadlessJobs(); // instrumentation is built in; there is no extra call

// Subscribing is the only opt-in: nothing is recorded until a listener attaches.
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddJobsInstrumentation() // AddSource(JobsDiagnostics.SourceName)
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddJobsInstrumentation() // AddMeter(JobsDiagnostics.SourceName)
            .AddOtlpExporter();
    });
```

- `AddJobsInstrumentation()` exists on `TracerProviderBuilder` (namespace `OpenTelemetry.Trace`) and `MeterProviderBuilder` (namespace `OpenTelemetry.Metrics`). `AddSource` or `AddMeter` with the source name is equivalent.
- A host with no listener pays one listener check per event and logs as before.
- To replace the spans, register your own `IJobsInstrumentation` after `AddHeadlessJobs()`.
- A tenant-scoped job's execution span carries `tenant.id` unless `TenantTelemetryOptions.EnrichTraces` is off. The tenant is never a metric tag.

**Verify:** after one job runs, your trace backend shows a `job.execute.timejob` span linked to the `job.schedule` span of the request that scheduled it, and `headless.jobs.execution.outcomes` has a `succeeded` point.

### Trace linking

Scheduling and running a job are separate traces, joined by a span link:

1. Every write that schedules a run starts a `job.schedule` span (kind `Producer`) as a child of the caller's current span. The row stores that span's W3C `traceparent` in its `TraceParent` column. When nothing listens to `Headless.Jobs`, the row stores the caller's current span instead, so a host that traces only ASP.NET Core still links its runs. With no current span, the row stores nothing.
2. The run starts its execution span as the root of a new trace, linked to the stored context. It never inherits the poll loop's span, the dispatching request, or a chain parent's span. A stored value that cannot be parsed starts an unlinked root instead of failing the run.

Jobs start with execution-context flow suppressed, so a job never inherits `Activity.Current`, a log scope, culture, or any other `AsyncLocal` from the request that scheduled it. The span link carries the trace lineage, correlation and causation carry the business lineage, and the tenant comes from the job's stored tenant.

| Write | `TraceParent` stored |
| --- | --- |
| Scheduling a time job, a batch, or a chain | The `job.schedule` span of that call, on every row of the tree (one span for a batch) |
| Keyed scheduling, including a supersede | The superseding call's span |
| Requeue of a time job or cron occurrence | Replaced with the requeue's span |
| Dashboard run-now of a cron job | The run-now request's span, on the on-demand occurrence |
| Ordinary update of a time job | Kept |
| Occurrence the scheduler generates from a cron definition | None: its run starts an unlinked root |
| Cron definition | Not stored |

The column holds one version-00 W3C `traceparent` (55 characters), whatever propagator the host configures. `tracestate` is not kept.

### Spans

| Span | Kind | When | Tags |
| --- | --- | --- | --- |
| `job.schedule` | Producer | Each scheduling write listed above | `headless.job.type`; `headless.job.function` when one function is scheduled; `headless.job.id` for a single time job, a requeue, or a run-now |
| `job.execute.timejob`, `job.execute.cronjoboccurrence` | Internal | One per run, a new trace root linked to the stored `TraceParent` | See the execution tags below |
| `job.complete` | Internal | A run finished, child of the execution span | `headless.job.id`, `headless.job.function`, `headless.job.duration_ms`, `headless.job.success`; status `Ok` or `Error` |
| `job.fail` | Internal | A run failed | `headless.job.id`, `headless.job.function`, `headless.job.retry_count`, `exception.type`, `exception.message`, `exception.stacktrace`; status `Error` |
| `job.cancel` | Internal | A run was cancelled | `headless.job.id`, `headless.job.function`, `headless.job.cancellation_reason`; status `Error` |
| `job.skip` | Internal | A run was skipped | `headless.job.id`, `headless.job.function`, `headless.job.skip_reason` |
| `job.deserialize.fail` | Internal | The stored request could not be deserialized | `headless.job.id`, `headless.job.function`, `exception.type`, `exception.message` |
| `seeding.start`, `seeding.complete` | Internal | Cron definition seeding | `headless.seeding.type`, `headless.seeding.environment` (the instance identifier) |

Execution span tags:

| Tag | Value |
| --- | --- |
| `headless.job.id` | Row id |
| `headless.job.type` | `TimeJob` or `CronJobOccurrence` |
| `headless.job.function` | Job identity, such as `orders.process` |
| `headless.job.contract_version` | Contract version of the stored request |
| `headless.job.correlation_id`, `headless.job.causation_id` | Business lineage, when set |
| `headless.job.priority` | `Normal`, `High`, `Low`, or `LongRunning` |
| `headless.job.machine` | Instance identifier of the node that runs it |
| `headless.job.retry_count` | Maximum retry attempts configured for the row (on `job.fail`, the attempts consumed) |
| `headless.job.parent_id` | Parent row id, for a chain child |
| `headless.job.run_condition` | Run condition, for a chain child |
| `headless.job.is_due`, `headless.job.is_child` | Whether the run was due, and whether it runs as a chain child |
| `headless.job.current_attempt` | The attempt in progress, from 1 |
| `headless.job.final_status` | The status the run ended with |
| `headless.job.final_retry.count` | Retry attempts consumed when the run ended |
| `headless.job.cancellation.reason` | Set when a durable cancellation stopped the run |
| `headless.job.skip.reason` | Why the run was skipped |
| `headless.job.contract_version_error` | Set when this node does not register the stored contract version; the row is released for another node |
| `exception.type` | Short type name of the last exception, on a failed run |

Exception messages, stacks, and reasons on spans hold at most 1,024 characters.

### Metrics

| Instrument | Type | Unit | Tags | Measures |
| --- | --- | --- | --- | --- |
| `headless.jobs.execution.duration` | Histogram | `s` | `headless.jobs.function`, `headless.jobs.outcome`, `error.type` | Runs whose handler was invoked, from the first attempt to the outcome |
| `headless.jobs.execution.outcomes` | Counter | `{run}` | `headless.jobs.function`, `headless.jobs.outcome`, `error.type`, `headless.jobs.lease_loss.reason` | Runs whose terminal write the store accepted, including runs whose handler never ran, such as a skipped chain child, and runs that ended on lease loss (`lease_lost`) |
| `headless.jobs.completion.rejected` | Counter | `{run}` | `headless.jobs.function`, `headless.jobs.outcome` | Terminal writes that matched no row because a reclaim took the row first; the outcome is the one the write tried to store |
| `headless.jobs.reclaims` | Counter | `{row}` | `headless.jobs.reclaim.reason`, `headless.jobs.reclaim.action` | Rows a reclaim sweep moved out of a lease its owner no longer held |
| `headless.jobs.execution.retries` | Counter | `{attempt}` | `headless.jobs.retry.kind` | Attempts beyond the first |
| `headless.jobs.queue.lag` | Histogram | `s` | `headless.jobs.trigger` | How late a run started after the instant it was due, clamped at zero |
| `headless.jobs.claim.conflicts` | Counter | `{row}` | `headless.jobs.claim.strategy` | Rows a claim selected and lost to another node |
| `headless.jobs.cron.occurrences.skipped` | Counter | `{occurrence}` | `headless.jobs.skip_reason`, `headless.jobs.function` | Due cron occurrences recorded as skipped instead of running |
| `headless.jobs.post_commit_signals.dropped` | Counter | `{signal}` | `headless.jobs.drop_reason` | Post-commit signals dropped before the worker ran them; the next poll picks up the row |
| `headless.jobs.cron.fingerprint_sweep.deferred` | Counter | `{pass}` | `headless.jobs.deferral_reason` | Cron rule sweeps deferred for [fleet agreement](recurring.md#fleet-agreement) |

Tag values:

| Tag | Values |
| --- | --- |
| `headless.jobs.function` | A job identity this host registers, or `_other` for any other name read from a stored row, so a stray row cannot grow the series without limit |
| `headless.jobs.outcome` | `succeeded` (including a success the fallback sweep recorded), `failed`, `cancelled`, `skipped`, or `lease_lost`. The first four count once the store holds that status. `lease_lost` is not a status: the run stopped without owning its row, counted once per run, and its `OnNodeDeath` policy settles it on reclaim. A run a graceful stop hands back records nothing |
| `headless.jobs.reclaim.reason` | `lease_expired` (a lease lapsed), `dead_node` (membership declared the owner dead), or `orphaned_owner` (the owner is absent from membership) |
| `headless.jobs.reclaim.action` | `released` (back to `Idle`), `marked_failed` (`MarkFailed`), or `skipped` (`Skip`) |
| `error.type` | Fully qualified type name of the exception behind a `failed` outcome; absent on other outcomes |
| `headless.jobs.lease_loss.reason` | On `lease_lost` only, the first reason the run lost its row: `renewal_unmatched`, `renewal_timed_out`, `renewal_failed`, `membership_lost`, `row_not_owned`, `retry_write_unmatched`, `completion_rejected`, `release_unmatched`, or `release_failed` |
| `headless.jobs.retry.kind` | `in_process` (the failure policy retried inside the run) or `recovery` (a run claimed with attempts already spent) |
| `headless.jobs.trigger` | `scheduled`, `recovery` (a missed-run recovery, whose lag spans the outage), or `run_now` |
| `headless.jobs.claim.strategy` | `postgresql`, `sqlserver`, or `cas` |
| `headless.jobs.skip_reason` | `overlap` or `missed_run` |
| `headless.jobs.drop_reason` | `full` (the queue is at capacity) or `stopping` (the host is shutting down) |

Both histograms use buckets from 5 ms to 1 hour (`0.005` to `3600` seconds). Every tag value except `error.type` comes from a fixed set or from the host's registered jobs, so the series count stays bounded.

### Reading lease loss

- **The run's side.** The node that lost the lease records `execution.outcomes` with `lease_lost` once, tagged with the first reason. An alert can tell a slow store (`renewal_timed_out`, `renewal_failed`) from a partition (`membership_lost`) or a peer that reclaimed first (`renewal_unmatched`, `completion_rejected`). A refused terminal write also records `completion.rejected`, never the terminal outcome itself, so a sum of `execution.outcomes` counts each run once.
- **The sweep's side.** The node that reclaims the row records `headless.jobs.reclaims` with the reason and the action its `OnNodeDeath` policy took.
- **The durable record.** The dashboard's run timeline reads [attempt history](#attempt-history): `marked_failed` and `skipped` match `Failed` and `Skipped` attempts whose reason is `Lease lapsed while running!` or `Node is not alive!`, and a `released` running row matches a `Lost` attempt. A row released before it started has no attempt, so `released` can exceed the `Lost` attempts.
- **The log.** Lease loss logs event `3130` at `Warning` with the job, function, and reason. Each function logs at most one such warning per minute, stating how many more it held back; the held-back ones log event `3131` at `Debug`. A loss before the handler starts logs its own warning (`3105`).

## Attempt history

Every execution of a time job or cron occurrence is stored as an attempt, so a run's timeline shows each retry with its node, times, outcome, exception, and failure fingerprint. The run row keeps only the latest exception and a retry count.

- **Tables.** `TimeJobAttempts` (`TimeJobAttemptEntity`) and `CronJobOccurrenceAttempts` (`CronJobOccurrenceAttemptEntity`), keyed by run id and `Number`. Deleting a run deletes its attempts.
- **One attempt per handler invocation.** An in-process retry is a new attempt. `Number` counts from 1 and keeps counting after a requeue. Opening an attempt is best effort: a failed open is logged and the run continues.
- **Outcomes** (`JobAttemptOutcome`):
  - `Succeeded`, including `DueDone`.
  - `Failed`, with the exception text and fingerprint: a handler failure, a run refused because recovery spent its retry budget, or a `MarkFailed` run the store failed after node death, a lapsed lease, or a graceful stop.
  - `Cancelled` for a durable cancellation.
  - `Lost` when a dead-node or lapsed-lease sweep hands a `Retry` run back while an attempt was running.
  - `Skipped` for a handler skip or a `Skip` reclaim.
  - `Released` when a stopping node hands the run back.
- **Times.** `StartedAt` and `EndedAt` are UTC. A close the store writes for a sweep has no known start, so `StartedAt` stays `null`, and the reason (`Node is not alive!`, `Lease lapsed while running!`, or the stop reason) is in `ExceptionMessage`.
- **Reads.** `IJobPersistenceProvider.GetTimeJobAttemptsAsync(id)` and `GetCronJobOccurrenceAttemptsAsync(id)` return a run's attempts in number order, the open one last.
- **Model.** The built-in model customizer and `JobsDbContext` map both tables. A model you manage maps them through `FinalizeJobsModel<TTimeJob>(this)`. A model without them has no attempt history, and the dashboard's late-run figure then reports none.

## Failure fingerprint

A failed time job or cron occurrence stores `FailureFingerprint`, a versioned key that groups failures by cause. Grouping by the first exception line would split one cause into many groups whenever the message carries an id; the fingerprint ignores the message.

- **From the live exception.** The fingerprint uses the innermost exception (after unwrapping `AggregateException` and `TargetInvocationException`) and its top three stack frames outside `System.*` and `Microsoft.*`, without line numbers. It is `v1:` followed by 16 hex characters. An exception the handler wraps itself is fingerprinted as the wrapper. It is computed by `FailureFingerprint.Compute` from `Headless.Extensions` ([extensions.md](../extensions.md)), so fingerprints compare across features.
- **Stored with the failure.** The row keeps the fingerprint of its most recent failed attempt, even after a later attempt succeeds. A requeue clears it.
- **Fixed causes.** A failure the store writes without an exception stores a constant from `JobFailureFingerprints`: `v1:node-not-alive` (dead-node sweep, `MarkFailed`), `v1:lease-lapsed` (lapsed-lease sweep, `MarkFailed`), `v1:interrupted-by-shutdown` (a graceful stop settled a `MarkFailed` run), or `v1:retry-budget-exhausted-by-recovery`.
- **Versioned.** A change to the algorithm is a new prefix. Rows under an older version keep their groups until they age out.
- **Bounds.** The column holds at most `JobFailureFingerprints.MaxLength` (64) characters. The stored exception text is JSON with the outer exception's type and message, its frames, and up to five inner exceptions, at most 4,096 characters, with the message capped at 1,024. Cut text ends with `… [truncated]`.

---

## Headless.Jobs.Dashboard

The Jobs module of the unified dashboard: runs, cron jobs, functions, and live nodes of the host's Jobs store, with Needs-you items, Overview figures, live changes from every node, commands, and Operations.

Read [dashboards.md](../dashboards.md) for the shell, its authentication modes, and the production security boundary. [dashboards/jobs-module.md](../dashboards/jobs-module.md) lists the module's commands, Needs-you items, Overview figures, stream topics, API, and views.

### Setup

```bash
dotnet add package Headless.Jobs.Dashboard
```

```csharp
using Headless.Dashboard;
using Headless.Jobs;

builder.Services.AddHeadlessJobs(jobs => jobs.AddModule<MyApp.JobsModule>());
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "JobsViewers"; host.OperatePolicy = "JobsOperators"; })
    .AddJobs());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessDashboard();
app.Run();
```

`AddJobs()` adds a module with the id `jobs`. It takes no entity type arguments, and the call order of `AddHeadlessJobs` and `AddHeadlessDashboard` does not matter. The rail shows Jobs with the Runs, Cron jobs, Functions, and Nodes views at `{shell base}/jobs`. The package embeds the module's front end, so it needs no separate deployment.

**Verify:** `{shell base}/jobs` renders the Runs view. A host that adds `AddJobs()` without `AddHeadlessJobs` fails to start with a message naming `AddHeadlessJobs`.

### Configuration

| `JobsDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | Host policies a user must pass to read the module and to run its commands, in host authentication mode |
| `ChangeOverlapWindow` | 5 s | How far back each change read reads again, so a write that committed after a later one is still seen. Make it longer than the store's slowest single write |
| `ChangePageSize` | 200 | The most changed rows one change read returns, 1 to 1000; a larger burst is read over several polls |

`AddJobs(Action<JobsDashboardOptions>)`, `AddJobs(Action<JobsDashboardOptions, IServiceProvider>)`, and `AddJobs(IConfiguration)` set them, and `JobsDashboardOptionsValidator` validates them.

### Runtime behavior

- **Authentication** comes from the shell's mode (`WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth`). The module has none of its own. Empty module policies inherit the shell's. The module exposes operational commands, so configure authentication before exposing it outside an isolated development environment.
- **Live changes on every node.** A write a job manager makes on this node is published after it returns. Writes from other nodes reach open views within one `ChangePollInterval`, through the store's change reads. Progress writes move the bar without reloading.
- **Command rules.** Requeue is allowed on a `Failed`, `Cancelled`, or `Skipped` run that `JobRequeueRules` accepts. Cancel is allowed on a queued or running time job with no cancel requested yet; a second cancel is refused with `Cancel requested — waiting for the job to stop`. Delete is allowed on a run that is not running and not keyed. Run now is allowed on a cron job that is not paused, and not running when its overlap policy forbids overlap. Pause is allowed on an active cron job, and Resume on a paused one not removed from code. Edit schedule is allowed on a cron job not defined in code (`Defined in code`). The same rules drive the allowed actions, the Needs-you counts, and Operation scopes.
- **Operations.** Requeue, Cancel, and Delete over a filter run in chunks. Requeue and Delete write a row only while it is unchanged since you confirmed, so no row is written twice.
- **Nodes.** The Nodes view lists each live node from `Headless.Coordination` membership and the functions its run filter claims; without membership it shows this host. A function no live node claims becomes a Needs-you item.
- **Cron "Run now"** stamps the on-demand occurrence with the store's clock and links the run to the request's trace.
- **Traces.** A run with a stored W3C trace parent offers `Open trace`. Use the dashboard for triage and OpenTelemetry for trace-level diagnostics.
- **AOT.** The dashboard is not AOT-compatible.
