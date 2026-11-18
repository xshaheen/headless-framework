---
domain: Jobs (Background Jobs)
packages:
---

# Handle failures, retries, and requeue

> Part of [Jobs](../jobs.md). Give a job a failure policy, react to failed and exhausted runs, stop a run on purpose, choose what a node crash does, and requeue a failed run.

## Failure policy

A job's failure policy decides how many times a failed run retries, how long each retry waits, and which exceptions fail the run at once. Without a policy a job does not retry. The policy model, its delay math, and how it compares with Messaging are in [reliability.md](../reliability.md).

Name the policy type on the attribute:

```csharp
using Headless.Reliability;

public sealed record SyncInvoiceArgs(string InvoiceId);

public sealed class LedgerFailurePolicy : FailurePolicy
{
    protected override void Configure(FailurePolicyBuilder policy) =>
        policy
            .Immediate(retries: 1)
            .Delayed(retries: 4, initialDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromMinutes(10))
            .FailOn<InvalidOperationException>();
}

[Job("billing.sync-invoice", FailurePolicy = typeof(LedgerFailurePolicy))]
public sealed class SyncInvoiceJob : IJob<SyncInvoiceArgs>
{
    public ValueTask ExecuteAsync(JobContext<SyncInvoiceArgs> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

The policy type is a concrete, closed, `public` or `internal` class derived from `Headless.Reliability.FailurePolicy` with a public parameterless constructor, else `HF023`.

The host sets the default for jobs that declare nothing, and replaces one job's policy by identity:

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.DefaultFailurePolicy(policy => policy.Delayed(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)));
    options.Tune("reports.daily", job => job.FailurePolicy(policy => policy.Immediate(2)));
});
```

Configuration adjusts the numbers without a redeploy and keeps the fail rules:

```json
{
  "Headless": {
    "Jobs": {
      "Jobs": {
        "billing.sync-invoice": {
          "FailurePolicy": { "DelayedRetries": 6, "DelayedMaxDelay": "00:30:00" }
        }
      }
    }
  }
}
```

**Resolution.**

