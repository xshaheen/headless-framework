# Headless.Jobs.OpenTelemetry

OpenTelemetry instrumentation for `Headless.Jobs` — activity tracing for the full job execution lifecycle plus structured logging.

## Problem Solved

Provides distributed tracing (OpenTelemetry activities/spans), structured log events, and execution metrics for every Jobs job execution without modifying job code. Replaces the default `LoggerInstrumentation` with a full `ActivitySource`-based implementation.

## Key Features

- **Activity tracing**: spans for job execution, enqueue, completion, failure, cancellation, skip, and data seeding.
- **Retry tracking**: activity tags for `current_attempt` and `final_retry_count`.
- **Error telemetry**: `error_type`, `error_message`, `error_stack_trace` tags on failed spans.
- **Caller information**: `enqueued_from` tag captures the call site where the job was enqueued.
- **Parent–child trace linking**: `parent_id` tag links child job spans to their parent.
- **Structured log events**: correlated with trace context for Serilog, NLog, and any `ILogger`-backed sink.

## Installation

```bash
dotnet add package Headless.Jobs.OpenTelemetry
```

## Quick Start

```csharp
using OpenTelemetry.Trace;

// 1. Add Jobs with OpenTelemetry instrumentation
builder.Services
    .AddHeadlessJobs()
    .AddOpenTelemetryInstrumentation(); // replaces LoggerInstrumentation with OTel

// 2. Configure the OpenTelemetry pipeline to include the Jobs ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Headless.Jobs") // the Jobs ActivitySource name
            .AddConsoleExporter();      // or Jaeger, OTLP, Azure Monitor, etc.
    });
```

## Configuration

`AddOpenTelemetryInstrumentation()` takes no options. The `ActivitySource` name is `"Headless.Jobs"`. Add it to your tracing pipeline's `AddSource(...)` call to activate spans.

Activity tag reference:

| Tag | Example |
|-----|---------|
| `headless.jobs.job.id` | `123e4567-…` |
| `headless.jobs.job.type` | `TimeJob`, `CronJob` |
| `headless.jobs.job.function` | `ProcessOrder` |
| `headless.jobs.job.priority` | `Normal`, `High`, `LongRunning` |
| `headless.jobs.job.machine` | `web-01` |
| `headless.jobs.job.parent_id` | parent job GUID |
| `headless.jobs.job.enqueued_from` | `OrderController.Create (Program.cs:42)` |
| `headless.jobs.job.retries` | `3` |
| `headless.jobs.job.current_attempt` | `2` |
| `headless.jobs.job.final_status` | `Succeeded`, `Failed`, `Cancelled`, `Skipped` |
| `headless.jobs.job.execution_time_ms` | `1250` |
| `headless.jobs.job.error_type` | `SqlException` |
| `headless.jobs.job.error_message` | `Connection timeout` |

Note: `headless.jobs.job.final_status` emits `Succeeded` (not the former `Done` value). Update dashboards or alerts that matched the literal `Done`.

## Dependencies

- `Headless.Jobs.Abstractions`
- `OpenTelemetry` (≥ 1.7.0 recommended)

## Side Effects

Registers `OpenTelemetryInstrumentation` as the singleton `IJobsInstrumentation`, replacing the default `LoggerInstrumentation`. No other registrations.
