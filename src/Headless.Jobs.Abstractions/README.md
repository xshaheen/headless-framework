# Headless.Jobs.Abstractions

Contracts, entity types, manager interfaces, and execution primitives for the Jobs system.

## Problem Solved

Provides the shared contracts — `ITimeJobManager<TTimeJob>`, `ICronJobManager<TCronJob>`, entity types, enums, exception types, and execution context — that decouple job enqueueing code from any specific Jobs persistence provider or scheduler implementation. All consumer code can target these abstractions without referencing `Headless.Jobs.Core` or `Headless.Jobs.EntityFramework`.

## Key Features

- **Manager interfaces**: `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` with `AddAsync`, `AddBatchAsync`, `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, `DeleteBatchAsync`.
- **Entity types**: `TimeJobEntity` / `TimeJobEntity<TTicker>` (parent–child chains), `CronJobEntity`, `CronJobOccurrenceEntity`, and `BaseJobEntity`.
- **Execution context**: `JobFunctionContext` and `JobFunctionContext<TRequest>` — exposes `Id`, `Type`, `RetryCount`, `IsDue`, `ScheduledFor`, `FunctionName`, `CronOccurrenceOperations`, and `RequestCancellation()`.
- **Attribute types**: `JobFunctionAttribute` (`[JobFunction]`) for function/cron registration; `JobsConstructorAttribute` (`[JobsConstructor]`) for custom DI injection.
- **Retry primitives**: `TimeJobEntity.Retries`, `RetryIntervals`, `RetryCount`; `CronJobEntity.Retries`, `RetryIntervals`.
- **Node-death policy**: `NodeDeathPolicy` enum (`Retry` / `MarkFailed` / `Skip`) on both entity types; propagated from `CronJobEntity` to every generated occurrence.
- **Exception types**: `JobValidatorException` (with `Errors` list for batch failures); `TerminateExecutionException` (stop without retry, optional final `JobStatus`).
- **Fluent chain builder**: `FluentChainJobBuilder<TTimeJob>` for defining parent–child–grandchild job chains up to 3 levels / 5 siblings per level.
- **Global exception handler**: `IJobExceptionHandler` with `HandleExceptionAsync` and `HandleCanceledExceptionAsync`.
- **Job status**: `JobStatus` enum: `Idle`, `Queued`, `InProgress`, `Succeeded`, `DueDone`, `Failed`, `Cancelled`, `Skipped`.

## Installation

```bash
dotnet add package Headless.Jobs.Abstractions
```

Pulled in transitively by `Headless.Jobs.Core`. Install directly only when building a library that targets Jobs interfaces without depending on the Core implementation.

## Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;

// Inject manager and enqueue a time job
public sealed class OrderService(ITimeJobManager<TimeJobEntity> jobs)
{
    public async Task ScheduleReminderAsync(string orderId, CancellationToken ct)
    {
        await jobs.AddAsync(new TimeJobEntity
        {
            Function = "SendOrderReminder",
            Description = $"order-reminder-{orderId}",
            ExecutionTime = DateTime.UtcNow.AddHours(24),
            Request = JobsHelper.SerializeRequest(new { OrderId = orderId }),
            Retries = 3,
            RetryIntervals = [30, 60, 120],
        }, ct);
    }
}

// Mark a method for registration (requires Headless.Jobs.SourceGenerator)
[JobFunction("SendOrderReminder")]
public static async Task ExecuteAsync(
    JobFunctionContext<OrderReminderRequest> context,
    CancellationToken ct)
{
    // context.Request.OrderId, context.RetryCount, context.ScheduledFor available
}
```

## Configuration

None at the abstractions layer. All configuration is done in `Headless.Jobs.Core` via `AddHeadlessJobs(options => ...)`.

## Dependencies

None. Zero external NuGet dependencies.

## Side Effects

None.

## Commit Coordination (Atomic Enqueue)

When a `Headless.CommitCoordination` provider is registered (`services.AddPostgreSqlCommitCoordination()` or `services.AddSqlServerCommitCoordination()`), `ITimeJobManager.AddAsync` / `AddBatchAsync` and `ICronJobManager.AddAsync` / `AddBatchAsync` write the job row inside the caller's ambient transaction and defer dispatch, scheduler restart, notifications, and cron-cache invalidation to post-commit.

```csharp
await db.ExecuteCoordinatedTransactionAsync(async (ctx, ct) =>
{
    ctx.Orders.Add(order);
    await ctx.SaveChangesAsync(ct);

    await outboxBus.PublishAsync(new OrderPlaced(order.Id), ct);

    await timeJobManager.AddAsync(new TimeJobEntity
    {
        Function = "SendOrderReminder",
        ExecutionTime = DateTime.UtcNow.AddHours(24),
        Request = JobsHelper.SerializeRequest(new { order.Id }),
    }, ct);
}, services, ct);
```

The coordinated path needs **two** separate registrations: `AddHeadlessCoordination(...)` (the `Headless.Coordination` distributed-lock/membership subsystem for the operational store) AND `Add{Provider}CommitCoordination()` (the `Headless.CommitCoordination` transactional scope subsystem). Similar names, different systems.

`AddAsync` / `AddBatchAsync` **throw** on failure; wrap in `try/catch`. The scope capture is synchronous — do not `await` before calling `AddAsync` inside a coordinated scope or the scope is lost.