1. `Tune(...).FailurePolicy(...)` replaces the declared policy.
2. A job with neither gets `DefaultFailurePolicy`. A later `DefaultFailurePolicy` call replaces an earlier one. Without one, the job does not retry.
3. `Headless:Jobs:Jobs:{identity}:FailurePolicy` then overrides `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, or `DelayedMaxDelay` of the winner.

An unknown key, an invalid value, or a declared policy whose `Configure` throws fails startup. `ConfigureDefaults`, `ConfigureJob<TRequest>`, and `Tune(...).Options(...)` reject `Retries` and `RetryIntervals` with an `ArgumentException`, because the policy owns them. Only a scheduling call's `Retries`, `RetryIntervals`, `WithRetries`, or `WithRetryIntervals` overrides the stored count for one run.

**Verify:** the `failurePolicy` of the job in the `jobs` section of `describe` shows the resolved numbers ([host manifest](operations.md#host-manifest)).

### What a row stores

Scheduling flattens the resolved policy into the row's durable `Retries` and `RetryIntervals`:

- `Retries` is `ImmediateRetries + DelayedRetries`, and excludes the first execution.
- Each immediate retry stores `0` seconds. Each delayed retry stores its base delay rounded up to whole seconds. Jitter is not stored.
- A call that sets `WithRetries(n)` stores `n` and only the call's own intervals. A call that sets only `WithRetryIntervals(...)` keeps the policy's count with the call's intervals.
- Rows added through `ITimeJobManager`, `ICronJobManager`, or the dashboard keep the `Retries` and `RetryIntervals` they carry. The policy is not applied.
- A row with no intervals waits the policy's delays: none for its immediate retries, then the policy's jittered delay, held at the cap once doubling reaches it. A retry past the immediate retries of a policy with no delayed retries waits 30 seconds.
- When `RetryIntervals` is shorter than `Retries`, the last interval repeats. Each interval is clamped between zero and 24 hours.

**Policy changes and stored rows.** Fail rules are looked up by function name when the run executes, so a changed fail rule applies to rows already stored. A changed count or delay applies only to rows scheduled afterwards.

**Cron definitions** store their flattened retries when they are created, and each occurrence runs with the definition's stored retries. A later policy change does not reach an existing definition, even on the next seed. Edit it through `ICronJobManager`.

### How a retry runs

- **Short waits stay in process.** A retry whose delay is at most one second runs on the same node under the job's lease, with the row `InProgress`. So does every retry of a cron occurrence or a chain node, whatever its delay.
- **Long waits go back to the store.** A standalone time job whose retry delay is longer returns to `Idle` with `ExecutionTime` set to the store's clock plus the delay, and frees its worker and cluster slot while it waits. If the node that wrote it stops, another node claims the retry at its next poll or fallback sweep, up to `FallbackIntervalChecker` late. `OnNodeDeath` does not apply to that wait.
- **The budget survives crashes.** `RetryCount` is persisted before each wait, so a run resumed after a crash continues from the spent budget. `JobContext.RetryCount` reports it.
- **Each attempt has its own DI scope.** An attempt ends only when its scope disposes. A scoped service whose `DisposeAsync` throws, such as a unit of work that flushes on dispose, fails the attempt, and the policy retries it, so the handler can run again after it returned.
- **Fail rules end the run.** A failure a fail rule matches ends the run `Failed` without its remaining retries. A `FailWhen` predicate that throws counts as a match and is logged.
- **A failed retry write is lease loss.** If the write that charges a failed attempt matches no row, the run stops as lease loss and the row is left for reclaim.

## Cancellation and exceptions

Cancellation of the run's own token and `TerminateExecutionException` never reach the failure policy.

- **The run's token** is cancelled by a durable cancel, host shutdown, or lease loss. Once it is cancelled, any `OperationCanceledException` that ends the run belongs to that cause, even one that carries a token the handler linked to the job token.
  - A durable cancel writes `Cancelled`.
  - Lease loss leaves the row non-terminal for reclaim and does not call `OnExhausted`.
  - Host shutdown settles the run by its node-death policy without charging the budget ([graceful stop](#graceful-stop)).
- **An `OperationCanceledException` while the token is live**, such as an `HttpClient` timeout or the handler's own `CancelAfter`, is an ordinary failure. Fail rules see it, and the budget retries it. Declare `FailOn<OperationCanceledException>()` to end it at once. The rule also matches `TaskCanceledException`.
- Never serialize fail-rule predicates or callback delegates into job arguments.

Handle an expected failure inside the job when it should complete instead of retry:

```csharp
[Job("orders.process")]
public sealed class ProcessOrderJob(ILogger<ProcessOrderJob> logger) : IJob<OrderRequest>
{
    public async ValueTask ExecuteAsync(JobContext<OrderRequest> context, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(context.Request, ct);
        }
        catch (HttpRequestException ex) when (context.RetryCount < 3)
        {
            logger.LogWarning(ex, "Transient failure on attempt {Attempt}", context.RetryCount + 1);
            throw; // the failure policy retries it
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Permanent failure, not retrying");
            return; // completes without retry
        }
    }

    private static Task ProcessAsync(OrderRequest request, CancellationToken ct) => Task.CompletedTask;
}
```

## Stop a run with TerminateExecutionException

Throw `TerminateExecutionException` to stop a run at once without spending retry budget:

```csharp
using Headless.Jobs;

