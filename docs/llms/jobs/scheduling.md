---
domain: Jobs (Background Jobs)
packages: Jobs.Abstractions
---

# Schedule, cancel, and chain jobs

> Part of [Jobs](../jobs.md). Schedule one-off and delayed jobs through `IJobScheduler`, set per-call options, deduplicate enqueues, schedule keyed deadlines, cancel jobs, compose job chains, and use the low-level managers.

## Headless.Jobs.Abstractions

The contracts application code calls: `IJob`, `IJob<TArgs>`, `JobAttribute`, `JobContext`, `IJobScheduler`, `JobOptions`, `JobChain`, the managers, the entity types, and the exceptions. `Headless.Jobs` references it, so install it directly only for a library that targets the Jobs interfaces without the scheduler implementation:

```bash
dotnet add package Headless.Jobs.Abstractions
```

### Schedule with IJobScheduler

Use `IJobScheduler` for routine immediate, delayed, and recurring scheduling. Address a job with arguments by its argument value, and a job without arguments by its class:

```csharp
using Headless.Jobs;

public sealed record OrderReminderRequest(string OrderId);

[Job("orders.send-reminder")]
public sealed class SendOrderReminderJob : IJob<OrderReminderRequest>
{
    public ValueTask ExecuteAsync(JobContext<OrderReminderRequest> context, CancellationToken ct)
    {
        // context.Request.OrderId, context.RetryCount, and context.ScheduledFor are available.
        return ValueTask.CompletedTask;
    }
}

public sealed class OrderService(IJobScheduler jobs)
{
    public async Task<Guid> ScheduleReminderAsync(string orderId, CancellationToken ct) =>
        await jobs.EnqueueAsync(
            new OrderReminderRequest(orderId),
            new JobOptions { Description = $"order-reminder-{orderId}" },
            ct
        );
}
```

```csharp
var jobId = await scheduler.EnqueueAsync<CleanupJob>(ct);

var delayedId = await scheduler.ScheduleAfterAsync(new OrderReminderRequest(orderId), TimeSpan.FromHours(1), ct);

var absoluteId = await scheduler.ScheduleAsync(
    new OrderReminderRequest(orderId),
    DateTimeOffset.UtcNow.AddHours(24),
    cancellationToken: ct
);
```

| Call | Due time |
| --- | --- |
| `EnqueueAsync(args, ct)`, `EnqueueAsync<TJob>(ct)` | Now, on the store's clock |
| `ScheduleAfterAsync(args, delay, ct)`, `ScheduleAfterAsync<TJob>(delay, ct)` | The store's now plus `delay` |
| `ScheduleAsync(args, dueAt, ct)`, `ScheduleAsync<TJob>(dueAt, ct)` | The `DateTimeOffset` you pass, stored in UTC |
| `ScheduleRecurringAsync(args, cron, ct)`, `ScheduleRecurringAsync<TJob>(cron, ct)` | Each cron occurrence; see [recurring.md](recurring.md) |

- **Return value.** Immediate, delayed, and absolute calls return the persisted time job's `Guid`. `ScheduleRecurringAsync` returns the cron definition's id.
- **Unknown job.** An argument type or job class that no added module declares throws `JobFunctionNotFoundException` before anything is persisted.
- **Job-class overloads** (`EnqueueAsync<TJob>`, `ScheduleAsync<TJob>`, `ScheduleAfterAsync<TJob>`, `ScheduleRecurringAsync<TJob>`, `ScheduleKeyedAsync<TJob>`, `ReplaceKeyedAsync<TJob>`) are constrained to `IJob`, so a job that takes arguments cannot be scheduled without them. There is no descriptor-based or function-name overload.
- **Absolute instants are `DateTimeOffset`.** `ScheduleAsync`, `ScheduleKeyedAsync`, `ReplaceKeyedAsync`, and timed chain steps take a `DateTimeOffset`. Passing a `DateTime` is a compile error. Convert a wall-clock value with an explicit zone or offset first. A known UTC value can use `new DateTimeOffset(utcDateTime, TimeSpan.Zero)`.
- **Delays.** `ScheduleAfterAsync` accepts zero. A negative delay throws `ArgumentOutOfRangeException` at once, and a delay that overflows from the store's instant throws `ArgumentOutOfRangeException` before any row is written.
- **Priority is not a scheduling option.** It comes from `[Job(Priority = ...)]`, and a host changes it with `Tune`.
- **Arguments** serialize through the host's request JSON options (`ConfigureRequestJsonOptions`) and optional GZip (`UseGZipCompression()`). A job's stored argument is read just before its `ExecuteAsync` runs. A read or deserialization failure fails that attempt like any exception, and the handler never runs with a default argument.

