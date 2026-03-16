# Headless.Jobs.Abstractions

Simple utilities for queuing and executing cron/time-based jobs in the background.

---

## 📦 Installation
[Headless.Jobs.Abstractions.csproj](Headless.Jobs.Abstractions.csproj)
```bash
dotnet add package Headless.Jobs.Abstractions
```

## Error Handling Primitives

This package provides the core contracts used by Jobs error handling and retry flow:

- `IJobExceptionHandler` for global exception/cancellation hooks.
- `JobFunctionContext.RetryCount` for attempt-aware logic.
- `JobFunctionContext.RequestCancellation()` to mark jobs as cancelled.
- `CronOccurrenceOperations.SkipIfAlreadyRunning()` for overlap-safe cron execution.

Status values are represented by `JobStatus` (`Failed`, `Cancelled`, `Skipped`, etc.).

## Core Types

- `TimeJobEntity` for one-off scheduled jobs (`ExecutionTime`, `Retries`, `RetryIntervals`).
- `CronJobEntity` for recurring jobs (`Expression`, `Retries`, `RetryIntervals`).
- `JobFunctionContext` / `JobFunctionContext<TRequest>` for runtime execution context.
- `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` for enqueue/update/delete operations.