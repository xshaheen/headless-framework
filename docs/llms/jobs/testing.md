---
domain: Jobs (Background Jobs)
packages:
---

# Test code that uses jobs

> Part of [Jobs](../jobs.md). Unit-test a job handler, run a test host that schedules without executing, and know what integration hosts do with Jobs at startup.

## Unit-test a handler

Construct the job and a `JobContext` directly, and call `ExecuteAsync`. A typed job takes a `JobContext<TArgs>` built from a base context and the argument:

```csharp
using Headless.Jobs;

[Job("orders.process")]
public sealed class ProcessOrderJob(IOrderService orders) : IJob<OrderRequest>
{
    public async ValueTask ExecuteAsync(JobContext<OrderRequest> context, CancellationToken ct) =>
        await orders.ProcessAsync(context.Request, ct);
}

public static class ProcessOrderJobTest
{
    public static async Task RunsTheOrderAsync(IOrderService orders, CancellationToken ct)
    {
        var job = new ProcessOrderJob(orders);
        var context = new JobContext<OrderRequest>(
            new JobContext { FunctionName = "orders.process" },
            new OrderRequest("order-1")
        );

        await job.ExecuteAsync(context, ct);
        // Assert on the IOrderService fake.
    }
}
```

- `ReportProgress` on a context the scheduler did not create validates its arguments and records nothing, so a handler that reports progress runs unchanged.
- `RequestCancellationAsync` needs a context the scheduler created. Test cancellation through a test host instead.

## Run a test host that schedules but does not execute

Call `options.DisableBackgroundServices()` so the host registers the scheduler facade and managers but never claims or runs a job. Assert on the stored rows instead.

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.AddModule<MyApp.JobsModule>();
    options.DisableBackgroundServices();
    options.DisableStartupInitialization(); // also skip store access at startup
});
```

- **`DisableStartupInitialization()`** skips the startup catalog checks, the cron fingerprint drain, and, on the durable store, node registration before start. It requires `DisableBackgroundServices()`. `AddHeadlessJobs` throws otherwise. A startup validator still builds the job catalog and validates scheduling policies, so a missing module or a bad `Tune` still fails the host.
- **Durable store.** A durable-store test host also disables the membership heartbeat, so the node neither registers nor beats. Do not remove hosted services from the collection instead.

  ```csharp
  using Microsoft.EntityFrameworkCore;

  builder.Services.AddDbContext<AppDbContext>(db => db.UseNpgsql(connectionString));
  builder.Services.AddHeadlessJobs(options =>
  {
      options.AddModule<MyApp.JobsModule>();
      options.UsePostgreSql<AppDbContext>(coordination => coordination.DisableMembershipHeartbeat());
      options.DisableBackgroundServices();
      options.DisableStartupInitialization();
  });
  ```

  On the advanced `UseEntityFramework` path, call `DisableMembershipHeartbeat()` on `AddHeadlessCoordination` ([coordination.md](../coordination.md)).
- `DisableMembershipHeartbeat()` with background services on fails `AddHeadlessJobs`, because peers would declare a node that claims rows without beating dead and release its running rows.
- The host still runs dead-owner recovery, so rows left by dead nodes are still released.
- The post-commit worker still runs, so `unit.Jobs` writes still notify the dashboard.

**Verify:** schedule through `IJobScheduler`, then assert the row exists through `ITimeJobManager<TimeJobEntity>` or your `DbContext`, with status `Idle`.

## Integration hosts

- **`HeadlessTestServer`** runs seeders on start by default (`seed: true`), so `[Job(Cron = ...)]` and `SeedRecurringJob` definitions exist before the tests. It returns only once the warm-ups finish, including the Jobs fingerprint drain ([testing.md](../testing.md#headlesstestingaspnetcore)).
- **Database reset** clears the Jobs tables, including seeded cron definitions. A test that relies on them adds the cron-job table to `TablesToPreserve` ([testing.md](../testing.md#tables-a-reset-preserves)).
- **Time.** In-memory leases follow the registered `TimeProvider`. EF leases follow the database clock, so a fake application clock does not control lease deadlines in an EF test. Callback timeouts (`OnExhaustedTimeout`) and post-commit signal timeouts wait real time, so a `FakeTimeProvider` that never advances still releases a hung callback.
- **The in-memory store** loses cron rows on every restart. A host on it needs `AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None)` to reseed.
- **Running jobs in a test.** With background services on, a due job runs on the host's scheduler. Wait on a signal your handler sets rather than a fixed delay.
