using System.Diagnostics;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Instrumentation;

public abstract partial class JobsBaseLoggerInstrumentation(ILogger logger, string instanceIdentifier)
{
    protected string InstanceIdentifier { get; } = instanceIdentifier;

    public abstract Activity? StartJobActivity(string activityName, InternalFunctionContext context);

    public virtual void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
    {
        logger.JobEnqueued(jobType, functionName, jobId, enqueuedFrom ?? "Unknown");
    }

    public virtual void LogJobStarted(Guid jobId, string functionName, string jobType)
    {
        logger.JobStarted(jobType, functionName, jobId);
    }

    public virtual void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        logger.JobCompleted(functionName, jobId, executionTimeMs, success);
    }

    public virtual void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        logger.JobFailed(exception, functionName, jobId, retryCount, exception.Message);
    }

    public virtual void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        logger.JobCancelled(functionName, jobId, reason);
    }

    public virtual void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        logger.JobSkipped(functionName, jobId, reason);
    }

    public virtual void LogSeedingDataStarted(string seedingDataType)
    {
        logger.SeedingDataStarted(seedingDataType, InstanceIdentifier);
    }

    public virtual void LogSeedingDataCompleted(string seedingDataType)
    {
        logger.SeedingDataCompleted(seedingDataType, InstanceIdentifier);
    }

    public virtual void LogRequestDeserializationFailure(
        string requestType,
        string functionName,
        Guid jobId,
        JobType type,
        Exception exception
    )
    {
        logger.RequestDeserializationFailed(exception, requestType, jobId, type);
    }
}

internal static partial class JobsBaseLoggerInstrumentationLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Jobs Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}"
    )]
    public static partial void JobEnqueued(
        this ILogger logger,
        string jobType,
        string function,
        Guid jobId,
        string enqueuedFrom
    );

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Jobs Job started: {JobType} - {Function} ({JobId})"
    )]
    public static partial void JobStarted(this ILogger logger, string jobType, string function, Guid jobId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Jobs Job completed: {Function} ({JobId}) in {ExecutionTime}ms - Success: {Success}"
    )]
    public static partial void JobCompleted(
        this ILogger logger,
        string function,
        Guid jobId,
        long executionTime,
        bool success
    );

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Jobs Job cancelled: {Function} ({JobId}) - {Reason}"
    )]
    public static partial void JobCancelled(this ILogger logger, string function, Guid jobId, string reason);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Jobs Job skipped: {Function} ({JobId}) - {Reason}"
    )]
    public static partial void JobSkipped(this ILogger logger, string function, Guid jobId, string reason);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Jobs start seeding data: {JobType} ({EnvironmentName})"
    )]
    public static partial void SeedingDataStarted(this ILogger logger, string jobType, string environmentName);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Jobs completed seeding data: {JobType} ({EnvironmentName})"
    )]
    public static partial void SeedingDataCompleted(this ILogger logger, string jobType, string environmentName);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Error,
        Message = "Jobs Job failed: {Function} ({JobId}) - Retry {RetryCount} - {Error}"
    )]
    public static partial void JobFailed(
        this ILogger logger,
        Exception exception,
        string function,
        Guid jobId,
        int retryCount,
        string error
    );

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "Failed to deserialize request to {RequestType} - {JobId} - {JobType}"
    )]
    public static partial void RequestDeserializationFailed(
        this ILogger logger,
        Exception exception,
        string requestType,
        Guid jobId,
        JobType jobType
    );
}