**Verify:** the returned id resolves to a row through `ITimeJobManager` or the dashboard's run list, and the job's `ExecuteAsync` runs once the row is due. In a test with background services disabled, assert the row exists instead.

### Due times follow the store clock

Claims decide whether a row is due on the store's clock, so the store also sets every due time you state relative to now. A node minutes ahead of or behind the database does not move when a run-now or delayed job runs.

| Due time | Who sets `ExecutionTime` |
| --- | --- |
| Run now (`EnqueueAsync`, a `JobChain`, `ITimeJobManager.AddAsync` with `ExecutionTime` null) | The store, at its instant when it writes the row |
| Delay (`ScheduleAfterAsync`, `ITimeJobManager.AddAsync(entity, delay)`, `AddIdempotentAsync(entity, key, ttl, delay)`) | The store, at its instant when it writes the row, plus the delay |
| Retry parked by a graceful stop | The store, at its instant when it settles the row, plus the part of the retry wait still owed |
| Dashboard cron "Run now" occurrence | The store, at its instant when it writes the occurrence |
| Absolute (`ScheduleAsync`, an explicit `ExecutionTime`, a keyed schedule, or one a schedule middleware sets) | The caller; stored unchanged |

- **Store instant by provider.** PostgreSQL reads `clock_timestamp()` and SQL Server `SYSUTCDATETIME()`, inside the write. The in-memory store uses its `TimeProvider`. Other EF backends (SQLite, MySQL, and the rest) have no statement clock, so their run-now and delayed stamps come from the host's `TimeProvider` and carry the node's skew.
- **One instant per write.** Every run-now and delayed root in one batch shares the same store instant.
- **Enlisted writes anchor at the write, not the commit.** A unit that commits one minute after `ScheduleAfterAsync(request, TimeSpan.FromMinutes(5))` leaves the job due four minutes after the commit.
- **Middleware wins.** Schedule middleware sees `ExecutionTime` null for both a run-now and a delayed job. A middleware that sets an absolute `ExecutionTime` overrides the delay.
- **Delayed manager calls.** `ITimeJobManager.AddAsync(entity, delay, ct)` and `AddIdempotentAsync(entity, idempotencyKey, idempotencyTtl, delay, ct)` require `ExecutionTime` null on entry, else `ArgumentException`. `ScheduleAfterAsync` calls them through the registered `ITimeJobManager<TTimeJob>`, so a decorator of that interface sees delayed schedules too.
- **Audit stamps stay on the node clock.** `CreatedAt` and the first `UpdatedAt` come from the enqueuing node's `TimeProvider`, so a run-now job's `ExecutionTime` can differ from its `CreatedAt` by that node's skew.
- **Lateness shows skew.** `JobContext.Lateness` and the `headless.jobs.queue.lag` metric compare the store-stamped due time with the executing node's clock. A node ahead of the database reports its skew as lateness, even on a run-now job.

### Set per-call options

`JobOptions` carries per-call values: `Description`, `Retries`, `RetryIntervals`, `OnNodeDeath`, `CorrelationId`, `CausationId`, `TenantId`, `IsSystemJob`, `IdempotencyKey`, and `IdempotencyTtl`. `RecurringJobOptions`, for recurring calls, carries `Description`, `Retries`, `RetryIntervals`, `OnNodeDeath`, `CorrelationId`, `CausationId`, and a nullable IANA `TimeZoneId`. The execution time and the cron expression are method arguments, not options.

