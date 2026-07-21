# Headless.Jobs.Abstractions

Contracts, entity types, manager interfaces, and execution primitives for the Jobs system.

## Problem Solved

Provides the shared contracts — `IJobScheduler`, `ITimeJobManager<TTimeJob>`, `ICronJobManager<TCronJob>`, descriptors, entity types, options, enums, exception types, and execution context — that decouple job enqueueing code from any specific Jobs persistence provider or scheduler implementation. All consumer code can target these abstractions without referencing `Headless.Jobs.EntityFramework`.

## Key Features

- **Routine scheduling facade**: `IJobScheduler` resolves generated `[JobFunction]` metadata, serializes typed requests, schedules immediate, delayed, and recurring jobs, requests durable cancellation by job ID, and pauses or resumes cron definitions by ID.
- **Generated descriptors**: immutable `JobFunctionDescriptor` values expose function identity, nullable request type, cron metadata, priority, and maximum concurrency without exposing execution delegates.
- **Scheduling options**: `EnqueueOptions` and `RecurringJobOptions` map description, durable retry count/intervals, and node-death policy; recurring options also accept a nullable IANA `TimeZoneId`. Priority remains immutable `[JobFunction]` / descriptor metadata.
- **Manager interfaces**: `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` with `AddAsync`, `AddBatchAsync`, `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, `DeleteBatchAsync`.
- **Entity types**: `TimeJobEntity` / `TimeJobEntity<TTicker>` (parent–child chains), `CronJobEntity`, `CronJobOccurrenceEntity`, and `BaseJobEntity`. New entities keep `Id`, `CreatedAt`, and `UpdatedAt` unset until a Jobs manager stamps them during `AddAsync` / `AddBatchAsync`.
- **Execution context**: `JobFunctionContext` and `JobFunctionContext<TRequest>` — exposes `Id`, `Type`, `RetryCount`, `IsDue`, `ScheduledFor`, `FunctionName`, `CronOccurrenceOperations`, and durable `RequestCancellationAsync()` for time jobs.
- **Generated execution delegate**: `JobFunctionDelegate(IServiceProvider, JobFunctionContext, CancellationToken)` keeps the cancellation token last. Rebuild source-generated consumers together with the Jobs runtime when upgrading this contract.
- **Attribute types**: `JobFunctionAttribute` (`[JobFunction]`) for function/cron registration; `JobsConstructorAttribute` (`[JobsConstructor]`) for custom DI injection.
- **Retry primitives**: `TimeJobEntity.Retries`, `RetryIntervals`, `RetryCount`; `CronJobEntity.Retries`, `RetryIntervals`.

These fields are the durable representation. Runtime retry predicates, delays, and observation are configured with Polly.Core directly by `Headless.Jobs.Core`; Polly objects and delegates are never serialized into job rows.
- **Dashboard provider projection**: `IJobPersistenceProvider.GetCronOccurrenceGraphStatusCountsAsync` returns
  date/status counts plus the exact inclusive graph boundaries without requiring occurrence entities for empty dates.
- **Node-death policy**: `NodeDeathPolicy` enum (`Retry` / `MarkFailed` / `Skip`) on both entity types; propagated from `CronJobEntity` to every generated occurrence.
- **Exception types**: `JobValidatorException` (with `Errors` list for batch failures); `TerminateExecutionException` (stop without retry, optional final `JobStatus`).
- **Typed job chains**: `JobChain` / `JobChainBuilder` / `JobChainNodeBuilder` author a conditional sequential tree of descriptor-backed steps — `Then` (on-success) and `Catch` (on-failure), one of each per node — frozen by `Build()` into an immutable `JobChain` and enqueued atomically through `IJobScheduler.EnqueueAsync(JobChain, …)`.
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

The generated delegate ABI uses service provider, context, then cancellation token. Handwritten registrations use the same order:

```csharp
using Headless.Jobs;

