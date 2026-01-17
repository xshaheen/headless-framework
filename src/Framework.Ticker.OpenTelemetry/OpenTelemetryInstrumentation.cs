using System.Diagnostics;
using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Enums;
using Framework.Ticker.Utilities.Instrumentation;
using Framework.Ticker.Utilities.Models;
using Microsoft.Extensions.Logging;

namespace Framework.Ticker.Instrumentation.OpenTelemetry;

internal class OpenTelemetryInstrumentation(
    ILogger<OpenTelemetryInstrumentation> logger,
    SchedulerOptionsBuilder optionsBuilder
) : TickerQBaseLoggerInstrumentation(logger, optionsBuilder.NodeIdentifier), ITickerQInstrumentation
{
    private static readonly ActivitySource _ActivitySource = new("TickerQ", "1.0.0");

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context)
    {
        var activity = _ActivitySource.StartActivity(activityName);

        if (activity != null)
        {
            activity.SetTag("Framework.Ticker.job.id", context.TickerId.ToString());
            activity.SetTag("Framework.Ticker.job.type", context.Type.ToString());
            activity.SetTag("Framework.Ticker.job.function", context.FunctionName);
            activity.SetTag("Framework.Ticker.job.priority", context.CachedPriority.ToString());
            activity.SetTag("Framework.Ticker.job.machine", InstanceIdentifier);
            activity.SetTag("Framework.Ticker.job.retries", context.Retries);

            if (context.ParentId.HasValue)
            {
                activity.SetTag("Framework.Ticker.job.parent_id", context.ParentId.Value.ToString());
            }

            if (context.Type == TickerType.TimeTicker && context.ParentId.HasValue)
            {
                activity.SetTag("Framework.Ticker.job.run_condition", context.RunCondition.ToString());
            }
        }

        return activity;
    }

    public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.job.enqueued");
        activity?.SetTag("Framework.Ticker.job.id", jobId.ToString());
        activity?.SetTag("Framework.Ticker.job.type", jobType);
        activity?.SetTag("Framework.Ticker.job.function", functionName);

        // Get detailed caller information for OpenTelemetry
        var callerInfo = string.IsNullOrEmpty(enqueuedFrom) ? CallerInfoHelper.GetCallerInfo(6) : enqueuedFrom;
        activity?.SetTag("Framework.Ticker.job.enqueued_from", callerInfo);

        logger.LogInformation(
            "TickerQ Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}",
            jobType,
            functionName,
            jobId,
            callerInfo
        );
    }

    public override void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.job.completed");
        activity?.SetTag("Framework.Ticker.job.id", jobId.ToString());
        activity?.SetTag("Framework.Ticker.job.function", functionName);
        activity?.SetTag("Framework.Ticker.job.execution_time_ms", executionTimeMs);
        activity?.SetTag("Framework.Ticker.job.success", success);

        // Set activity status based on success
        if (activity != null)
        {
            activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        base.LogJobCompleted(jobId, functionName, executionTimeMs, success);
    }

    public override void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.job.failed");
        activity?.SetTag("Framework.Ticker.job.id", jobId.ToString());
        activity?.SetTag("Framework.Ticker.job.function", functionName);
        activity?.SetTag("Framework.Ticker.job.retry_count", retryCount);
        activity?.SetTag("Framework.Ticker.job.error_type", exception.GetType().Name);
        activity?.SetTag("Framework.Ticker.job.error_message", exception.Message);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            // Record exception information in tags instead of RecordException (not available in all .NET versions)
            if (exception.StackTrace != null)
            {
                activity.SetTag("Framework.Ticker.job.error_stack_trace", exception.StackTrace);
            }
        }

        base.LogJobFailed(jobId, functionName, exception, retryCount);
    }

    public override void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.job.cancelled");
        activity?.SetTag("Framework.Ticker.job.id", jobId.ToString());
        activity?.SetTag("Framework.Ticker.job.function", functionName);
        activity?.SetTag("Framework.Ticker.job.cancellation_reason", reason);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, reason);
        }

        base.LogJobCancelled(jobId, functionName, reason);
    }

    public override void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.job.skipped");
        activity?.SetTag("Framework.Ticker.job.id", jobId.ToString());
        activity?.SetTag("Framework.Ticker.job.function", functionName);
        activity?.SetTag("Framework.Ticker.job.skip_reason", reason);

        base.LogJobSkipped(jobId, functionName, reason);
    }

    public override void LogSeedingDataStarted(string seedingDataType)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.seeding.started");
        activity?.SetTag("Framework.Ticker.seeding.type", seedingDataType);
        activity?.SetTag("Framework.Ticker.seeding.environment", InstanceIdentifier);

        base.LogSeedingDataStarted(seedingDataType);
    }

    public override void LogSeedingDataCompleted(string seedingDataType)
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.seeding.completed");
        activity?.SetTag("Framework.Ticker.seeding.type", seedingDataType);
        activity?.SetTag("Framework.Ticker.seeding.environment", InstanceIdentifier);

        base.LogSeedingDataCompleted(seedingDataType);
    }

    public override void LogRequestDeserializationFailure(
        string requestType,
        string functionName,
        Guid tickerId,
        TickerType type,
        Exception exception
    )
    {
        using var activity = _ActivitySource.StartActivity("Framework.Ticker.job_request_serialization.failed");
        activity?.SetTag("Framework.Ticker.job.id", tickerId.ToString());
        activity?.SetTag("Framework.Ticker.job.function", functionName);
        activity?.SetTag("Framework.Ticker.job.cancellation_reason", exception.Message);
        base.LogRequestDeserializationFailure(requestType, functionName, tickerId, type, exception);
    }
}
