# Headless.Jobs.Abstractions

Contracts, entity types, manager interfaces, and execution primitives for the Jobs system.

## Problem Solved

Provides the shared contracts — `IJobScheduler`, `ITimeJobManager<TTimeJob>`, `ICronJobManager<TCronJob>`, descriptors, entity types, options, enums, exception types, and execution context — that decouple job enqueueing code from any specific Jobs persistence provider or scheduler implementation. All consumer code can target these abstractions without referencing `Headless.Jobs.EntityFramework`.

## Key Features

- **Routine scheduling facade**: `IJobScheduler` resolves generated `[JobFunction]` metadata, serializes typed requests, and schedules immediate, delayed, and recurring jobs without copied function strings or entity construction.
- **Generated descriptors**: immutable `JobFunctionDescriptor` values expose function identity, nullable request type, cron metadata, priority, and maximum concurrency without exposing execution delegates.
- **Scheduling options**: `EnqueueOptions` and `RecurringJobOptions` map description, durable retry count/intervals, and node-death policy. Priority remains immutable `[JobFunction]` / descriptor metadata.
- **Manager interfaces**: `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` with `AddAsync`, `AddBatchAsync`, `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, `DeleteBatchAsync`.
- **Entity types**: `TimeJobEntity` / `TimeJobEntity<TTicker>` (parent–child chains), `CronJobEntity`, `CronJobOccurrenceEntity`, and `BaseJobEntity`.
- **Execution context**: `JobFunctionContext` and `JobFunctionContext<TRequest>` — exposes `Id`, `Type`, `RetryCount`, `IsDue`, `ScheduledFor`, `FunctionName`, `CronOccurrenceOperations`, and `RequestCancellation()`.
- **Attribute types**: `JobFunctionAttribute` (`[JobFunction]`) for function/cron registration; `JobsConstructorAttribute` (`[JobsConstructor]`) for custom DI injection.
- **Retry primitives**: `TimeJobEntity.Retries`, `RetryIntervals`, `RetryCount`; `CronJobEntity.Retries`, `RetryIntervals`.

These fields are the durable representation. Runtime retry predicates, delays, and observation are configured with Polly.Core directly by `Headless.Jobs.Core`; Polly objects and delegates are never serialized into job rows.
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
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

public sealed record OrderReminderRequest(string OrderId);

// The facade resolves the generated descriptor for OrderReminderRequest.
public sealed class OrderService(IJobScheduler jobs)
{
    public async Task ScheduleReminderAsync(string orderId, CancellationToken ct)
    {
        var jobId = await jobs.EnqueueAsync(
            new OrderReminderRequest(orderId),
            new EnqueueOptions
            {
                Description = $"order-reminder-{orderId}",
                Retries = 3,
                RetryIntervals = [30, 60, 120],
            },
            ct
        );

        Console.WriteLine($"Scheduled {jobId}");
    }
}

// Requestless functions use their generated descriptor instead of a synthetic request type.
public sealed class CleanupService(IJobScheduler jobs)
{
    public Task<Guid> RunAsync(JobFunctionDescriptor descriptor, CancellationToken ct) =>
        jobs.EnqueueAsync(
            descriptor,
            new EnqueueOptions
            {
                Description = "manual-cleanup",
            },
            ct
        );
}

// Mark methods for registration (requires Headless.Jobs.SourceGenerator).
[JobFunction("SendOrderReminder")]
public static Task SendReminderAsync(
    JobFunctionContext<OrderReminderRequest> context,
    CancellationToken ct
)
{
    // context.Request.OrderId, context.RetryCount, and context.ScheduledFor are available.
    return Task.CompletedTask;
}

[JobFunction("Cleanup")]
public static Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;
```

All facade methods return the persisted entity `Guid`; recurring scheduling returns the persisted cron-definition ID. Unknown request types or descriptor names throw `JobFunctionNotFoundException` before persistence. Duplicate function names or typed request mappings fail deterministically while `JobFunctionProvider` builds its configuration-independent canonical indexes; Core projects a separate configuration-resolved runtime registry for each `IHost`.

Delayed and recurring scheduling keep time and cron expressions explicit:

```csharp
var delayedId = await jobs.ScheduleAsync(
    new OrderReminderRequest(orderId),
    DateTime.UtcNow.AddHours(24),
    new EnqueueOptions { Description = "delayed-reminder" },
    ct
);

var recurringId = await jobs.ScheduleRecurringAsync(
    new OrderReminderRequest(orderId),
    "0 0 * * *",
    new RecurringJobOptions { Description = "daily-reminder" },
    ct
);
```

The managers remain supported public APIs. Use `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` for CRUD, batching, seeding, custom entity types, chains, or other advanced persistence scenarios:

```csharp
await timeJobManager.AddAsync(
    new TimeJobEntity
    {
        Function = "SendOrderReminder",
        ExecutionTime = DateTime.UtcNow.AddHours(24),
        Request = serializedRequest,
    },
    ct
);
```

## Configuration

None at the abstractions layer. All configuration is done in `Headless.Jobs.Core` via `AddHeadlessJobs(options => ...)`.

## Dependencies

- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

None.

## Commit Coordination (Atomic Enqueue)

When a `Headless.CommitCoordination` provider is registered (`services.AddPostgreSqlCommitCoordination()` or `services.AddSqlServerCommitCoordination()`), `IJobScheduler` inherits the same atomic behavior from the managers it calls. `ITimeJobManager.AddAsync` / `AddBatchAsync` and `ICronJobManager.AddAsync` / `AddBatchAsync` write the job row inside the caller's ambient transaction and defer dispatch, scheduler restart, notifications, and cron-cache invalidation to post-commit.

```csharp
await db.ExecuteCoordinatedTransactionAsync(
    async (ctx, ct) =>
    {
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);

        await outboxBus.PublishAsync(new OrderPlaced(order.Id), ct);

        await jobScheduler.ScheduleAsync(
            new OrderReminderRequest(order.Id),
            DateTime.UtcNow.AddHours(24),
            new EnqueueOptions { Description = "order-reminder" },
            ct
        );
    },
    services,
    ct
);
```

The coordinated path needs **two** separate registrations: `AddHeadlessCoordination(...)` (the `Headless.Coordination` distributed-lock/membership subsystem for the operational store) AND `Add{Provider}CommitCoordination()` (the `Headless.CommitCoordination` transactional scope subsystem). Similar names, different systems.

`AddAsync` / `AddBatchAsync` **throw** on failure; wrap in `try/catch`. Establish the coordinated scope synchronously (the provided `ExecuteCoordinatedTransactionAsync` helpers do this correctly). Once established, the scope flows across awaits inside the operation, so domain writes and message publishes may be awaited before the job enqueue.
