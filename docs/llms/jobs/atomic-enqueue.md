---
domain: Jobs (Background Jobs)
packages:
---

# Schedule jobs inside a transaction

> Part of [Jobs](../jobs.md). Commit a job row in the same transaction as your business writes through `unit.Jobs`, and know what the injected scheduler does instead.

## Enlisted enqueue (atomic enqueue)

The receiver you call decides enlistment. No option, function policy, or host default forces a job onto a unit of work.

- **Enlisted receivers** live on the unit: `unit.Jobs` (an `IJobScheduler`), `unit.TimeJobs<T>()` (an `ITimeJobManager<T>`), and `unit.CronJobs<T>()` (an `ICronJobManager<T>`). `Headless.Jobs.Abstractions` adds them to `IUnitOfWork`. Every write through them lands in the unit's transaction, and dispatch, scheduler wake, and dashboard notifications run after the commit.
- **Autonomous receivers** are the injected `IJobScheduler`, `ITimeJobManager<T>`, and `ICronJobManager<T>`. They are singletons that write in the store's own transaction with immediate side effects, and they never inspect an open unit.
- **Callees.** There is no ambient lookup. Hand a callee the unit, or let it read the unit from the `DbContext` it was begun on with `db.UnitOfWork()`.

This is the same model as Messaging, where `IBus` and `IQueue` publish autonomously and `unit.Outbox` publishes inside the transaction ([unit-of-work.md](../unit-of-work.md#guarantee-matrix)). A job scheduled from several sites, such as the order transaction, an admin "resend" endpoint, and a backfill, picks the receiver at each site.

```csharp
// Capture a stable absolute deadline before any retry of the business operation.
var reminderDueAt = timeProvider.GetUtcNow().AddHours(24);
await factory.RunAsync(
    db,
    async (unit, ct) =>
    {
        db.Set<Order>().Add(order);
        await db.SaveChangesAsync(ct);
        // unit.Outbox, not an injected IBus: the autonomous publisher would leave a row behind on rollback.
        await unit.Outbox.PublishAsync(new OrderPlaced(order.Id), ct);
        // unit.Jobs, not an injected IJobScheduler, for the same reason.
        await unit.Jobs.ScheduleAsync(new OrderReminderRequest(order.Id), reminderDueAt, ct);
    },
    cancellationToken: cancellationToken
);
// The application row, the durable message, and the job row commit or roll back together.
// IUnitOfWorkFactory.RunAsync(db, ...) (Headless.UnitOfWork.EntityFramework) opens the transaction,
// for a HeadlessDbContext and a plain DbContext alike.
```

You can also begin a unit explicitly with `await using var unit = await factory.BeginAsync(db, ct)`.

| Receiver | Write |
| --- | --- |
| `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()` over a unit with a live relational resource on the Jobs database | The row enlists in the unit's transaction. Side effects run after commit |
| `unit.Jobs` over a unit with no resource, a dead resource, or one on another database ([unit-of-work.md](../unit-of-work.md#database-identity-and-several-databases)) | Throws before scheduling middleware, persistence, or side effects. It never degrades to an autonomous write |
| Injected `IJobScheduler` or manager | Inserts in the store's own transaction. Side effects run at once |

**Wiring.** `UsePostgreSql<TContext>` and `UseSqlServer<TContext>` register the EF Core unit-of-work provider for you. On the advanced `UseEntityFramework` path, register it with `services.AddEntityFrameworkUnitOfWork()`. Cluster membership (`Headless.Coordination`) and enlistment (`Headless.UnitOfWork`) stay separate subsystems.

**Analyzer.** With `Headless.UnitOfWork.Analyzers` referenced, [HF2002](../unit-of-work.md#hf2002) reports an injected `IJobScheduler`, `ITimeJobManager<T>`, or `ICronJobManager<T>` call made while a unit is in scope, and its code fix moves the call onto `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()`.

**Verify:** in an integration test, schedule through `unit.Jobs`, throw before the unit commits, and assert the job row is absent. Commit the same block and assert the row exists.

### What enlists and what does not

- **Adds enlist.** `EnqueueAsync`, `ScheduleAsync`, `ScheduleAfterAsync`, `ScheduleRecurringAsync`, chains, keyed schedule and replace, and `CancelKeyedAsync` enlist through `unit.Jobs`. A recurring definition written through `unit.Jobs` commits or rolls back with its schedule position.
- **Other changes do not enlist.** Update, delete, `CancelAsync(jobId)`, pause, resume, and requeue through `unit.Jobs` each run on their own connection and transaction and commit at once, so rolling back the unit does not undo them. On SQL Server without `READ_COMMITTED_SNAPSHOT`, such a call on a row the same unit inserted and has not committed blocks behind the unit's own lock until the command times out. Make those changes after the unit commits.
- **Keyed results are provisional.** A keyed result returned inside a unit has `IsProvisional = true` until the outer commit.
- **Run-now and delayed due times anchor at the write,** not the commit ([scheduling.md](scheduling.md#due-times-follow-the-store-clock)).

### Failures and replay

- The enlisted path throws on any failure: validation, a dead or completed transaction, or a wiring mistake. Wrap it in `try/catch` where you need to handle that.
- Operations that share one unit's relational resource run sequentially. The connection and transaction are not thread-safe.
- Under EF's execution strategy, `IUnitOfWorkFactory.RunAsync(db, …)` replays the whole block, job writes included, for a failure before the commit starts. The first attempt's rows roll back and the replay schedules again. Capture absolute due times before the block, as in the example.
- From a domain-event handler inside the `HeadlessDbContext` save pipeline (observed mode), an enlisted Jobs write calls `IUnitOfWork.PreventRetry()`, because the pipeline does not re-run that handler on replay. A later failure then surfaces without replay. Recover with a fresh context after a known rollback, or reconcile an unknown commit first.
- A keyed write needs savepoints. A failed savepoint restore, or a transaction poisoned before commit, needs an outer rollback and a fresh unit. An unknown commit outcome may already be durable: reconcile the key and business state before you retry.
- Keyed writes reuse the unit's open connection and transaction after checking the configured provider, endpoint, and database. An external connection must be unowned (`contextOwnsConnection: false`). An owned external connection is rejected.
- `OnCompleted` callbacks ignore savepoints. A job row written inside a savepoint that later rolls back still sends its post-commit signal when the outer transaction commits. The worker re-reads due rows and finds nothing, so this is harmless.

### After the commit

- A returned entity on the enlisted path means the row enlisted, not that dispatch ran.
- Post-commit work needs a running host. The commit hands one signal per write to a hosted worker, registered even with `DisableBackgroundServices()`. The worker acquires immediately due jobs, wakes the scheduler, invalidates cached cron expressions, and notifies the dashboard.
- The worker's queue holds 1,024 signals. A full queue or a stopping host drops the signal with a warning and increments `headless.jobs.post_commit_signals.dropped`. A signal that runs longer than 30 seconds is abandoned. In every case the next fallback poll picks the committed row up: work is delayed, never lost.
- A dropped cron-cache invalidation leaves cached cron expressions stale until the entry expires (10 minutes). No claim reads that cache.
- Committed rows stay authoritative if post-commit work fails.