JobFunctionDelegate handler = static (serviceProvider, context, cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    return Task.CompletedTask;
};
```

All facade methods return the persisted entity `Guid`; recurring scheduling returns the persisted cron-definition ID. Unknown request types or descriptor names throw `JobFunctionNotFoundException` before persistence. Duplicate function names or typed request mappings fail deterministically while `JobFunctionProvider` builds its configuration-independent canonical indexes; Core projects a separate configuration-resolved runtime registry for each `IHost`.

`IJobScheduler.CancelAsync(jobId)` is job-ID-only and durable. It returns `true` only for the first accepted request: an idle job becomes terminal `Cancelled`, while queued or in-progress work records `CancelRequested` for its owning node to observe. Unknown, already-requested, and terminal jobs return `false`. `CancelRequested` remains audit data even when an in-progress handler ignores its token and completes naturally.

`IJobScheduler.PauseCronAsync(cronJobId)` and `ResumeCronAsync(cronJobId)` are descriptor-backed, durable definition controls. Each returns `true` only when it wins the state transition. Pause atomically marks the definition paused and skips pending `Idle` / `Queued` occurrences without cancelling `InProgress` work. Resume creates exactly one next occurrence strictly after the resume time; it never replays the paused interval.

Relational consumers must apply the Jobs migrations before deployment, including non-null `TimeJobs.CancelRequested` (`false` default). Cron definitions now persist nullable `TimeZoneId`, non-null `IsPaused` (`false` default), and non-null `ScheduleRevision` (`0` default). The cron-occurrence uniqueness constraint applies only to live `Idle` / `Queued` / `InProgress` rows so a resumed definition can schedule an instant previously terminalized as `Skipped`. Quiesce scheduler nodes while replacing that index because the reference migrations use blocking DDL. The PostgreSQL demos and SQL Server conformance project include reference migrations; custom stores own the equivalent application migration. Custom `IJobPersistenceProvider` implementations must also implement the new atomic pause, resume, and definition-update operations with the documented boolean/null and all-or-nothing semantics before upgrading.

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
    new RecurringJobOptions { Description = "daily-reminder", TimeZoneId = "America/New_York" },
    ct
);

var pauseAccepted = await jobs.PauseCronAsync(recurringId, ct);
var resumeAccepted = await jobs.ResumeCronAsync(recurringId, ct);
```

`TimeZoneId` accepts IANA identifiers only. A null value falls back to the configured scheduler-global timezone. Cron expressions are evaluated in that timezone, while every occurrence remains a UTC instant in persistence. DST gaps shift forward by the gap; overlaps select the later UTC instant deterministically.

Compose a multi-step workflow with the typed `JobChain` model. Each step's identity is a generated `JobFunctionDescriptor`, resolved from the step's payload type (or supplied explicitly for a requestless step); no handler contract is named:

```csharp
using Headless.Jobs;

var chain = JobChain.Start(new ProcessOrder(orderId));
var chargeCard = chain.Root.Then(new ChargeCard(orderId));   // runs when ProcessOrder succeeds
chargeCard.Then(new SendReceipt(orderId));                   // runs when ChargeCard succeeds
chargeCard.Catch(new RefundPayment(orderId));                // runs when ChargeCard fails

var rootJobId = await jobs.EnqueueAsync(chain.Build(), ct);
```

`Then` attaches the single on-success child and `Catch` the single on-failure child (a second edge of the same kind on one node throws `InvalidOperationException`); each returns the new child handle so a branch extends further. `Catch` is pure on-failure sugar — it never recovers the parent, which stays `Failed`. `Build()` freezes an immutable chain, and `EnqueueAsync(JobChain, …)` resolves every descriptor, enforces the configured `MaxChainDepth` (default 10, ceiling `JobChain.MaxStructuralDepth` = 64), and persists the whole tree in one atomic write — re-enqueueing a built chain yields independent trees. Per-step `EnqueueOptions` and an optional execution time apply per node; priority stays descriptor-canonical. `JobChain` replaces the removed fluent chain builder; see [docs/llms/jobs.md](../../docs/llms/jobs.md) for chain semantics, timed-descendant gating, and migration guidance.

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

`GetCronOccurrenceGraphStatusCountsAsync` is an additive persistence-provider SPI method. Its default implementation
preserves third-party provider compatibility by reducing the existing occurrence-list result in memory. Durable
providers should override it to select distinct UTC dates and aggregate status counts in storage; boundary entries
have `IsRangeBoundary = true`, a zero count, and a status value that callers must ignore.

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
