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
    options.MaxConcurrency(10);
    options.TimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
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
public sealed class OrderService(ITimeJobManager<TimeJobEntity> ticker)
{
    public async Task SendReminderAsync(string orderId, CancellationToken ct)
    {
        await ticker.ScheduleAsync(new TimeJobEntity
        {
            TickerKey = $"order-reminder-{orderId}",
            OccurrenceTime = DateTime.UtcNow.AddHours(24),
            Request = new { OrderId = orderId }
        }, ct);
    }
}
```

## Configuration

```csharp
builder.Services.AddJobs(options =>
{
    // Scheduler options
    options.MaxConcurrency(10);
    options.IdleWorkerTimeout(TimeSpan.FromMinutes(5));
    options.TimeZone(TimeZoneInfo.Utc);

    // Seeding options
    options.SeedDefinedCronTickers = true;
    options.CustomTimeSeeder = async sp =>
    {
        // Seed initial jobs
    };

    // Exception handling
    options.UseExceptionHandler<CustomTickerExceptionHandler>();

    // Disable background services (for testing)
    options.DisableBackgroundServices();
});

// Start modes
app.UseJobs(JobsStartMode.Immediate); // Start immediately (default)
app.UseJobs(JobsStartMode.Manual);    // Wait for manual trigger
```

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Extensions`

## Side Effects

- Starts background hosted services for job scheduling and execution
- Creates in-memory job storage (or database tables with persistence providers)
- Runs custom thread pool for job execution
- Periodically scans for due jobs and executes them
