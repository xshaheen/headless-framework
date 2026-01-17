# Framework.Ticker.Instrumentation.OpenTelemetry

OpenTelemetry instrumentation package for TickerQ job scheduler with distributed tracing support.

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
dotnet add package Framework.Ticker.Instrumentation.OpenTelemetry
```

## Usage

### Basic Setup

```csharp
using Framework.Ticker.Instrumentation.OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry with TickerQ ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("TickerQ") // Add TickerQ ActivitySource
               .AddConsoleExporter()
               .AddJaegerExporter();
    });

// Add TickerQ with OpenTelemetry instrumentation
builder.Services.AddTickerQ<MyTimeTicker, MyCronTicker>(options => { })
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
        tracing.AddTickerQInstrumentation()
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
        tracing.AddTickerQInstrumentation()
               .AddAzureMonitorTraceExporter();
    });
```

## Trace Structure

### Job Execution Activities
```
Framework.Ticker.job.execute.timeticker (main job execution span)
├── Framework.Ticker.job.enqueued (when job starts execution)
├── Framework.Ticker.job.completed (on successful completion)
├── Framework.Ticker.job.failed (on failure)
├── Framework.Ticker.job.cancelled (on cancellation)
├── Framework.Ticker.job.skipped (when skipped)
├── Framework.Ticker.seeding.started (for data seeding)
└── Framework.Ticker.seeding.completed (seeding completion)
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `Framework.Ticker.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `Framework.Ticker.job.type` | Type of ticker | `TimeTicker`, `CronTicker` |
| `Framework.Ticker.job.function` | Function name being executed | `ProcessEmails` |
| `Framework.Ticker.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `Framework.Ticker.job.machine` | Machine executing the job | `web-server-01` |
| `Framework.Ticker.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `Framework.Ticker.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `Framework.Ticker.job.is_due` | Whether the job was due | `true`, `false` |
| `Framework.Ticker.job.is_child` | Whether this is a child job | `true`, `false` |
| `Framework.Ticker.job.retries` | Maximum retry attempts | `3` |
| `Framework.Ticker.job.current_attempt` | Current retry attempt | `1`, `2`, `3` |
| `Framework.Ticker.job.final_status` | Final execution status | `Done`, `Failed`, `Cancelled`, `Skipped` |
| `Framework.Ticker.job.final_retry_count` | Final retry count reached | `2` |
| `Framework.Ticker.job.execution_time_ms` | Execution time in milliseconds | `1250` |
| `Framework.Ticker.job.success` | Whether execution was successful | `true`, `false` |
| `Framework.Ticker.job.error_type` | Exception type for failures | `SqlException`, `TimeoutException` |
| `Framework.Ticker.job.error_message` | Error message | `Connection timeout` |
| `Framework.Ticker.job.error_stack_trace` | Full stack trace | `at MyService.ProcessData()...` |
| `Framework.Ticker.job.cancellation_reason` | Reason for cancellation | `Task was cancelled` |
| `Framework.Ticker.job.skip_reason` | Reason for skipping | `Another instance is already running` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] TickerQ Job enqueued: TimeTicker - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from ExecutionTaskHandler
[INF] TickerQ Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] TickerQ Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[INF] TickerQ Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 2500ms - Success: False
[WRN] TickerQ Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Task was cancelled
[INF] TickerQ Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another CronOccurrence is already running!
[INF] TickerQ start seeding data: TimeTicker (production-node-01)
[INF] TickerQ completed seeding data: TimeTicker (production-node-01)
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
- Framework.Ticker.Utilities (automatically included)
