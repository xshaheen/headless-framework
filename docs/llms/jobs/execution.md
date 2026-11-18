---
domain: Jobs (Background Jobs)
packages:
---

# Control how jobs run

> Part of [Jobs](../jobs.md). Limit concurrency per node and across the cluster, report progress, size leases, drain on shutdown, and understand how nodes claim and recover work.

## Concurrency and priority on one node

- **Host limit.** `SchedulerOptionsBuilder.MaxConcurrency` (default `Environment.ProcessorCount`) bounds how many jobs run at once on a node. Jobs run as logical workers on the shared .NET thread pool. An idle host arms no polling timers.
- **Priority.** Workers dequeue `High`, then `Normal`, then `Low`.
- **Per-job limit.** A job's `MaxConcurrency` (or `Tune(...).Concurrency(n)`) caps that job on one node. A job at its cap waits in arrival order without holding a worker, so other jobs keep running. It stays `Queued` while it waits.
- **Long-running jobs.** `JobPriority.LongRunning` work starts on a dedicated thread within a separate budget, `MaxLongRunningConcurrency` (default: the smaller of `MaxConcurrency` and 4). An `async` handler releases that thread at its first `await`, so the dedicated thread protects only the synchronous start of the handler, and the budget bounds how many such jobs run at once. A saturated budget never blocks other dispatch. An admission refused at the cap, or dropped by cancellation or shutdown, never runs: the row goes back for another claim with its retry count unchanged.
- **Immediate dispatch.** `EnqueueAsync` of a due job (and a dashboard run-now) tries to start the job on the enqueuing node at once. When the pool does not admit it, the row is handed back to `Idle` and the next poll runs it. The enqueue still succeeds, because the row is written.
- **Polling.** The scheduler wakes on its own signals for near-zero latency, and a fallback sweep runs every `FallbackIntervalChecker` (default 30 seconds) to pick up anything missed.

## Cluster-wide concurrency

`MaxConcurrency` caps a job on one node, so N nodes run up to N times the limit. `ClusterMaxConcurrency` caps the job across every node that shares the job store, which is what a job guarding a rate-limited API or a shared resource needs. Set it on the attribute, with `Tune`, or in configuration. `0` (the default) means no cluster-wide limit.

```csharp
[Job("payments.sync", ClusterMaxConcurrency = 2, MaxConcurrency = 1)]
public sealed class PaymentsSync : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

options.Tune("payments.sync", job => job.ClusterConcurrency(3)); // or Headless:Jobs:Jobs:payments.sync:ClusterConcurrency
```

- **What counts.** Every live leased run of the job on any node: a time job or cron occurrence in `Queued` or `InProgress` with an unexpired lease. A run takes its slot when a node claims it and keeps it until it finishes or its lease lapses. That includes time spent waiting for the node's per-job limit or for a worker, and an in-process retry backing off. A standalone time job's retry delay of more than one second waits in the store and gives its slot back until the retry is due.
- **Recovery.** A crashed node's runs stop counting once their leases lapse (`LeaseDuration`, default 5 minutes). A time job scheduled for later holds no slot until it is due.
- **Claims** lock each limited job, count its live leased rows, and lease no more than the free slots. On PostgreSQL and SQL Server the lock is a transaction-scoped advisory lock (`pg_try_advisory_xact_lock`, `sp_getapplock`), so claims on different nodes never overshoot. Jobs without a limit take no lock.
- **Cron.** A due occurrence with no free slot is still created, `Idle`, and runs when a slot frees. The overlap policy counts it as unfinished.
- **Immediate dispatch** never starts a limited job directly. The row stays `Idle`, and the scheduler's claim leases it against the limit.
- **In-memory storage** applies the same rule under a process-wide lock.

Limits:

- Each node reads the limit from its own registration, not from the rows. Configure the same value on every node. During a rolling change each node enforces the value it has.
- A chain descendant without its own execution time runs under its root's claim, so it is neither counted when claimed nor gated when it starts. Once it runs, its lease counts against new claims.
- The portable EF optimistic-CAS claim path cannot enforce the limit. A host that runs background services, sets a `ClusterMaxConcurrency`, and would claim through that path fails at startup with an `InvalidOperationException` naming the limited jobs and the reason: no native claim provider, or a jobs model the native claim cannot serve (a global query filter, discriminator inheritance, or a shared table). Use `UsePostgreSql`/`UsePostgreSqlClaims()` or `UseSqlServer`/`UseSqlServerClaims()`. A host with `DisableBackgroundServices()` never claims, so it passes.

