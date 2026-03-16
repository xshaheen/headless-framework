# Headless.Jobs.EntityFramework

Entity Framework Core integration for Jobs, a high-performance background job scheduler for .NET.

This package enables persistence of time-based and cron-based jobs using EF Core, allowing for robust tracking, retry logic, and job state management.

---

## 📦 Installation

```bash
dotnet add package Headless.Jobs.EntityFramework
```

## Quick Start

```csharp
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;

builder.Services
    .AddJobs(options =>
    {
        options.ConfigureScheduler(scheduler => scheduler.SchedulerTimeZone = TimeZoneInfo.Utc);
    })
    .AddOperationalStore(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db =>
        {
            db.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
        });
    });

app.UseJobs();
```

## Error Handling and Retry Persistence

With EF enabled, Jobs persists retry and failure state across restarts:

- `Retries`, `RetryIntervals`, `RetryCount`
- Final status (`Failed`, `Cancelled`, `Skipped`, `Done`)
- Exception details (`ExceptionMessage`) and skip reason (`SkippedReason`)
- Execution timing (`ExecutionTime`, `ExecutedAt`, `ElapsedTime`)

This allows reliable post-mortem analysis and dashboard visibility for failed or unstable jobs.