// Called from a job's ExecuteAsync.
private static void StopIfUnrecoverable(bool configurationValid, bool permanentFailure)
{
    if (!configurationValid)
    {
        // Defaults to JobStatus.Skipped
        throw new TerminateExecutionException("Configuration invalid");
    }

    if (permanentFailure)
    {
        // Explicit final status
        throw new TerminateExecutionException(JobStatus.Failed, "Permanent data error");
    }
}
```

- `TerminateExecutionException("message")` ends the run `Skipped`.
- `TerminateExecutionException(JobStatus status, "message")` ends it with `Succeeded`, `DueDone`, `Failed`, `Cancelled`, or `Skipped`. Any other status throws `ArgumentOutOfRangeException`.
- Both have an overload that takes an `innerException`.
- It does not call `OnExhausted`.

## React to failures

### OnExhausted

`ConfigureRetries` configures only the terminal-failure notification. The row's `Retries` and the job's policy decide retries; no host setting caps them.

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureRetries(retry =>
    {
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

- `OnExhausted` runs once, in a fresh DI scope, after the run's owned transition to `Failed`: when the budget ran out, a fail rule matched, or crash recovery found the budget spent.
- It does not run for `TerminateExecutionException`, for lease loss, or when another node already wrote the terminal status.
- `OnExhaustedTimeout` bounds it on the system clock, so a test host whose `TimeProvider` never advances still cuts off a hung callback. A timeout or a callback exception is logged and contained.

### Exception handler

`IJobExceptionHandler` receives every failed attempt, not only the last one. Use it for per-attempt side effects such as alerting, metrics, and log sinks. Use `OnExhausted` for the once-only terminal notification.

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

- `SetExceptionHandler<THandler>()` registers the handler as a singleton.
- `HandleExceptionAsync` runs after each attempt's retry state is persisted, and once more at final failure.
- Each call is bounded by `OnExhaustedTimeout` on the system clock. A hanging handler is logged and abandoned, so it cannot stall retries.
- Handlers run only after the attempt's write applied. A run whose terminal write lost to a reclaim calls none.
- For a retry that waits in the store, the handler runs after the reschedule write, so a slow handler can still run when the retry starts, on this node or another.
- The job's tenant is restored around both callbacks ([tenancy.md](tenancy.md)).

## Job status reference

| Status | Meaning |
| --- | --- |
| `Idle` | Waiting to be claimed |
| `Queued` | Claimed, waiting for a worker or a concurrency slot |
| `InProgress` | Running, with its lease renewing |
| `Succeeded` | Completed |
| `DueDone` | Completed by a fallback sweep run, counted as success |
| `Failed` | The failure policy gave up: retries exhausted or a fail rule matched. Requeue it with `RequeueAsync` or `RequeueOccurrenceAsync` |
| `Cancelled` | A cancel of an `Idle` job, or a running time job that exited after seeing `CancelRequested`. Host shutdown and lease loss never write this status |
| `Skipped` | `TerminateExecutionException`, or the scheduler retired the occurrence before it ran: pause, definition update, missed-run recovery, overlap policy, a `Skip` node-death policy, or a chain branch not taken |

A terminal run stays `Failed`. There is no dead-letter status.

## Node-death policy (OnNodeDeath)

`NodeDeathPolicy` decides what happens to a run whose node dies, or whose lease lapses, while it is `InProgress`. The default is `Retry`.

| Policy | On node death | Use when |
| --- | --- | --- |
| `Retry` (default) | The row goes back for another claim. A crash or lease lapse counts toward the retry budget | The job is idempotent and safe to run again |
| `MarkFailed` | Terminal `Failed`, never run again | A second run is wrong and the failure must show |
| `Skip` | Terminal `Skipped`, never run again | The job must run at most once |

Set it per job with `ConfigureJob<TRequest>` or `Tune(...).Options(...)` ([scheduling.md](scheduling.md#set-startup-defaults)), per call or per chain step with `JobOptions`, or on an entity:

```csharp
// Per call through IJobScheduler
await scheduler.EnqueueAsync(
    new ChargeCard(orderId),
    new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed },
    ct
);

// Per node in a typed JobChain
var chain = JobChain.Start(
    new ChargeCard(orderId),
    new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed }
);
await scheduler.EnqueueAsync(chain.Build(), ct);