**Verify:** the `jobs` section of `describe` shows `clusterMaxConcurrency` for the job. Under load, the dashboard's run list never shows more `InProgress` runs of the job than the limit.

## Progress reporting

Report how far a long-running job has got with `JobContext.ReportProgress(percent, message)`. The scheduler stores the latest report on the job's row, and the dashboard shows it on any node:

```csharp
[Job("customers.import")]
public sealed class ImportCustomers : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        const int batches = 40;
        for (var batch = 1; batch <= batches; batch++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); // import one batch
            context.ReportProgress(100d * batch / batches, $"batch {batch} of {batches}");
        }
    }
}
```

- **Arguments.** `percent` runs from `0` to `100` and may be fractional. `message` is optional and holds at most `JobProgress.MessageMaxLength` (512) characters. A percent outside the range, `NaN`, an infinity, or a longer message throws `ArgumentOutOfRangeException`. Each report replaces the stored one, so a report without a message clears the stored message.
- **Cheap to call.** The call records the value in memory and returns. It never waits for the store. The first report is written at once. After that, at most one write per `SchedulerOptionsBuilder.ProgressReportInterval` (default 2 seconds) carries the latest value, and a report still unwritten when the run ends is written with its final status. Budget one row update per interval per reporting job.
- **Advisory.** A failed progress write is logged and retried with the latest value after the interval. It never fails the run. A write from a node that lost the lease is dropped, so it cannot overwrite the new owner's progress.
- **Retries and requeue.** Stored progress stays through retries and crash re-runs until the next report, and stays on the terminal row, so a failed job shows how far it got. A requeue clears it. Each cron occurrence starts with none.
- **Columns.** `TimeJobEntity` and `CronJobOccurrenceEntity` carry `ProgressPercent`, `ProgressMessage`, and `ProgressUpdatedAt`, all nullable. Relational stores stamp `ProgressUpdatedAt` with the database clock.
- **Unit tests.** On a `JobContext` the scheduler did not create, such as one built in a unit test, the call validates its arguments and records nothing.

**Verify:** while the job runs, the dashboard's run view shows the percent and message, and `ProgressUpdatedAt` on the row is set.

## Leases

Every claimed time job and cron occurrence holds a lease: a holder, an expiry, and a generation that increases with each claim. Every later write by that attempt (start, renewal, progress, retry, completion, release) lands only while the row still holds that generation with a live lease. A write from an attempt that lost the row is refused, even when the same node has claimed the row again since.

- **Sliding renewal.** A running job renews its lease every `LeaseRenewalInterval` (default `LeaseDuration / 3`; an explicit value must be positive and less than `LeaseDuration`). `LeaseDuration` (default 5 minutes) does not need to exceed the longest job runtime.
- **Cancel on loss.** A renewal that matches no row (the row was reclaimed or the node has no live identity), or that does not complete within the renewal interval (a hung store), cancels the job's `CancellationToken`. The run writes nothing and leaves its row for reclaim. A handler that ignores the token keeps running, and its late completion matches no row.
- **Membership loss cancels at once.** When the node loses coordination membership outside a host stop, every job running on it has its token cancelled immediately, without waiting for the next renewal.
- **Start check.** Before user code starts, the job checks that the node's live identity is the one that claimed the row. With no live identity the node starts nothing and hands the row back to `Idle` with `RetryCount` unchanged.
- **Stalled runs.** A job stuck `InProgress` whose lease lapses is reclaimed by its `OnNodeDeath` policy within about one `LeaseDuration`, whether or not its node died.
- **Claimed but waiting.** A claimed row waits `Queued` for a worker. Keep `LeaseDuration` at least `FallbackIntervalChecker`, so rows waiting for admission are not re-claimed. A queued row whose lease expired is skipped at admission instead of running unowned.
- **Database clock.** On the EF store, leases are judged and stamped with the database clock, so cross-node clock skew cannot reclaim a healthy job. The in-memory store uses the registered `TimeProvider`, so in EF tests a fake application clock does not control lease deadlines.
- **Lease writes bypass EF interceptors.** Claims, renewals, settles, and progress run as raw SQL on the context's connection, so application EF interceptors do not see them.