- **Nullable fields inherit.** A null `Retries`, `RetryIntervals`, or `OnNodeDeath` inherits the configured default. `Retries = 0` disables retries for that call.
- **Retries override the policy for one call.** A call's `Retries` or `RetryIntervals` overrides what the job's [failure policy](failures.md#failure-policy) would store for that run.
- **Cancellation token overloads.** Omit an unused token, or pass `default` or `cancellationToken: default`, to select the token overload. Supplying `default` followed by a token selects the options overload. Bare `null` and `default` are ambiguous between the options-record and callback overloads: write `options:` or `configure:`.

Fluent callbacks are available on `EnqueueAsync`, `ScheduleAsync`, and `ScheduleAfterAsync`, for both argument and job-class forms. Import `Headless.Jobs` for `JobOptionsBuilder` (singular; one call's options) and `JobSchedulerExtensions`. Do not confuse it with `JobsOptionsBuilder<TTimeJob, TCronJob>` (plural), which configures the whole subsystem inside `AddHeadlessJobs`.

```csharp
using Headless.Jobs;

public sealed class JobCaller(IJobScheduler scheduler)
{
    public Task<Guid> EnqueueAsync<TRequest>(TRequest request, CancellationToken ct) =>
        scheduler.EnqueueAsync(request, options => options.WithRetries(0).WithCorrelationId("checkout"), ct);

    public Task<Guid> ScheduleCleanupAsync(CancellationToken ct) =>
        scheduler.ScheduleAfterAsync<CleanupJob>(TimeSpan.FromMinutes(5), options => options.WithRetryIntervals(2, 5), ct);
}
```

- Each callback runs once, synchronously, on a fresh builder, before the call submits. Async callbacks are unsupported. A throwing callback submits nothing. `configure: null!` throws before submission.
- `WithRetries(0)` disables retries. `WithRetryIntervals()` replaces inherited intervals with an empty array, and `WithRetryIntervals(null)` inherits them. Other nullable setters accept `null` to restore inheritance.
- `AsSystemJob()` stays set when you reuse a builder. Use a fresh builder to reset it.
- `Build()` returns a plain `JobOptions` without validation or resolved defaults. Builders copy retry arrays, so mutating an input array or a reused builder never changes an earlier snapshot. Concurrent mutation of one builder is unsupported.
- `WithIdempotencyKey` and `WithIdempotencyTtl` set the enqueue idempotency window for one call.
- Keyed, recurring, and chain conveniences are outside this fluent surface. Use `JobOptions` records, or `builder.Build() with { ... }`.

### Set startup defaults

Startup policies set only the node-death default. Set the application default with `ConfigureDefaults`, and override it per job with `ConfigureJob<TRequest>` (a job with arguments) or `Tune(identity, job => job.Options(...))`:

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureDefaults(job => job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed));
    options.ConfigureJob<OrderReminderRequest>(new JobOptions { OnNodeDeath = NodeDeathPolicy.Skip });
    options.Tune("maintenance.cleanup", job => job.Options(policy => policy.WithNodeDeathPolicy(NodeDeathPolicy.Retry)));
});
```

- Precedence is call, then function, then application default. Null fields inherit.
- Configure each job through either `ConfigureJob<TRequest>` or `Tune(...).Options(...)`, not both.
- `Retries` or `RetryIntervals` in a startup policy throws `ArgumentException` that names `DefaultFailurePolicy`, `[Job(FailurePolicy = ...)]`, and `Tune(...).FailurePolicy(...)` as the replacements.
- Tenant, system scope, description, correlation, causation, and idempotency values are per-call. A startup policy that sets them throws.
- Each policy callback runs once, synchronously. A failing callback or an invalid setting leaves the earlier policy in place. Unknown identities and invalid settings fail before startup completes.

### Lineage

`JobOptions` and `RecurringJobOptions` accept `CorrelationId` and `CausationId`, and `JobContext` exposes both.

- A root time job uses its own row id as its correlation id unless you supply one.
- A job scheduled from inside an executing job inherits that job's correlation id and uses the parent run's row id as its causation id. A chain step uses its direct parent's row id.
- A root cron occurrence takes its occurrence id as its correlation id unless the definition carries one.
- Retries keep lineage, independent of `Activity` traces. Ordinary edits of a time job or cron definition keep the stored correlation and causation.
- A correlation id holds at most `JobContract.CorrelationIdMaxLength` (200) characters. The dashboard lists every run of one correlation id.

### Deduplicate an enqueue

Supply `JobOptions.IdempotencyKey` and `JobOptions.IdempotencyTtl` together (or neither) on `EnqueueAsync`, `ScheduleAsync`, or `ScheduleAfterAsync`. The TTL runs from 1 second to 30 days, and the host sets no default.

```csharp
var jobId = await scheduler.EnqueueAsync(
    new OrderReminderRequest(orderId),
    new JobOptions { IdempotencyKey = $"reminder:{orderId}", IdempotencyTtl = TimeSpan.FromHours(1) },
    ct
);
```

- **Identity.** The reservation is (tenant or system scope, function, contract version, key). The payload is not part of it. A repeat with different arguments inside the window returns the first call's job id.
- **Window, not exactly-once.** This deduplicates enqueues. It does not make execution exactly-once.
- **Lifetime.** The key survives the job's completion, failure, cancellation, and retention deletion. Only expiry releases it. A background sweep deletes expired reservations once per `FallbackIntervalChecker`, in batches of 1,000 judged by the store's clock.
- **Contention.** Same-key callers serialize on a transaction-owned key lock with a bounded wait. A creator that holds a long-open transaction makes same-key callers time out rather than see its result.
- **Rejected on** keyed, recurring, and chain scheduling, and in startup policies. The window is never inherited from host or function policy.

**Verify:** two calls with one key inside the TTL return the same job id, and the store holds one row.

### Keyed deadlines

Use keyed scheduling for a standalone one-shot deadline that a business key identifies, such as "expire invoice 42". Capture the due instant once and reuse it on every retry of the same intent.

```csharp
var dueAt = timeProvider.GetUtcNow().AddDays(7);
var result = await scheduler.ScheduleKeyedAsync(new JobKey("invoice-42"), new InvoiceRequest("42"), dueAt, ct);
```

- **Scope.** A key is unique per tenant or system scope, plus function, plus business key. The contract version is part of the intent, not the identity. Keys compare ordinally, hold at most 200 characters, and reject padding, controls, and invalid Unicode.
- **Outcomes.** Keyed calls return `JobScheduleResult`, which separates the disposition from the observed run id, generation, and execution state. `Created` writes generation 1. `Existing` reports the current run with the same intent, even when it already finished. A different intent returns `Conflict`.
- **Intent.** The `v1` intent fingerprint hashes the contract version, the exact stored argument bytes after schedule middleware, and the due time in UTC truncated to microseconds. Null and empty arguments differ. Retry policy, node-death policy, description, lineage, and tracing are not part of it. Resubmit with the same absolute instant and arguments that serialize to the same bytes.
- **Policy of a duplicate.** `Retries`, `RetryIntervals`, and `OnNodeDeath` come from the first successful create. A later matching call returns `Existing` and changes nothing, even when it passes different overrides.
- **Replace.** `ReplaceKeyedAsync(key, observedGeneration, request, dueInstant, options, ct)` reschedules or changes the arguments or policy. It advances to generation N+1 only while the current run is pending and unclaimed. A lost-response replay reports `StaleGeneration`. A replacement takes the call's policy even when the intent is unchanged.
- **Cancel.** `CancelKeyedAsync(new JobKeyScope(functionName, tenantId), key, observedGeneration, ct)` targets that generation only. A pending run becomes `Cancelled`. A claimed run reports `CancellationRequested`, which is cooperative and does not prove the handler or its side effects stopped. A missing key returns `NotFound`.
- **Retention.** Current and earlier keyed rows stay indefinitely. Ordinary edits, resets, retries, requeues, and deletes reject them, and a mixed delete rejects before removing anything. There is no cleanup worker or forget-key API, so size storage and backups for them.
- **Providers.** The in-memory store, PostgreSQL, and SQL Server support keyed scheduling. Other stores reject it before any effect. Relational stores need the keyed model finalized ([storage.md](storage.md#keyed-scheduling-storage)).
- **Transactions.** Schedule, replace, or cancel a keyed job through `unit.Jobs` to bind it to a business transaction ([atomic-enqueue.md](atomic-enqueue.md)). Results inside a unit carry `IsProvisional = true` until the outer commit.

**Verify:** a second `ScheduleKeyedAsync` with the same key and intent returns `Existing` with the first run's id.

### Cancel a job

Cancel a time job by id with `IJobScheduler.CancelAsync(jobId, ct)`, or from inside the job with `context.RequestCancellationAsync()`. Both return `true` only when they recorded a new cancel request. Cron occurrences have no durable cancellation: `RequestCancellationAsync` on a cron occurrence throws `InvalidOperationException`.

- An `Idle` job becomes `Cancelled` atomically.
- A `Queued` or `InProgress` job keeps its status and sets `CancelRequested`. The running node checks the flag before user code starts and then on a bounded cadence, and cancels the job's `CancellationToken` when it sees it.
- When the handler then exits with any `OperationCanceledException`, the run ends `Cancelled`, even when the exception carries a token the handler linked to the job token.
- A handler that ignores the token keeps its natural result, and `CancelRequested` stays as audit data.
- Host shutdown and lease loss also cancel the token, but they never write `Cancelled`. See [failures.md](failures.md#cancellation-and-exceptions).

**Verify:** after `CancelAsync` on an `Idle` job, the row's status is `Cancelled`.

### Typed job chains

A job chain composes one root time job with conditional continuation steps into one tree that the scheduler persists atomically. Author it with `JobChain`. Each step names its job by argument type, or by job class for a step without arguments (`Start<TJob>()`, `Then<TJob>()`, `Catch<TJob>()`).

```csharp
using Headless.Jobs;

