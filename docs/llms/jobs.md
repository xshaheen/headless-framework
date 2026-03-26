---
domain: Jobs (Background Jobs)
packages: Jobs.Abstractions, Jobs.Core, Jobs.Dashboard, Jobs.SourceGenerator, Jobs.OpenTelemetry, Jobs.Caching.Redis, Jobs.EntityFramework
---

# Jobs (Background Jobs)

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Error Handling and Retries](#error-handling-and-retries)
  - [Retry Configuration](#retry-configuration)
  - [Global Exception Handler](#global-exception-handler)
  - [Job-Level Error Handling](#job-level-error-handling)
  - [TerminateExecutionException and Status Control](#terminateexecutionexception-and-status-control)
  - [Cron Occurrence Skipping](#cron-occurrence-skipping)
  - [Job Status After Errors](#job-status-after-errors)
  - [Best Practices](#best-practices)
- [Headless.Jobs.Abstractions](#headlessjobsabstractions)
  - [Installation](#installation)
- [Headless.Jobs.Core](#headlessjobscore)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Jobs.Dashboard](#headlessjobsdashboard)
  - [Installation](#installation-2)
  - [Minimal Setup](#minimal-setup)
  - [🚀 Quick Examples](#-quick-examples)
    - [No Authentication (Public Dashboard)](#no-authentication-public-dashboard)
    - [Basic Authentication](#basic-authentication)
    - [API Key Authentication](#api-key-authentication)
    - [Use Host Application's Authentication](#use-host-applications-authentication)
    - [Use Host Authentication with Custom Policy](#use-host-authentication-with-custom-policy)
  - [🔧 Fluent API Methods](#-fluent-api-methods)
  - [🔒 How It Works](#-how-it-works)
  - [🌐 Frontend Integration](#-frontend-integration)
- [Headless.Jobs.SourceGenerator](#headlessjobssourcegenerator)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Jobs.OpenTelemetry](#headlessjobsopentelemetry)
  - [Features](#features)
  - [Installation](#installation-4)
  - [Usage](#usage)
    - [Basic Setup](#basic-setup)
    - [With Jaeger](#with-jaeger)
    - [With Application Insights](#with-application-insights)
  - [Trace Structure](#trace-structure)
    - [Job Execution Activities](#job-execution-activities)
    - [Tags Added to Activities](#tags-added-to-activities)
  - [Logging Output](#logging-output)
  - [Integration with Logging Frameworks](#integration-with-logging-frameworks)
    - [Serilog](#serilog)
    - [NLog](#nlog)
  - [Performance Impact](#performance-impact)
  - [Requirements](#requirements)
- [Headless.Jobs.Caching.Redis](#headlessjobscachingredis)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-5)
  - [Quick Start](#quick-start-2)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Jobs.EntityFramework](#headlessjobsentityframework)
  - [📦 Installation](#-installation)

> High-performance background job scheduler for .NET with cron expressions, time-based scheduling, source-generated registration, and distributed coordination.

## Quick Orientation

Minimum setup: `Jobs.Core` + `Jobs.EntityFramework` (for persistence) + `Jobs.SourceGenerator` (for compile-time job registration).

Additional packages:
- `Jobs.Dashboard` -- monitoring UI with authentication (basic, API key, host auth)
- `Jobs.Caching.Redis` -- multi-instance coordination via Redis (node registry, heartbeat, dead node detection)
- `Jobs.OpenTelemetry` -- distributed tracing and structured logging
- `Jobs.Abstractions` -- interfaces only (pulled in transitively by Core)

Wiring:
```csharp
builder.Services.AddJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });
});
app.UseJobs();
```

Mark job classes with `[Jobs("cron-expression")]` and add the SourceGenerator package for zero-reflection auto-discovery.

## Agent Instructions

- Do NOT use Hangfire or Quartz -- use Headless.Jobs (Jobs) for all background jobs in this framework.
- Mark job methods with `[Jobs("cron-expression")]` attribute. Add `Headless.Jobs.SourceGenerator` to the project for compile-time job registration (eliminates reflection).
- Use `Jobs.EntityFramework` for job persistence. Without it, jobs are in-memory only and lost on restart.
- Use `Jobs.Caching.Redis` for multi-instance deployments -- it provides node heartbeat, dead node detection, and distributed coordination.
- Call `app.UseJobs()` after `builder.Build()` to start the scheduler. Use `JobsStartMode.Manual` if you need delayed startup.
- For time-based (one-off) jobs, inject `ITimeJobManager<TimeJobEntity>` and call `AddAsync()`.
- For cron jobs, the `[Jobs]` attribute takes a cron expression and optional `Priority` parameter (`JobPriority.High`, `Normal`, `LongRunning`).
- Use `[JobsConstructor]` attribute on constructors when you need custom DI injection in job classes.
- Dashboard authentication: call `dashboard.WithBasicAuth()`, `WithApiKey()`, or `WithHostAuthentication()` inside `config.AddDashboard()`.
- OpenTelemetry: call `.AddOpenTelemetryInstrumentation()` on the Jobs builder and add `"Jobs"` as a source to your tracing config.
- Exception handling: register a custom handler via `options.SetExceptionHandler<CustomJobExceptionHandler>()`.
- For testing, call `options.DisableBackgroundServices()` to prevent the scheduler from running.

## Error Handling and Retries

Headless.Jobs ports the same error-handling concepts from TickerQ into Jobs-native APIs.

### Retry Configuration

Configure retries when creating jobs:

```csharp
await timeJobs.AddAsync(new TimeJobEntity
{
    Function = "ProcessPayment",
    Description = "Process payment for order #A-1024",
    ExecutionTime = DateTime.UtcNow,
    Request = JobsHelper.SerializeRequest(new { PaymentId = "pay_123" }),
    Retries = 3,                           // Total retry attempts after first failure
    RetryIntervals = [30, 60, 120],        // Seconds between retries
}, cancellationToken);
```

Retry behavior:
- Retries run automatically when a job throws an exception.
- Job status stays `InProgress` while retrying.
- After retries are exhausted, status becomes `Failed`.
- `JobFunctionContext.RetryCount` tracks the current attempt.

Retry interval strategies:
- Fixed delay: `RetryIntervals = [60, 60, 60]`
- Exponential backoff: `RetryIntervals = [1, 2, 4, 8, 16, 32]`
- Progressive backoff: `RetryIntervals = [30, 60, 300, 900, 3600]`
- Immediate retry: `RetryIntervals = [0, 0, 0]`

Default behavior:
- If `RetryIntervals` is null/empty, Jobs defaults to `30` seconds.
- If `RetryIntervals` is shorter than `Retries`, the last interval is reused.

### Global Exception Handler

Implement `IJobExceptionHandler`:

```csharp
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;

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

Register it:

```csharp
builder.Services.AddJobs(options =>
{
    options.SetExceptionHandler<CustomJobExceptionHandler>();
});
```

### Job-Level Error Handling

Use `try/catch` in job methods and choose whether to retry:

```csharp
[Jobs("ProcessOrder")]
public sealed class ProcessOrderJob(ILogger<ProcessOrderJob> logger)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
    {
        try
        {
            await ProcessOrderAsync(context.Request, ct);
        }
        catch (HttpRequestException ex) when (context.RetryCount < 3)
        {
            logger.LogWarning(ex, "Transient failure. Attempt {Attempt}", context.RetryCount + 1);
            throw; // Retry
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Permanent validation failure. No retry.");
            return; // Complete without retry
        }
    }

    private static Task ProcessOrderAsync(OrderRequest request, CancellationToken ct) => Task.CompletedTask;
}
```

### TerminateExecutionException and Status Control

Use `TerminateExecutionException` to stop execution without retries and optionally set final status:

```csharp
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;

if (!IsConfigurationValid())
{
    throw new TerminateExecutionException(
        JobStatus.Failed,
        "Configuration is invalid for this job"
    );
}
```

Available patterns:
- `new TerminateExecutionException("message")` -> `Skipped`
- `new TerminateExecutionException(JobStatus status, "message")` -> explicit status
- `new TerminateExecutionException("message", innerException)` -> `Skipped` + inner exception details
- `new TerminateExecutionException(JobStatus status, "message", innerException)` -> explicit status + inner details

### Cron Occurrence Skipping

Prevent overlapping cron runs:

```csharp
[Jobs("0 * * * *")]
public sealed class LongRunningCronJob
{
    public async Task ExecuteAsync(JobFunctionContext context, CancellationToken ct)
    {
        context.CronOccurrenceOperations.SkipIfAlreadyRunning();
        await RunLongTaskAsync(ct);
    }

    private static Task RunLongTaskAsync(CancellationToken ct) => Task.CompletedTask;
}
```

### Job Status After Errors

- `Failed`: retries exhausted or unhandled exception.
- `Cancelled`: cancellation requested via token or `context.RequestCancellation()`.
- `Skipped`: terminated explicitly (`TerminateExecutionException`) or overlapping cron occurrence skipped.

### Best Practices

- Treat transient and permanent failures differently.
- Use retry intervals that match dependency behavior.
- Log `FunctionName`, `Id`, and `RetryCount` for every failure.
- Monitor failed/cancelled/skipped jobs through Dashboard and OpenTelemetry.

---
# Headless.Jobs.Abstractions

Simple utilities for queuing and executing cron/time-based jobs in the background.

---

## Installation
[Headless.Jobs.Abstractions.csproj](Headless.Jobs.Abstractions.csproj)
```bash
dotnet add package Headless.Jobs.Abstractions
```
---
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

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Extensions`

## Side Effects

- Starts background hosted services for job scheduling and execution
- Creates in-memory job storage (or database tables with persistence providers)
- Runs custom thread pool for job execution
- Periodically scans for due jobs and executes them
---
# Headless.Jobs.Dashboard

Monitoring dashboard for Headless.Jobs with built-in authentication options and real-time updates.

## Installation

```bash
dotnet add package Headless.Jobs.Dashboard
```

## Minimal Setup

```csharp
builder.Services
    .AddJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs-dashboard");
        dashboard.WithHostAuthentication();
    });

app.UseJobs();
```

## 🚀 Quick Examples

### No Authentication (Public Dashboard)
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        // No authentication setup = public dashboard
    });
});
```

### Basic Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});
```

### API Key Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication();
    });
});
```

### Use Host Authentication with Custom Policy
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

## 🔧 Fluent API Methods

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy)` - Use your app's existing auth with optional policy (e.g., "AdminPolicy")
- `SetBasePath(path)` - Set dashboard URL path
- `SetBackendDomain(domain)` - Set backend API domain
- `SetCorsPolicy(policy)` - Configure CORS

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** -> Public dashboard
2. **Basic auth configured** -> Username/password login
3. **Bearer token configured** -> API key authentication
4. **Host auth configured** -> Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication
- Supports both header and query parameter auth (for WebSockets)

That's it! Simple and clean. 🎉
---
# Headless.Jobs.SourceGenerator

C# source generator for Jobs that generates boilerplate code for background job registration and execution.

## Problem Solved

Eliminates reflection overhead and manual job registration by generating compile-time code for Jobs job functions marked with `[Jobs]` attribute.

## Key Features

- **Zero Reflection**: Compile-time code generation
- **Auto-Registration**: Automatic job discovery and registration
- **Type Safety**: Compile-time validation of job signatures
- **DI Integration**: Generates constructor injection code
- **Incremental**: Fast rebuild with incremental generation
- **Diagnostics**: Rich compile-time error messages

## Installation

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

## Quick Start

```csharp
// Define job with attribute
[Jobs("*/5 * * * *")] // Every 5 minutes
public static class CleanupJob
{
    public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<CleanupJob>>();
        logger.LogInformation("Running cleanup");
    }
}

// Source generator creates:
// - Job registration code
// - Execution delegates
// - Constructor injection
// - Request type mapping

// No manual registration needed - jobs auto-discovered at compile time
```

## Configuration

No runtime configuration. Uses attributes:

```csharp
// Cron job
[Jobs("0 0 * * *", Priority = JobPriority.High)]
public static class DailyReport { /* ... */ }

// Job with request payload
[Jobs("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(
        JobFunctionContext<OrderRequest> context,
        CancellationToken ct)
    {
        await orders.ProcessAsync(context.Request, ct);
    }
}

// Custom constructor
public sealed class ComplexJob
{
    [JobsConstructor]
    public ComplexJob(ILogger<ComplexJob> logger, IConfiguration config)
    {
        // Custom initialization
    }

    [Jobs("ComplexTask")]
    public async Task ExecuteAsync(/* ... */) { }
}
```

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (analyzer/generator)

## Side Effects

Generates `JobsInstanceFactoryExtensions.g.cs` at compile time with:
- Module initializer for auto-registration
- Job execution delegates
- Constructor factory methods
- Request type registrations
---
# Headless.Jobs.OpenTelemetry

OpenTelemetry instrumentation package for Jobs job scheduler with distributed tracing support.

## Features

- **Distributed Tracing**: Full OpenTelemetry activity/span creation for job execution lifecycle
- **Structured Logging**: Rich logging with job context through ILogger integration
- **Parent-Child Relationships**: Maintains trace relationships between parent and child jobs
- **Retry Tracking**: Tracks retry attempts with detailed context
- **Performance Metrics**: Comprehensive execution time and outcome tracking
- **Error Tracking**: Detailed exception and cancellation tracking
- **Caller Information**: Automatic detection of where jobs are enqueued from

## Installation

```bash
dotnet add package Headless.Jobs.OpenTelemetry
```

## Usage

### Basic Setup

```csharp
using Headless.Jobs;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry with Jobs ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Jobs") // Add Jobs ActivitySource
               .AddConsoleExporter()
               .AddJaegerExporter();
    });

// Add Jobs with OpenTelemetry instrumentation
builder.Services.AddJobs<MyTimeJob, MyCronJob>(options => { })
    .AddOperationalStore(ef => { })
    .AddOpenTelemetryInstrumentation(); // Enable tracing

var app = builder.Build();
app.Run();
```

### With Jaeger

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddJobsInstrumentation()
               .AddJaegerExporter(options =>
               {
                   options.Endpoint = new Uri("http://localhost:14268/api/traces");
               });
    });
```

### With Application Insights

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddJobsInstrumentation()
               .AddAzureMonitorTraceExporter();
    });
```

## Trace Structure

### Job Execution Activities
```
headless.jobs.job.execute.timeticker (main job execution span)
├── headless.jobs.job.enqueued (when job starts execution)
├── headless.jobs.job.completed (on successful completion)
├── headless.jobs.job.failed (on failure)
├── headless.jobs.job.cancelled (on cancellation)
├── headless.jobs.job.skipped (when skipped)
├── headless.jobs.seeding.started (for data seeding)
└── headless.jobs.seeding.completed (seeding completion)
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `headless.jobs.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `headless.jobs.job.type` | Type of job | `TimeJob`, `CronJob` |
| `headless.jobs.job.function` | Function name being executed | `ProcessEmails` |
| `headless.jobs.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `headless.jobs.job.machine` | Machine executing the job | `web-server-01` |
| `headless.jobs.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `headless.jobs.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `headless.jobs.job.is_due` | Whether the job was due | `true`, `false` |
| `headless.jobs.job.is_child` | Whether this is a child job | `true`, `false` |
| `headless.jobs.job.retries` | Maximum retry attempts | `3` |
| `headless.jobs.job.current_attempt` | Current retry attempt | `1`, `2`, `3` |
| `headless.jobs.job.final_status` | Final execution status | `Done`, `Failed`, `Cancelled`, `Skipped` |
| `headless.jobs.job.final_retry_count` | Final retry count reached | `2` |
| `headless.jobs.job.execution_time_ms` | Execution time in milliseconds | `1250` |
| `headless.jobs.job.success` | Whether execution was successful | `true`, `false` |
| `headless.jobs.job.error_type` | Exception type for failures | `SqlException`, `TimeoutException` |
| `headless.jobs.job.error_message` | Error message | `Connection timeout` |
| `headless.jobs.job.error_stack_trace` | Full stack trace | `at MyService.ProcessData()...` |
| `headless.jobs.job.cancellation_reason` | Reason for cancellation | `Task was cancelled` |
| `headless.jobs.job.skip_reason` | Reason for skipping | `Another instance is already running` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] Jobs Job enqueued: TimeJob - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from ExecutionTaskHandler
[INF] Jobs Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] Jobs Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[INF] Jobs Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 2500ms - Success: False
[WRN] Jobs Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Task was cancelled
[INF] Jobs Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another CronOccurrence is already running!
[INF] Jobs start seeding data: TimeJob (production-node-01)
[INF] Jobs completed seeding data: TimeJob (production-node-01)
```

## Integration with Logging Frameworks

This package works seamlessly with any logging framework that integrates with `ILogger`:

### Serilog
```csharp
builder.Host.UseSerilog((context, config) =>
{
    config.WriteTo.Console()
          .WriteTo.File("logs/jobs-.txt", rollingInterval: RollingInterval.Day)
          .Enrich.FromLogContext();
});
```

### NLog
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddNLog();
```

## Performance Impact

- **Minimal Overhead**: Activities are only created when OpenTelemetry listeners are active
- **Efficient Logging**: Uses structured logging with minimal string allocations
- **Conditional Tracing**: No performance impact when tracing is disabled

## Requirements

- .NET 8.0 or later
- OpenTelemetry 1.7.0 or later
- Headless.Jobs.Abstractions (automatically included)
---
# Headless.Jobs.Caching.Redis

Redis-backed distributed coordination for Jobs with node heartbeat monitoring and dead node detection.

## Problem Solved

Enables multi-instance Jobs deployments with Redis-based node registry, heartbeat monitoring, and automatic dead node cleanup for high availability job scheduling.

## Key Features

- **Node Registry**: Track all Jobs nodes in Redis
- **Heartbeat Monitoring**: Periodic node liveness checks
- **Dead Node Detection**: Automatic cleanup of failed nodes
- **Distributed Coordination**: Shared state across Jobs instances
- **Dashboard Integration**: Real-time cluster visibility

## Installation

```bash
dotnet add package Headless.Jobs.Caching.Redis
```

## Quick Start

```csharp
builder.Services
    .AddJobs(options =>
    {
        options.ConfigureScheduler(scheduler => scheduler.MaxConcurrency = 10);
    })
    .AddStackExchangeRedis(redis =>
    {
        redis.Configuration = "localhost:6379";
        redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(30);
    });

app.UseJobs();
```

## Configuration

```csharp
builder.Services
    .AddJobs()
    .AddStackExchangeRedis(redis =>
{
    redis.Configuration = "localhost:6379,ssl=true,password=secret";
    redis.InstanceName = "jobs:";
    redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeIdentifier = "instance-1";
    });
});
```

## Dependencies

- `Headless.Jobs.Abstractions`
- `Microsoft.Extensions.Caching.StackExchangeRedis`

## Side Effects

- Stores node registry and heartbeats in Redis
- Background service sends periodic heartbeats
- Periodically scans for and removes dead nodes
- Creates Redis keys: `nodes:registry`, `hb:{nodeId}`
---
# Headless.Jobs.EntityFramework

Entity Framework Core integration for Jobs, a high-performance background job scheduler for .NET.

This package enables persistence of time-based and cron-based jobs using EF Core, allowing for robust tracking, retry logic, and job state management.

---

## 📦 Installation

```bash
dotnet add package Headless.Jobs.EntityFramework
```