**Verify:** `headless.jobs.execution.outcomes` with `headless.jobs.outcome` = `lease_lost` counts runs that lost their row ([operations.md](operations.md#reading-lease-loss)).

## Shutdown and drain

When the host stop begins, the scheduler stops claiming and drains:

- A queued item that has not started releases its claim uncharged.
- Running jobs get `SchedulerOptionsBuilder.ShutdownTimeout` (default 30 seconds) to finish and write their own outcome. The budget counts from the host's stop start (`ApplicationStopping`), so time other services spend stopping first comes out of it.
- When only a release window remains (5 seconds, or half the budget when that is shorter), the scheduler cancels jobs still running and settles each by its node-death policy without charging the budget ([failures.md](failures.md#graceful-stop)).
- A job that ignores cancellation through the release window stays `InProgress` and is reclaimed like a crash.
- The node stays a coordination member until the drain finishes, so jobs that end during the stop still renew and settle.

Set `ShutdownTimeout` to the same value as `HostOptions.ShutdownTimeout`, because Jobs cannot read the host's timeout and a shorter host timeout ends the stop before the release window opens. Give the platform's termination grace period at least that long. The value must be greater than zero and has no upper limit.

**Verify:** stop a host while a job runs, and check that the run ends `Succeeded`, or returns to `Idle` under `Retry`, rather than staying `InProgress`. The `jobs` readiness check reports `Unhealthy` from the start of the stop.

## Node identity and recovery

The durable (EF) store uses `Headless.Coordination` for node identity and dead-node recovery. Register coordination before Jobs, or use `UsePostgreSql<TContext>` / `UseSqlServer<TContext>`, which register it for you ([storage.md](storage.md)).

- **Row owner.** The owner stamped on rows is the coordination owner tag `cluster/node@incarnation`, qualified with the coordination `ClusterName` and a store-allocated incarnation. The node part is the host name `Headless.Context` resolves (`HostIdentityOptions.HostName`, then `POD_NAMESPACE/POD_NAME`, then the machine name; see [context.md](../context.md)). `SchedulerOptionsBuilder.NodeId` is the owner only on the in-memory store (default `Environment.MachineName`).
- **Dead-node recovery** runs on coordination `NodeLeft` events and on a periodic reconcile every `DeadNodeReconcileInterval`. The default, `null`, is half the coordination `DeadRetentionWindow` (15 seconds with default coordination options). A larger value is clamped to that ceiling with a warning. If the reconcile's scan cost shows on a very large jobs table, raise `DeadRetentionWindow` instead, which lifts the ceiling. Reclaim matches the dead incarnation exactly, so it never touches rows of a restarted node's new incarnation.
- **Orphaned owners.** On the same cadence, rows stamped by an owner that is neither `Alive` nor `Suspected`, such as a dead identity, a superseded incarnation, or a pruned one, are reclaimed. `Suspected` owners are never swept. Each app sees only nodes of its own `ClusterName`, so apps that share a Jobs store under different cluster names never reclaim each other's rows.
- **Fail-stop on membership loss.** A node that loses coordination membership stops processing instead of stamping a stale owner. Once the node passes the coordination self-fence deadline without an accepted heartbeat, no new row is stamped with its owner ([coordination.md](../coordination.md)).
- **Settling after loss.** A run that finishes after its node left membership still writes its outcome while its lease is live. It matches no row once the lease expired or another claim took the row.
- **Registration.** The node registers when the host starts, and the coordination heartbeat beats from then on, so a slow start in another hosted service does not expire it.
- **Without coordination,** the durable store throws `InvalidOperationException` at startup naming `AddHeadlessCoordination`.

**Verify:** kill a node mid-run. Within about `DeadRetentionWindow` plus one reconcile interval, its runs move by their `OnNodeDeath` policy, and `headless.jobs.reclaims` records them with `headless.jobs.reclaim.reason` = `dead_node` or `orphaned_owner`.

## Stop and start the scheduler

- `SchedulerOptionsBuilder.StartMode` is `JobsStartMode.Immediate` (default) or `JobsStartMode.Manual`. Set `scheduler.StartMode = JobsStartMode.Manual` inside `ConfigureScheduler`, then start the loop through `IJobsHostScheduler`.
- While the loop is not running, the `jobs-scheduler` readiness check reports `Degraded` ([operations.md](operations.md#health-checks)).
- `DisableBackgroundServices()` registers only the managers and the scheduler facade, for enqueue-only nodes and tests. Such a host never claims or runs a job.
