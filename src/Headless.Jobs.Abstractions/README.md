# Headless.Jobs.Abstractions

Simple utilities for queuing and executing cron/time-based jobs in the background.

---

## 📦 Installation
[Headless.Jobs.Abstractions.csproj](Headless.Jobs.Abstractions.csproj)
```bash
dotnet add package Headless.Jobs.Abstractions
```

## Error Handling Primitives

This package provides the core contracts used by Jobs error handling and retry flow:

- `IJobExceptionHandler` for global exception/cancellation hooks.
- `JobFunctionContext.RetryCount` for attempt-aware logic.
- `JobFunctionContext.RequestCancellation()` to mark jobs as cancelled.
- `CronOccurrenceOperations.SkipIfAlreadyRunning()` for overlap-safe cron execution.

Status values are represented by `JobStatus` (`Failed`, `Cancelled`, `Skipped`, etc.).

## Core Types

- `TimeJobEntity` for one-off scheduled jobs (`ExecutionTime`, `Retries`, `RetryIntervals`).
- `CronJobEntity` for recurring jobs (`Expression`, `Retries`, `RetryIntervals`).
- `JobFunctionContext` / `JobFunctionContext<TRequest>` for runtime execution context.
- `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` for enqueue/update/delete operations.

## Commit Coordination (Atomic Enqueue)

When a relational commit coordinator is active (see `Headless.CommitCoordination`), `ITimeJobManager.AddAsync` /
`AddBatchAsync` and `ICronJobManager.AddAsync` / `AddBatchAsync` write the job row **inside the caller's ambient
transaction** and defer their dispatch / scheduler-restart / notify / cron-cache-invalidation side effects to
post-commit — mirroring the messaging outbox. A domain write, an integration-event publish, and a job enqueue can
therefore commit (or roll back) as one unit:

```csharp
// db is a relational DbContext; services is the request scope; both are enlisted by the helper.
await db.ExecuteCoordinatedTransactionAsync(async (ctx, ct) =>
{
    ctx.Orders.Add(order);                                       // domain write
    await ctx.SaveChangesAsync(ct);

    await outboxBus.PublishAsync(new OrderPlaced(order.Id), ct); // message publish (outbox)

    await timeJobManager.AddAsync(new TimeJobEntity              // job enqueue, written into the same transaction
    {
        Function = "SendOrderReminder",
        ExecutionTime = DateTime.UtcNow.AddHours(24),
        Request = JobsHelper.SerializeRequest(new { order.Id }),
    }, ct);
}, services, ct);
// On commit: order row + outbox message + job row all persist; the job's dispatch/scheduler/notify fire post-commit.
// On rollback: none persist and no dispatch occurs.
```

**Required DI registration**: atomic enqueue activates only when a `Headless.CommitCoordination` provider is registered —
`services.AddPostgreSqlCommitCoordination()` or `services.AddSqlServerCommitCoordination()` (core seam:
`AddCommitCoordination()`). This is a **different subsystem** from `AddHeadlessCoordination(...)`, which is the
`Headless.Coordination` distributed-lock / node-membership provider required by the durable operational store
(`AddOperationalStore`). The durable atomic-enqueue path needs **both**: `AddHeadlessCoordination(...)` for the
operational store, **and** a `Add{Provider}CommitCoordination()` for the commit-coordination scope. The similar names
name two distinct systems — register both.

**Capture is synchronous (pre-await)**: the ambient commit-coordinator scope is an `AsyncLocal` captured at the point
`AddAsync` is entered. Do **not** wrap `AddAsync` behind an intermediate `async` method that `await`s before reaching
`AddAsync` — any `await` before that call executes outside the captured scope, so the enqueue silently falls back to the
direct path and auto-commits even if the outer transaction rolls back.

**Concurrency**: coordinated enqueues within a single coordinated scope must not run concurrently — the scope's single
DB connection / transaction is not thread-safe (the same constraint as any code sharing one EF `DbContext` /
connection). Keep enqueue calls in one scope sequential.

Behavior and caveats:

- **No coordinator (or no `AddOperationalStore`)**: unchanged — `AddAsync` direct-inserts and dispatches in-band.
- **Coordinated scope with no relational capability** (e.g. a messaging-only scope): falls back to the direct path —
  coordination is not made infectious.
- **Fail loud**: `AddAsync` throws only when a relational transaction was offered but is unusable (dead / completed),
  or when a relational coordinator is active but the provider cannot write coordinated. Silent fallback there would
  reintroduce the divergence this feature prevents.
- **Return-contract (SLA)**: on the coordinated path `JobResult.IsSucceeded` means the row **committed**, not that the
  deferred dispatch ran. A post-commit dispatch failure is swallowed (the commit is already durable); the fallback poll
  sweep (`FallbackIntervalChecker`, default 30s) is the recovery path.
- **Tenancy** stamping inside the coordinated write is out of scope until a tenant column exists (issue #278).
