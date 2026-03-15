using System.Diagnostics;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal class OpenTelemetryInstrumentation(
    ILogger<OpenTelemetryInstrumentation> logger,
    SchedulerOptionsBuilder optionsBuilder
) : JobsBaseLoggerInstrumentation(logger, optionsBuilder.NodeIdentifier), IJobsInstrumentation
{
    private static readonly ActivitySource _ActivitySource = new("Jobs", "1.0.0");

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context)
    {
        var activity = _ActivitySource.StartActivity(activityName);

        if (activity != null)
        {
            activity.SetTag("Headless.Jobs.job.id", context.TickerId.ToString());
            activity.SetTag("Headless.Jobs.job.type", context.Type.ToString());
            activity.SetTag("Headless.Jobs.job.function", context.FunctionName);
            activity.SetTag("Headless.Jobs.job.priority", context.CachedPriority.ToString());
            activity.SetTag("Headless.Jobs.job.machine", InstanceIdentifier);
            activity.SetTag("Headless.Jobs.job.retries", context.Retries);

            if (context.ParentId.HasValue)
            {
                activity.SetTag("Headless.Jobs.job.parent_id", context.ParentId.Value.ToString());
            }

            if (context.Type == TickerType.TimeTicker && context.ParentId.HasValue)
            {
                activity.SetTag("Headless.Jobs.job.run_condition", context.RunCondition.ToString());
            }
        }

        return activity;
    }

    public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.job.enqueued");
        activity?.SetTag("Headless.Jobs.job.id", jobId.ToString());
        activity?.SetTag("Headless.Jobs.job.type", jobType);
        activity?.SetTag("Headless.Jobs.job.function", functionName);

        // Get detailed caller information for OpenTelemetry
        var callerInfo = string.IsNullOrEmpty(enqueuedFrom) ? CallerInfoHelper.GetCallerInfo(6) : enqueuedFrom;
        activity?.SetTag("Headless.Jobs.job.enqueued_from", callerInfo);

        logger.LogInformation(
            "Jobs Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}",
            jobType,
            functionName,
            jobId,
            callerInfo
        );
    }

    public override void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.job.completed");
        activity?.SetTag("Headless.Jobs.job.id", jobId.ToString());
        activity?.SetTag("Headless.Jobs.job.function", functionName);
        activity?.SetTag("Headless.Jobs.job.execution_time_ms", executionTimeMs);
        activity?.SetTag("Headless.Jobs.job.success", success);

        // Set activity status based on success
        if (activity != null)
        {
            activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        base.LogJobCompleted(jobId, functionName, executionTimeMs, success);
    }

    public override void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.job.failed");
        activity?.SetTag("Headless.Jobs.job.id", jobId.ToString());
        activity?.SetTag("Headless.Jobs.job.function", functionName);
        activity?.SetTag("Headless.Jobs.job.retry_count", retryCount);
        activity?.SetTag("Headless.Jobs.job.error_type", exception.GetType().Name);
        activity?.SetTag("Headless.Jobs.job.error_message", exception.Message);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            // Record exception information in tags instead of RecordException (not available in all .NET versions)
            if (exception.StackTrace != null)
            {
                activity.SetTag("Headless.Jobs.job.error_stack_trace", exception.StackTrace);
            }
        }

        base.LogJobFailed(jobId, functionName, exception, retryCount);
    }

    public override void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.job.cancelled");
        activity?.SetTag("Headless.Jobs.job.id", jobId.ToString());
        activity?.SetTag("Headless.Jobs.job.function", functionName);
        activity?.SetTag("Headless.Jobs.job.cancellation_reason", reason);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, reason);
        }

        base.LogJobCancelled(jobId, functionName, reason);
    }

    public override void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.job.skipped");
        activity?.SetTag("Headless.Jobs.job.id", jobId.ToString());
        activity?.SetTag("Headless.Jobs.job.function", functionName);
        activity?.SetTag("Headless.Jobs.job.skip_reason", reason);

        base.LogJobSkipped(jobId, functionName, reason);
    }

    public override void LogSeedingDataStarted(string seedingDataType)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.seeding.started");
        activity?.SetTag("Headless.Jobs.seeding.type", seedingDataType);
        activity?.SetTag("Headless.Jobs.seeding.environment", InstanceIdentifier);

        base.LogSeedingDataStarted(seedingDataType);
    }

    public override void LogSeedingDataCompleted(string seedingDataType)
    {
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.seeding.completed");
        activity?.SetTag("Headless.Jobs.seeding.type", seedingDataType);
        activity?.SetTag("Headless.Jobs.seeding.environment", InstanceIdentifier);

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
        using var activity = _ActivitySource.StartActivity("Headless.Jobs.job_request_serialization.failed");
        activity?.SetTag("Headless.Jobs.job.id", tickerId.ToString());
        activity?.SetTag("Headless.Jobs.job.function", functionName);
        activity?.SetTag("Headless.Jobs.job.cancellation_reason", exception.Message);
        base.LogRequestDeserializationFailure(requestType, functionName, tickerId, type, exception);
    }
}
