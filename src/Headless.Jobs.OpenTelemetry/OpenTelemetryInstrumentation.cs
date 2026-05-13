using System.Diagnostics;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal sealed class OpenTelemetryInstrumentation(
    ILogger<OpenTelemetryInstrumentation> logger,
    SchedulerOptionsBuilder optionsBuilder
) : JobsBaseLoggerInstrumentation(logger, optionsBuilder.NodeIdentifier), IJobsInstrumentation
{
    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context)
    {
        var activity = JobsDiagnostics.Start(activityName);

        if (activity != null)
        {
            activity.SetTag("headless.job.id", context.JobId.ToString());
            activity.SetTag("headless.job.type", context.Type.ToString());
            activity.SetTag("headless.job.function", context.FunctionName);
            activity.SetTag("headless.job.priority", context.CachedPriority.ToString());
            activity.SetTag("headless.job.machine", InstanceIdentifier);
            activity.SetTag("headless.job.retry.count", context.Retries);

            if (context.ParentId.HasValue)
            {
                activity.SetTag("headless.job.parent.id", context.ParentId.Value.ToString());
            }

            if (context is { Type: JobType.TimeJob, ParentId: not null })
            {
                activity.SetTag("headless.job.run_condition", context.RunCondition.ToString());
            }
        }

        return activity;
    }

    public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
    {
        using var activity = JobsDiagnostics.Start("job.enqueue");
        activity?.SetTag("headless.job.id", jobId.ToString());
        activity?.SetTag("headless.job.type", jobType);
        activity?.SetTag("headless.job.function", functionName);

        // Get detailed caller information for OpenTelemetry
        var callerInfo = enqueuedFrom;
        if (string.IsNullOrEmpty(callerInfo))
        {
            callerInfo = logger.IsEnabled(LogLevel.Information) ? CallerInfoHelper.GetCallerInfo(6) : null;
        }

        activity?.SetTag("headless.job.enqueued_from", callerInfo);

        base.LogJobEnqueued(jobType, functionName, jobId, callerInfo);
    }

    public override void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        using var activity = JobsDiagnostics.Start("job.complete");
        activity?.SetTag("headless.job.id", jobId.ToString());
        activity?.SetTag("headless.job.function", functionName);
        activity?.SetTag("headless.job.duration_ms", executionTimeMs);
        activity?.SetTag("headless.job.success", success);

        // Set activity status based on success
        if (activity != null)
        {
            activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        base.LogJobCompleted(jobId, functionName, executionTimeMs, success);
    }

    public override void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        using var activity = JobsDiagnostics.Start("job.fail");
        activity?.SetTag("headless.job.id", jobId.ToString());
        activity?.SetTag("headless.job.function", functionName);
        activity?.SetTag("headless.job.retry.count", retryCount);
        activity?.SetTag("exception.type", exception.GetType().Name);
        activity?.SetTag("exception.message", exception.Message);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            // Record exception information in tags instead of RecordException (not available in all .NET versions)
            if (exception.StackTrace != null)
            {
                activity.SetTag("exception.stacktrace", exception.StackTrace);
            }
        }

        base.LogJobFailed(jobId, functionName, exception, retryCount);
    }

    public override void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        using var activity = JobsDiagnostics.Start("job.cancel");
        activity?.SetTag("headless.job.id", jobId.ToString());
        activity?.SetTag("headless.job.function", functionName);
        activity?.SetTag("headless.job.cancellation.reason", reason);

        activity?.SetStatus(ActivityStatusCode.Error, reason);

        base.LogJobCancelled(jobId, functionName, reason);
    }

    public override void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        using var activity = JobsDiagnostics.Start("job.skip");
        activity?.SetTag("headless.job.id", jobId.ToString());
        activity?.SetTag("headless.job.function", functionName);
        activity?.SetTag("headless.job.skip.reason", reason);

        base.LogJobSkipped(jobId, functionName, reason);
    }

    public override void LogSeedingDataStarted(string seedingDataType)
    {
        using var activity = JobsDiagnostics.Start("seeding.start");
        activity?.SetTag("headless.seeding.type", seedingDataType);
        activity?.SetTag("headless.seeding.environment", InstanceIdentifier);

        base.LogSeedingDataStarted(seedingDataType);
    }

    public override void LogSeedingDataCompleted(string seedingDataType)
    {
        using var activity = JobsDiagnostics.Start("seeding.complete");
        activity?.SetTag("headless.seeding.type", seedingDataType);
        activity?.SetTag("headless.seeding.environment", InstanceIdentifier);

        base.LogSeedingDataCompleted(seedingDataType);
    }

    public override void LogRequestDeserializationFailure(
        string requestType,
        string functionName,
        Guid jobId,
        JobType type,
        Exception exception
    )
    {
        using var activity = JobsDiagnostics.Start("job.deserialize.fail");
        activity?.SetTag("headless.job.id", jobId.ToString());
        activity?.SetTag("headless.job.function", functionName);
        activity?.SetTag("exception.type", exception.GetType().Name);
        activity?.SetTag("exception.message", exception.Message);
        base.LogRequestDeserializationFailure(requestType, functionName, jobId, type, exception);
    }
}
