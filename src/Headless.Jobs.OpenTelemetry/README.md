# Headless.Jobs.Instrumentation.OpenTelemetry

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
dotnet add package Headless.Jobs.Instrumentation.OpenTelemetry
```

## Usage

### Basic Setup

```csharp
using Headless.Jobs.Instrumentation.OpenTelemetry;
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
builder.Services.AddJobs<MyTimeTicker, MyCronTicker>(options => { })
    .AddOperationalStore(ef => { })
    .AddOpenTelemetryInstrumentation(); // 👈 Enable tracing

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
headless.ticker.job.execute.timeticker (main job execution span)
├── headless.ticker.job.enqueued (when job starts execution)
├── headless.ticker.job.completed (on successful completion)
├── headless.ticker.job.failed (on failure)
├── headless.ticker.job.cancelled (on cancellation)
├── headless.ticker.job.skipped (when skipped)
├── headless.ticker.seeding.started (for data seeding)
└── headless.ticker.seeding.completed (seeding completion)
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `headless.ticker.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `headless.ticker.job.type` | Type of ticker | `TimeTicker`, `CronTicker` |
| `headless.ticker.job.function` | Function name being executed | `ProcessEmails` |
| `headless.ticker.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `headless.ticker.job.machine` | Machine executing the job | `web-server-01` |
| `headless.ticker.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `headless.ticker.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `headless.ticker.job.is_due` | Whether the job was due | `true`, `false` |
| `headless.ticker.job.is_child` | Whether this is a child job | `true`, `false` |
| `headless.ticker.job.retries` | Maximum retry attempts | `3` |
| `headless.ticker.job.current_attempt` | Current retry attempt | `1`, `2`, `3` |
| `headless.ticker.job.final_status` | Final execution status | `Done`, `Failed`, `Cancelled`, `Skipped` |
| `headless.ticker.job.final_retry_count` | Final retry count reached | `2` |
| `headless.ticker.job.execution_time_ms` | Execution time in milliseconds | `1250` |
| `headless.ticker.job.success` | Whether execution was successful | `true`, `false` |
| `headless.ticker.job.error_type` | Exception type for failures | `SqlException`, `TimeoutException` |
| `headless.ticker.job.error_message` | Error message | `Connection timeout` |
| `headless.ticker.job.error_stack_trace` | Full stack trace | `at MyService.ProcessData()...` |
| `headless.ticker.job.cancellation_reason` | Reason for cancellation | `Task was cancelled` |
| `headless.ticker.job.skip_reason` | Reason for skipping | `Another instance is already running` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] Jobs Job enqueued: TimeTicker - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from ExecutionTaskHandler
[INF] Jobs Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] Jobs Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[INF] Jobs Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 2500ms - Success: False
[WRN] Jobs Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Task was cancelled
[INF] Jobs Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another CronOccurrence is already running!
[INF] Jobs start seeding data: TimeTicker (production-node-01)
[INF] Jobs completed seeding data: TimeTicker (production-node-01)
```

## Integration with Logging Frameworks

This package works seamlessly with any logging framework that integrates with `ILogger`:

### Serilog
```csharp
builder.Host.UseSerilog((context, config) =>
{
    config.WriteTo.Console()
          .WriteTo.File("logs/tickerq-.txt", rollingInterval: RollingInterval.Day)
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
