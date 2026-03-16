# Headless.Jobs.Core

Core implementation of Jobs, a high-performance background job scheduler for .NET with cron expressions and time-based scheduling.

## Problem Solved

Provides reliable, distributed background job scheduling with cron expressions, delayed execution, custom task scheduling, and real-time monitoring without external dependencies like Hangfire or Quartz.

## Key Features

- **Cron Scheduling**: Full cron expression support with timezone handling
- **Time-Based Jobs**: Schedule jobs at specific times or intervals
- **Custom Thread Pool**: Optimized task scheduler for background jobs
- **Persistence**: In-memory or database-backed job storage
- **Fallback**: Automatic recovery and retry for failed jobs
- **Zero Allocations**: High-performance execution with minimal GC pressure
- **Hot Reload**: Dynamic job registration and configuration updates

## Installation

```bash
dotnet add package Headless.Jobs.Core
```

## Quick Start

```csharp
// Register Jobs
builder.Services.AddJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    });
});

// Define cron job
[Jobs("*/5 * * * *")] // Every 5 minutes
public static class CleanupJob
{
    public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<CleanupJob>>();
        logger.LogInformation("Running cleanup job");

        await Task.CompletedTask;
    }
}

// Initialize Jobs
app.UseJobs();

// Schedule time-based job programmatically
public sealed class OrderService(ITimeJobManager<TimeJobEntity> job)
{
    public async Task SendReminderAsync(string orderId, CancellationToken ct)
    {
        await job.AddAsync(new TimeJobEntity
        {
            Function = "SendOrderReminder",
            Description = $"order-reminder-{orderId}",
            ExecutionTime = DateTime.UtcNow.AddHours(24),
            Request = JobsHelper.SerializeRequest(new { OrderId = orderId })
        }, ct);
    }
}
```

## Configuration

```csharp
builder.Services.AddJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.IdleWorkerTimeOut = TimeSpan.FromMinutes(5);
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });

    // Exception handling
    options.SetExceptionHandler<CustomJobExceptionHandler>();

    // Disable background services (for testing)
    options.DisableBackgroundServices();
});

// Start modes
app.UseJobs(JobsStartMode.Immediate); // Start immediately (default)
app.UseJobs(JobsStartMode.Manual);    // Wait for manual trigger
```

## Error Handling and Retries

Headless.Jobs supports the same error-handling model as TickerQ, using Jobs-native APIs.

### Retry Configuration

```csharp
await timeJobs.AddAsync(new TimeJobEntity
{
    Function = "ProcessPayment",
    Description = "payment-processing",
    ExecutionTime = DateTime.UtcNow,
    Request = JobsHelper.SerializeRequest(new { PaymentId = "pay_123" }),
    Retries = 3,
    RetryIntervals = [30, 60, 120],
}, cancellationToken);
```

- Retries are automatic when job execution throws.
- Status remains `InProgress` during retries.
- After retries are exhausted, final status becomes `Failed`.
- If `RetryIntervals` is null/empty, default interval is 30 seconds.
- If fewer intervals are provided than retries, the last interval is reused.

### Global Exception Handler

```csharp
public sealed class CustomJobExceptionHandler(ILogger<CustomJobExceptionHandler> logger)
    : IJobExceptionHandler
{
    public Task HandleExceptionAsync(Exception exception, Guid jobId, JobType jobType)
    {
        logger.LogError(exception, "Job {JobId} ({JobType}) failed", jobId, jobType);
        return Task.CompletedTask;
    }

    public Task HandleCanceledExceptionAsync(Exception exception, Guid jobId, JobType jobType)
    {
        logger.LogWarning("Job {JobId} ({JobType}) cancelled", jobId, jobType);
        return Task.CompletedTask;
    }
}
```

Register it with:

```csharp
builder.Services.AddJobs(options =>
{
    options.SetExceptionHandler<CustomJobExceptionHandler>();
});
```

### Job-Level Controls

- Throw to trigger retry.
- Catch and return to stop retry for permanent failures.
- Use `context.RetryCount` from `JobFunctionContext` for attempt-aware behavior.
- Call `context.RequestCancellation()` to mark as `Cancelled`.
- Call `context.CronOccurrenceOperations.SkipIfAlreadyRunning()` for overlap-safe cron jobs.

### TerminateExecutionException

Use `TerminateExecutionException` to stop execution without retries and optionally set final status:

```csharp
throw new TerminateExecutionException(JobStatus.Failed, "Configuration is invalid for this job");
```

- `TerminateExecutionException("message")` -> `Skipped`
- `TerminateExecutionException(JobStatus status, "message")` -> explicit status
- Overloads with `innerException` keep details for diagnostics

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Extensions`

## Side Effects

- Starts background hosted services for job scheduling and execution
- Creates in-memory job storage (or database tables with persistence providers)
- Runs custom thread pool for job execution
- Periodically scans for due jobs and executes them