// On a cron job (every occurrence inherits it)
await cronJobManager.AddAsync(
    new CronJobEntity
    {
        Function = "reports.nightly",
        Expression = "0 0 2 * * *",
        OnNodeDeath = NodeDeathPolicy.Skip,
    },
    ct
);
```

- **Only started runs.** The policy applies only to a run that reached `InProgress`. A claimed row that never started (`Idle` or `Queued`) is reclaimed when its lease lapses, whatever its policy, and runs once.
- **Crash budget.** A crash, dead-node, or lease-lapse reclaim under `Retry` charges one attempt, unless the run was waiting between in-process retries, whose failed attempt was already charged. A row whose persisted count already exceeds its budget is failed, with `OnExhausted`, instead of running again. So a handler that reliably kills its host cannot re-run forever, and a `Retries = 0` job that a crash reclaims fails without running again.
- **At-least-once.** A run interrupted under `Retry` runs again from the start on another node, whatever its `Retries`. Write handlers for at-least-once execution, or choose `MarkFailed` or `Skip` where a second run is wrong.

### Graceful stop

A host stop is not a node death and does not charge the budget. A run interrupted by the stop settles by its policy:

- `Retry` returns the row to `Idle`. A time job stopped during a retry wait resumes after the part of the wait it still owed, measured on the store's clock.
- `MarkFailed` records `Failed` and `Skip` records `Skipped`, with the reason "interrupted by host shutdown".
- A claimed run that had not started is released to `Idle` uncharged.

[execution.md](execution.md#shutdown-and-drain) describes the stop budget.

## Requeue a failed job

A run the policy gave up on stays `Failed`. Put it back with `IJobScheduler`:

```csharp
public static async Task<bool> TryRequeueAsync(IJobScheduler scheduler, Guid jobId, CancellationToken ct)
{
    var outcome = await scheduler.RequeueAsync(jobId, ct);

    // Every other outcome is a refusal that left the row unchanged; its name says why.
    return outcome == JobRequeueOutcome.Requeued;
}

public static async Task<bool> TryRequeueOccurrenceAsync(IJobScheduler scheduler, Guid occurrenceId, CancellationToken ct) =>
    await scheduler.RequeueOccurrenceAsync(occurrenceId, ct) == JobRequeueOutcome.Requeued;
```

- **`RequeueAsync(timeJobId)`** moves a `Failed`, `Cancelled`, or `Skipped` standalone time job to `Idle`. `RetryCount` returns to 0. The stored exception, skip reason, cancel request, failure fingerprint, progress, owner, and lease are cleared. `ExecutionTime` moves to the store's current instant, so the next claim runs it. The stored `Retries` and `RetryIntervals` stay, so the row gets its full budget again.
- **`RequeueOccurrenceAsync(occurrenceId)`** does the same for a cron occurrence but keeps its `ExecutionTime`, which identifies the instant it stands for, so the fallback sweep (`FallbackIntervalChecker`) claims it. An occurrence of a paused definition can be requeued and waits until the definition resumes.
- **Refusals** return the reason and change nothing:

| `JobRequeueOutcome` | Why |
| --- | --- |
| `NotFound` | No such row |
| `NotFailed` | The row is not failed, cancelled, or skipped. A repeated or concurrent requeue lands here, so requeue is safe to repeat |
| `ChainMember` | The time job has a parent or a child |
| `SupersededGeneration` | A keyed time job that is no longer its key's current generation |
| `Overlap` | The occurrence's definition forbids overlap and another occurrence is unfinished |
| `Conflict` | The row changed during the request, or another live occurrence holds the instant |

- A requeue replaces the run's stored trace parent with the requeue's span, so the next run links to the operator's request.
- The dashboard offers Requeue on one run and as an Operation over every run a filter matches. A refusal on one run answers `409` with the reason.
- `JobRequeueRules` is the public eligibility rule. Only a `Failed` run is an actionable failure: a cancelled or skipped run can be requeued but was stopped on purpose, so it never counts as one. A failed time job stays actionable until it is requeued or deleted. A failed cron occurrence stops being actionable once a later occurrence of the same definition succeeds.

**Verify:** `RequeueAsync` returns `JobRequeueOutcome.Requeued`, and the row's status is `Idle` with `RetryCount` 0.