// processOrder → chargeCard; on success send a receipt, on failure refund.
var chain = JobChain.Start(new ProcessOrder(orderId));
var chargeCard = chain.Root.Then(new ChargeCard(orderId)); // runs when ProcessOrder succeeds
chargeCard.Then(new SendReceipt(orderId)); // runs when ChargeCard succeeds
chargeCard.Catch(new RefundPayment(orderId)); // runs when ChargeCard fails

var rootJobId = await scheduler.EnqueueAsync(chain.Build(), ct);
```

- **Start.** `JobChain.Start(payload, options?)` or `JobChain.Start<TJob>(options?)` starts an immediate root and returns a `JobChainBuilder`. `builder.Root` is the root's handle. A timed root uses `Start(payload, executionTime, options?)` or `Start<TJob>(executionTime, options?)` with a `DateTimeOffset`. Timed `Then` and `Catch` take the same arguments.
- **Edges.** `Then(...)` attaches the one on-success child and `Catch(...)` the one on-failure child. Each returns the child's handle. A second `Then` or `Catch` on one node throws `InvalidOperationException`. There is no parallel fan-out. A root-only chain is valid.
- **Catch does not recover.** A caught parent stays `Failed`. A failing catch step is another failed job, whose own continuations follow the same rules.
- **Per-step options.** Each step carries `JobOptions` (description, retries, retry intervals, node-death policy) and an optional execution time. Priority comes from the job's declaration.
- **Build.** `Build()` returns an immutable `JobChain`. Each `EnqueueAsync` writes a fresh tree, so enqueueing one built chain twice gives two independent trees.
- **Atomic write.** `EnqueueAsync(JobChain, ct)` writes the root and every descendant in one write and returns the root's id. Through `unit.Jobs` the tree commits with the unit. Through the injected scheduler it commits in the store's own transaction, so a failure leaves no partial chain.
- **Depth.** `SchedulerOptionsBuilder.MaxChainDepth` (default 10) bounds the longest root-to-leaf path. `EnqueueAsync` rejects a deeper chain before writing, naming the limit. `JobChain.MaxStructuralDepth` (64) is the hard bound, and `MaxChainDepth` cannot exceed it.
- **No keyed control.** A chain has no keyed identity, signals, joins, waits, or compensation.

Timed and run-condition behavior:

- A descendant with its own execution time becomes eligible at the later of its parent reaching the matching terminal state and its own time. It never starts before its parent finishes. When the parent reaches the other terminal state, the descendant and its subtree are `Skipped`.
- The lower-level entity APIs expose other `RunCondition` values. A `RunCondition.InProgress` child runs alongside its parent once the parent starts its handler. If the root ends without running, such as when crash recovery spent its retry budget, that child is `Skipped` with its subtree.

Operational limits:

- The whole chain runs on the node that claimed the root, under the root's claim. If that node crashes mid-chain after the root completed, a descendant without its own execution time can be stranded: reclaim returns it to `Idle` with no execution time, and nothing picks it up again. Treat this as a known limitation. Each node's `OnNodeDeath` policy still applies.
- A descendant without its own execution time stays `Idle` under the root's claim until it runs, and its start moves it from `Idle` to `InProgress` on every store, so the row and the dashboard show it running. A start the store refuses (the row was cancelled, released, reclaimed, or is already running) ends that child as lease loss (`start_rejected`) without running it.
- Lowering `MaxChainDepth` after deeper chains were stored stops the scheduler from claiming nodes below the new limit. Nothing rejects those chains.
- Deleting a time job deletes its whole descendant subtree. Deleting a non-root node removes only its subtree.

**Verify:** after `EnqueueAsync(chain.Build(), ct)`, the dashboard's run detail for the root lists each child, and a failing parent leaves its `Then` child `Skipped` and runs its `Catch` child.

### Use the managers

`ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` remain supported for CRUD, batching, custom entity types, and advanced persistence work. They do not resolve job metadata: you set `Function`, `Request`, `Retries`, and the other entity fields yourself, and the job's failure policy is not applied.

- **Members.** `AddAsync`, `AddBatchAsync`, `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, and `DeleteBatchAsync`. `ITimeJobManager` adds `AddIdempotentAsync`, the keyed `ScheduleKeyedAsync` and `CancelKeyedAsync`, and the delayed `AddAsync(entity, delay)` and `AddIdempotentAsync(entity, key, ttl, delay)`. A decorator or fake of the interface implements them all.
- **Errors.** `AddAsync` and `AddBatchAsync` throw on failure. `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, and `DeleteBatchAsync` return a failed `JobResult` instead, also for caller cancellation.
- **What an update may change.** `ITimeJobManager.UpdateAsync` edits only a job no node has started: one that is `Idle` and unowned, or `Queued`. Editing a `Queued` row releases its claim in the same write. A running, finished, owned `Idle`, or missing row returns a failed `JobResult` and changes nothing. A batch update is all-or-nothing. Status, owner, lease, retry count, and outcome are never taken from your entity. `IJobScheduler.RequeueAsync` is the way back from `Failed` ([failures.md](failures.md#requeue-a-failed-job)).
- **Stamps.** New entities keep `Id`, `CreatedAt`, and `UpdatedAt` unset until the manager stamps them on add. Managers assign ids through the registered `IGuidGenerator` and times through the registered `TimeProvider`.
- **Entity types.** `TimeJobEntity` and `TimeJobEntity<TTicker>` (parent-child chains), `CronJobEntity`, `CronJobOccurrenceEntity`, and their base `BaseJobEntity` (`Id`, `Function`, `Description`, `CreatedAt`, `UpdatedAt`, `TenantId`). Time jobs and cron jobs carry `Retries`, `RetryIntervals`, and `OnNodeDeath`.
- **Cron on a non-PostgreSQL, non-SQL Server EF backend.** `ICronJobManager.AddAsync` and `AddBatchAsync` throw `NotSupportedException` there ([storage.md](storage.md#backend-support)).

A manager-built request uses the host's serialization settings:

```csharp
await timeJobManager.AddAsync(
    new TimeJobEntity
    {
        Function = "orders.process",
        ExecutionTime = DateTime.UtcNow,
        // requestSerialization is the host's JobsRequestSerializationOptions singleton (resolve from DI)
        Request = JobsHelper.CreateJobRequest(new OrderRequest("pay_123"), requestSerialization),
        Retries = 3,
        RetryIntervals = [30, 60, 120], // seconds between attempts
    },
    ct
);
```

The generated invoker uses the delegate shape `JobFunctionDelegate(IServiceProvider, JobContext, CancellationToken)`, cancellation token last. A handwritten registration uses the same order:

```csharp
using Headless.Jobs;

JobFunctionDelegate handler = static (serviceProvider, context, cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    return Task.CompletedTask;
};
```

### Exceptions

| Exception | Thrown when |
| --- | --- |
| `JobFunctionNotFoundException` | A scheduling call names an argument type or job class no added module declares |
| `JobValidatorException` | Scheduling validation fails, such as a tenant rule; `Errors` lists each failure of a batch |
| `TerminateExecutionException` | A handler throws it to stop without retry ([failures.md](failures.md#stop-a-run-with-terminateexecutionexception)) |
| `ArgumentOutOfRangeException` | A negative or overflowing delay, or an invalid `ReportProgress` argument |
| `NotSupportedException` | A capability the store lacks, such as runtime cron creation on an EF backend without a statement clock |
