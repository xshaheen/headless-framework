---
domain: Jobs (Background Jobs)
packages:
---

# Run recurring jobs

> Part of [Jobs](../jobs.md). Declare cron jobs and seed them at deploy time, create recurring definitions at run time, pause and resume them, and control missed runs, overlap, time zones, and DST.

## Cron expressions and time zones

- **Six fields, seconds first.** `"0 */5 * * * *"` fires every five minutes. A five-field expression is rejected (`HF003` on the attribute). A value that starts with `%`, such as `"%Reports:UsCron%"`, names a configuration key resolved per host at startup.
- **Zone.** A definition uses its own `TimeZone` (attribute) or `RecurringJobOptions.TimeZoneId`, else the scheduler's `SchedulerTimeZone`. Both must be IANA zones. An unknown or non-IANA `TimeZoneId` is rejected. `AddHeadlessJobs` throws `InvalidOperationException` when `SchedulerTimeZone` has no IANA id, such as a Windows id, a custom zone, or a non-IANA `TimeZoneInfo.Local`. `TimeZoneInfo.Utc` is accepted.
- **Same zone on every node.** When nodes disagree, a tick still never runs twice, but each tick fires at the time projected in the zone of whichever node advanced it last, and missed-run detection can be wrong. Nodes with different `SchedulerTimeZone` also never reach [fleet agreement](#fleet-agreement).
- **Occurrences are stored in UTC.** Each firing creates a `CronJobOccurrenceEntity` with its own `Id`, claimed and run like a time job.

## Seeded recurring jobs

Declare a recurring job the app knows at startup in code, and let the `seed` deploy step create it. Do not call `ScheduleRecurringAsync` at startup.

The seed has two sources, reconciled as one set:

- `[Job(Cron = ...)]` on a job class: one definition for that job, without arguments.
- `SeedRecurringJob` on `JobsOptionsBuilder`: one definition per key, so one job can run on several schedules, with different arguments, or in different zones.

```csharp
using Headless.Jobs;

public sealed record ReportRequest(string Region);

[Job("reports.regional")]
public sealed class RegionalReportJob : IJob<ReportRequest>
{
    public ValueTask ExecuteAsync(JobContext<ReportRequest> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

builder.Services.AddHeadlessJobs(options =>
{
    options.AddModule<MyApp.JobsModule>();

    // A job with arguments: TArgs picks the job, and the request is stored with the definition.
    options.SeedRecurringJob(
        "reports.daily-eu",
        new ReportRequest("eu"),
        "0 0 6 * * *",
        new RecurringJobOptions { TimeZoneId = "Europe/Berlin", Description = "EU daily report" }
    );
    options.SeedRecurringJob(
        "reports.daily-us",
        new ReportRequest("us"),
        "%Reports:UsCron%", // a %name% value resolves from configuration, as [Job(Cron)] does
        new RecurringJobOptions { TimeZoneId = "America/New_York" }
    );

    // A job without arguments, addressed by its class.
    options.SeedRecurringJob<CleanupJob>("cleanup.nightly", "0 30 2 * * *");
});
```

- `SeedRecurringJob<TArgs>(key, request, cronExpression, options?)` addresses the job that accepts `TArgs`. `SeedRecurringJob<TJob>(key, cronExpression, options?)` addresses a job without arguments by its class.
- `RecurringJobOptions` carries the zone, description, retries, retry intervals, node-death policy, and correlation and causation ids. The missed-run and overlap policies come from the job.
- The request is serialized when the seed runs, with the host's request serialization settings.
- The builder rejects a blank key or expression at once. Startup validation, which the `seed` verb runs before writing anything, rejects: two declarations with one key (ordinal comparison), a job the host does not register, `SeedRecurringJob<TJob>` on a job that takes arguments, invalid options, and a cron that cannot fire (not six fields, no future occurrence, an unset `%name%` key, or a zone the host cannot resolve). The cron check matters because the seed restores the declared expression over the stored one, so a typo would replace a working schedule with one that never runs.

**Verify:** run `dotnet MyApp.dll seed` (or `deploy`), then confirm each definition appears in the dashboard's Cron jobs view or through `ICronJobManager`. `seed verify` in a release check catches a deployment that skipped the seed ([utilities.md](../utilities.md#database-seeders)).

### Run the seed as a deploy step

The host start does not seed. Run `dotnet MyApp.dll seed`, or `deploy` (which runs `resources provision` and `seed`), once per release from the new version. A development or test host can call `AddDeployStepsOnStartup()`, whose seeding finishes before the scheduler starts dispatching.

Without a seed:

- A changed cron expression or zone is not applied. The old schedule keeps firing, and its `Idle` and `Queued` occurrences are not retired.
- A cron removed from code keeps its row and keeps firing occurrences that resolve no handler.
- A cron added to code gets no row, so it never fires.
- On the in-memory store every restart loses the cron rows, so an in-memory host needs `AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None)`.

A `seed` run while hosts are running updates the rows. A sleeping scheduler picks up a new or rescheduled cron at its next wake or restart.

With `UseDistributedLock`, a seed that finds the lock held waits up to the 2-minute lease plus 30 seconds, so a lease a crashed seed left behind expires inside the wait. A lock still held after that fails the seed with event `CronSeedMigrationLockTimedOut`. A lock acquire that faults fails the seed with event `CronSeedMigrationLockAcquireFailed`. Nothing retries a failed seed: the `seed` verb exits non-zero, and an `AddDeployStepsOnStartup` host fails to start.

### Reconcile rules

Each seed applies these rules to the rows of its own seed owner:

- **One row per declaration.** Each `[Job(Cron)]` function and each declaration key has one definition, whose primary key derives from the owner and the function or key. A declaration key never shares a row with an attribute cron, even when it equals the function name.
- **Code wins on job, expression, zone, and arguments.** When the stored expression or zone differs, the seed restores it. When a declaration's stored arguments or contract version differ, the seed rewrites both. When a seeded row was pointed at another job, the seed restores the declared job with its arguments and contract version. This overrides values an operator changed through the dashboard or `ICronJobManager.UpdateAsync`. A restore resets the schedule position without replaying missed runs. Arguments compare by a hash of their canonical JSON, so hosts that serialize the same arguments differently, or with and without GZip, do not rewrite each other's row. An attribute cron's arguments and contract version are never rewritten.
- **Policies are written at creation only.** Missed-run, overlap, retry, node-death, description, and lineage values are set when the seed creates the definition. Later code changes do not reach it, and operator edits stay. Change them through `ICronJobManager`.
- **An operator's pause survives.** A live seeded definition stays paused across seeds. Re-declaring a retired definition unpauses it.
- **Removed definitions stop without deleting running work.** For each owned definition missing from the set, the seed deletes its `Idle` and `Queued` occurrences. If an occurrence is still `InProgress`, the seed retires the definition: it pauses the row and marks it retired, keeping the definition, running occurrences, and history so workers can finish. A later seed deletes the retired definition once nothing runs. Without a running occurrence, removal deletes the definition and all its occurrences at once. Renaming a key retires or deletes the old definition and creates a new one with a fresh schedule position.
- **Concurrent seeds converge.** Seeds of the same set from several hosts at once leave one row per declaration. On the EF store each reconcile runs in one transaction.
- **Nothing else is touched.** Rows created at run time (`ScheduleRecurringAsync`, `ICronJobManager.AddAsync`, the dashboard) and rows another owner seeded are never matched, rewritten, or deleted, even when they run the same function.

The reconcile runs on every seed, even when the app declares nothing, so removing the last declaration removes or retires its row. `IgnoreSeedDefinedCronJobs()` removes only the attribute crons from the set: their seeded rows are removed or retired on the next seed, and `SeedRecurringJob` declarations still seed.

### Seed owner

`options.SeedOwner(owner)` names the owner every seeded row records. The default is `"default"`. The owner must not be blank or contain `:`.

- Apps that share one Jobs store must set distinct owners. Two apps under one owner treat each other's seeded rows as their own and remove or retire them.
- Every host of one app must set the same owner, including an app split across executables with `RunOnly`. Hosts with different owners each create their own copy of every definition, and each copy fires.
- Changing an owner orphans the rows seeded under the old one. The next seed creates new rows and never deletes the old ones. Delete the old rows through `ICronJobManager` or the dashboard.
- Run the seed from one version of the app at a time. Each seed treats its declared set as complete, so during a rolling deploy an older version's seed removes or retires definitions a newer version declared, and the rows flip until every host runs one version. This is why seeding from every replica at startup with `AddDeployStepsOnStartup` is unsafe during a rolling deploy.

### Imperative seeders

`UseJobsSeeder(Func<ITimeJobManager<TTimeJob>, Task>)` and `UseJobsSeeder(Func<ICronJobManager<TCronJob>, Task>)` run after the reconcile on every seed. The seed does not track what they write, so an `AddAsync` call inside one inserts a new definition on every seed. Use declarations for recurring jobs the app knows at startup.

## Create recurring jobs at run time

Create a definition from a request with `ScheduleRecurringAsync`. Each call inserts a new definition, so call it only for definitions a user or request creates, never at startup.

```csharp
var recurringId = await scheduler.ScheduleRecurringAsync(
    new OrderReminderRequest(orderId),
    "0 0 9 * * *",
    new RecurringJobOptions { Description = "daily-reminder", TimeZoneId = "America/New_York" },
    ct
);
```

- The call resolves the job's failure policy into the definition's stored retries when it creates the definition.
- Through `unit.Jobs`, the definition commits with the unit ([atomic-enqueue.md](atomic-enqueue.md)). Through the injected scheduler it is its own commit.
- `ICronJobManager.AddAsync` and `AddBatchAsync` also insert a new definition per call. On an EF backend other than PostgreSQL and SQL Server they throw `NotSupportedException` ([storage.md](storage.md#backend-support)).
- Change a definition's expression, zone, or policies through `ICronJobManager.UpdateAsync`. A schedule edit retires the pending occurrence and creates its replacement.

**Verify:** the returned id resolves through `ICronJobManager` or the dashboard's Cron jobs view, with its next fire time.

## Pause and resume

`PauseCronAsync(cronJobId, ct)` and `ResumeCronAsync(cronJobId, ct)` on `IJobScheduler` control one durable definition by id.

```csharp
public static async Task<bool> PauseThenResumeAsync(IJobScheduler scheduler, Guid recurringId, CancellationToken ct)
{
    var pauseAccepted = await scheduler.PauseCronAsync(recurringId, ct);
    var resumeAccepted = await scheduler.ResumeCronAsync(recurringId, ct);
    return pauseAccepted && resumeAccepted;
}
```

- **Pause** returns `true` only when it marks the definition paused and skips its pending `Idle` and `Queued` occurrences in one write. It never cancels an `InProgress` occurrence. While paused, no node claims an occurrence of the definition, including one a lapsed lease or a dead node returned to `Idle`, which waits for resume.
- **Resume** returns `true` only when it wins the definition's revision check and creates exactly one occurrence strictly after the resume instant. It never replays the paused interval: missed-run recovery sees no backlog for the paused span.

**Verify:** after a pause, the definition's pending occurrences show `Skipped`, and no new occurrence appears until resume.

## Misfire recovery

A cron definition stores the instant through which its schedule has been accounted for, so a run missed during an outage is detected on the next wake instead of lost.

- **Pending instants.** An instant is pending when it is at or before now and not yet accounted for, whether or not an occurrence row exists for it.
- **When recovery starts.** A definition enters recovery when more than one instant is pending, or when its one pending instant is older than its grace threshold. The threshold defaults to 60 seconds and is stored per definition, so every node judges it the same way.
- **Creation anchors the schedule.** A definition created at run time is positioned when it is inserted, on the store's statement clock. Ticks between creation and the first scheduler poll belong to its missed-run policy, and creation never manufactures a historical backlog.

| `MissedRunPolicy` | Behavior |
| --- | --- |
| `Coalesce` (default) | Runs exactly one occurrence for the whole missed window, reporting the first missed instant as its scheduled instant |
| `Skip` | Runs nothing and moves past the backlog. Retired occurrences are `Skipped` |

- Recovery never runs an instant twice and never leaves two live occurrences for one instant. An occurrence already running or finished is left alone. One that has not started is reused as the coalesced run or marked `Skipped`.
- A schedule whose interval is shorter than the scheduler's wake latency can re-enter recovery on the next wake. That is expected.
- Bounded catch-up, replaying more than one missed occurrence, is not offered.

Configure it on the job, or set scheduler-wide defaults for definitions that declare neither:

```csharp
// Declared in code: seeds the definition when it is first created.
[Job("reports.nightly", Cron = "0 0 2 * * *", OnMissedRun = MissedRunPolicy.Skip, MissedRunGraceSeconds = 300)]
public sealed class NightlyReportJob : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
```

```csharp
options.ConfigureScheduler(scheduler =>
{
    scheduler.DefaultMissedRunPolicy = MissedRunPolicy.Coalesce;
    scheduler.DefaultMissedRunGraceSeconds = 60;
});
```

**The stored value wins.** The attribute sets the policy only when the definition is created. Changing the attribute later does not change an existing definition. Change it through `ICronJobManager.UpdateAsync`, and that value stays in force across restarts and redeploys.

### What a recovery run sees

```csharp
public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
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

private static Task ProcessSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
    Task.CompletedTask; // the job's own catch-up work
```

`Lateness` reports how late the run started. For a recovery run it measures from the first missed instant, so it spans the outage. It never goes negative. The `headless.jobs.queue.lag` metric measures the same span, tagged `headless.jobs.trigger` = `recovery`.

**Verify:** stop every node across two scheduled instants, restart, and assert one run with `IsRecoveryRun` true under `Coalesce`, or none under `Skip`.

## Overlap policy

A cron definition's overlap policy decides what happens when an occurrence falls due while an earlier occurrence of the same definition is unfinished. It is independent of the missed-run policy and of `MaxConcurrency`.

| `CronOverlapPolicy` | Behavior |
| --- | --- |
| `Allow` (default) | Occurrences run on schedule whether or not an earlier one is unfinished |
| `Skip` | The due occurrence is written as `Skipped` instead of running. The instant counts as handled, so recovery never replays it |

An earlier occurrence is unfinished while it is `Idle`, `Queued`, or `InProgress`. That includes an occurrence waiting to re-run after its node died, an `InProgress` occurrence whose lease lapsed but has not been reclaimed, and an occurrence waiting in `Queued` for a `MaxConcurrency` slot or as `Idle` for a cluster slot.

Example, an hourly definition with `Skip`: the 02:00 occurrence is still `InProgress` at 03:00. The 03:00 occurrence is written as `Skipped` with the reason `"Skipped by overlap policy: an earlier occurrence was still unfinished"`. If the 02:00 run finishes at 03:40, the 04:00 occurrence runs.

```csharp
[Job("reports.nightly", Cron = "0 0 2 * * *", OnOverlap = CronOverlapPolicy.Skip)]
public sealed class NightlyReportJob : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
```

```csharp
options.ConfigureScheduler(scheduler => scheduler.DefaultOverlapPolicy = CronOverlapPolicy.Skip);
```

- **Cluster-wide.** The policy is checked when the occurrence falls due, under a lock on the definition row, so two nodes cannot both decide an instant is free. Do not check for a running sibling inside the job: that check sees only the current node.
- **Covers** ordinary ticks, the one run a missed-run recovery produces, and occurrences that schedule edits and resumes create.
- **Not covered.** A dashboard "Run now" is an explicit operator action and is not subject to the policy, but it counts as unfinished for the next scheduled instant. Cancelling the running occurrence in favor of the new one is not offered.
- **Configuration** follows the missed-run policy: the attribute sets it at creation, `DefaultOverlapPolicy` applies when the attribute leaves it unset, and the stored `OnOverlap` value wins. A change through `ICronJobManager.UpdateAsync` applies to the next occurrence.

**Verify:** a skipped occurrence stays visible as a `Skipped` row with the overlap reason. `headless.jobs.cron.occurrences.skipped` counts it with `headless.jobs.skip_reason` = `overlap`, and each skip logs at `Information` with event id 3242.

## Cron across DST transitions

A cron expression names local wall-clock times. A zone that changes its UTC offset removes some local times (a spring-forward gap) and repeats others (a fall-back overlap). Every scheduling path computes the same fire instants by these rules, for every expression and every shift length:

- **Gap.** Each occurrence whose local time does not exist fires shifted forward by the size of the gap. Occurrences that land on the same instant fire once. In `Africa/Cairo` on 24 April 2026, 00:00 (+02:00) becomes 01:00 (+03:00): `0 */15 0 * * *` fires at 01:00, 01:15, 01:30, and 01:45 (+03:00), and `0 */15 * * * *` fires every 15 minutes with no instant fired twice.
- **Overlap, first pass.** A repeated local time always fires on its first pass. In Cairo on 29 October 2026, 24:00 (+03:00) becomes 23:00 (+02:00): `0 30 23 * * *` fires once, at 23:30 (+03:00).
- **Overlap, second pass.** The second pass fires only when the expression's cadence continues across the repeated window: its occurrence offsets inside the window equal its offsets in the window of the same length just before or just after. On the same Cairo night `0 */15 * * * *` fires every 15 real minutes through both passes, while `0 */15 23 * * *` fires at 23:00–23:45 (+03:00) only. In `America/New_York` on 1 November 2026, `0 30 1,2 * * *` fires three times (01:30 −04:00, 01:30 −05:00, 02:30 −05:00), and `0 0 1/2 * * *` fires at 01:00 (−04:00) and next at 03:00 (−05:00).
- **A missed first pass is an ordinary misfire.** The second pass is not a fallback for it. The definition's `OnMissedRun` decides: `Coalesce` runs it once, late, and `Skip` records it as missed. A definition resumed after its first pass runs again the next day.

There is no per-definition override of these rules. During a rolling deploy that changes them on a transition night, nodes on the old and new versions can compute different instants for one occurrence and run it twice.

## Schedule-interpretation drift

An expression and a zone can stay byte-identical while the instants they resolve to move: a tzdata update shifts a zone's transitions, the cron library reads a field differently, or the framework's DST rule changes. Each definition stores a fingerprint of the rules its next fire time was computed under.

- A background sweep recomputes the next fire time of every definition whose fingerprint no longer matches, whether or not it is due. Every instant the new rules place between the last accounted instant and now is owed, and the definition's `OnMissedRun` policy settles it: `Coalesce` runs it once and `Skip` records it as missed.
- After the host starts, a [warm-up](../utilities.md#warm-up) drains stale fingerprints before the scheduler starts dispatching. The `jobs` readiness check stays `Unhealthy` until that drain finishes. A storage failure during the drain keeps the host not-ready and retries with the warm-up back-off; it never stops the host.
- The periodic sweep runs every `FingerprintSweepInterval` (default 1 hour, at most 24 hours) in pages of `FingerprintSweepBatchSize` (default 100).
- A definition that is invalid on every host (an undefined missed-run policy, a negative grace, an unparseable expression, a blank zone) is deferred with an exponential retry capped at 24 hours.
- A zone that only the running host cannot resolve means that host's tzdata is behind. That host logs it and skips the definition in memory until its revision changes or the process restarts. Peers keep dispatching it.

### Fleet agreement

On the durable store, a node recomputes fire times only when every node that runs the sweep evaluates cron under the same rules. Without that gate, nodes on different rules, such as during a rolling deploy that changes tzdata or the cron library, or with different `SchedulerTimeZone`, would rewrite each other's definitions on every sweep.

- **What a node publishes.** Each durable Jobs node publishes two coordination metadata keys at registration ([coordination.md](../coordination.md#node-metadata)): `headless.jobs.cron-rules-digest`, a digest of its rules for every IANA zone plus the `SchedulerTimeZone` fallback, and `headless.jobs.fingerprint-sweep`, `true` when the node runs background services. A node republishes its digest only when it restarts.
- **Who counts.** Each sweep compares every `Alive` or `Suspected` node whose sweep flag is `true` with its own digest. API-only Jobs hosts and nodes of other features never count. A fleet of one agrees.
- **Deferral.** When a sweeping peer publishes another digest, or membership cannot be read, the sweep changes nothing and retries within `min(FingerprintSweepInterval, 5 minutes)`. Startup activation and readiness never wait on agreement.
- **Mixed window.** Dispatch does not wait for agreement. While nodes disagree, each dispatches under its own rules, so a definition's next instant follows whichever node advanced it last.
- **Same images.** Fingerprints hash each operating system's own zone rules, so a fleet that mixes OS families, or nodes with and without tzdata, never agrees. Give every sweeping node the same OS family and tzdata source, or disable background services on the outlier.
- **One coordination cluster per Jobs store.** Two Jobs stores whose nodes share one coordination `ClusterName` gate each other. Give each store its own cluster.
- **Observe it.** `headless.jobs.cron.fingerprint_sweep.deferred` counts deferred passes, tagged `headless.jobs.deferral_reason` (`fleet_disagreement` or `membership_unavailable`). A disagreement that lasts over an hour logs one warning (event 3233) listing each digest with the nodes that publish it. An unreadable membership snapshot logs event 3234.

The in-memory store is one process, so it recomputes without any agreement check.
